using ClaudeBarWin.Services;

namespace ClaudeBarWin.Tests;

/// <summary>
/// Cobertura del bloque footer PURO (<see cref="FooterLayout"/>) que arregla el truncamiento del sello
/// de privacidad (F2 → F3, Tarea 8). El nº de líneas depende del reloj (flag stale + texto relativo) y
/// del envoltorio del sello; al ser puro (medida y "ahora" inyectados) se testea sin GDI+/fuentes/reloj.
/// La <see cref="FooterLayout.Signature"/> es lo que el tick compara para re-layoutar y no recortar.
/// </summary>
public class FooterLayoutTests
{
    // Medidor sintético: 10 px por carácter. Determinista, sin fuentes ni GDI+.
    private static double Measure(string s) => s.Length * 10.0;
    private static readonly DateTime Now = new(2026, 6, 3, 1, 0, 0, DateTimeKind.Utc);

    private static AppSnapshot OkSnap(int ageMinutes = 5) => new()
    {
        LatestState = UsageFetchState.Ok,
        UsageAtUtc = Now.AddMinutes(-ageMinutes),
    };

    [Fact]
    public void Null_snapshot_yields_loading_and_seal_without_stale()
    {
        var lines = FooterLayout.Build(snap: null, new Strings(), stale: false, sticky: false,
            nowUtc: Now, maxWidth: 10_000, Measure);

        Assert.DoesNotContain(lines, l => l.Kind == FooterLayout.LineKind.Stale);
        Assert.Contains(lines, l => l.Kind == FooterLayout.LineKind.Footer);
        Assert.Contains(lines, l => l.Kind == FooterLayout.LineKind.Seal);
    }

    [Fact]
    public void Stale_flag_adds_exactly_one_stale_line_first()
    {
        var s = new Strings();
        var fresh = FooterLayout.Build(null, s, stale: false, sticky: false, Now, 10_000, Measure);
        var stale = FooterLayout.Build(null, s, stale: true, sticky: false, Now, 10_000, Measure);

        Assert.Equal(fresh.Count + 1, stale.Count);
        Assert.Equal(FooterLayout.LineKind.Stale, stale[0].Kind);
        Assert.Single(stale, l => l.Kind == FooterLayout.LineKind.Stale);
    }

    [Fact]
    public void Narrow_width_wraps_the_seal_instead_of_truncating()
    {
        // El sello EN ("Your credentials and data never leave this device") es largo:
        // a 150 px (15 chars) NO cabe en una línea → debe ENVOLVERSE a varias (antes se recortaba).
        var lines = FooterLayout.Build(OkSnap(), new Strings(), stale: false, sticky: false,
            nowUtc: Now, maxWidth: 150, Measure);

        var sealLines = lines.Where(l => l.Kind == FooterLayout.LineKind.Seal).ToList();
        Assert.True(sealLines.Count >= 2, "el sello debe envolverse a 2+ líneas a ancho estrecho");
        // Ninguna palabra del sello supera 150 px → ninguna línea desborda.
        Assert.All(sealLines, l => Assert.True(Measure(l.Text) <= 150));
    }

    [Fact]
    public void Signature_counts_match_the_actual_lines()
    {
        var lines = FooterLayout.Build(OkSnap(), new Strings(), stale: true, sticky: false,
            nowUtc: Now, maxWidth: 150, Measure);
        var sig = FooterLayout.Signature(lines);

        Assert.Equal(lines.Count(l => l.Kind == FooterLayout.LineKind.Stale), sig.Stale);
        Assert.Equal(lines.Count(l => l.Kind == FooterLayout.LineKind.Footer), sig.Footer);
        Assert.Equal(lines.Count(l => l.Kind == FooterLayout.LineKind.Seal), sig.Seal);
        Assert.Equal(1, sig.Stale); // exactamente un marcador stale
    }

    [Fact]
    public void Footer_and_seal_lines_never_start_or_end_with_the_separator()
    {
        // T9a (§3 #10): el footer ES envolvía dejando una línea que EMPEZABA por "· sin telemetría".
        // Con la ruptura preferente en " · ", a CUALQUIER ancho ninguna línea Footer/Seal empieza ni
        // termina con el separador (la línea Stale termina en "·" a propósito: es un prefijo).
        var es = Localization.Get("es");
        for (int wpx = 60; wpx <= 700; wpx += 10)
        {
            var lines = FooterLayout.Build(OkSnap(), es, stale: false, sticky: false,
                nowUtc: Now, maxWidth: wpx, Measure);
            foreach (var l in lines.Where(l => l.Kind != FooterLayout.LineKind.Stale))
            {
                Assert.False(l.Text.StartsWith("·"), $"ancho {wpx}: línea empieza por '·': '{l.Text}'");
                Assert.False(l.Text.EndsWith("·"), $"ancho {wpx}: línea termina en '·': '{l.Text}'");
            }
        }
    }

    [Fact]
    public void Signature_changes_on_fresh_to_stale_transition()
    {
        // Es justo la condición que dispara el Relayout del tick (red de seguridad del alto).
        var s = new Strings();
        var fresh = FooterLayout.Signature(FooterLayout.Build(OkSnap(), s, false, false, Now, 150, Measure));
        var stale = FooterLayout.Signature(FooterLayout.Build(OkSnap(), s, true, false, Now, 150, Measure));
        Assert.NotEqual(fresh, stale);
    }
}
