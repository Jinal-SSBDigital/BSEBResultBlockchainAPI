using BSEBResultBlockchainAPI.Helpers;
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

        private string NormalizeFlureeJson(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return input;

            // Convert EDN map → JSON map
            // {:ENC_v1 "abc"} → {"ENC_v1":"abc"}
            input = System.Text.RegularExpressions.Regex.Replace(
                input,
                @"\{:([^\s]+)\s+""([^""]*)""\}",
                "{\"$1\":\"$2\"}"
            );

            return input;
        }
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
                    select = new[] { "?sid", "?encrypteddata", "?bsebid", "?createddate", "?updateddate" },
                    where = new object[]
                    {
                new object[] { "?sid", "BSEB_FinalPublishedResult/rollcode",      rollCode         },
                new object[] { "?sid", "BSEB_FinalPublishedResult/rollnumber",    rollNo           },
                new object[] { "?sid", "BSEB_FinalPublishedResult/bsebid", "?bsebid" },
                new object[] { "?sid", "BSEB_FinalPublishedResult/encrypteddata", "?encrypteddata" },
                new object[] { "?sid", "BSEB_FinalPublishedResult/createddate",   "?createddate"   },
                new object[] { "?sid", "BSEB_FinalPublishedResult/updateddate",   "?updateddate"   }
                    }
                };

                var responseBody = await PostFlureeAsync("/query", query);

                if (string.IsNullOrWhiteSpace(responseBody)) return null;

                // Fluree returns: [ [sid, encrypteddata, createddate, updateddate], ... ]
                var rows = JsonSerializer.Deserialize<List<List<JsonElement>>>(responseBody);

                if (rows == null || rows.Count == 0) return null;

                var row = rows[0];

                // ── Extract raw encrypteddata string ──────────────────────────────────
                var encRaw = row[1].ValueKind == JsonValueKind.String
                    ? row[1].GetString()!
                    : row[1].GetRawText();

                // ── Deserialize JSON string → List<Dictionary<string, string>> ────────
                // Data is now stored as clean JSON: [{"ENC_v1":"..."},{"ENC_v2":"..."}]
                // No Base64 decode, no EDN normalization needed
                List<Dictionary<string, string>> encList;
                try
                {
                    encList = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(encRaw)
                              ?? new List<Dictionary<string, string>>();
                }
                catch
                {
                    // Fallback: if old EDN-format data still exists in DB, normalize it
                    _logger.LogWarning("[Fluree] Falling back to EDN normalization for encrypteddata. Raw: {Data}", encRaw);

                    var normalized = System.Text.RegularExpressions.Regex.Replace(
                        encRaw,
                        @"\{:([^\s]+)\s+""([^""]*)""\}",
                        "{\"$1\":\"$2\"}"
                    );

                    try
                    {
                        encList = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(normalized)
                                  ?? new List<Dictionary<string, string>>();
                    }
                    catch
                    {
                        // Last resort: wrap raw string as ENC_v1
                        _logger.LogWarning("[Fluree] EDN normalization also failed. Wrapping raw value as ENC_v1.");
                        encList = new List<Dictionary<string, string>>
                {
                    new Dictionary<string, string> { { "ENC_v1", encRaw } }
                };
                    }
                }

                return new FlureeResultRecord
                {
                    FlureeSubjectId = row[0].GetRawText().Trim('"'),

                    RollCode = rollCode,
                    RollNumber = rollNo,

                    EncryptedData = encList,
                    //BsebId = row[2].GetString(),

                    CreatedDate = ParseFlureeDate(row[3]),
                    UpdatedDate = ParseFlureeDate(row[4])
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Fluree] GetByRollAsync failed → rollcode={RollCode} rollnumber={RollNo}", rollCode, rollNo);
                throw;
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
        //public async Task SaveNewRecordAsync(string rollCode, string rollNo, string encryptedData)
        //{
        //    try
        //    {
        //        // ✅ Use epoch milliseconds — Fluree instant type expects long, not ISO string
        //        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        //        var encList = JsonSerializer.Serialize(new List<string> { encryptedData });
        //        // encList = '["ENC_v1"]'

        //        var transaction = new[]
        //        {
        //            new Dictionary<string, object>
        //            {
        //                // Collection name as _id → tells Fluree to CREATE new subject
        //                ["_id"] = "BSEB_FinalPublishedResult",

        //                ["BSEB_FinalPublishedResult/bsebid"]        = Guid.NewGuid().ToString(),
        //                ["BSEB_FinalPublishedResult/rollcode"]      = rollCode,
        //                ["BSEB_FinalPublishedResult/rollnumber"]    = rollNo,
        //                ["BSEB_FinalPublishedResult/encrypteddata"] = encList,   // '["ENC_v1"]'
        //                ["BSEB_FinalPublishedResult/createddate"]   = nowMs,     // epoch ms (instant)
        //                ["BSEB_FinalPublishedResult/updateddate"]   = nowMs      // epoch ms (instant)
        //            }
        //        };

        //        await TransactAsync(transaction);

        //        _logger.LogInformation("[Fluree] INSERT → rollcode={RollCode} rollnumber={RollNo}", rollCode, rollNo);
        //    }
        //    catch (Exception ex)
        //    {

        //        throw ex;
        //    }

        //}
        public async Task SaveNewRecordAsync(string rollCode, string rollNo, string encryptedData)
        {
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var encList = new List<Dictionary<string, string>>
            {
                new Dictionary<string, string> { { "ENC_v1", encryptedData } }
            };

            var encJson = JsonSerializer.Serialize(encList); // ← plain, no options needed

            var transaction = new[]
            {
                new Dictionary<string, object>
                {
                    ["_id"] = "BSEB_FinalPublishedResult",
                    ["BSEB_FinalPublishedResult/bsebid"]        = Guid.NewGuid().ToString(),
                    ["BSEB_FinalPublishedResult/rollcode"]      = rollCode,
                    ["BSEB_FinalPublishedResult/rollnumber"]    = rollNo,
                    ["BSEB_FinalPublishedResult/encrypteddata"] = encJson,
                    ["BSEB_FinalPublishedResult/createddate"]   = nowMs,
                    ["BSEB_FinalPublishedResult/updateddate"]   = nowMs
                }
             };

            await TransactAsync(transaction);
        }

        public async Task AppendEncryptedVersionAsync(FlureeResultRecord existing, string newEncryptedData)
        {
            var updatedList = existing.EncryptedData != null ? new List<Dictionary<string, string>>(existing.EncryptedData) : new List<Dictionary<string, string>>();

            int nextVersion = updatedList.Count == 0 ? 1 : updatedList.SelectMany(d => d.Keys).Select(k => int.TryParse(k.Replace("ENC_v", ""), out int v) ? v : 0).Max() + 1;

            updatedList.Add(new Dictionary<string, string>
            {
                { $"ENC_v{nextVersion}", newEncryptedData }
            });

            var encJson = JsonSerializer.Serialize(updatedList); // ← plain, no options needed

            var transaction = new[]
            {
                new Dictionary<string, object>
                {
                    //["BSEB_FinalPublishedResult/bsebid"] = existing.BsebId!,
                     //["_id"] = existing.FlureeSubjectId!,
                     ["_id"] = long.Parse(existing.FlureeSubjectId!),
                    ["BSEB_FinalPublishedResult/encrypteddata"] = encJson,
                    ["BSEB_FinalPublishedResult/updateddate"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                }
             };

            await TransactAsync(transaction);

            _logger.LogInformation("[Fluree] APPEND v{Version} → rollcode={RollCode} rollnumber={RollNo}",nextVersion, existing.RollCode, existing.RollNumber);
        }
        // public async Task SaveNewRecordAsync(string rollCode, string rollNo, string encryptedData)
        // {
        //     var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        //      var encList = new List<Dictionary<string, string>>
        //      {
        //          new Dictionary<string, string>
        //           {
        //             { "ENC_v1", encryptedData }
        //          }
        //      };

        //     // ❌ REMOVE THIS
        //     // var encJson = JsonSerializer.Serialize(encList);

        //     var transaction = new[]
        //     {
        //         new Dictionary<string, object>
        //         {
        //             ["_id"] = "BSEB_FinalPublishedResult",

        //             ["BSEB_FinalPublishedResult/bsebid"]        = Guid.NewGuid().ToString(),
        //             ["BSEB_FinalPublishedResult/rollcode"]      = rollCode,
        //             ["BSEB_FinalPublishedResult/rollnumber"]    = rollNo,

        //             // ✅ PASS OBJECT DIRECTLY
        //             ["BSEB_FinalPublishedResult/encrypteddata"] = encList,

        //             ["BSEB_FinalPublishedResult/createddate"]   = nowMs,
        //             ["BSEB_FinalPublishedResult/updateddate"]   = nowMs
        //         }
        //     };
        //     //    var encList = new List<Dictionary<string, string>>
        //     //    {
        //     //        new Dictionary<string, string>
        //     //        {
        //     //            { "ENC_v1", encryptedData }
        //     //        }
        //     //    };

        //     //    var encJson = JsonSerializer.Serialize(encList);

        //     //    var transaction = new[]
        //     //    {
        //     //new Dictionary<string, object>
        //     //{
        //     //    ["_id"] = "BSEB_FinalPublishedResult",

        //     //    ["BSEB_FinalPublishedResult/bsebid"]        = Guid.NewGuid().ToString(),
        //     //    ["BSEB_FinalPublishedResult/rollcode"]      = rollCode,
        //     //    ["BSEB_FinalPublishedResult/rollnumber"]    = rollNo,
        //     //    ["BSEB_FinalPublishedResult/encrypteddata"] = encJson,
        //     //    ["BSEB_FinalPublishedResult/createddate"]   = nowMs,
        //     //    ["BSEB_FinalPublishedResult/updateddate"]   = nowMs
        //     //}
        //// };

        //     await TransactAsync(transaction);
        // }

        // ─────────────────────────────────────────────────────────────────────────
        // UPDATE: Append new encrypted version to existing record
        //
        // Before: encrypteddata = '["ENC_v1"]'
        // After:  encrypteddata = '["ENC_v1","ENC_v2"]'
        //         or              '["ENC_v1","ENC_v2","ENC_v3"]' etc.
        //
        // Uses FlureeSubjectId (numeric internal _id) → UPDATE, NOT insert
        // ─────────────────────────────────────────────────────────────────────────
        //public async Task AppendEncryptedVersionAsync(FlureeResultRecord existing, string newEncryptedData)
        //{
        //    try
        //    {
        //        // Build updated list from existing + new version
        //        var updatedList = new List<string>(existing.EncryptedData) { newEncryptedData };
        //        var updatedEnc = JsonSerializer.Serialize(updatedList);
        //        // e.g. '["ENC_v1","ENC_v2"]'

        //        var transaction = new[]
        //        {
        //            new Dictionary<string, object>
        //            {
        //                // ✅ Numeric subject _id → UPDATES existing record, does NOT create new
        //                ["_id"] = existing.FlureeSubjectId!,

        //                ["BSEB_FinalPublishedResult/encrypteddata"] = updatedEnc,
        //                ["BSEB_FinalPublishedResult/updateddate"]   = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        //            }
        //        };

        //        await TransactAsync(transaction);

        //        _logger.LogInformation("[Fluree] APPEND v{Version} → rollcode={RollCode} rollnumber={RollNo}", updatedList.Count, existing.RollCode,existing.RollNumber);
        //    }
        //    catch (Exception ex)
        //    {

        //        throw ex;
        //    }

        //}


        // ─────────────────────────────────────────────────────────────────────────
        // HELPER: Parse Fluree date field
        // Fluree instant can return epoch ms (long) OR ISO string depending on version
        // ─────────────────────────────────────────────────────────────────────────
        private static DateTime ParseFlureeDate(JsonElement el)
        {
            return el.ValueKind switch
            {
                JsonValueKind.Number => DateTimeOffset.FromUnixTimeMilliseconds(el.GetInt64()).UtcDateTime,

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
        private static readonly JsonSerializerOptions _flureeJsonOptions = new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private async Task<string> PostFlureeAsync(string endpoint, object payload)
        {
            try
            {
                // ✅ Use UnsafeRelaxedJsonEscaping here — this is the ONLY serialization that matters
                var json = JsonSerializer.Serialize(payload, _flureeJsonOptions);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var url = $"{FlureeBase}/fdb/{Ledger}{endpoint}";

                _logger.LogDebug("[Fluree] POST {Url} | Payload: {Json}", url, json);

                var response = await _http.PostAsync(url, content);
                var body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("[Fluree] {Endpoint} FAILED ({Status}): {Body}", endpoint, response.StatusCode, body);
                    throw new Exception($"[Fluree] {endpoint} failed ({response.StatusCode}): {body}");
                }

                return body;
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        public async Task<object?> GetDecryptedAllWithVersionAsync(string rollCode, string rollNo)
        {
            var record = await GetByRollAsync(rollCode, rollNo);

            if (record == null || record.EncryptedData == null || record.EncryptedData.Count == 0)
                return null;

            var result = new List<object>();

            foreach (var entry in record.EncryptedData)
            {
                foreach (var kv in entry)
                {
                    var decrypted = QrUtility.DecryptStudentData(kv.Value);

                    result.Add(new
                    {
                        Version = kv.Key,
                        Data = decrypted
                    });
                }
            }

            return result; // still works because List<object> is object
        }
    }
}