namespace BSEBResultBlockchainAPI.Services
{
    public interface IResultUpdateService
    {
        Task<ProcessResult> UpdateSingleResultAsync(string rollCode, string rollNo);
    }
}