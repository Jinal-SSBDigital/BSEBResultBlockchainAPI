using BSEBResultBlockchainAPI.Models;

namespace BSEBResultBlockchainAPI.Services.Interfaces
{
    public interface IFlureeService
    {
        Task<FlureeResultRecord?> GetByRollAsync(string rollCode, string rollNo);
        Task SaveNewRecordAsync(string rollCode, string rollNo, string encryptedData);

        // Takes full existing record so we can append without a second round-trip
        Task AppendEncryptedVersionAsync(FlureeResultRecord existing, string newEncryptedData);
        //Task<object?> GetDecryptedLatestAsync(string rollCode, string rollNo);
        Task<object?> GetDecryptedAllWithVersionAsync(string rollCode, string rollNo);
    }
}