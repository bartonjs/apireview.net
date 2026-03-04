using ApiReviewDotNet.Data;
using ApiReviewDotNet.Services;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;

namespace ApiReviewDotNet.Pages;

public sealed partial class Backlog : IDisposable
{
    [Inject]
    public NavigationManager NavigationManager { get; set; } = null!;

    [Inject]
    private IssueService IssueService { get; set; } = null!;

    [Inject]
    private RepositoryGroupService RepositoryGroupService { get; set; } = null!;

    private RepositoryGroup _selectedGroup = null!;
    private string _filter = null!;
    private SortedDictionary<string, bool> _milestones = null!;

    public RepositoryGroup SelectedGroup => _selectedGroup;
    public string Filter => _filter;

    public IReadOnlyList<ApiReviewIssue> Issues => IssueService.Issues;
    public IEnumerable<ApiReviewIssue> VisibleIssues => Issues.Where(IsVisible);

    public string SelectAllMilestonesUrl => BuildMilestoneUrl();
    public string SelectNoneMilestonesUrl => BuildMilestoneUrl("m=__none__");

    public int GetRank(ApiReviewIssue issue)
    {
        for (int i = 0; i < Issues.Count; i++)
        {
            if (ReferenceEquals(Issues[i], issue))
                return i + 1;
        }

        return -1;
    }

    protected override void OnInitialized()
    {
        _selectedGroup = RepositoryGroupService.Default;
        LoadData();

        Uri uri = NavigationManager.ToAbsoluteUri(NavigationManager.Uri);

        Dictionary<string, StringValues> queryParameters = QueryHelpers.ParseQuery(uri.Query);

        if (queryParameters.TryGetValue("g", out StringValues g))
        {
            string name = g.ToString();
            RepositoryGroup? group = RepositoryGroupService.RepositoryGroups.FirstOrDefault(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase));
            if (group is not null)
                _selectedGroup = group;
        }

        if (queryParameters.TryGetValue("q", out StringValues q))
            _filter = q!;

        // m_submitted sentinel distinguishes form POST (where unchecked = absent) from direct URL nav.
        // Without the sentinel, no m params = all milestones selected (default).
        if (queryParameters.ContainsKey("m_submitted"))
        {
            foreach (string m in _milestones.Keys.ToArray())
                _milestones[m] = false;

            if (queryParameters.TryGetValue("m", out StringValues submittedMilestones))
            {
                foreach (string? m in submittedMilestones)
                {
                    if (_milestones.ContainsKey(m!))
                        _milestones[m!] = true;
                }
            }
        }
        else if (queryParameters.TryGetValue("m", out StringValues selectedMilestones))
        {
            // Direct URL navigation (Select All/None links or manual URL)
            foreach (string m in _milestones.Keys.ToArray())
                _milestones[m] = false;

            foreach (string? m in selectedMilestones)
            {
                if (_milestones.ContainsKey(m!))
                    _milestones[m!] = true;
            }
        }

        IssueService.Changed += IssuesChanged;
    }

    public void Dispose()
    {
        IssueService.Changed -= IssuesChanged;
    }

    private string BuildMilestoneUrl(string? extraParam = null)
    {
        string path = NavigationManager.ToAbsoluteUri(NavigationManager.Uri).AbsolutePath;
        var parts = new List<string>();
        if (_selectedGroup != RepositoryGroupService.Default)
            parts.Add($"g={Uri.EscapeDataString(_selectedGroup.Name)}");
        if (!string.IsNullOrEmpty(_filter))
            parts.Add($"q={Uri.EscapeDataString(_filter)}");
        if (extraParam != null)
            parts.Add(extraParam);
        return parts.Count > 0 ? $"{path}?{string.Join("&", parts)}" : path;
    }

    private void LoadData()
    {
        _milestones = CreateMilestones(Issues, _milestones);
    }

    private async void IssuesChanged(object? sender, EventArgs e)
    {
        await InvokeAsync(() =>
        {
            LoadData();
            StateHasChanged();
        });
    }

    private bool IsVisible(ApiReviewIssue issue)
    {
        if (!SelectedGroup.Repos.Any(r => string.Equals(r.FullName, issue.RepoFull, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (_milestones is not null && _milestones.TryGetValue(issue.Milestone ?? ApiReviewConstants.NoMilestone, out bool isChecked) && !isChecked)
            return false;

        if (string.IsNullOrEmpty(Filter))
            return true;

        if (issue.Title.Contains(Filter, StringComparison.OrdinalIgnoreCase))
            return true;

        if (issue.IdFull.Contains(Filter, StringComparison.OrdinalIgnoreCase))
            return true;

        if (issue.Author.Contains(Filter, StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (ApiReviewLabel label in issue.Labels)
        {
            if (label.Name.Contains(Filter, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private SortedDictionary<string, bool> CreateMilestones(IReadOnlyList<ApiReviewIssue> issues,
                                                            SortedDictionary<string, bool> existingMilestones)
    {
        SortedDictionary<string, bool> result = new SortedDictionary<string, bool>();

        foreach (ApiReviewIssue issue in issues)
            result[issue.Milestone ?? ApiReviewConstants.NoMilestone] = true;

        if (existingMilestones is not null)
        {
            foreach ((string k, bool v) in existingMilestones)
            {
                if (result.ContainsKey(k))
                    result[k] = v;
            }
        }

        return result;
    }
}
