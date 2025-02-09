using System.Text.RegularExpressions;

using ApiReviewDotNet.Data;
using ApiReviewDotNet.Services;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;

namespace ApiReviewDotNet.Pages;

[Authorize(Roles = ApiReviewConstants.ApiApproverRole)]
public partial class Publish
{
    [Inject]
    private RepositoryGroupService RepositoryGroupService { get; set; } = null!;

    [Inject]
    private NotesService NotesService { get; set; } = null!;

    private CancellationTokenSource _cts = null!;
    private DateTimeOffset _start;
    private DateTimeOffset _end;
    private string? _videoUrl;
    private string? _videoId;

    private string? SelectedRepositoryGroupName { get; set; }

    private RepositoryGroup SelectedRepositoryGroup => RepositoryGroupService.RepositoryGroups.First(rg => rg.Name == SelectedRepositoryGroupName);

    private DateTimeOffset Date
    {
        get => _start;
        set
        {
            _start = value.Date.Add(_start.TimeOfDay);
            _end = value.Date.Add(_end.TimeOfDay);
            UpdateValidation();
        }
    }

    private DateTimeOffset Start
    {
        get => _start;
        set
        {
            _start = _start.Date.Add(value.TimeOfDay);
            UpdateValidation();
        }
    }

    private DateTimeOffset End
    {
        get => _end;
        set
        {
            _end = _end.Date.Add(value.TimeOfDay);
            UpdateValidation();
        }
    }

    private string? VideoUrl
    {
        get => _videoUrl;
        set
        {
            _videoUrl = value;
            if (string.IsNullOrEmpty(_videoUrl))
            {
                _videoId = null;
            }
            else
            {
                Match match = Regex.Match(_videoUrl, @"https://www\.youtube\.com/watch\?v=(?<videoId>[^&]+)");
                if (!match.Success)
                {
                    VideoUrlValidationMessage = "The YouTube video URL isn't recognized";
                }
                else
                {
                    _videoId = match.Groups["videoId"].Value;
                    VideoUrlValidationMessage = null;
                }
            }
        }
    }

    private string? DateValidationMessage { get; set; }
    private string? StartValidationMessage { get; set; }
    private string? EndValidationMessage { get; set; }
    private string? VideoUrlValidationMessage { get; set; }
    private bool HasValidationErrors => DateValidationMessage is not null ||
                                        StartValidationMessage is not null ||
                                        EndValidationMessage is not null ||
                                        VideoUrlValidationMessage is not null;

    private bool CanSearch => !HasValidationErrors && !IsLoading;

    private bool IncludeVideo { get; set; }

    private IReadOnlyList<ApiReviewVideo> Videos { get; set; } = Array.Empty<ApiReviewVideo>();
    private ApiReviewVideo? SelectedVideo { get; set; }

    private bool IsLoading { get; set; }
    private ApiReviewSummary? Summary { get; set; }

    private ApiReviewPublicationResult? PublicationResult { get; set; }

    protected override void OnInitialized()
    {
        SelectedRepositoryGroupName = RepositoryGroupService.Default.Name;
        _start = DateTime.Now.Date;
        _end = _start.AddHours(23).AddMinutes(59);
        IncludeVideo = true;
    }

    private void UpdateValidation()
    {
        if (Start.Date > DateTimeOffset.Now.Date)
        {
            DateValidationMessage = "Date cannot be in the future";
            StartValidationMessage = null;
        }
        else
        {
            DateValidationMessage = null;

            if (Start > DateTimeOffset.Now)
            {
                StartValidationMessage = "Start cannot be in the future";
            }
            else
            {
                StartValidationMessage = null;
            }
        }

        if (End <= Start)
        {
            EndValidationMessage = "End Time must be after Start Time";
        }
        else
        {
            EndValidationMessage = null;
        }
    }

    private async Task FindIssuesAsync()
    {
        IsLoading = true;
        Videos = Array.Empty<ApiReviewVideo>();
        SelectedVideo = null;
        Summary = null;
        PublicationResult = null;
        StateHasChanged();

        if (_cts is not null)
            _cts.Cancel();

        _cts = new CancellationTokenSource();

        CancellationToken token = _cts.Token;

        IReadOnlyList<ApiReviewVideo> videos = Array.Empty<ApiReviewVideo>();

        if (IncludeVideo)
        {
            if (string.IsNullOrEmpty(_videoId))
            {
                videos = await NotesService.GetVideos(Start, End);
            }
            else
            {
                ApiReviewVideo? video = await NotesService.GetVideo(_videoId);
                if (video is null)
                    videos = Array.Empty<ApiReviewVideo>();
                else
                    videos = new[] { video };
            }

            if (token.IsCancellationRequested)
                return;
        }

        Videos = videos;
        SelectedVideo = videos.FirstOrDefault();
        StateHasChanged();

        ApiReviewSummary? summary;

        if (SelectedVideo is not null)
            summary = await NotesService.IssuesForVideo(SelectedRepositoryGroup, SelectedVideo.Id);
        else
            summary = await NotesService.IssuesForRange(SelectedRepositoryGroup, Start, End);

        if (token.IsCancellationRequested)
            return;

        Summary = summary;
        IsLoading = false;
    }

    private async Task SelectVideoAsync(ApiReviewVideo video)
    {
        if (video == SelectedVideo)
            return;

        SelectedVideo = video;
        Summary = null;
        PublicationResult = null;
        IsLoading = true;
        StateHasChanged();

        if (_cts is not null)
            _cts.Cancel();

        _cts = new CancellationTokenSource();

        CancellationToken token = _cts.Token;

        ApiReviewSummary? summary = await NotesService.IssuesForVideo(SelectedRepositoryGroup, video.Id);
        if (token.IsCancellationRequested)
            return;

        Summary = summary;
        IsLoading = false;
    }

    private async Task PublishNotesAsync()
    {
        IsLoading = true;

        PublicationResult = await NotesService.PublishNotesAsync(Summary);

        IsLoading = false;
    }
}
