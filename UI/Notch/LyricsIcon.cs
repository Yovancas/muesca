using ClaudeBarWin.Services;

namespace ClaudeBarWin.UI.Notch;

/// <summary>
/// Icono del botón de la letra del notch: «Apple Music Lyrics» de Icons8 (https://icons8.com; licencia gratuita con
/// atribución, ver THIRD-PARTY-NOTICES.md), un bocadillo con dos comillas. Va embebido tal cual en
/// <c>Resources/icons/lyrics.svg</c> y se lee UNA vez (perezoso) con <see cref="SvgPath"/>. Si el recurso faltara o no se
/// pudiera leer, <see cref="Segments"/> es <c>null</c> y el botón vuelve al texto «Letra».
/// </summary>
internal static class LyricsIcon
{
    private static readonly Lazy<(IReadOnlyList<SvgSegment> Segments, RectangleF ViewBox)?> Icon = new(Load);

    /// <summary>Tramos absolutos del icono (en <see cref="ViewBox"/>); <c>null</c> si no hay icono.</summary>
    public static IReadOnlyList<SvgSegment>? Segments => Icon.Value?.Segments;

    /// <summary>Caja de coordenadas del SVG (0 0 50 50 en el de Icons8).</summary>
    public static RectangleF ViewBox => Icon.Value?.ViewBox ?? RectangleF.Empty;

    /// <summary>El texto del SVG embebido; <c>null</c> si no está.</summary>
    internal static string? ReadSvg()
    {
        var asm = typeof(LyricsIcon).Assembly;
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("icons.lyrics.svg", StringComparison.OrdinalIgnoreCase));
        if (name is null) return null;
        using var stream = asm.GetManifestResourceStream(name);
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static (IReadOnlyList<SvgSegment>, RectangleF)? Load()
    {
        try
        {
            if (ReadSvg() is not { } svg || SvgPath.ReadIcon(svg) is not { } icon) return null;
            var segs = SvgPath.Parse(icon.Data);
            return segs.Count > 0 && icon.ViewBox.Width > 0 && icon.ViewBox.Height > 0 ? (segs, icon.ViewBox) : null;
        }
        catch (Exception ex) when (ex is FormatException or IOException)
        {
            AppLog.Warn($"notch: icono de la letra ilegible: {ex.Message}");
            return null;
        }
    }
}
