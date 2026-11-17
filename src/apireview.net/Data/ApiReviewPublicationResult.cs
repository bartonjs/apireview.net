namespace ApiReviewDotNet.Data;

public sealed class ApiReviewPublicationResult
{
    public static ApiReviewPublicationResult Failed() => new(success: false, url: null, pullRequestUrl: null);
    public static ApiReviewPublicationResult Suceess(string url, string? pullRequestUrl) => new(success: true, url, pullRequestUrl);

    private ApiReviewPublicationResult(bool success, string? url, string? pullRequestUrl)
    {
        Success = success;
        Url = url;
        PullRequestUrl = pullRequestUrl;
    }

    public bool Success { get; }
    public string? Url { get; }
    public string? PullRequestUrl { get; }
}
