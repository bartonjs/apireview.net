using ApiReviewDotNet.Data;
using ApiReviewDotNet.Services.GitHub;
using ApiReviewDotNet.Services.YouTube;

using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;

using Markdig;

using Octokit;

using SendGrid;
using SendGrid.Helpers.Mail;

using EmailAddress = SendGrid.Helpers.Mail.EmailAddress;

namespace ApiReviewDotNet.Services;

public sealed class SummaryPublishingService
{
    private readonly ILogger<SummaryPublishingService> _logger;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _configuration;
    private readonly RepositoryGroupService _repositoryGroupService;
    private readonly GitHubClientFactory _clientFactory;
    private readonly YouTubeServiceFactory _youTubeServiceFactory;

    public SummaryPublishingService(ILogger<SummaryPublishingService> logger,
                                    IWebHostEnvironment env,
                                    IConfiguration configuration,
                                    RepositoryGroupService repositoryGroupService,
                                    GitHubClientFactory clientFactory,
                                    YouTubeServiceFactory youTubeServiceFactory)
    {
        _logger = logger;
        _env = env;
        _configuration = configuration;
        _repositoryGroupService = repositoryGroupService;
        _clientFactory = clientFactory;
        _youTubeServiceFactory = youTubeServiceFactory;
    }

    public async Task<ApiReviewPublicationResult> PublishAsync(ApiReviewSummary summary)
    {
        if (!summary.Items.Any())
            return ApiReviewPublicationResult.Failed();

        RepositoryGroup? group = _repositoryGroupService.Get(summary.RepositoryGroup);
        if (group is null)
            return ApiReviewPublicationResult.Failed();

        if (_env.IsDevelopment())
        {
            await UpdateCommentsDevAsync(summary);
        }
        else
        {
            // Apparently, we can't easily modify video descriptions in the cloud.
            // If someone has a fix for that, I'd be massively thankful.
            //
            // await UpdateVideoDescriptionAsync(summary);
            await UpdateCommentsAsync(summary);
        }

        string url = await CommitOrCreatePullRequestAsync(group, summary);
        await SendEmailAsync(group, summary);
        return ApiReviewPublicationResult.Suceess(url);
    }

    private async Task SendEmailAsync(RepositoryGroup group, ApiReviewSummary summary)
    {
        if (group.MailingList is null)
            return;

        string? key = _configuration["SendGridKey"];
        DateTime date = summary.Items.First().FeedbackDateTime.Date;
        string subject = $"API Review Notes {date:d}";
        string markdown = GetMarkdown(summary);
        string body = Markdown.ToHtml(markdown);
        SendGridMessage msg = new SendGridMessage();
        msg.SetFrom(new EmailAddress("notes@apireview.net", ".NET API Reviews"));
        msg.AddTo(new EmailAddress(group.MailingList));
        msg.SetReplyTo(new EmailAddress(group.MailingReplyTo));
        msg.SetSubject(subject);
        msg.AddContent(MimeType.Html, body);

        try
        {
            SendGridClient client = new SendGridClient(key);
            await client.SendEmailAsync(msg);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending email: {Message}", ex.Message);
        }
    }

    private async Task UpdateVideoDescriptionAsync(ApiReviewSummary summary)
    {
        if (summary.Video is null)
            return;

        using StringWriter descriptionBuilder = new StringWriter();
        foreach (ApiReviewItem item in summary.Items)
        {
            TimeSpan tc = item.TimeCode;
            descriptionBuilder.WriteLine($"{tc.Hours:00}:{tc.Minutes:00}:{tc.Seconds:00} - {item.Decision}: {item.Issue.Title} {item.FeedbackUrl}");
        }

        string description = descriptionBuilder.ToString()
                                            .Replace("<", "(")
                                            .Replace(">", ")");

        YouTubeService service = _youTubeServiceFactory.Create();

        VideosResource.ListRequest? listRequest = service.Videos.List("snippet");
        listRequest.Id = summary.Video.Id;
        VideoListResponse? listResponse = await listRequest.ExecuteAsync();

        Video? video = listResponse.Items[0];
        video.Snippet.Description = description;

        VideosResource.UpdateRequest? updateRequest = service.Videos.Update(video, "snippet");
        await updateRequest.ExecuteAsync();
    }

    private async Task UpdateCommentsAsync(ApiReviewSummary summary)
    {
        GitHubClient github = await _clientFactory.CreateForAppAsync();

        foreach (ApiReviewItem item in summary.Items)
        {
            string? videoUrl = summary.GetVideoUrl(item.TimeCode);

            if (item.FeedbackId is not null && videoUrl is not null)
            {
                string updatedMarkdown = $"[Video]({videoUrl})\n\n{item.FeedbackMarkdown}";
                long commentId = Convert.ToInt64(item.FeedbackId);
                await github.Issue.Comment.Update(item.Issue.Owner, item.Issue.Repo, commentId, updatedMarkdown);
            }
        }
    }

    private async Task UpdateCommentsDevAsync(ApiReviewSummary summary)
    {
        (string owner, string repo) = _repositoryGroupService.Repositories.First();

        if (!summary.Items.All(i => i.Issue.Owner == owner &&
                                    i.Issue.Repo == repo))
            return;

        GitHubClient github = await _clientFactory.CreateForAppAsync();

        foreach (ApiReviewItem item in summary.Items)
        {
            if (item.FeedbackId is not null)
            {
                string status = item.Decision.ToString();
                string updatedMarkdown = $"[Video]({status})\n\n{item.FeedbackMarkdown}";
                long commentId = Convert.ToInt64(item.FeedbackId);
                await github.Issue.Comment.Update(item.Issue.Owner, item.Issue.Repo, commentId, updatedMarkdown);
            }
        }
    }

