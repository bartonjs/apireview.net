using ApiReviewDotNet.Data;
using ApiReviewDotNet.Services.Ospo;

using Octokit;

namespace ApiReviewDotNet.Services.GitHub;

public sealed class GitHubManager
{
    private readonly ILogger<GitHubManager> _logger;
    private readonly RepositoryGroupService _repositoryGroupService;
    private readonly GitHubClientFactory _clientFactory;
    private readonly AreaOwnerService _areaOwnerService;
    private readonly OspoService _ospoService;

    public GitHubManager(ILogger<GitHubManager> logger, RepositoryGroupService repositoryGroupService, GitHubClientFactory clientFactory, AreaOwnerService areaOwnerService, OspoService ospoService)
    {
        _logger = logger;
        _repositoryGroupService = repositoryGroupService;
        _clientFactory = clientFactory;
        _areaOwnerService = areaOwnerService;
        _ospoService = ospoService;
    }

    public async Task<IReadOnlyList<ApiReviewItem>> GetFeedbackAsync(IReadOnlyCollection<OrgAndRepo> repos, DateTimeOffset start, DateTimeOffset end)
    {
        static bool MightBeAnApiIssue(Issue issue)
        {
            bool isClosed = issue.State.Value == ItemState.Closed;
            bool isReadyForReview = issue.Labels.Any(l => l.Name == ApiReviewConstants.ApiReadyForReview);
            bool isApproved = issue.Labels.Any(l => l.Name == ApiReviewConstants.ApiApproved);
            bool needsWork = issue.Labels.Any(l => l.Name == ApiReviewConstants.ApiNeedsWork);
            return isClosed || isReadyForReview || isApproved || needsWork;
        }

        static (string? VideoLink, string? Markdown) ParseFeedback(string? body)
        {
            if (body is null)
                return (null, null);

            const string prefix = "[Video](";
            if (body.StartsWith(prefix))
            {
                int videoUrlEnd = body.IndexOf(")");
                if (videoUrlEnd > 0)
                {
                    int videoUrlStart = prefix.Length;
                    int videoUrlLength = videoUrlEnd - videoUrlStart;
                    string videoUrl = body.Substring(videoUrlStart, videoUrlLength);
                    string remainingBody = body.Substring(videoUrlEnd + 1).TrimStart();
                    return (videoUrl, remainingBody);
                }
            }

            return (null, body);
        }

        // NOTE: Ideally, we'd use the user here, but if we do, we get a FORBIDDEN error.
        //       Not a biggie though, this API is only called by people with the api-approver
        //       role, so using the app quota seems fine.

        GitHubClient github = await _clientFactory.CreateForAppAsync();
        List<ApiReviewItem> results = new List<ApiReviewItem>();

        foreach ((string owner, string repo) in repos)
        {
            RepositoryIssueRequest request = new RepositoryIssueRequest
            {
                Filter = IssueFilter.All,
                State = ItemStateFilter.All,
                Since = start
            };

            IReadOnlyList<Issue>? issues = await github.Issue.GetAllForRepository(owner, repo, request);

            foreach (Issue issue in issues)
            {
                if (!MightBeAnApiIssue(issue))
                    continue;

                IReadOnlyList<IssueEvent>? events = await github.Issue.Events.GetAllForIssue(owner, repo, issue.Number);
                ApiReviewOutcome? reviewOutcome = ApiReviewOutcome.Get(events, start, end);

                if (reviewOutcome is not null)
                {
                    DateTimeOffset feedbackDateTime = reviewOutcome.DecisionTime;

                    ApiReviewDecision decision = reviewOutcome.Decision;
                    IReadOnlyList<IssueComment>? comments = await github.Issue.Comment.GetAllForIssue(owner, repo, issue.Number);
                    IssueComment? comment = comments.Where(c => start <= c.CreatedAt && c.CreatedAt <= end).
                        Where(c => string.Equals(c.User.Login, reviewOutcome.DecisionMaker, StringComparison.OrdinalIgnoreCase)).
                        Select(c => (Comment: c, TimeDifference: Math.Abs((c.CreatedAt - feedbackDateTime).TotalSeconds))).
                        OrderBy(c => c.TimeDifference).
                        Select(c => c.Comment).
                        FirstOrDefault();

                    string? feedbackId = comment?.Id.ToString();
                    string feedbackAuthor = reviewOutcome.DecisionMaker;
                    string? feedbackUrl = comment?.HtmlUrl ?? issue.HtmlUrl;
                    (_, string? feedbackMarkdown) = ParseFeedback(comment?.Body);

                    ApiReviewIssue apiReviewIssue = CreateIssue(owner, repo, issue, events, end);

                    ApiReviewItem feedback = new ApiReviewItem(
                        decision: decision,
                        issue: apiReviewIssue,
                        feedbackId: feedbackId,
                        feedbackAuthor: feedbackAuthor,
                        feedbackDateTime: feedbackDateTime,
                        feedbackUrl: feedbackUrl,
                        feedbackMarkdown: feedbackMarkdown
                    );
                    results.Add(feedback);
                }
            }
        }

        results.Sort((x, y) => x.FeedbackDateTime.CompareTo(y.FeedbackDateTime));
        return results;
    }

