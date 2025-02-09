using ApiReviewDotNet.Data;
using Octokit;

namespace ApiReviewDotNet.Services.GitHub;

public sealed class GitHubMembershipService
{
    private readonly ILogger _logger;

    public GitHubMembershipService(ILogger<GitHubMembershipService> logger)
    {
        _logger = logger;
    }

    public async Task<bool> IsMemberOfAnyTeamAsync(string accessToken, string orgName, IEnumerable<string> teamSlugs, string userName)
    {
        try
        {
            ProductHeaderValue productInformation = new ProductHeaderValue(ApiReviewConstants.ProductName);
            GitHubClient client = new GitHubClient(productInformation)
            {
                Credentials = new Credentials(accessToken)
            };

            foreach (string teamSlug in teamSlugs)
            {
                Team team;
                try
                {
                    team = await client.Organization.Team.GetByName(orgName, teamSlug);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Can't find team '{teamSlug}': {ex.Message}");
                    continue;
                }

                try
                {
                    TeamMembershipDetails? membership = await client.Organization.Team.GetMembershipDetails(team.Id, userName);
                    if (membership is not null && membership.State == MembershipState.Active)
                        return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Can't check team membership for team '{teamSlug}' and user '{userName}': {ex.Message}");
                    continue;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Can't check team membership: {ex.Message}");
        }

        return false;
    }
}
