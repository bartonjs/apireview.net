using System.Text;
using System.Text.Encodings.Web;
using Markdig.Extensions.Emoji;
using Microsoft.AspNetCore.Components;

namespace ApiReviewDotNet.Shared;

public static class MarkdownExtensions
{
    private static readonly IDictionary<string, string> _emojiMapping = EmojiMapping.GetDefaultEmojiShortcodeToUnicode();

    public static MarkupString HighlightCode(this string text)
    {
        if (!text.Contains('`'))
            return new MarkupString(HtmlEncoder.Default.Encode(text));

        using StringWriter writer = new StringWriter();
        int p = 0;

        while (p < text.Length)
        {
            if (!GetTicks(text, p, out int firstTick, out int secondTick))
            {
                HtmlEncoder.Default.Encode(writer, text, p, text.Length - p);
                break;
            }

            int length = secondTick - firstTick - 1;
            if (length > 0)
            {
                HtmlEncoder.Default.Encode(writer, text, p, firstTick - p);
                writer.Write("<code>");
                HtmlEncoder.Default.Encode(writer, text, firstTick + 1, secondTick - firstTick - 1);
                writer.Write("</code>");
            }

            p = secondTick + 1;
        }

        static bool GetTicks(string text, int start, out int firstTick, out int secondTick)
        {
            firstTick = text.IndexOf('`', start);
            if (firstTick < 0)
            {
                secondTick = firstTick;
                return false;
            }

            secondTick = text.IndexOf('`', firstTick + 1);
            return secondTick >= 0;
        }

        return new MarkupString(writer.ToString());
    }

    public static string ExpandEmojis(this string text)
    {
        StringBuilder sb = new StringBuilder(text);
        foreach ((string key, string value) in _emojiMapping)
            sb.Replace(key, value);

        return sb.ToString();
    }
}
