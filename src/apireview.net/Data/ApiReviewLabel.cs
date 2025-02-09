using System.Drawing;
using System.Globalization;
using System.Text;

namespace ApiReviewDotNet.Data;

public sealed class ApiReviewLabel
{
    public ApiReviewLabel(string name,
                          string color,
                          string description)
    {
        Name = name;
        Color = color;
        Description = description;
    }

    public string Name { get; }
    public string Color { get; }
    public string Description { get; }

    public string GetStyle()
    {
        Color color = ParseColor(Color);
        byte labelR = color.R;
        byte labelG = color.G;
        byte labelB = color.B;
        float labelH = color.GetHue();
        float labelS = color.GetSaturation() * 100;
        float labelL = color.GetBrightness() * 100;
        StringBuilder sb = new StringBuilder();
        sb.Append($"--label-r: {labelR};");
        sb.Append($"--label-g: {labelG};");
        sb.Append($"--label-b: {labelB};");
        sb.Append($"--label-h: {labelH};");
        sb.Append($"--label-s: {labelS};");
        sb.Append($"--label-l: {labelL};");
        return sb.ToString();
    }

    private static Color ParseColor(string color)
    {
        if (!string.IsNullOrEmpty(color) && color.Length == 6 &&
            int.TryParse(color.AsSpan(0, 2), NumberStyles.HexNumber, null, out int r) &&
            int.TryParse(color.AsSpan(2, 2), NumberStyles.HexNumber, null, out int g) &&
            int.TryParse(color.AsSpan(4, 2), NumberStyles.HexNumber, null, out int b))
        {
            return System.Drawing.Color.FromArgb(r, g, b);
        }

        return System.Drawing.Color.Black;
    }
}
