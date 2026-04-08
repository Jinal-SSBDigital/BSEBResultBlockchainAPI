using BSEBResultBlockchainAPI.Helpers;
using BSEBResultBlockchainAPI.Services.Interfaces;

namespace BSEBResultBlockchainAPI.Services
{
    public class ResultPublishService : IResultPublishService
    {
        private readonly DbHelper _dbHelper;
        private readonly IFlureeService _flureeService;
        private readonly ILogger<ResultPublishService> _logger;
        private readonly IConfiguration _config;

        private int BatchSize => _config.GetValue<int>("Processing:BatchSize", 50);
        private int DelayBetweenBatchesMs => _config.GetValue<int>("Processing:DelayBetweenBatchesMs", 500);
        private int DegreeOfParallelism => _config.GetValue<int>("Processing:DegreeOfParallelism", 5);

        public ResultPublishService(DbHelper dbHelper,IFlureeService flureeService,  ILogger<ResultPublishService> logger,IConfiguration config)
        {
            _dbHelper = dbHelper;
            _flureeService = flureeService;
            _logger = logger;
            _config = config;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // MAIN: Fetch all roll pairs → batch process → publish to Fluree
        // ─────────────────────────────────────────────────────────────────────────
        public async Task PublishAllResultsAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("=== Starting BSEB Result Blockchain Publish ===");

                // Step 1: Fetch all rollcode + rollnumber pairs from SQL
                var allRolls = await _dbHelper.GetAllRollCodesAsync();
                _logger.LogInformation("Total roll pairs fetched: {Count}", allRolls.Count);

                if (allRolls.Count == 0)
                {
                    _logger.LogWarning("No roll pairs found. Exiting publish.");
                    return;
                }

                // Step 2: Divide into batches
                var batches = allRolls.Select((item, index) => new { item, index }).GroupBy(x => x.index / BatchSize).Select(g => g.Select(x => x.item).ToList()).ToList();

                int processed = 0, failed = 0, skipped = 0;

                foreach (var batch in batches)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        _logger.LogWarning("Cancellation requested. Stopping publish.");
                        break;
                    }

                    // Step 3: Process batch with limited parallelism
                    var semaphore = new SemaphoreSlim(DegreeOfParallelism);

                    var tasks = batch.Select(async roll =>
                    {
                        await semaphore.WaitAsync(cancellationToken);
                        try
                        {
                            var result = await ProcessSingleRollAsync(roll.RollCode, roll.RollNo);

                            if (result == ProcessResult.Processed)
                                Interlocked.Increment(ref processed);
                            else if (result == ProcessResult.Skipped)
                                Interlocked.Increment(ref skipped);
                        }
                        catch (Exception ex)
                        {
                            Interlocked.Increment(ref failed);
                            _logger.LogError(ex, "[Error] RollCode={RollCode} RollNo={RollNo}", roll.RollCode, roll.RollNo);
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    });

                    await Task.WhenAll(tasks);

                    _logger.LogInformation("Batch done → Processed={P} Skipped={S} Failed={F}", processed, skipped, failed);

                    // Step 4: Delay between batches to reduce load
                    if (!cancellationToken.IsCancellationRequested)
                        await Task.Delay(DelayBetweenBatchesMs, cancellationToken);
                }

                _logger.LogInformation("=== Publish Complete → Total={Total} Processed={P} Skipped={S} Failed={F} ===", allRolls.Count, processed, skipped, failed);
            }
            catch (Exception ex)
            {

                throw ex;
            }
          
        }

        // ─────────────────────────────────────────────────────────────────────────
        // SINGLE ROLL PROCESSOR
        //
        // Logic:
        //   1. Get student data from SQL
        //   2. Encrypt student data → encrypted string
        //   3. Check Fluree for existing record
        //      a. NOT FOUND  → INSERT new record  : encrypteddata = ["ENC_v1"]
        //      b. FOUND + NO CHANGE → SKIP
        //      c. FOUND + CHANGED   → APPEND      : encrypteddata = ["ENC_v1","ENC_v2",...]
        // ─────────────────────────────────────────────────────────────────────────
        private async Task<ProcessResult> ProcessSingleRollAsync(string rollCode, string rollNo)
        {
            try
            {
                // ── Step 1: Get student result data from SQL DB ──────────────────────
                var student = await _dbHelper.GetStudentResultAsync(rollCode, rollNo);
                if (student == null)
                {
                    _logger.LogWarning("[Skip] No SQL data found → {RollCode}/{RollNo}", rollCode, rollNo);
                    return ProcessResult.Skipped;
                }

                // ── Step 2: Encrypt student data ─────────────────────────────────────
                string encrypted = QrUtility.GenerateEncrypteForstudentdata(student);

                // ── Step 3: Check Fluree for existing record ──────────────────────────
                var existing = await _flureeService.GetByRollAsync(rollCode, rollNo);

                if (existing == null)
                {
                    // ✅ CASE A: No record in Fluree → First time INSERT
                    // Fluree will store: encrypteddata = '["ENC_v1"]'
                    await _flureeService.SaveNewRecordAsync(rollCode, rollNo, encrypted);

                    _logger.LogInformation("[INSERT] New record → rollcode={RollCode} rollnumber={RollNo}", rollCode, rollNo);

                    return ProcessResult.Processed;
                }

                // ── Step 4: Record exists — check if data actually changed ────────────
                var latestVersion = existing.EncryptedData.LastOrDefault();

                if (latestVersion == encrypted)
                {
                    // ✅ CASE B: Encrypted string unchanged — no scrutiny changes, skip
                    _logger.LogDebug(
                        "[Skip] No change detected → rollcode={RollCode} rollnumber={RollNo}",
                        rollCode, rollNo);

                    return ProcessResult.Skipped;
                }

                // ✅ CASE C: Data changed (scrutiny/correction applied) → APPEND new version
                // Fluree will store: encrypteddata = '["ENC_v1","ENC_v2"]' or '["ENC_v1","ENC_v2","ENC_v3"]' etc.
                await _flureeService.AppendEncryptedVersionAsync(existing, encrypted);

                _logger.LogInformation(
                    "[APPEND] Version {Version} added → rollcode={RollCode} rollnumber={RollNo}",
                    existing.EncryptedData.Count + 1, rollCode, rollNo);

                return ProcessResult.Processed;
            }
            catch (Exception ex)
            {

                throw ex;
            }
           
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Enum to track per-roll outcome
    // ─────────────────────────────────────────────────────────────────────────────
    public enum ProcessResult
    {
        Processed,
        Skipped
    }
}