    private async Task<string> CommitOrCreatePullRequestAsync(RepositoryGroup group, ApiReviewSummary summary)
    {
        DateTime date = summary.Items.First().FeedbackDateTime.DateTime;
        (string owner, string repo) = group.NotesRepo;
        
        string branch = ApiReviewConstants.ApiReviewsBranch;
        string head = $"heads/{branch}";
        string fallbackBranch = $"apireview/{date:yyyy-MM-dd}-{group.NotesSuffix}";
        string fallbackHead = $"heads/{fallbackBranch}";
        string path = $"{date.Year}/{date.Month:00}-{date.Day:00}-{group.NotesSuffix}/README.md";
        
        string markdown = $"# API Review {date:d}\n\n{GetMarkdown(summary)}";
        string commitMessage = $"Add review notes for {date:d}";

        GitHubClient github = await _clientFactory.CreateForAppAsync();
        (Commit? latestCommit, TreeItem? file) = await GetFile(github, owner, repo, head, path);

        if (file is null)
        {
            (_, file) = await GetFile(github, owner, repo, fallbackHead, path);

            if (file is not null)
            {
                branch = fallbackBranch;
            }
        }

        if (file is null)
        {
            NewTreeItem newTreeItem = new NewTreeItem
            {
                Mode = "100644",
                Path = path,
                Content = markdown
            };

            NewTree newTree = new NewTree
            {
                BaseTree = latestCommit!.Tree.Sha
            };
            newTree.Tree.Add(newTreeItem);

            TreeResponse? newTreeResponse = await github.Git.Tree.Create(owner, repo, newTree);
            NewCommit newCommit = new NewCommit(commitMessage, newTreeResponse.Sha, latestCommit.Sha);
            Commit? newCommitResponse = await github.Git.Commit.Create(owner, repo, newCommit);

            try
            {
                ReferenceUpdate newReference = new ReferenceUpdate(newCommitResponse.Sha);
                await github.Git.Reference.Update(owner, repo, head, newReference);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Direct commit to {Owner}/{Repo} on {Branch} failed; trying fallback branch.", owner, repo, branch);

                string? branchLatestCommit = await EnsureBranch(github, owner, repo, fallbackHead, latestCommit.Sha);

                // If the branch already exists and is on a different latest commit
                if (branchLatestCommit is not null)
                {
                    newCommit = new NewCommit(commitMessage, newTreeResponse.Sha, branchLatestCommit);
                    newCommitResponse = await github.Git.Commit.Create(owner, repo, newCommit);
                }

                ReferenceUpdate newReference = new ReferenceUpdate(newCommitResponse.Sha);
                await github.Git.Reference.Update(owner, repo, fallbackHead, newReference);

                branch = fallbackBranch;
            }
        }

        string url = $"https://github.com/{owner}/{repo}/blob/{branch}/{path}";
        return url;

        static async Task<(Commit? LatestCommit, TreeItem? TreeItem)> GetFile(
            GitHubClient github,
            string owner,
            string repo,
            string head,
            string path)
        {
            Reference? branchReference = null;

            try
            {
                branchReference = await github.Git.Reference.Get(owner, repo, head);
            }
            catch (Exception)
            {
            }

            if (branchReference is null)
            {
                return (null, null);
            }

            Commit? latestCommit = await github.Git.Commit.Get(owner, repo, branchReference.Object.Sha);
            TreeResponse? recursiveTreeResponse = await github.Git.Tree.GetRecursive(owner, repo, latestCommit.Tree.Sha);
            TreeItem? file = recursiveTreeResponse.Tree.SingleOrDefault(t => t.Path == path);
            return (latestCommit, file);
        }

        static async Task<string?> EnsureBranch(
            GitHubClient github,
            string owner,
            string repo,
            string fallbackHead,
            string baseCommitId)
        {
            try
            {
                await github.Git.Reference.Create(owner, repo, new NewReference(fallbackHead, baseCommitId));
            }
            catch (Exception)
            {
                Reference? branchReference = await github.Git.Reference.Get(owner, repo, fallbackHead);

                if (!string.Equals(baseCommitId, branchReference.Object.Sha, StringComparison.OrdinalIgnoreCase))
                {
                    return branchReference.Object.Sha;
                }
            }

            return null;
        }
    }

    private static string GetMarkdown(ApiReviewSummary summary)
    {
        StringWriter noteWriter = new StringWriter();

        foreach (ApiReviewItem item in summary.Items)
        {
            noteWriter.WriteLine($"## {item.Issue.Title}");
            noteWriter.WriteLine();
            noteWriter.Write($"**{item.Decision}** | [#{item.Issue.Repo}/{item.Issue.Id}]({item.FeedbackUrl})");

            string? videoUrl = summary.GetVideoUrl(item.TimeCode);
            if (videoUrl is not null)
                noteWriter.Write($" | [Video]({videoUrl})");

            noteWriter.WriteLine();
            noteWriter.WriteLine();

            if (item.FeedbackMarkdown is not null)
            {
                noteWriter.Write(item.FeedbackMarkdown);
                noteWriter.WriteLine();
            }
        }

        return noteWriter.ToString();
    }
}
