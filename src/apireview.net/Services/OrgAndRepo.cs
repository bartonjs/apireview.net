namespace ApiReviewDotNet.Services;

public sealed class OrgAndRepo
{
    public OrgAndRepo(string orgName, string repoName)
    {
        OrgName = orgName;
        RepoName = repoName;
    }

    public string OrgName { get; }
    public string RepoName { get; }
    public string FullName => $"{OrgName}/{RepoName}";

    public static OrgAndRepo? Parse(string text)
    {
        string[] parts = text.Split('/');
        if (parts.Length != 2)
            return null;

        string org = parts[0].Trim();
        string repo = parts[1].Trim();
        return new OrgAndRepo(org, repo);
    }

    public static IEnumerable<OrgAndRepo> ParseList(string text)
    {
        string[] elements = text.Split(',');
        return elements.Select(Parse).Where(r => r is not null).Select(r => r!);
    }

    public override string ToString()
    {
        return FullName;
    }

    public void Deconstruct(out string orgName, out string repoName)
    {
        orgName = OrgName;
        repoName = RepoName;
    }
}
