using ClaudeBarWin.Services;

namespace ClaudeBarWin.Tests;

/// <summary>
/// Cobertura del envoltorio de texto puro (<see cref="TextWrap.WordWrap"/>) que arregla el
/// truncamiento del sello de privacidad y el hint del footer (F2 → F3, Tarea 8). El algoritmo es
/// puro: la medida del ancho se inyecta como <c>Func&lt;string,double&gt;</c> (en producción es
/// <c>g.MeasureString</c>), así que el test es determinista sin GDI+.
/// </summary>
public class TextWrapTests
{
    // Medidor sintético: 10 px por carácter. Determinista, sin fuentes ni GDI+.
    private static double Measure(string s) => s.Length * 10.0;

    [Fact]
    public void Single_short_line_is_not_wrapped()
    {
        var lines = TextWrap.WordWrap("hola mundo", maxWidth: 500, Measure);
        Assert.Single(lines);
        Assert.Equal("hola mundo", lines[0]);
    }

    [Fact]
    public void Wraps_on_word_boundary_when_it_overflows()
    {
        // "uno dos tres" mide 120 px; con 80 px de ancho cabe "uno dos" (70) pero no "tres".
        var lines = TextWrap.WordWrap("uno dos tres", maxWidth: 80, Measure);
        Assert.Equal(2, lines.Count);
        Assert.Equal("uno dos", lines[0]);
        Assert.Equal("tres", lines[1]);
        // Ninguna línea excede el ancho máximo.
        Assert.All(lines, l => Assert.True(Measure(l) <= 80));
    }

    [Fact]
    public void Reassembling_preserves_words_and_order()
    {
        var lines = TextWrap.WordWrap("alfa beta gamma delta", maxWidth: 100, Measure);
        Assert.Equal("alfa beta gamma delta", string.Join(" ", lines));
    }

    [Fact]
    public void A_single_word_longer_than_max_is_kept_on_its_own_line_not_dropped()
    {
        // Palabra de 120 px con ancho 80: no se puede partir por espacios → se emite igual (no se pierde).
        var lines = TextWrap.WordWrap("supercalifragilis", maxWidth: 80, Measure);
        Assert.Single(lines);
        Assert.Equal("supercalifragilis", lines[0]);
    }

    [Fact]
    public void Empty_or_whitespace_yields_a_single_empty_line()
    {
        Assert.Equal(new[] { "" }, TextWrap.WordWrap("", 100, Measure));
        Assert.Equal(new[] { "" }, TextWrap.WordWrap("   ", 100, Measure));
    }

    [Fact]
    public void Collapses_runs_of_whitespace_between_words()
    {
        // Espacios múltiples no generan palabras vacías ni líneas sueltas.
        var lines = TextWrap.WordWrap("uno   dos", maxWidth: 500, Measure);
        Assert.Single(lines);
        Assert.Equal("uno dos", lines[0]);
    }

    [Fact]
    public void Non_positive_width_keeps_each_word_on_its_own_line()
    {
        // Defensa: con ancho <= 0 no se entra en bucle infinito; cada palabra va a su línea.
        var lines = TextWrap.WordWrap("uno dos tres", maxWidth: 0, Measure);
        Assert.Equal(new[] { "uno", "dos", "tres" }, lines);
    }

    // ---------------- T3: elipsis medida (Ellipsize) ----------------

    [Fact]
    public void Ellipsize_returns_text_unchanged_when_it_fits()
    {
        // "hola" (40 px) cabe en 100 px → sin tocar.
        Assert.Equal("hola", TextWrap.Ellipsize("hola", maxWidth: 100, Measure));
    }

    [Fact]
    public void Ellipsize_clips_with_measured_ellipsis_when_too_wide()
    {
        // "Personalizada" (130 px) no cabe en 70 px → recorta y añade "…", resultado mide ≤ 70.
        var clipped = TextWrap.Ellipsize("Personalizada", maxWidth: 70, Measure);
        Assert.NotEqual("Personalizada", clipped);
        Assert.EndsWith("…", clipped);
        Assert.True(Measure(clipped) <= 70, $"el texto elidido debe caber en 70 px, midió {Measure(clipped)}");
    }

    [Fact]
    public void Ellipsize_never_exceeds_width_for_any_target()
    {
        // Invariante: para cualquier ancho objetivo, el resultado nunca excede ese ancho.
        for (int wpx = 0; wpx <= 200; wpx += 7)
        {
            var clipped = TextWrap.Ellipsize("abcdefghijklmnop", maxWidth: wpx, Measure);
            Assert.True(Measure(clipped) <= Math.Max(wpx, Measure("…")) || clipped == "…" || clipped.Length == 0,
                $"width {wpx}: '{clipped}' midió {Measure(clipped)}");
        }
    }

    [Fact]
    public void Ellipsize_when_only_ellipsis_fits_returns_just_ellipsis()
    {
        // Si ni un carácter + "…" cabe, devuelve solo "…" (10 px) cuando este cabe.
        var clipped = TextWrap.Ellipsize("abcdef", maxWidth: 12, Measure);
        Assert.Equal("…", clipped);
    }

    [Fact]
    public void Ellipsize_is_deterministic_same_input_same_output()
    {
        // Determinismo: misma entrada → misma salida (clave para medir==pintar).
        var a = TextWrap.Ellipsize("texto largo de prueba", maxWidth: 95, Measure);
        var b = TextWrap.Ellipsize("texto largo de prueba", maxWidth: 95, Measure);
        Assert.Equal(a, b);
    }

