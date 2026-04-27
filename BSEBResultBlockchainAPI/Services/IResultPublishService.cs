namespace BSEBResultBlockchainAPI.Services.Interfaces
{
    public interface IResultPublishService
    {
        Task PublishAllResultsAsync(CancellationToken cancellationToken = default);
        Task PublishAllResultsAsyncNew(CancellationToken cancellationToken = default);
        Task Encv2PublishAllResultsAsync(CancellationToken cancellationToken = default);
    }
}