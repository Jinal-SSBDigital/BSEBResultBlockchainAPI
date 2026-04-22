using BSEBResultBlockchainAPI.Models;

namespace BSEBResultBlockchainAPI.Services.Interfaces
{
    public interface IFlureeService
    {
        Task<FlureeResultRecord?> GetByRollAsync(string rollCode, string rollNo);
        //Task SaveNewRecordAsync(string rollCode, string rollNo, string encryptedData);
        Task SaveEncV1RecordAsync(string rollCode, string rollNo, string encryptedData);
        //Task SaveEncV2RecordAsync(string rollCode, string rollNo,string Enc_V1, string Enc_V2);
        Task UpsertRecordAsync(string rollCode, string rollNo,string Enc_V1, string Enc_V2, long FlureeSubjectId,string BsebId);

        // Takes full existing record so we can append without a second round-trip
        //Task AppendEncryptedVersionAsync(FlureeResultRecord existing, string newEncryptedData);
        //Task<object?> GetDecryptedLatestAsync(string rollCode, string rollNo);
        Task<object?> GetDecryptedAllWithVersionAsync(string rollCode, string rollNo);
    }
}