    public async Task<IReadOnlyList<ApiReviewIssue>> GetIssuesAsync()
    {
        IReadOnlyList<OrgAndRepo> repos = _repositoryGroupService.Repositories;

        GitHubClient github = await _clientFactory.CreateForAppAsync();
        List<ApiReviewIssue> result = new List<ApiReviewIssue>();

        foreach ((string owner, string repo) in repos)
        {
            RepositoryIssueRequest request = new RepositoryIssueRequest
            {
                Filter = IssueFilter.All,
                State = ItemStateFilter.Open
            };
            request.Labels.Add(ApiReviewConstants.ApiReadyForReview);

            IReadOnlyList<Issue>? issues = await github.Issue.GetAllForRepository(owner, repo, request);

            foreach (Issue issue in issues)
            {
                IReadOnlyList<IssueEvent>? events = await github.Issue.Events.GetAllForIssue(owner, repo, issue.Number);
                ApiReviewIssue apiReviewIssue = CreateIssue(owner, repo, issue, events, DateTime.Now);

                result.Add(apiReviewIssue);
            }
        }

        result.Sort();

        return result;
    }

    private ApiReviewer[] GetReviewers(string author,
                                       IReadOnlyList<string> assignees,
                                       string? markedReadyForReviewBy,
                                       string? markedBlockingBy,
                                       IReadOnlyList<string> areaOwners)
    {
        OspoLinkSet linkSet = _ospoService.LinkSet;
        List<ApiReviewer> result = new List<ApiReviewer>();

        // We want to assign reviewers based on relevance,
        //
        // 1. Whoever marked it blocking
        // 2. Whoever marked it as ready-for-review
        // 3. Whoever is the author
        // 4. Assignees
        // 5. Area owners

        Add(result, linkSet, markedBlockingBy);
        Add(result, linkSet, markedReadyForReviewBy);
        Add(result, linkSet, author);

        foreach (string assignee in assignees ?? Array.Empty<string>())
            Add(result, linkSet, assignee);

        foreach (string areaOwner in areaOwners ?? Array.Empty<string>())
            Add(result, linkSet, areaOwner);

        return result.ToArray();
    }

    private void Add(List<ApiReviewer> target, OspoLinkSet linkSet, string? userName)
    {
        if (userName is null)
            return;

        if (target.Any(r => string.Equals(r.GitHubUserName, userName, StringComparison.OrdinalIgnoreCase)))
            return;

        if (!linkSet.LinkByLogin.TryGetValue(userName, out OspoLink? link))
        {
            _logger.LogWarning("Ignored non-OSPO linked user '{userName}'", userName);
        }
        else
        {
            ApiReviewer reviewer = new ApiReviewer(
                gitHubUserName: userName,
                name: link.MicrosoftInfo.PreferredName,
                email: link.MicrosoftInfo.EmailAddress
            );
            target.Add(reviewer);
        }
    }

    private ApiReviewIssue CreateIssue(string owner, string repo, Issue issue, IReadOnlyList<IssueEvent> events, DateTimeOffset end)
    {
        ApiReadyEvent? readyEvent = ApiReadyEvent.Get(events, end);
        ApiBlockingEvent? blockingEvent = ApiBlockingEvent.Get(events, end);

        string title = GitHubIssueHelpers.FixTitle(issue.Title);
        string? author = issue.User.Login;
        string[] assignees = issue.Assignees.Select(a => a.Login).ToArray();
        string? markedReadyForReviewBy = readyEvent?.DecisionMaker;
        DateTimeOffset? markedReadyAt = readyEvent?.CreatedAt;
        string? markedBlockingBy = blockingEvent?.DecisionMaker;
        DateTimeOffset? markedBlockingAt = blockingEvent?.CreatedAt;
        string[] areaOwners = GetAreaOwners(issue.Labels.Select(l => l.Name));
        string milestone = issue.Milestone?.Title ?? ApiReviewConstants.NoMilestone;
        ApiReviewLabel[] labels = issue.Labels.Select(l => new ApiReviewLabel(l.Name, l.Color, l.Description)).ToArray();
        ApiReviewer[] reviewers = GetReviewers(author, assignees, markedReadyForReviewBy, markedBlockingBy, areaOwners);

        ApiReviewIssue result = new ApiReviewIssue(
            owner,
            repo,
            issue.Number,
            title,
            author,
            assignees,
            markedReadyForReviewBy,
            markedReadyAt,
            markedBlockingBy,
            markedBlockingAt,
            areaOwners,
            issue.CreatedAt,
            issue.HtmlUrl,
            milestone,
            labels,
            reviewers
        );

        return result;
    }

