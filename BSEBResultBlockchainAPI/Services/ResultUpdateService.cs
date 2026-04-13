using BSEBResultBlockchainAPI.Helpers;
using BSEBResultBlockchainAPI.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace BSEBResultBlockchainAPI.Services
{
    public class ResultUpdateService : IResultUpdateService
    {
        private readonly DbHelper _dbHelper;
        private readonly IFlureeService _flureeService;
        private readonly ILogger<ResultUpdateService> _logger;

        public ResultUpdateService(
            DbHelper dbHelper,
            IFlureeService flureeService,
            ILogger<ResultUpdateService> logger)
        {
            _dbHelper = dbHelper;
            _flureeService = flureeService;
            _logger = logger;
        }

        public async Task<ProcessResult> UpdateSingleResultAsync(string rollCode, string rollNo)
        {
            try
            {
                return await ProcessSingleRollAsync(rollCode, rollNo);
            }
            catch (Exception)
            {
                throw;
            }
        }

        private async Task<ProcessResult> ProcessSingleRollAsync(string rollCode, string rollNo)
        {
            try
            {
            
                var student = await _dbHelper.GetStudentResultAsync(rollCode, rollNo);

                if (student == null)
                {
                    _logger.LogWarning("[Skip] No SQL data found → {RollCode}/{RollNo}", rollCode, rollNo);
                    return ProcessResult.Skipped;
                }

                
                string encrypted = QrUtility.GenerateEncrypteForstudentdata(student);

              
                var existing = await _flureeService.GetByRollAsync(rollCode, rollNo);

                if (existing == null)
                {
                    await _flureeService.SaveNewRecordAsync(rollCode, rollNo, encrypted);

                    _logger.LogInformation("[INSERT] New record → {RollCode}/{RollNo}", rollCode, rollNo);

                    return ProcessResult.Processed;
                }

                var lastEntry = existing.EncryptedData.LastOrDefault();
                string? latestValue = lastEntry?.Values.FirstOrDefault();

                if (latestValue == encrypted)
                {
                    _logger.LogDebug("[Skip] No change → {RollCode}/{RollNo}", rollCode, rollNo);
                    return ProcessResult.Skipped;
                }

                await _flureeService.AppendEncryptedVersionAsync(existing, encrypted);

                _logger.LogInformation("[APPEND] New version added → {RollCode}/{RollNo}", rollCode, rollNo);

                return ProcessResult.Processed;
            }
            catch (Exception)
            {
                throw;
            }
        }
    }

    public enum ProcessResult2
    {
        Processed,
        Skipped
    }
}