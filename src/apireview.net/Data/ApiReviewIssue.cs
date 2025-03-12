namespace ApiReviewDotNet.Data;

public sealed class ApiReviewIssue : IComparable<ApiReviewIssue>
{
    public ApiReviewIssue(string owner,
                          string repo,
                          int id,
                          string title,
                          string author,
                          IReadOnlyList<string>? assignees,
                          string? markedReadyForReviewBy,
                          DateTimeOffset? markedReadyAt,
                          string? markedBlockingBy,
                          DateTimeOffset? markedBlockingAt,
                          IReadOnlyList<string>? areaOwners,
                          DateTimeOffset createdAt,
                          string url,
                          string? milestone,
                          IReadOnlyList<ApiReviewLabel>? labels,
                          IReadOnlyList<ApiReviewer>? reviewers)
    {
        Owner = owner;
        Repo = repo;
        Id = id;
        Title = title;
        Author = author;
        Assignees = assignees ?? Array.Empty<string>();
        MarkedReadyForReviewBy = markedReadyForReviewBy;
        MarkedReadyAt = markedReadyAt;
        MarkedBlockingBy = markedBlockingBy;
        MarkedBlockingAt = markedBlockingAt;
        AreaOwners = areaOwners ?? Array.Empty<string>();
        CreatedAt = createdAt;
        Url = url;
        Milestone = milestone;
        Labels = labels ?? Array.Empty<ApiReviewLabel>();
        Reviewers = reviewers ?? Array.Empty<ApiReviewer>();
        DetailText = CreateDetailText();
    }

    public string Owner { get; }

    public string Repo { get; }

    public string RepoFull => $"{Owner}/{Repo}";

    public int Id { get; }

    public string IdFull => $"{Owner}/{Repo}#{Id}";

    public string Title { get; }

    public string Author { get; }

    public IReadOnlyList<string> Assignees { get; }

    public string? MarkedReadyForReviewBy { get; }

    public DateTimeOffset? MarkedReadyAt { get; }

    public string? MarkedBlockingBy { get; }

    public DateTimeOffset? MarkedBlockingAt { get; }

    public IReadOnlyList<string> AreaOwners { get; }

    public DateTimeOffset CreatedAt { get; }

    public string DetailText { get; }

    public string Url { get; }

    public string? Milestone { get; }

    public IReadOnlyList<ApiReviewLabel> Labels { get; }

    public bool IsBlocking => Labels is not null && Labels.Any(l => string.Equals(l.Name, ApiReviewConstants.Blocking, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<ApiReviewer> Reviewers { get; }

    private string CreateDetailText()
    {
        if (MarkedBlockingAt is not null &&
            MarkedBlockingBy is not null)
        {
            return $"{IdFull} last marked blocking {MarkedBlockingAt.Value.FormatRelative()} by {MarkedBlockingBy}";
        }

        if (MarkedReadyAt is not null &&
            MarkedReadyForReviewBy is not null)
        {
            return $"{IdFull} first marked for review {MarkedReadyAt.Value.FormatRelative()} by {MarkedReadyForReviewBy}";
        }

        return $"{IdFull} {CreatedAt.FormatRelative()} by {Author}";
    }

    public int CompareTo(ApiReviewIssue? other)
    {
        if (other is null)
            return -1;

        int result = -IsBlocking.CompareTo(other.IsBlocking);
        if (result != 0)
            return result;

        result = CompareMilestone(Milestone, other.Milestone);
        if (result != 0)
            return result;

        result = CompareDates(MarkedBlockingAt, other.MarkedBlockingAt);
        if (result != 0)
            return result;

        result = CompareDates(MarkedReadyAt, other.MarkedReadyAt);
        if (result != 0)
            return result;

        return CreatedAt.CompareTo(other.CreatedAt);
    }

    private static int CompareDates(DateTimeOffset? x, DateTimeOffset? y)
    {
        if (x is null && y is null)
            return 0;

        if (x is null)
            return 1;

        if (y is null)
            return -1;

        return x.Value.CompareTo(y.Value);
    }

    private static int CompareMilestone(string? x, string? y)
    {
        // The desired sort order is:
        //
        // 1. Milestones that look like versions, sorted by version number
        // 2. Milestones that aren't versions, sorted by text (e.g. "Backlog", "Future")
        // 3. No milestone
        //
        // Why does no milestone go last? Because we don't to punish folks for triaging milestones.

        if (string.IsNullOrEmpty(x) && string.IsNullOrEmpty(y))
            return 0;

        if (string.IsNullOrEmpty(x))
            return 1;

        if (string.IsNullOrEmpty(y))
            return -1;

        if (ApiReviewConstants.NoMilestone.Equals(x))
        {
            if (ApiReviewConstants.NoMilestone.Equals(y))
            {
                return 0;
            }

            return 1;
        }

        if (ApiReviewConstants.NoMilestone.Equals(y))
        {
            return -1;
        }

        bool xIsVersion = Version.TryParse(x, out Version? xVersion);
        bool yIsVersion = Version.TryParse(y, out Version? yVersion);

        int result = -xIsVersion.CompareTo(yIsVersion);
        if (result != 0)
            return result;

        if (xIsVersion && yIsVersion)
            return xVersion!.CompareTo(yVersion);

        return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
    }
}
