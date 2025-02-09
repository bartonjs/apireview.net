namespace ApiReviewDotNet.Data;

public sealed class ApiReviewSummary
{
    public ApiReviewSummary(string repositoryGroup,
                            ApiReviewVideo? video,
                            IReadOnlyList<ApiReviewItem> items)
    {
        RepositoryGroup = repositoryGroup;
        Video = video;
        Items = items;
    }

    public string RepositoryGroup { get; }

    public ApiReviewVideo? Video { get; }

    public IReadOnlyList<ApiReviewItem> Items { get; }

    public string? GetVideoUrl(TimeSpan timeCode)
    {
        if (Video == null)
            return null;

        string timeCodeText = $"{timeCode.Hours}h{timeCode.Minutes}m{timeCode.Seconds}s";
        return $"https://www.youtube.com/watch?v={Video.Id}&t={timeCodeText}";
    }
}
