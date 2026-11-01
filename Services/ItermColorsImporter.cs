using System.Globalization;
using System.Xml.Linq;
using ClaudeBarWin.Config;

namespace ClaudeBarWin.Services;

/// <summary>
/// Parses an iTerm2 .itermcolors file (an Apple plist) into theme colours.
/// Maps ANSI colours: green→Ok, yellow→Warn, red→Critical; plus background/foreground.
/// </summary>
public static class ItermColorsImporter
{
    public static ImportedThemeColors? TryImport(string path)
    {
        try
        {
            var doc = XDocument.Load(path);
            var topDict = doc.Root?.Element("dict");
            if (topDict is null) return null;

            var map = ReadDictColors(topDict);

            Color bg = map.GetValueOrDefault("Background Color", Theme.Dark.Background);
            Color fg = map.GetValueOrDefault("Foreground Color", Theme.Dark.Foreground);
            Color ok = map.GetValueOrDefault("Ansi 2 Color", Theme.Dark.Ok);      // green
            Color warn = map.GetValueOrDefault("Ansi 3 Color", Theme.Dark.Warn);   // yellow
            Color crit = map.GetValueOrDefault("Ansi 1 Color", Theme.Dark.Critical); // red
            Color dim = map.TryGetValue("Ansi 8 Color", out var d) ? d : Mix(fg, bg, 0.45);
            Color track = Mix(bg, fg, 0.22);

            return new ImportedThemeColors
            {
                Bg = ToHex(bg),
                Fg = ToHex(fg),
                Dim = ToHex(dim),
                Track = ToHex(track),
                Ok = ToHex(ok),
                Warn = ToHex(warn),
                Critical = ToHex(crit)
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>In a plist &lt;dict&gt;, &lt;key&gt; elements are followed by their value element.</summary>
    private static Dictionary<string, Color> ReadDictColors(XElement dict)
    {
        var result = new Dictionary<string, Color>(StringComparer.Ordinal);
        var nodes = dict.Elements().ToList();
        for (int i = 0; i + 1 < nodes.Count; i++)
        {
            if (nodes[i].Name.LocalName != "key") continue;
            var value = nodes[i + 1];
            if (value.Name.LocalName != "dict") continue;
            if (TryReadColor(value, out var color))
                result[nodes[i].Value] = color;
        }
        return result;
    }

    private static bool TryReadColor(XElement colorDict, out Color color)
    {
        color = Color.Black;
        double? r = null, g = null, b = null;
        var nodes = colorDict.Elements().ToList();
        for (int i = 0; i + 1 < nodes.Count; i++)
        {
            if (nodes[i].Name.LocalName != "key") continue;
            string comp = nodes[i].Value;
            if (!double.TryParse(nodes[i + 1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var val))
                continue;
            switch (comp)
            {
                case "Red Component": r = val; break;
                case "Green Component": g = val; break;
                case "Blue Component": b = val; break;
            }
        }
        if (r is null || g is null || b is null) return false;
        color = Color.FromArgb(To255(r.Value), To255(g.Value), To255(b.Value));
        return true;
    }

    private static int To255(double v) => Math.Clamp((int)Math.Round(v * 255), 0, 255);

    private static Color Mix(Color a, Color b, double t) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * t),
        (int)(a.G + (b.G - a.G) * t),
        (int)(a.B + (b.B - a.B) * t));

    private static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}
