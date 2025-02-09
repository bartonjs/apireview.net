using System.Text.RegularExpressions;

namespace ApiReviewDotNet.Services.GitHub;

public static class GitHubIssueHelpers
{
    public static string FixTitle(string title)
    {
        // Let's get rid of all the boiler plate prefixes, like:
        // [API] Add an API that does somethign
        // [Feature Request] I'd love some API
        // API: Linq expressions

        string[] labels = new[]
        {
                    "api",
                    "proposal",
                    "feature",
                    "request"
            };

        bool modified;

        do
        {
            modified = false;

            Match match = Regex.Match(title, "^(\\[(?<prefix>[^\\]]+)\\]\\:?)|(?<prefix>(\\S+\\s+){1,3}\\S+\\:)");
            if (match.Success)
            {
                string prefix = match.Groups["prefix"].Value;
                if (labels.Any(l => prefix.Contains(l, StringComparison.OrdinalIgnoreCase)))
                {
                    title = title.Substring(match.Index + match.Length).Trim();
                    modified = true;
                }
            }
        } while (modified);

        return title;
    }

    public static string FixLabel(string labelName)
    {
        while (true)
        {
            int firstColon = labelName.IndexOf(':');
            if (firstColon < 0)
                break;

            int secondColon = labelName.IndexOf(':', firstColon + 1);
            if (secondColon < 0)
                break;

            int emojiStart = firstColon;
            int emojiLength = secondColon - emojiStart + 1;
            labelName = labelName.Remove(emojiStart, emojiLength);
        }

        return labelName.Trim();
    }

    public static string GetMarkdownLink(string owner, string repo, int id, string url, string title)
    {
        string fixedTitle = FixTitle(title);
        return $"[{owner}/{repo}#{id}: {fixedTitle}]({url})";
    }

    public static string GetHtmlLink(string owner, string repo, int id, string url, string title)
    {
        string fixedTitle = FixTitle(title);
        return $"<a href=\"{url}\">{owner}/{repo}#{id}: {fixedTitle}</a>";
    }
}
