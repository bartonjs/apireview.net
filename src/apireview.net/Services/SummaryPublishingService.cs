using System.Runtime.ExceptionServices;

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

        string url = await CommitAsync(group, summary);
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

    private async Task<string> CommitAsync(RepositoryGroup group, ApiReviewSummary summary)
    {
        (string owner, string repo) = group.NotesRepo;
        DateTime date = summary.Items.First().FeedbackDateTime.DateTime;
        string markdown = $"# API Review {date:d}\n\n{GetMarkdown(summary)}";
        string path = $"{date.Year}/{date.Month:00}-{date.Day:00}-{group.NotesSuffix}/README.md";
        string commitMessage = $"Add review notes for {date:d}";

        GitHubClient github = await _clientFactory.CreateForAppAsync();
        ExceptionDispatchInfo? lastEx = null;

        string[] branchesToTry = [
            ApiReviewConstants.ApiReviewsBranch,
            $"apireview/{date:yyyy-MM-dd}-{group.NotesSuffix}"
        ];

        foreach (string branch in branchesToTry)
        {
            try
            {
                return await CommitAsync(group, summary, github, branch, path, commitMessage, markdown);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Direct commit to {Owner}/{Repo} on {Branch} failed; creating pull request instead.", owner, repo, branch);
                lastEx = ExceptionDispatchInfo.Capture(ex);
            }
        }

        if (lastEx is not null)
        {
            lastEx.Throw();
        }

        throw new InvalidOperationException();
    }

    private async Task<string> CommitAsync(
        RepositoryGroup group,
        ApiReviewSummary summary,
        GitHubClient github,
        string branch,
        string path,
        string commitMessage,
        string markdown)
    {
        (string owner, string repo) = group.NotesRepo;
        string head = $"heads/{branch}";
        Reference? masterReference = await github.Git.Reference.Get(owner, repo, head);
        Commit? latestCommit = await github.Git.Commit.Get(owner, repo, masterReference.Object.Sha);

        TreeResponse? recursiveTreeResponse = await github.Git.Tree.GetRecursive(owner, repo, latestCommit.Tree.Sha);
        TreeItem? file = recursiveTreeResponse.Tree.SingleOrDefault(t => t.Path == path);

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
                BaseTree = latestCommit.Tree.Sha
            };
            newTree.Tree.Add(newTreeItem);

            TreeResponse? newTreeResponse = await github.Git.Tree.Create(owner, repo, newTree);
            NewCommit newCommit = new NewCommit(commitMessage, newTreeResponse.Sha, latestCommit.Sha);
            Commit? newCommitResponse = await github.Git.Commit.Create(owner, repo, newCommit);

            ReferenceUpdate newReference = new ReferenceUpdate(newCommitResponse.Sha);
            await github.Git.Reference.Update(owner, repo, head, newReference);
        }

        string url = $"https://github.com/{owner}/{repo}/blob/{branch}/{path}";
        return url;
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
