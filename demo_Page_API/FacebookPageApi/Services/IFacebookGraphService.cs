namespace FacebookPageApi.Services;

public interface IFacebookGraphService
{
    Task<string> GetPageAsync(string pageId, CancellationToken cancellationToken = default);

    Task<string> GetPostsAsync(string pageId, CancellationToken cancellationToken = default);

    Task<string> CreatePostAsync(string pageId, string message, CancellationToken cancellationToken = default);

    Task<string> DeletePostAsync(string postId, CancellationToken cancellationToken = default);

    Task<string> GetCommentsAsync(string postId, CancellationToken cancellationToken = default);

    Task<string> GetLikesAsync(string postId, CancellationToken cancellationToken = default);

    Task<string> GetInsightsAsync(string pageId, CancellationToken cancellationToken = default);
}
