using ApiReviewDotNet.Services.GitHub;

namespace ApiReviewDotNet.Services;

public sealed class AreaOwnerService
{
    private readonly ILogger<AreaOwnerService> _logger;
    private readonly GitHubTeamService _teamService;
    private Dictionary<string, string[]> _ownerByArea = new();

    public AreaOwnerService(ILogger<AreaOwnerService> logger,
                            GitHubTeamService teamService)
    {
        _logger = logger;
        _teamService = teamService;
    }

    public IReadOnlyList<string> GetOwners(string area)
    {
        if (!_ownerByArea.TryGetValue(area, out string[]? result))
            result = Array.Empty<string>();

        return result;
    }

    public async Task ReloadAsync()
    {
        try
        {
            _ownerByArea = await GetOwnersAsync(_teamService);
            _logger.LogInformation("Loaded {count} area owners", _ownerByArea.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading area owners");
        }
    }

    private static async Task<Dictionary<string, string[]>> GetOwnersAsync(GitHubTeamService teamService)
    {
        string url = "https://raw.githubusercontent.com/dotnet/runtime/main/docs/area-owners.md";
        HttpClient client = new HttpClient();
        string contents = await client.GetStringAsync(url);
        IEnumerable<string> lines = GetLines(contents);
        Dictionary<string, string[]> result = new Dictionary<string, string[]>();

        foreach (string line in lines)
        {
            string[] parts = line.Split('|');
            if (parts.Length != 6)
                continue;

            string area = parts[1].Trim();
            string ownerText = parts[3].Trim();
            string[] owners = ownerText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            owners = owners.Select(o => o.Replace("@", "").Trim()).ToArray();

            if (!area.StartsWith("area-", StringComparison.OrdinalIgnoreCase))
                continue;

            SortedSet<string> expandedOwners = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string owner in owners)
            {
                IReadOnlyList<string>? members = teamService.GetMembers(owner);
                if (members is not null)
                    expandedOwners.UnionWith(members);
                else
                    expandedOwners.Add(owner);
            }

            result[area] = expandedOwners.ToArray();
        }

        return result;
    }

    private static IEnumerable<string> GetLines(string text)
    {
        using StringReader stringReader = new StringReader(text);
        while (true)
        {
            string? line = stringReader.ReadLine();
            if (line is null)
                yield break;

            yield return line;
        }
    }
}
