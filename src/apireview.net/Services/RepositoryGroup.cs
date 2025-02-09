namespace ApiReviewDotNet.Services;

public sealed class RepositoryGroup
{
    public RepositoryGroup(string name,
                           string displayName,
                           bool isDefault,
                           IReadOnlyList<OrgAndRepo> repos,
                           string approverTeamSlug,
                           string mailingList,
                           string mailingReplyTo,
                           OrgAndRepo notesRepo,
                           string notesSuffix)
    {
        Name = name;
        DisplayName = displayName;
        IsDefault = isDefault;
        Repos = repos;
        ApproverTeamSlug = approverTeamSlug;
        MailingList = mailingList;
        MailingReplyTo = mailingReplyTo;
        NotesRepo = notesRepo;
        NotesSuffix = notesSuffix;
    }

    public string Name { get; }
    public string DisplayName { get; }
    public bool IsDefault { get; }
    public IReadOnlyList<OrgAndRepo> Repos { get; }
    public string ApproverTeamSlug { get; }
    public string MailingList { get; }
    public string MailingReplyTo { get; }
    public OrgAndRepo NotesRepo { get; }
    public string NotesSuffix { get; }

    public static IReadOnlyList<RepositoryGroup> Get(IConfiguration configuration)
    {
        List<RepositoryGroup> result = new List<RepositoryGroup>();

        foreach (IConfigurationSection groupConfiguration in configuration.GetChildren())
        {
            RepositoryGroup item = new RepositoryGroup(
                name: groupConfiguration.Key,
                displayName: groupConfiguration["DisplayName"]!,
                isDefault: groupConfiguration.GetValue("IsDefault", false),
                repos: groupConfiguration.GetSection("Repos")
                                         .GetChildren()
                                         .Select(r => OrgAndRepo.Parse(r.Value!)!)
                                         .ToArray(),
                approverTeamSlug: groupConfiguration["ApproverTeamSlug"]!,
                mailingList: groupConfiguration["MailingList"]!,
                mailingReplyTo: groupConfiguration["MailingReplyTo"]!,
                notesRepo: OrgAndRepo.Parse(groupConfiguration["NotesRepo"]!)!,
                notesSuffix: groupConfiguration["NotesSuffix"]!
            );
            result.Add(item);
        }

        return result.ToArray();
    }

    public override string ToString()
    {
        return $"{Name} ({string.Join(", ", Repos.AsEnumerable())})";
    }
}