    // ---------------- T9a: " · " como punto de ruptura preferente (WordWrapSeparated) ----------------

    [Fact]
    public void WordWrapSeparated_keeps_separators_when_everything_fits()
    {
        // Si cabe en una línea, el texto sale INTACTO (separadores incluidos).
        var lines = TextWrap.WordWrapSeparated("Actualizado · hace 2 min · clic para ocultar", 1000, Measure);
        Assert.Equal(new[] { "Actualizado · hace 2 min · clic para ocultar" }, lines);
    }

    [Fact]
    public void WordWrapSeparated_breaks_at_separator_and_omits_it()
    {
        // §3 #10: el wrap por palabras dejaba líneas que EMPIEZAN por "· sin telemetría" (o terminan
        // en " ·"). El separador es punto de ruptura PREFERENTE: se rompe ahí y el "·" se omite
        // (ni cierra la línea anterior ni abre la nueva).
        var lines = TextWrap.WordWrapSeparated("Actualizado · hace 2 min · clic para ocultar", 300, Measure);
        Assert.Equal(new[] { "Actualizado · hace 2 min", "clic para ocultar" }, lines);
    }

    [Fact]
    public void WordWrapSeparated_never_starts_a_line_with_the_separator()
    {
        // Caso exacto de la auditoría: el sello ES a un ancho donde WordWrap rompía justo antes del
        // "·" → línea que empezaba por "· sin telemetría".
        const string seal = "Tus credenciales y datos no salen del equipo · sin telemetría";
        var old = TextWrap.WordWrap(seal, 445, Measure);
        Assert.Contains(old, l => l.StartsWith("·")); // el bug que motiva el método nuevo

        var lines = TextWrap.WordWrapSeparated(seal, 445, Measure);
        Assert.Equal(new[] { "Tus credenciales y datos no salen del equipo", "sin telemetría" }, lines);
    }

    [Fact]
    public void WordWrapSeparated_never_ends_a_line_with_the_separator()
    {
        // A un ancho donde el "·" SÍ cabría colgando al final de la línea (460 px), la ruptura
        // preferente lo omite igualmente: nada de líneas terminadas en " ·".
        const string seal = "Tus credenciales y datos no salen del equipo · sin telemetría";
        var lines = TextWrap.WordWrapSeparated(seal, 460, Measure);
        Assert.Equal(new[] { "Tus credenciales y datos no salen del equipo", "sin telemetría" }, lines);
    }

    [Fact]
    public void WordWrapSeparated_falls_back_to_word_wrap_inside_an_oversized_segment()
    {
        // Un segmento más ancho que el máximo cae al wrap por palabras; el siguiente segmento puede
        // reengancharse a la última línea si cabe CON su separador.
        var lines = TextWrap.WordWrapSeparated("uno dos tres · x", 80, Measure);
        Assert.Equal(new[] { "uno dos", "tres · x" }, lines);
    }

    [Fact]
    public void WordWrapSeparated_sweep_no_line_touches_a_separator_edge()
    {
        // Propiedad: a CUALQUIER ancho, ninguna línea empieza ni termina con el separador, y no se
        // pierde ninguna palabra (las no-separador se conservan en orden).
        const string seal = "Tus credenciales y datos no salen del equipo · sin telemetría";
        string[] words = seal.Split(' ').Where(w => w != "·").ToArray();
        for (int wpx = 50; wpx <= 700; wpx += 7)
        {
            var lines = TextWrap.WordWrapSeparated(seal, wpx, Measure);
            Assert.All(lines, l =>
            {
                Assert.False(l.StartsWith("·"), $"ancho {wpx}: línea empieza por separador: '{l}'");
                Assert.False(l.EndsWith("·"), $"ancho {wpx}: línea termina en separador: '{l}'");
            });
            var got = string.Join(" ", lines).Split(' ').Where(w => w != "·").ToArray();
            Assert.Equal(words, got);
        }
    }

    [Fact]
    public void WordWrapSeparated_empty_yields_a_single_empty_line()
    {
        Assert.Equal(new[] { "" }, TextWrap.WordWrapSeparated("", 100, Measure));
        Assert.Equal(new[] { "" }, TextWrap.WordWrapSeparated(" · ", 100, Measure));
    }

    // ---------------- T8: FitLine (línea con tope derecho, generaliza FitHeaderLine) ----------------

    [Fact]
    public void FitLine_returns_text_unchanged_when_it_fits()
    {
        // drawX=0, rightEdge=200, margin=0 → 200 px útiles; "hola mundo" mide 100 → intacto.
        Assert.Equal("hola mundo", TextWrap.FitLine("hola mundo", drawX: 0, rightEdge: 200, margin: 0, Measure));
    }

    [Fact]
    public void FitLine_elides_to_right_edge_minus_margin()
    {
        // drawX=100, rightEdge=300, margin=20 → 180 px útiles (18 chars); 30 chars (300 px) no caben.
        var shown = TextWrap.FitLine(new string('a', 30), drawX: 100, rightEdge: 300, margin: 20, Measure);
        Assert.EndsWith(TextWrap.Ellipsis, shown);
        Assert.True(Measure(shown) <= 180, $"el texto elidido debe caber en 180 px, midió {Measure(shown)}");
    }

    [Fact]
    public void FitLine_with_no_room_returns_empty()
    {
        // Tope a la IZQUIERDA del punto de dibujo (ancho útil negativo): cadena vacía, sin reventar.
        Assert.Equal(string.Empty, TextWrap.FitLine("texto", drawX: 300, rightEdge: 100, margin: 0, Measure));
    }
}
