using BSEBResultBlockchainAPI.Models;
using BSEBResultBlockchainAPI.Services.Interfaces;
using System.Text;
using System.Text.Json;

namespace BSEBResultBlockchainAPI.Services
{
    public class FlureeService : IFlureeService
    {
        private readonly HttpClient _http;
        private readonly IConfiguration _config;
        private readonly ILogger<FlureeService> _logger;

        public FlureeService(IHttpClientFactory httpFactory, IConfiguration config, ILogger<FlureeService> logger)
        {
            _http = httpFactory.CreateClient("FlureeClient");
            _config = config;
            _logger = logger;
        }

        private string FlureeBase => _config["Fluree:BaseUrl"]!;
        private string Ledger => _config["Fluree:Ledger"]!;

        // ─────────────────────────────────────────────────────────────────────────
        // QUERY: Find record by rollcode + rollnumber
        // Returns null if not found → caller decides INSERT or APPEND
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<FlureeResultRecord?> GetByRollAsync(string rollCode, string rollNo)
        {
            try
            {
                var query = new
                {
                    select = new[] {"?sid","?encrypteddata","?createddate","?updateddate"},
                    where = new object[]
                     {
                    new object[] { "?sid", "BSEB_FinalPublishedResult/rollcode",      rollCode          },
                    new object[] { "?sid", "BSEB_FinalPublishedResult/rollnumber",    rollNo            },
                    new object[] { "?sid", "BSEB_FinalPublishedResult/encrypteddata", "?encrypteddata"  },
                    new object[] { "?sid", "BSEB_FinalPublishedResult/createddate",   "?createddate"    },
                    new object[] { "?sid", "BSEB_FinalPublishedResult/updateddate",   "?updateddate"    }
                    }
                };

                var responseBody = await PostFlureeAsync("/query", query);
                //var responseBody = await PostFlureeAsync("/query", query);
                if (string.IsNullOrWhiteSpace(responseBody)) return null;

                // Fluree returns: [ [sid, encrypteddata, createddate, updateddate], ... ]
                var rows = JsonSerializer.Deserialize<List<List<JsonElement>>>(responseBody);
                if (rows == null || rows.Count == 0) return null;

                var row = rows[0];

                // encrypteddata is stored as a serialized JSON array string: '["ENC_v1","ENC_v2"]'
                var encRaw = row[1].ValueKind == JsonValueKind.String ? row[1].GetString()! : row[1].GetRawText();

                List<string> encList;
                try
                {
                    encList = JsonSerializer.Deserialize<List<string>>(encRaw) ?? new List<string>();
                }
                catch
                {
                    // Fallback: plain single string (legacy) → wrap in list
                    encList = new List<string> { encRaw };
                }

                return new FlureeResultRecord
                {
                    FlureeSubjectId = row[0].GetRawText().Trim('"'),  // Fluree internal numeric _id
                    RollCode = rollCode,
                    RollNumber = rollNo,
                    EncryptedData = encList,
                    CreatedDate = ParseFlureeDate(row[2]),
                    UpdatedDate = ParseFlureeDate(row[3])
                };
            }
            catch (Exception ex)
            {

                throw ex;
            }
          
        }

        // ─────────────────────────────────────────────────────────────────────────
        // INSERT: First-time record creation
        //
        // Fluree stores: encrypteddata = '["ENC_v1"]'
        //
        // Fields:
        //   log_id        → uuid (unique per schema)
        //   rollcode      → string (indexed)
        //   rollnumber    → string (indexed)
        //   encrypteddata → string (indexed) — serialized JSON array
        //   createddate   → instant (epoch ms, as per Fluree schema)
        //   updateddate   → instant (epoch ms, as per Fluree schema)
        // ─────────────────────────────────────────────────────────────────────────
        public async Task SaveNewRecordAsync(string rollCode, string rollNo, string encryptedData)
        {
            try
            {
                // ✅ Use epoch milliseconds — Fluree instant type expects long, not ISO string
                var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var encList = JsonSerializer.Serialize(new List<string> { encryptedData });
                // encList = '["ENC_v1"]'

                var transaction = new[]
                {
                    new Dictionary<string, object>
                    {
                        // Collection name as _id → tells Fluree to CREATE new subject
                        ["_id"] = "BSEB_FinalPublishedResult",

                        ["BSEB_FinalPublishedResult/bsebid"]        = Guid.NewGuid().ToString(),
                        ["BSEB_FinalPublishedResult/rollcode"]      = rollCode,
                        ["BSEB_FinalPublishedResult/rollnumber"]    = rollNo,
                        ["BSEB_FinalPublishedResult/encrypteddata"] = encList,   // '["ENC_v1"]'
                        ["BSEB_FinalPublishedResult/createddate"]   = nowMs,     // epoch ms (instant)
                        ["BSEB_FinalPublishedResult/updateddate"]   = nowMs      // epoch ms (instant)
                    }
                };

                await TransactAsync(transaction);

                _logger.LogInformation("[Fluree] INSERT → rollcode={RollCode} rollnumber={RollNo}", rollCode, rollNo);
            }
            catch (Exception ex)
            {

                throw ex;
            }
           
        }