    private string[] GetAreaOwners(IEnumerable<string> labels)
    {
        List<string> result = new List<string>();

        foreach (string label in labels)
        {
            IReadOnlyList<string> owners = _areaOwnerService.GetOwners(label);
            result.AddRange(owners);
        }

        return result.ToArray();
    }

    private sealed class ApiReadyEvent
    {
        public ApiReadyEvent(string decisionMaker, DateTimeOffset createdAt)
        {
            DecisionMaker = decisionMaker;
            CreatedAt = createdAt;
        }

        public string DecisionMaker { get; }
        public DateTimeOffset CreatedAt { get; }

        public static ApiReadyEvent? Get(IEnumerable<IssueEvent> events, DateTimeOffset end)
        {
            // NOTE: We want to know when an API was first marked as ready-for-review, as opposed to last.
            //
            // The reason being that when an API is reviewed and marked as need-work, flipping it back to
            // ready-for-review should come back earlier, rather than later to aid review flow.

            foreach (IssueEvent e in events.Where(e => e.CreatedAt <= end)
                                    .OrderBy(e => e.CreatedAt))
            {
                switch (e.Event.StringValue)
                {
                    case "labeled" when string.Equals(e.Label.Name, ApiReviewConstants.ApiReadyForReview, StringComparison.OrdinalIgnoreCase):
                        return new ApiReadyEvent(e.Actor.Login, e.CreatedAt);
                }
            }

            return null;
        }
    }

    private sealed class ApiBlockingEvent
    {
        private ApiBlockingEvent(string decisionMaker, DateTimeOffset createdAt)
        {
            DecisionMaker = decisionMaker;
            CreatedAt = createdAt;
        }

        public string DecisionMaker { get; }

        public DateTimeOffset CreatedAt { get; }

        public static ApiBlockingEvent? Get(IEnumerable<IssueEvent> events, DateTimeOffset end)
        {
            // NOTE: Since we use this expedite review, we generally want to know when an issue was last labelled as
            //       blocking without maintaining its position within the blocking queue.

            foreach (IssueEvent e in events.Where(e => e.CreatedAt <= end)
                                    .OrderByDescending(e => e.CreatedAt))
            {
                switch (e.Event.StringValue)
                {
                    case "labeled" when string.Equals(e.Label.Name, ApiReviewConstants.Blocking, StringComparison.OrdinalIgnoreCase):
                        return new ApiBlockingEvent(e.Actor.Login, e.CreatedAt);
                    case "unlabeled" when string.Equals(e.Label.Name, ApiReviewConstants.Blocking, StringComparison.OrdinalIgnoreCase):
                        return null;
                }
            }

            return null;
        }
    }

    private sealed class ApiReviewOutcome
    {
        public ApiReviewOutcome(ApiReviewDecision decision, string decisionMaker, DateTimeOffset decisionTime)
        {
            Decision = decision;
            DecisionMaker = decisionMaker;
            DecisionTime = decisionTime;
        }

        public static ApiReviewOutcome? Get(IEnumerable<IssueEvent> events, DateTimeOffset start, DateTimeOffset end)
        {
            IssueEvent? readyEvent = default(IssueEvent);
            ApiReviewOutcome? current = default(ApiReviewOutcome);
            ApiReviewOutcome? rejection = default(ApiReviewOutcome);

            foreach (IssueEvent e in events.Where(e => e.CreatedAt <= end).OrderBy(e => e.CreatedAt))
                switch (e.Event.StringValue)
                {
                    case "labeled" when string.Equals(e.Label.Name, ApiReviewConstants.ApiReadyForReview, StringComparison.OrdinalIgnoreCase):
                        current = null;
                        readyEvent = e;
                        break;
                    case "labeled" when string.Equals(e.Label.Name, ApiReviewConstants.ApiApproved, StringComparison.OrdinalIgnoreCase):
                        current = new ApiReviewOutcome(ApiReviewDecision.Approved, e.Actor.Login, e.CreatedAt);
                        readyEvent = null;
                        break;
                    case "labeled" when string.Equals(e.Label.Name, ApiReviewConstants.ApiNeedsWork, StringComparison.OrdinalIgnoreCase):
                        current = new ApiReviewOutcome(ApiReviewDecision.NeedsWork, e.Actor.Login, e.CreatedAt);
                        readyEvent = null;
                        break;
                    case "reopened":
                        rejection = null;
                        break;
                    case "closed":
                        if (readyEvent is not null)
                            rejection = new ApiReviewOutcome(ApiReviewDecision.Rejected, e.Actor.Login, e.CreatedAt);
                        break;
                }

            if (rejection is not null)
                current = rejection;

            if (current is not null)
            {
                bool inInterval = start <= current.DecisionTime && current.DecisionTime <= end;
                if (!inInterval)
                    return null;
            }

            return current;
        }

        public ApiReviewDecision Decision { get; }
        public string DecisionMaker { get; }
        public DateTimeOffset DecisionTime { get; }
    }
}
