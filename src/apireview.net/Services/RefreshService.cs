using ApiReviewDotNet.Services.GitHub;
using ApiReviewDotNet.Services.Ospo;

using Microsoft.Extensions.Hosting;

namespace ApiReviewDotNet.Services;

public sealed class RefreshService : BackgroundService
{
    private static readonly TimeSpan _refreshInterval = TimeSpan.FromHours(1);

    private readonly ILogger<RefreshService> _logger;
    private readonly OspoService _ospoService;
    private readonly GitHubTeamService _teamService;
    private readonly AreaOwnerService _areaOwnerService;
    private readonly IssueService _issueService;

    public RefreshService(ILogger<RefreshService> logger,
                          OspoService ospoService,
                          GitHubTeamService teamService,
                          AreaOwnerService areaOwnerService,
                          IssueService issueService)
    {
        _logger = logger;
        _ospoService = ospoService;
        _teamService = teamService;
        _areaOwnerService = areaOwnerService;
        _issueService = issueService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ReloadAsync();

        using PeriodicTimer timer = new(_refreshInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await ReloadAsync();
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("RefreshService is stopping.");
        }
    }

    private async Task ReloadAsync()
    {
        try
        {
            //await _ospoService.ReloadAsync();
            await _teamService.ReloadAsync();
            await _areaOwnerService.ReloadAsync();
            await _issueService.ReloadAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing data");
        }
    }
}