        // ─────────────────────────────────────────────────────────────────────────
        // UPDATE: Append new encrypted version to existing record
        //
        // Before: encrypteddata = '["ENC_v1"]'
        // After:  encrypteddata = '["ENC_v1","ENC_v2"]'
        //         or              '["ENC_v1","ENC_v2","ENC_v3"]' etc.
        //
        // Uses FlureeSubjectId (numeric internal _id) → UPDATE, NOT insert
        // ─────────────────────────────────────────────────────────────────────────
        public async Task AppendEncryptedVersionAsync(FlureeResultRecord existing, string newEncryptedData)
        {
            try
            {
                // Build updated list from existing + new version
                var updatedList = new List<string>(existing.EncryptedData) { newEncryptedData };
                var updatedEnc = JsonSerializer.Serialize(updatedList);
                // e.g. '["ENC_v1","ENC_v2"]'

                var transaction = new[]
                {
                    new Dictionary<string, object>
                    {
                        // ✅ Numeric subject _id → UPDATES existing record, does NOT create new
                        ["_id"] = existing.FlureeSubjectId!,

                        ["BSEB_FinalPublishedResult/encrypteddata"] = updatedEnc,
                        ["BSEB_FinalPublishedResult/updateddate"]   = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    }
                };

                await TransactAsync(transaction);

                _logger.LogInformation("[Fluree] APPEND v{Version} → rollcode={RollCode} rollnumber={RollNo}", updatedList.Count, existing.RollCode,existing.RollNumber);
            }
            catch (Exception ex)
            {

                throw ex;
            }
          
        }

        // ─────────────────────────────────────────────────────────────────────────
        // HELPER: Parse Fluree date field
        // Fluree instant can return epoch ms (long) OR ISO string depending on version
        // ─────────────────────────────────────────────────────────────────────────
        private static DateTime ParseFlureeDate(JsonElement el)
        {
            return el.ValueKind switch
            {
                JsonValueKind.Number => DateTimeOffset
                    .FromUnixTimeMilliseconds(el.GetInt64())
                    .UtcDateTime,

                JsonValueKind.String => DateTime.Parse(el.GetString()!),

                _ => DateTime.UtcNow
            };
        }

        // ─────────────────────────────────────────────────────────────────────────
        // SHARED: POST to Fluree /transact
        // ─────────────────────────────────────────────────────────────────────────
        private async Task TransactAsync(object transaction)
        {
            var body = await PostFlureeAsync("/transact", transaction);
            _logger.LogDebug("[Fluree] Transact response: {Body}", body);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // SHARED: Generic POST helper for all Fluree endpoints
        // ─────────────────────────────────────────────────────────────────────────
        private async Task<string> PostFlureeAsync(string endpoint, object payload)
        {
            try
            {
                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var url = $"{FlureeBase}/fdb/{Ledger}{endpoint}";
                //var url = $"{FlureeBase}/fdb/{Ledger}{endpoint}";

                _logger.LogDebug("[Fluree] POST {Url} | Payload: {Json}", url, json);

                var response = await _http.PostAsync(url, content);
                var body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError(
                        "[Fluree] {Endpoint} FAILED ({Status}): {Body}",
                        endpoint, response.StatusCode, body);

                    throw new Exception($"[Fluree] {endpoint} failed ({response.StatusCode}): {body}");
                }

                return body;
            }
            catch (Exception ex)
            {

                throw ex;
            }
            
        }
    }
}