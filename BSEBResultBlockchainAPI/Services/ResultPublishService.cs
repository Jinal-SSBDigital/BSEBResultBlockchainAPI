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

                //// ── Step 4: Record exists — check if data actually changed ────────────
                //var latestVersion = existing.EncryptedData.LastOrDefault();

                //if (latestVersion == encrypted)
                //{
                //    // ✅ CASE B: Encrypted string unchanged — no scrutiny changes, skip
                //    _logger.LogDebug(
                //        "[Skip] No change detected → rollcode={RollCode} rollnumber={RollNo}",
                //        rollCode, rollNo);

                //    return ProcessResult.Skipped;
                //}
                var lastEntry = existing.EncryptedData.LastOrDefault();
                string? latestValue = lastEntry?.Values.FirstOrDefault();

                if (latestValue == encrypted)
                {
                    _logger.LogDebug("[Skip] No change detected → rollcode={RollCode} rollnumber={RollNo}",rollCode, rollNo);

                    return ProcessResult.Skipped;
                }
                // ✅ CASE C: Data changed (scrutiny/correction applied) → APPEND new version
                // Fluree will store: encrypteddata = '["ENC_v1","ENC_v2"]' or '["ENC_v1","ENC_v2","ENC_v3"]' etc.
                //string encrypted1 = "dm?jmVRUtKB04cFB589&Lq$zta%*!UIxs9Ea&K&GLvLhdB03^DGBPkQA}k_uZ)|K%Zz4J(GBz+VFfcGVA}k_eb7f*xZfS9KWl2OLIwCSMG%+zUFfcGMG&UkEB4lr3B03^6FfcbQF)=MLGE^`yIxsLgFfbx4B6DscIwDO(AVxt_NI^~@RzXrpQz9%PW^N)nB27dfRzXrpQz9%PZEhkuB3eO6Nkl;)S3y!qQz9%PV{B(`VQpn1IwDm=NJB^<O+iFRSwT%nOCVNBL|H*hL0Lf{LP1PdK|xe3AVE?=Qb|D~EFyAcXKrsIIwDdnIWjUZFfcGNH!U(WA}k_iVPkb{ba^5=B2z<2MNUISA}k_wZ**a7L1$-8VRCD8B03^7GdLh1C{$>2Wn~~pb#7#GWn>^!XlZhEc_2k;XJ~XOA}k_wZ**a7L1$-IZ*pXFB03^eXmVv`AV_s?WO8L>AXI2+a&&nhMQLYfbRsMwWNCJ3b7^mGB03^5b95j?X?AIIX>V>KEFyDtVrpe$bW&w=b!>EVB05`pB6D?OB03^PZf9(1b7&$gB5h%KO<{6tB04cJFf1Z)VRLg$VRCCCIx{dVB6MhFZ*qAeIwCkVA}k_rLSIl)B03@>EFx!eLtj)#Pa--ZFd{4>XL3VdP*Nf~A}}H>B4cA^O<{6tb0Rt-A}k_wZ*)_2Vj?;sH#HzcA}k_vbz*8|V{}JyZ*_1^VQpn1IwCPHAY64YIWRR`buc+HI9zowIWtCFbuc+IHC%NtIWt9Ebuc+IFkE#oIW$CEbuc+HMj%6PZE$sLb8m8aB7H1-B6D?OB03^fa%6QPEFx`Tcuiq)Ya%)^Ffc44aA9+EO<{6tB04iLEFyGhWp8qMB03^BI3g?}Z$e*CQX)DcA}k_jazkHKNKYa<A}}H>B4=_#Ur<saIwCM4EFxoLWldpnYjYwxA|fmzbZ>N1bz&kqA~!f7L?SFAb9G{BWn*+la&L8TPGN0jB03^5E+AZWFgY+aTy-!xGB{jyFgY_uTy-!xGc{awFgY_tTy-!xGca6rFgY|tTy-!xGDaXnZ*6dOY;$jNc_Mu*dm?jnVj?;sP-uB`X=8IDEFx`Tcuiq)Ya%)^Ffc44aA9+EO<{6tB04iOEFyGhWp8qMB03^7I3g?}Z$e*CQX)DcGB_eEB4=_#UsOm>B03^4A}k_jazkHGQX)DcFd{4>V`F7aVRCD8B03@>EFyGobW?R=B03^AHX<w{b9G{BWn*+la&L8TPGN0jB03^6E+AZWFgY<WTy-!xF+p5)FgY|tTy-!xF+yB*FgY_uE-o$";
                await _flureeService.AppendEncryptedVersionAsync(existing, encrypted);
                //await _flureeService.AppendEncryptedVersionAsync(existing, encrypted);

                _logger.LogInformation("[APPEND] Version {Version} added → rollcode={RollCode} rollnumber={RollNo}", existing.EncryptedData.Count + 1, rollCode, rollNo);

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