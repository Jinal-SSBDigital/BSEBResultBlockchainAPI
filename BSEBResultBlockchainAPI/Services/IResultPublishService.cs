namespace BSEBResultBlockchainAPI.Services.Interfaces
{
    public interface IResultPublishService
    {
        Task PublishAllResultsAsync(CancellationToken cancellationToken = default);
    }
}