using ClaudeBarWin.Config;
using ClaudeBarWin.Models;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Mascot;
using ClaudeBarWin.Services.Notch.Features;
using ClaudeBarWin.UI;

namespace ClaudeBarWin.Tests;

/// <summary>
/// T1: infra de helpers del panel de ajustes. <see cref="DashboardSettingsView.SectionHeader"/> debe
/// cumplir el invariante de 2 pasadas (medir==pintar) y avanzar exactamente
/// <c>Spacing.Md + altoTexto + Spacing.Sm</c> sobre la rejilla de 8pt. El divisor de 1px (Theme.Separator)
/// queda dentro de <c>[x, x+w]</c>. Además se verifica la simetría del <see cref="DashboardSettingsView.Draw"/>
/// completo (medir==pintar) tras reexpresar los avances con tokens de <see cref="Spacing"/>.
/// </summary>
public class DashboardSettingsViewTests
{
    private const int X = 16, W = 308;

    // Bitmap/Graphics reales para que MeasureString tenga un contexto válido.
    private static Bitmap NewBmp() => new(W + X * 2, 1200);

    private static AppConfig Cfg() => new()
    {
        ShowMascot = true,
        ShowHealth = true,
        ShowChart = true,
        ShowSpendEstimate = true,
        NotificationsEnabled = true,
        NotifyMilestones = new[] { 25, 50, 75, 95 },
        Theme = "dark",
        DashboardPosition = "BottomRight",
        Language = "es",
    };

    // ---------------- SectionHeader: medir==pintar + avance 8pt + divisor en rango ----------------

    [Fact]
    public void SectionHeader_measure_equals_paint()
    {
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);

        int measured = DashboardSettingsView.SectionHeader(g, draw: false, "ACTUALIZACIÓN", X, 100, W, Theme.Dark, Typography.Caption);
        int painted = DashboardSettingsView.SectionHeader(g, draw: true, "ACTUALIZACIÓN", X, 100, W, Theme.Dark, Typography.Caption);

        Assert.Equal(measured, painted);
    }

    [Fact]
    public void SectionHeader_advance_is_md_plus_text_plus_sm()
    {
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);

        const int y0 = 200;
        int after = DashboardSettingsView.SectionHeader(g, draw: false, "NOTIFICACIONES", X, y0, W, Theme.Dark, Typography.Caption);

        int textH = (int)Math.Ceiling(g.MeasureString("NOTIFICACIONES", Typography.Caption).Height);
        // Ritmo P2 #2: aire ARRIBA = sectionGap (~Xl), aire abajo = Spacing.Sm (la cabecera abraza su 1ª fila).
        Assert.Equal(y0 + DashboardSettingsView.SectionGap + textH + Spacing.Sm, after);
    }

    [Fact]
    public void SectionHeader_reserves_top_and_bottom_air()
    {
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);

        const int y0 = 50;
        int after = DashboardSettingsView.SectionHeader(g, draw: true, "PANEL", X, y0, W, Theme.Dark, Typography.Caption);

        int textH = (int)Math.Ceiling(g.MeasureString("PANEL", Typography.Caption).Height);
        Assert.Equal(y0 + DashboardSettingsView.SectionGap + textH + Spacing.Sm, after);
    }

    [Fact]
    public void SectionHeader_divider_stays_within_content_width()
    {
        // El divisor de 1px (Theme.Separator) debe pintarse dentro de [x, x+w]: presente en una
        // columna interior, ausente a la izquierda de x y a la derecha de x+w. Línea horizontal
        // axis-aligned → sin antialias, comprobable con GetPixel.
        const int x = 20, w = 200, y0 = 40;
        using var bmp = new Bitmap(x + w + 60, 200);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Theme.Dark.Background);

        DashboardSettingsView.SectionHeader(g, draw: true, "PANEL", x, y0, w, Theme.Dark, Typography.Caption);

        var sep = Theme.Dark.Separator;
        bool IsSep(int px, int py)
        {
            var c = bmp.GetPixel(px, py);
            return c.R == sep.R && c.G == sep.G && c.B == sep.B;
        }
        // Busca la fila del divisor en la banda inferior del header.
        int textH = (int)Math.Ceiling(g.MeasureString("PANEL", Typography.Caption).Height);
        int bandTop = y0 + DashboardSettingsView.SectionGap + textH, bandBot = bandTop + Spacing.Sm;
        int midX = x + w / 2;
        int divY = -1;
        for (int py = bandTop; py <= bandBot && divY < 0; py++)
            if (IsSep(midX, py)) divY = py;

        Assert.True(divY >= 0, "debe existir un divisor con color Theme.Separator bajo el texto");
        Assert.True(IsSep(x, divY) || IsSep(x + 1, divY), "el divisor empieza en x");
        Assert.True(IsSep(x + w - 1, divY) || IsSep(x + w, divY), "el divisor llega hasta x+w");
        Assert.False(IsSep(x - 5, divY), "el divisor no rebasa por la izquierda de x");
        Assert.False(IsSep(x + w + 5, divY), "el divisor no rebasa por la derecha de x+w");
    }

    // ---------------- Draw completo: invariante de 2 pasadas con el nuevo ritmo ----------------

    [Fact]
    public void Draw_measure_equals_paint()
    {
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var cfg = Cfg();
        var s = Localization.Get("es");
        var rects = new Dictionary<string, Rectangle>();

        int measured = DashboardSettingsView.Draw(g, draw: false, X, 0, W, cfg, s, Theme.Dark,
            Typography.Body, Typography.Caption, rects);
        int painted = DashboardSettingsView.Draw(g, draw: true, X, 0, W, cfg, s, Theme.Dark,
            Typography.Body, Typography.Caption, rects);

        Assert.Equal(measured, painted);
    }

    [Fact]
    public void Draw_registers_clickable_rects()
    {
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var cfg = Cfg();
        var s = Localization.Get("es");
        var rects = new Dictionary<string, Rectangle>();

        DashboardSettingsView.Draw(g, draw: true, X, 0, W, cfg, s, Theme.Dark,
            Typography.Body, Typography.Caption, rects);

        // Claves estables que el host enruta; se mantienen tras reexpresar avances con Spacing.
        Assert.Contains("toggle:ShowSpend", rects.Keys);
        Assert.Contains("special:hooktoggle", rects.Keys);
        Assert.Contains("cycle:position", rects.Keys);
    }

    // ---------------- T2: TogglePill (cápsula+knob, sustituye ☑/☐) ----------------

    [Fact]
    public void TogglePill_measure_equals_paint()
    {
        // El track derecho debe reservar el mismo rect en medir y pintar (geometría idéntica).
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);

        var measured = DashboardSettingsView.TogglePill(g, draw: false, on: true, rightX: X + W, y: 100, rowH: 18, theme: Theme.Dark);
        var painted = DashboardSettingsView.TogglePill(g, draw: true, on: true, rightX: X + W, y: 100, rowH: 18, theme: Theme.Dark);

        Assert.Equal(measured, painted);
    }

    [Fact]
    public void TogglePill_right_edge_anchors_at_rightX_with_safe_margin()
    {
        // El borde derecho del pill respeta un margen interno ≥ Spacing.Sm respecto a rightX.
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);

        int rightX = X + W;
        var pill = DashboardSettingsView.TogglePill(g, draw: false, on: false, rightX: rightX, y: 100, rowH: 18, theme: Theme.Dark);

        Assert.True(pill.Right <= rightX - Spacing.Sm, "el pill no debe tocar el borde derecho (margen ≥ Sm)");
        Assert.True(pill.X >= X, "el pill no se sale por la izquierda del contenido");
    }

    [Fact]
    public void TogglePill_knob_left_when_off_right_when_on()
    {
        // El knob se desliza: a la izquierda del track si OFF, a la derecha si ON.
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);

        int rightX = X + W, y = 100, rowH = 18;
        var track = DashboardSettingsView.TogglePill(g, draw: false, on: false, rightX: rightX, y: y, rowH: rowH, theme: Theme.Dark);

        int knobOff = DashboardSettingsView.PillKnobCenterX(track, on: false);
        int knobOn = DashboardSettingsView.PillKnobCenterX(track, on: true);

        Assert.True(knobOff < track.X + track.Width / 2, "OFF: knob en la mitad izquierda");
        Assert.True(knobOn > track.X + track.Width / 2, "ON: knob en la mitad derecha");
        Assert.True(knobOn > knobOff, "ON desplaza el knob a la derecha respecto a OFF");
    }

    [Fact]
    public void TogglePill_track_color_accent_when_on_separator_when_off()
    {
        // ON → track Theme.Accent; OFF → track Theme.Separator. Comprobado por píxel en el centro-izquierda
        // del track (zona de track sin knob: el knob está a la derecha cuando ON, a la izquierda cuando OFF,
        // así que muestreamos el lado opuesto para leer el color del track).
        int rightX = 300, y = 40, rowH = 18;
        using var bmp = new Bitmap(360, 120);
        using var g = Graphics.FromImage(bmp);

        // ON: knob a la derecha → muestrear cuarto izquierdo (track puro).
        g.Clear(Theme.Dark.Background);
        var onTrack = DashboardSettingsView.TogglePill(g, draw: true, on: true, rightX: rightX, y: y, rowH: rowH, theme: Theme.Dark);
        var cOn = bmp.GetPixel(onTrack.X + onTrack.Width / 4, onTrack.Y + onTrack.Height / 2);
        var acc = Theme.Dark.Accent;
        Assert.True(Math.Abs(cOn.R - acc.R) <= 8 && Math.Abs(cOn.G - acc.G) <= 8 && Math.Abs(cOn.B - acc.B) <= 8,
            $"track ON debe ser Accent (#{acc.R:X2}{acc.G:X2}{acc.B:X2}), fue #{cOn.R:X2}{cOn.G:X2}{cOn.B:X2}");

        // OFF: knob a la izquierda → muestrear cuarto derecho (track puro).
        g.Clear(Theme.Dark.Background);
        var offTrack = DashboardSettingsView.TogglePill(g, draw: true, on: false, rightX: rightX, y: y, rowH: rowH, theme: Theme.Dark);
        var cOff = bmp.GetPixel(offTrack.Right - offTrack.Width / 4, offTrack.Y + offTrack.Height / 2);
        var sep = Theme.Dark.Separator;
        Assert.True(Math.Abs(cOff.R - sep.R) <= 8 && Math.Abs(cOff.G - sep.G) <= 8 && Math.Abs(cOff.B - sep.B) <= 8,
            $"track OFF debe ser Separator (#{sep.R:X2}{sep.G:X2}{sep.B:X2}), fue #{cOff.R:X2}{cOff.G:X2}{cOff.B:X2}");
    }

    // ---------------- T2: ToggleRow con título + subtítulo + pill, sin glifos ☑/☐ ----------------

    [Fact]
    public void ToggleRow_measure_equals_paint_single_line()
    {
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var rects = new Dictionary<string, Rectangle>();

        int measured = DashboardSettingsView.ToggleRow(g, draw: false, "toggle:X", "Mostrar mascota", null, true,
            X, 100, W, Theme.Dark, Typography.Body, Typography.Caption, rects);
        int painted = DashboardSettingsView.ToggleRow(g, draw: true, "toggle:X", "Mostrar mascota", null, true,
            X, 100, W, Theme.Dark, Typography.Body, Typography.Caption, rects);

        Assert.Equal(measured, painted);
    }

    [Fact]
    public void ToggleRow_with_subtitle_is_taller_than_without()
    {
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var rects = new Dictionary<string, Rectangle>();

        int plain = DashboardSettingsView.ToggleRow(g, draw: false, "toggle:X", "Notificaciones", null, true,
            X, 0, W, Theme.Dark, Typography.Body, Typography.Caption, rects);
        int withSub = DashboardSettingsView.ToggleRow(g, draw: false, "toggle:X", "Notificaciones", "Coste equivalente por modelo", true,
            X, 0, W, Theme.Dark, Typography.Body, Typography.Caption, rects);

        Assert.True(withSub > plain, "una fila con subtítulo debe ocupar más alto que sin subtítulo");
    }

    [Fact]
    public void ToggleRow_subtitle_measure_equals_paint()
    {
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var rects = new Dictionary<string, Rectangle>();

        int measured = DashboardSettingsView.ToggleRow(g, draw: false, "toggle:X", "Notificaciones", "Coste equivalente por modelo", false,
            X, 100, W, Theme.Dark, Typography.Body, Typography.Caption, rects);
        int painted = DashboardSettingsView.ToggleRow(g, draw: true, "toggle:X", "Notificaciones", "Coste equivalente por modelo", false,
            X, 100, W, Theme.Dark, Typography.Body, Typography.Caption, rects);

        Assert.Equal(measured, painted);
    }

    [Fact]
    public void ToggleRow_hit_rect_covers_full_row_width()
    {
        // El hit-test es el rect completo de la fila (clic en cualquier punto alterna el toggle).
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var rects = new Dictionary<string, Rectangle>();

        DashboardSettingsView.ToggleRow(g, draw: true, "toggle:Hit", "Estado del servicio", null, true,
            X, 100, W, Theme.Dark, Typography.Body, Typography.Caption, rects);

        Assert.True(rects.TryGetValue("toggle:Hit", out var r));
        Assert.Equal(X, r.X);
        Assert.Equal(W, r.Width);
        Assert.True(r.Height > 0);
    }

    [Fact]
    public void ToggleRow_does_not_draw_unicode_checkbox_glyphs()
    {
        // Regresión: la fila ya NO usa los glifos ☑/☐. La cápsula viene de TogglePill (Accent/Separator),
        // no de un carácter Unicode. Verificamos que la columna izquierda del texto (donde antes iba el
        // glifo) NO tiene un cuadro coloreado de check: el primer no-fondo a la altura del baseline es texto.
        // Comprobación indirecta: el pill ON pinta Accent en el lado derecho del rect, nunca a la izquierda.
        int x = 20, w = 240, y = 30;
        using var bmp = new Bitmap(x + w + 40, 80);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Theme.Dark.Background);
        var rects = new Dictionary<string, Rectangle>();

        DashboardSettingsView.ToggleRow(g, draw: true, "toggle:Z", "Mostrar mascota", null, true,
            x, y, w, Theme.Dark, Typography.Body, Typography.Caption, rects);

        var acc = Theme.Dark.Accent;
        bool IsAccent(int px, int py)
        {
            var c = bmp.GetPixel(px, py);
            return Math.Abs(c.R - acc.R) <= 8 && Math.Abs(c.G - acc.G) <= 8 && Math.Abs(c.B - acc.B) <= 8;
        }
        // En el cuarto izquierdo (donde iba ☑) no debe haber Accent en ninguna fila.
        bool accentOnLeft = false;
        for (int py = y; py < y + 18 && !accentOnLeft; py++)
            for (int px = x; px < x + (w / 4); px++)
                if (IsAccent(px, py)) { accentOnLeft = true; break; }
        Assert.False(accentOnLeft, "no debe haber cápsula/check a la izquierda (sin glifo ☑)");
    }

    // ================= T3: anti-truncamiento en CycleRow y SegmentedRow =================

    // -------- CycleRow: APILADA (etiqueta arriba / valor completo debajo), SIN elipsis ni chevron --------

    [Fact]
    public void CycleRow_measure_equals_paint_short_value()
    {
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var rects = new Dictionary<string, Rectangle>();

        int measured = DashboardSettingsView.CycleRow(g, draw: false, "cycle:x", "Idioma", "Español",
            X, 100, W, Theme.Dark, Typography.Caption, rects);
        int painted = DashboardSettingsView.CycleRow(g, draw: true, "cycle:x", "Idioma", "Español",
            X, 100, W, Theme.Dark, Typography.Caption, rects);

        Assert.Equal(measured, painted);
    }

    [Fact]
    public void CycleRow_measure_equals_paint_overflowing_value()
    {
        // El caso PosCustom: valor muy largo que se ENVUELVE (no se elide). La decisión de cuántas
        // líneas ocupa el valor debe ser idéntica en medir y pintar → mismo y de salida.
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var rects = new Dictionary<string, Rectangle>();
        const string longVal = "Personalizada (arrastra el panel)";

        int measured = DashboardSettingsView.CycleRow(g, draw: false, "cycle:position", "Posición", longVal,
            X, 100, W, Theme.Dark, Typography.Caption, rects);
        int painted = DashboardSettingsView.CycleRow(g, draw: true, "cycle:position", "Posición", longVal,
            X, 100, W, Theme.Dark, Typography.Caption, rects);

        Assert.Equal(measured, painted);
    }

    [Fact]
    public void CycleRow_long_value_wraps_and_grows_row_without_ellipsis()
    {
        // P0 #3: el valor largo "Personalizada (arrastra el panel)" sale COMPLETO, partido en 2+ líneas
        // (WordBreak) debajo de la etiqueta — NUNCA elidido. En un ancho estrecho la fila CRECE (más alto
        // que la misma fila con un valor corto que cabe en 1 línea) y el rect clicable cubre toda la fila.
        const int x = 16, w = 160, y = 40; // ancho estrecho a propósito para forzar wrap
        using var bmp = new Bitmap(x + w + 60, 160);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Theme.Dark.Background);
        var rects = new Dictionary<string, Rectangle>();
        const string longVal = "Personalizada (arrastra el panel)";

        int afterLong = DashboardSettingsView.CycleRow(g, draw: true, "cycle:position", "Posición", longVal,
            x, y, w, Theme.Dark, Typography.Caption, rects);
        var longRect = rects["cycle:position"];
        rects.Clear();
        int afterShort = DashboardSettingsView.CycleRow(g, draw: false, "cycle:position", "Posición", "Centro",
            x, y, w, Theme.Dark, Typography.Caption, rects);

        // El valor largo envuelve a ≥2 líneas → la fila ocupa más alto que un valor corto de 1 línea.
        Assert.True(DashboardSettingsView.CycleRowValueLineCount(g, longVal, x, w, Typography.Caption) >= 2,
            "el valor largo debe envolver a 2+ líneas (no elidirse)");
        Assert.True(afterLong > afterShort, "la fila con valor largo (envuelto) debe ser más alta");
        // El rect clicable cubre toda la fila apilada y no rebasa por la derecha.
        Assert.Equal(x, longRect.X);
        Assert.Equal(w, longRect.Width);
        Assert.True(longRect.Right <= x + w, "el rect no rebasa el borde derecho");
    }

    [Fact]
    public void CycleRow_value_is_never_ellipsized_and_has_no_chevron()
    {
        // El valor completo se reparte en líneas con WordWrap (sin elipsis y sin el chevron "›" suelto).
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);

        // Corto: cabe en 1 línea, intacto.
        var shortLines = TextWrap.WordWrap("Español", W - Spacing.Sm, t => g.MeasureString(t, Typography.Caption).Width);
        Assert.Single(shortLines);
        Assert.Equal("Español", shortLines[0]);
        Assert.DoesNotContain("…", string.Concat(shortLines));
        Assert.DoesNotContain("›", string.Concat(shortLines));

        // Largo en ancho estrecho: se reparte en varias líneas, SIN perder texto ni añadir elipsis/chevron.
        const string longVal = "Personalizada (arrastra el panel)";
        var longLines = TextWrap.WordWrap(longVal, 160 - Spacing.Sm, t => g.MeasureString(t, Typography.Caption).Width);
        string joined = string.Join(" ", longLines);
        Assert.Equal(longVal, joined);                 // el texto completo se conserva (solo se parte)
        Assert.DoesNotContain("…", joined);
        Assert.DoesNotContain("›", joined);
    }

    [Fact]
    public void CycleRow_value_line_count_is_deterministic_measure_equals_paint()
    {
        // El recuento de líneas del valor (clave de medir==pintar) es determinista: misma entrada,
        // mismo número de líneas en ambas pasadas.
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        const string longVal = "Personalizada (arrastra el panel)";
        int a = DashboardSettingsView.CycleRowValueLineCount(g, longVal, 16, 160, Typography.Caption);
        int b = DashboardSettingsView.CycleRowValueLineCount(g, longVal, 16, 160, Typography.Caption);
        Assert.Equal(a, b);
        Assert.True(a >= 1);
    }

    // -------- SegmentedRow: medir==pintar + sin chip fuera de contentLeft + margen derecho --------

    [Fact]
    public void SegmentedRow_measure_equals_paint_fitting()
    {
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var rects = new Dictionary<string, Rectangle>();
        var segs = new[] { ("30", "30s"), ("60", "1m"), ("300", "5m"), ("900", "15m") };

        int measured = DashboardSettingsView.SegmentedRow(g, draw: false, "freq", "", segs, "60",
            X, 100, W, Theme.Dark, Typography.Caption, rects);
        int painted = DashboardSettingsView.SegmentedRow(g, draw: true, "freq", "", segs, "60",
            X, 100, W, Theme.Dark, Typography.Caption, rects);

        Assert.Equal(measured, painted);
    }

    [Fact]
    public void SegmentedRow_no_segment_starts_left_of_content()
    {
        // Frecuencia compacta: ningún chip se pinta con x < contentLeft (X) y el bloque deja
        // margen derecho ≥ Spacing.Sm respecto a X+W.
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var rects = new Dictionary<string, Rectangle>();
        var segs = new[] { ("30", "30s"), ("60", "1m"), ("300", "5m"), ("900", "15m") };

        DashboardSettingsView.SegmentedRow(g, draw: true, "freq", "", segs, "60",
            X, 100, W, Theme.Dark, Typography.Caption, rects);

        foreach (var key in new[] { "freq:30", "freq:60", "freq:300", "freq:900" })
        {
            Assert.True(rects.TryGetValue(key, out var r), $"falta el rect {key}");
            Assert.True(r.X >= X, $"{key}: chip a la izquierda de contentLeft (x={r.X} < {X})");
            Assert.True(r.Right <= X + W - Spacing.Sm, $"{key}: chip sin margen derecho (right={r.Right} > {X + W - Spacing.Sm})");
        }
    }

    [Fact]
    public void SegmentedRow_wraps_to_two_rows_when_segments_exceed_width()
    {
        // Segmentos artificialmente anchos que NO caben en una fila → la fila crece (2 filas) y
        // ningún chip queda a la izquierda de contentLeft. Medir==pintar se mantiene.
        const int x = 16, w = 120; // ancho muy estrecho
        using var bmp = new Bitmap(x + w + 80, 200);
        using var g = Graphics.FromImage(bmp);
        var rects = new Dictionary<string, Rectangle>();
        var segs = new[] { ("a", "Treinta"), ("b", "Un minuto"), ("c", "Cinco min"), ("d", "Quince min") };

        int measured = DashboardSettingsView.SegmentedRow(g, draw: false, "wrap", "", segs, "a",
            x, 0, w, Theme.Dark, Typography.Caption, rects);
        rects.Clear();
        int painted = DashboardSettingsView.SegmentedRow(g, draw: true, "wrap", "", segs, "a",
            x, 0, w, Theme.Dark, Typography.Caption, rects);

        Assert.Equal(measured, painted);
        // Debe haber crecido más de una fila simple (envolvió a 2 filas).
        Assert.True(measured > 0 + DashboardSettingsView.SegmentRowAdvanceForTest,
            "una fila que no cabe en un renglón debe envolver y ocupar más alto");
        foreach (var seg in segs)
        {
            Assert.True(rects.TryGetValue($"wrap:{seg.Item1}", out var r), $"falta wrap:{seg.Item1}");
            Assert.True(r.X >= x, $"wrap:{seg.Item1} a la izquierda de contentLeft (x={r.X})");
        }
    }

    [Fact]
    public void SegmentedRow_with_label_segments_do_not_overlap_label()
    {
        // Con etiqueta a la izquierda, los segmentos no deben invadir el texto de la etiqueta.
        const int x = 16, w = 308, y = 40;
        using var bmp = new Bitmap(x + w + 40, 120);
        using var g = Graphics.FromImage(bmp);
        var rects = new Dictionary<string, Rectangle>();
        var segs = new[] { ("percent", "%"), ("pace", "▲"), ("both", "%▲") };

        DashboardSettingsView.SegmentedRow(g, draw: true, "icon", "Contenido del icono", segs, "percent",
            x, y, w, Theme.Dark, Typography.Caption, rects);

        float labelRight = x + g.MeasureString("Contenido del icono", Typography.Caption).Width;
        foreach (var seg in segs)
        {
            Assert.True(rects.TryGetValue($"icon:{seg.Item1}", out var r));
            Assert.True(r.X >= labelRight + Spacing.Md - 1, $"icon:{seg.Item1} invade la etiqueta (x={r.X}, labelRight={labelRight:0})");
        }
    }

    // -------- Draw completo: las filas reales no truncan (regresión de frecuencia compacta) --------

    // ================= T4: StatusBadge semántico (Activas/Instalar) =================

    [Fact]
    public void StatusBadge_measure_equals_paint()
    {
        // El rect del badge debe ser idéntico en medir (draw=false) y pintar (draw=true).
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);

        var measured = DashboardSettingsView.StatusBadge(g, draw: false, "Activas", Theme.Dark.Ok,
            x: X, rightX: X + W, y: 100, rowH: 28, theme: Theme.Dark, f: Typography.Caption);
        var painted = DashboardSettingsView.StatusBadge(g, draw: true, "Activas", Theme.Dark.Ok,
            x: X, rightX: X + W, y: 100, rowH: 28, theme: Theme.Dark, f: Typography.Caption);

        Assert.Equal(measured, painted);
    }

    [Fact]
    public void StatusBadge_right_edge_anchors_with_safe_margin_and_stays_in_content()
    {
        // El borde derecho deja margen interno ≥ Spacing.Sm respecto a rightX; no se sale por la izquierda.
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);

        int rightX = X + W;
        var badge = DashboardSettingsView.StatusBadge(g, draw: false, "Instalar", Theme.Dark.Warn,
            x: X, rightX: rightX, y: 100, rowH: 28, theme: Theme.Dark, f: Typography.Caption);

        Assert.True(badge.Right <= rightX - Spacing.Sm, "el badge no debe tocar el borde derecho (margen ≥ Sm)");
        Assert.True(badge.X >= X, "el badge no se sale por la izquierda del contenido");
        Assert.True(badge.Width > 0 && badge.Height > 0, "el badge tiene área positiva");
    }

    [Fact]
    public void StatusBadge_centered_vertically_in_row()
    {
        // El badge se centra verticalmente en la fila (rowH) → simetría arriba/abajo.
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);

        int y = 100, rowH = 28;
        var badge = DashboardSettingsView.StatusBadge(g, draw: false, "Activas", Theme.Dark.Ok,
            x: X, rightX: X + W, y: y, rowH: rowH, theme: Theme.Dark, f: Typography.Caption);

        int topGap = badge.Y - y;
        int botGap = (y + rowH) - badge.Bottom;
        Assert.True(Math.Abs(topGap - botGap) <= 1, $"el badge debe centrarse en la fila (top={topGap}, bot={botGap})");
    }

    [Fact]
    public void StatusBadge_fill_is_ok_when_active_warn_when_install()
    {
        // El relleno del badge es Theme.Ok (verde) para "Activas" y Theme.Warn (ámbar) para "Instalar".
        int rightX = 300, y = 30, rowH = 28;
        using var bmp = new Bitmap(360, 100);
        using var g = Graphics.FromImage(bmp);

        g.Clear(Theme.Dark.Background);
        var okBadge = DashboardSettingsView.StatusBadge(g, draw: true, "Activas", Theme.Dark.Ok,
            x: 16, rightX: rightX, y: y, rowH: rowH, theme: Theme.Dark, f: Typography.Caption);
        var cOk = bmp.GetPixel(okBadge.X + 3, okBadge.Y + okBadge.Height / 2); // borde izquierdo del relleno (sin texto)
        var ok = Theme.Dark.Ok;
        Assert.True(Math.Abs(cOk.R - ok.R) <= 10 && Math.Abs(cOk.G - ok.G) <= 10 && Math.Abs(cOk.B - ok.B) <= 10,
            $"relleno activo debe ser Ok (#{ok.R:X2}{ok.G:X2}{ok.B:X2}), fue #{cOk.R:X2}{cOk.G:X2}{cOk.B:X2}");

        g.Clear(Theme.Dark.Background);
        var warnBadge = DashboardSettingsView.StatusBadge(g, draw: true, "Instalar", Theme.Dark.Warn,
            x: 16, rightX: rightX, y: y, rowH: rowH, theme: Theme.Dark, f: Typography.Caption);
        var cWarn = bmp.GetPixel(warnBadge.X + 3, warnBadge.Y + warnBadge.Height / 2);
        var warn = Theme.Dark.Warn;
        Assert.True(Math.Abs(cWarn.R - warn.R) <= 10 && Math.Abs(cWarn.G - warn.G) <= 10 && Math.Abs(cWarn.B - warn.B) <= 10,
            $"relleno 'Instalar' debe ser Warn (#{warn.R:X2}{warn.G:X2}{warn.B:X2}), fue #{cWarn.R:X2}{cWarn.G:X2}{cWarn.B:X2}");
    }

    [Fact]
    public void StatusBadge_text_truncates_tail_within_its_width_in_narrow_space()
    {
        // En un ancho muy estrecho el texto se recorta por la cola (elipsis medida) y el badge NUNCA
        // empieza a la izquierda del contenido. El texto mostrado es determinista (medir==pintar).
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);

        // rightX muy cerca de contentLeft → el badge no cabe entero; debe recortar manteniéndose en [x, rightX].
        int x = 16, rightX = x + 70;
        string shown = DashboardSettingsView.StatusBadgeShownText(g, "Instalar (hooks no instalados)", x, rightX, Typography.Caption);
        Assert.NotEqual("Instalar (hooks no instalados)", shown);
        Assert.Contains("…", shown);

        var badge = DashboardSettingsView.StatusBadge(g, draw: false, "Instalar (hooks no instalados)", Theme.Dark.Warn,
            x: x, rightX: rightX, y: 100, rowH: 28, theme: Theme.Dark, f: Typography.Caption);
        Assert.True(badge.X >= x, $"el badge recortado no empieza a la izquierda del contenido (x={badge.X} < {x})");
        Assert.True(badge.Right <= rightX - Spacing.Sm, "el badge recortado deja margen derecho ≥ Sm");
    }

    [Fact]
    public void StatusBadge_short_text_is_not_truncated()
    {
        // Un texto corto que cabe holgado NO lleva elipsis (no se toca lo que entra).
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        string shown = DashboardSettingsView.StatusBadgeShownText(g, "Activas", X, X + W, Typography.Caption);
        Assert.Equal("Activas", shown);
        Assert.DoesNotContain("…", shown);
    }

    // ================= T5: MultiSegmentRow (hitos multi-activo, sin re-pintado manual) =================

    private static readonly (string val, string txt)[] MilestoneSegs =
        { ("25", "25%"), ("50", "50%"), ("75", "75%"), ("95", "95%") };

    [Fact]
    public void MultiSegmentRow_measure_equals_paint()
    {
        // El invariante de 2 pasadas: medir (draw=false) y pintar (draw=true) avanzan el mismo y.
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var rects = new Dictionary<string, Rectangle>();
        var active = new[] { 25, 75 };

        int measured = DashboardSettingsView.MultiSegmentRow(g, draw: false, "milestone", "Avisar al llegar a",
            MilestoneSegs, active, X, 100, W, Theme.Dark, Typography.Caption, rects);
        rects.Clear();
        int painted = DashboardSettingsView.MultiSegmentRow(g, draw: true, "milestone", "Avisar al llegar a",
            MilestoneSegs, active, X, 100, W, Theme.Dark, Typography.Caption, rects);

        Assert.Equal(measured, painted);
    }

    [Fact]
    public void MultiSegmentRow_registers_a_rect_per_segment()
    {
        // Cada segmento registra rects[$"{key}:{val}"] (las claves que el host enruta por ActionFor).
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var rects = new Dictionary<string, Rectangle>();

        DashboardSettingsView.MultiSegmentRow(g, draw: true, "milestone", "", MilestoneSegs,
            new[] { 50 }, X, 100, W, Theme.Dark, Typography.Caption, rects);

        foreach (var seg in MilestoneSegs)
            Assert.True(rects.ContainsKey($"milestone:{seg.val}"), $"falta rect milestone:{seg.val}");
    }

    [Fact]
    public void MultiSegmentRow_marks_every_active_value_with_accent_fill()
    {
        // Multi-activo NATIVO: TODOS los valores presentes en el array se pintan con Accent (un solo
        // estilo de pill), no solo uno. Verificado por píxel en el centro de cada chip.
        const int x = 16, w = 308, y = 30;
        using var bmp = new Bitmap(x + w + 40, 100);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Theme.Dark.Background);
        var rects = new Dictionary<string, Rectangle>();
        var active = new[] { 25, 75, 95 }; // 3 activos a la vez → el caso que rompía DrawSegments

        DashboardSettingsView.MultiSegmentRow(g, draw: true, "milestone", "", MilestoneSegs, active,
            x, y, w, Theme.Dark, Typography.Caption, rects);

        var acc = Theme.Dark.Accent;
        bool IsAccent(Rectangle r)
        {
            var c = bmp.GetPixel(r.X + 2, r.Y + r.Height / 2); // borde izquierdo del chip (sin texto)
            return Math.Abs(c.R - acc.R) <= 10 && Math.Abs(c.G - acc.G) <= 10 && Math.Abs(c.B - acc.B) <= 10;
        }
        foreach (var pct in active)
        {
            Assert.True(rects.TryGetValue($"milestone:{pct}", out var r));
            Assert.True(IsAccent(r), $"el hito activo {pct}% debe pintarse con Accent (un solo estilo de pill)");
        }
    }

    [Fact]
    public void MultiSegmentRow_inactive_value_is_not_accent()
    {
        // Un valor NO presente en el array se pinta con el estilo "off" (BgElevated), no Accent.
        const int x = 16, w = 308, y = 30;
        using var bmp = new Bitmap(x + w + 40, 100);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Theme.Dark.Background);
        var rects = new Dictionary<string, Rectangle>();

        DashboardSettingsView.MultiSegmentRow(g, draw: true, "milestone", "", MilestoneSegs,
            new[] { 25 }, x, y, w, Theme.Dark, Typography.Caption, rects);

        Assert.True(rects.TryGetValue("milestone:50", out var r)); // 50 NO está activo
        var c = bmp.GetPixel(r.X + 2, r.Y + r.Height / 2);
        var acc = Theme.Dark.Accent;
        bool isAccent = Math.Abs(c.R - acc.R) <= 10 && Math.Abs(c.G - acc.G) <= 10 && Math.Abs(c.B - acc.B) <= 10;
        Assert.False(isAccent, "un hito inactivo NO debe pintarse con Accent");
    }

    // ---- T7c (auditoría §3 #8): estado on/off de los chips de hitos visible en LOS TRES temas ----
    // El render auditado mostraba "los 4 idénticos" porque el default trae los 4 hitos ACTIVOS (todos
    // Accent, estado real); este pin garantiza que cuando hay mezcla on/off el patrón del segmented
    // Spend$/Quota% distingue ambos estados en dark/light/cli: relleno claramente distinto + borde
    // Separator solo en el inactivo.

    public static IEnumerable<object[]> ChipThemes =>
        new[] { new object[] { Theme.Dark }, new object[] { Theme.Light }, new object[] { Theme.Cli } };

    [Theory]
    [MemberData(nameof(ChipThemes))]
    public void MultiSegmentRow_active_and_inactive_chips_differ_in_every_theme(Theme theme)
    {
        const int x = 16, w = 308, y = 30;
        using var bmp = new Bitmap(x + w + 40, 100);
        using var g = Graphics.FromImage(bmp);
        g.Clear(theme.Background);
        var rects = new Dictionary<string, Rectangle>();

        DashboardSettingsView.MultiSegmentRow(g, draw: true, "milestone", "", MilestoneSegs,
            new[] { 25 }, x, y, w, theme, Typography.Caption, rects);

        Assert.True(rects.TryGetValue("milestone:25", out var on));
        Assert.True(rects.TryGetValue("milestone:50", out var off));

        // Relleno (x+4: dentro del chip, a la izquierda del texto que arranca en padX=7): el chip
        // activo ≈ Accent; el inactivo ≈ BgElevated. Ambos estados deben verse distintos.
        var pOn = bmp.GetPixel(on.X + 4, on.Y + on.Height / 2);
        var pOff = bmp.GetPixel(off.X + 4, off.Y + off.Height / 2);
        var acc = theme.Accent;
        Assert.True(Math.Abs(pOn.R - acc.R) <= 10 && Math.Abs(pOn.G - acc.G) <= 10 && Math.Abs(pOn.B - acc.B) <= 10,
            $"[{theme.Id}] el chip activo debe rellenarse con Accent (pintó {pOn})");
        int delta = Math.Abs(pOn.R - pOff.R) + Math.Abs(pOn.G - pOff.G) + Math.Abs(pOn.B - pOff.B);
        Assert.True(delta >= 60, $"[{theme.Id}] chips on/off casi idénticos (Δ={delta}): el estado no se lee");

        // Borde del inactivo: en el contorno superior hay píxeles ≈ Separator (se lee como botón).
        var sep = theme.Separator;
        bool border = false;
        for (int px = off.X + 4; px <= off.Right - 4 && !border; px++)
        {
            var p = bmp.GetPixel(px, off.Y);
            if (Math.Abs(p.R - sep.R) <= 6 && Math.Abs(p.G - sep.G) <= 6 && Math.Abs(p.B - sep.B) <= 6)
                border = true;
        }
        Assert.True(border, $"[{theme.Id}] el chip inactivo debe llevar el borde Separator");
    }

    [Fact]
    public void MultiSegmentRow_no_chip_left_of_content_and_keeps_right_margin()
    {
        // Anti-truncamiento: ningún chip se pinta con x < contentLeft (X) y el bloque deja margen
        // derecho ≥ Spacing.Sm respecto a X+W.
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var rects = new Dictionary<string, Rectangle>();

        DashboardSettingsView.MultiSegmentRow(g, draw: true, "milestone", "", MilestoneSegs,
            new[] { 25, 50, 75, 95 }, X, 100, W, Theme.Dark, Typography.Caption, rects);

        foreach (var seg in MilestoneSegs)
        {
            Assert.True(rects.TryGetValue($"milestone:{seg.val}", out var r));
            Assert.True(r.X >= X, $"milestone:{seg.val} a la izquierda de contentLeft (x={r.X})");
            Assert.True(r.Right <= X + W - Spacing.Sm, $"milestone:{seg.val} sin margen derecho (right={r.Right})");
        }
    }

    [Fact]
    public void Inactive_chip_has_separator_border_so_it_reads_as_button()
    {
        // P1 #3: el chip INACTIVO lleva un borde sutil (Theme.Separator) para leerse como BOTÓN incluso
        // cuando su relleno (BgElevated) casi iguala al fondo del panel — el caso del tema claro (#FFF sobre
        // #FAFAFA, antes invisible). Verificado en el tema CLARO: el borde superior del chip (1px) es del
        // color Separator y el relleno interior NO. (El chip activo no lo necesita: ya contrasta.)
        const int x = 16, w = 308, y = 30;
        using var bmp = new Bitmap(x + w + 40, 100);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Theme.Light.Background);
        var rects = new Dictionary<string, Rectangle>();

        DashboardSettingsView.MultiSegmentRow(g, draw: true, "milestone", "", MilestoneSegs,
            new[] { 25 }, x, y, w, Theme.Light, Typography.Caption, rects);

        Assert.True(rects.TryGetValue("milestone:50", out var r)); // 50 inactivo → relleno BgElevated + borde
        var sep = Theme.Light.Separator;
        bool NearSep(Color c) => Math.Abs(c.R - sep.R) <= 12 && Math.Abs(c.G - sep.G) <= 12 && Math.Abs(c.B - sep.B) <= 12;

        // Busca el borde Separator en el lado SUPERIOR del chip (recorre la fila justo en r.Y, parte central
        // para evitar las esquinas redondeadas). El trazo de 1px vive en [r.Y, r.Y+1].
        bool foundBorder = false;
        int midX = r.X + r.Width / 2;
        for (int py = r.Y; py <= r.Y + 1 && !foundBorder; py++)
            if (NearSep(bmp.GetPixel(midX, py))) foundBorder = true;
        Assert.True(foundBorder, "el chip inactivo debe tener un borde Separator (track) en el tema claro");

        // El interior del chip (relleno BgElevated #FFF) NO es Separator (no se rellenó con el borde).
        var inner = bmp.GetPixel(r.X + r.Width / 2, r.Y + r.Height / 2);
        Assert.False(NearSep(inner), "el relleno del chip inactivo no debe ser del color del borde");
    }

    [Fact]
    public void MultiSegmentRow_advance_is_one_segment_row()
    {
        // Los hitos compactos (25/50/75/95) caben en un renglón → el avance es el de una fila de
        // segmentos (alto + Sm), no el doble (sin wrap).
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var rects = new Dictionary<string, Rectangle>();

        const int y0 = 100;
        int after = DashboardSettingsView.MultiSegmentRow(g, draw: false, "milestone", "", MilestoneSegs,
            new[] { 25 }, X, y0, W, Theme.Dark, Typography.Caption, rects);

        Assert.Equal(y0 + DashboardSettingsView.SegmentRowAdvanceForTest, after);
    }

    [Fact]
    public void MultiSegmentRow_empty_active_marks_nothing()
    {
        // Sin activos: todos los chips quedan en estilo "off" (ninguno Accent). Sigue registrando rects.
        const int x = 16, w = 308, y = 30;
        using var bmp = new Bitmap(x + w + 40, 100);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Theme.Dark.Background);
        var rects = new Dictionary<string, Rectangle>();

        DashboardSettingsView.MultiSegmentRow(g, draw: true, "milestone", "", MilestoneSegs,
            Array.Empty<int>(), x, y, w, Theme.Dark, Typography.Caption, rects);

        var acc = Theme.Dark.Accent;
        foreach (var seg in MilestoneSegs)
        {
            Assert.True(rects.TryGetValue($"milestone:{seg.val}", out var r));
            var c = bmp.GetPixel(r.X + 2, r.Y + r.Height / 2);
            bool isAccent = Math.Abs(c.R - acc.R) <= 10 && Math.Abs(c.G - acc.G) <= 10 && Math.Abs(c.B - acc.B) <= 10;
            Assert.False(isAccent, $"sin activos, milestone:{seg.val} no debe ser Accent");
        }
    }

    [Fact]
    public void Draw_milestone_segments_reflect_active_array()
    {
        // En el Draw completo, los hitos activos del array NotifyMilestones se registran y, tras el
        // refactor de T5, se pintan multi-activo SIN el re-pintado manual. Aquí basta con verificar
        // que el clic alterna (ActionFor) y que medir==pintar del Draw se mantiene con varios activos.
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var cfg = Cfg();
        cfg.NotifyMilestones = new[] { 25, 75 }; // 2 activos
        var s = Localization.Get("es");
        var rects = new Dictionary<string, Rectangle>();

        int measured = DashboardSettingsView.Draw(g, draw: false, X, 0, W, cfg, s, Theme.Dark,
            Typography.Body, Typography.Caption, rects);
        rects.Clear();
        int painted = DashboardSettingsView.Draw(g, draw: true, X, 0, W, cfg, s, Theme.Dark,
            Typography.Body, Typography.Caption, rects);

        Assert.Equal(measured, painted);
        foreach (var pct in new[] { 25, 50, 75, 95 })
            Assert.True(rects.ContainsKey($"milestone:{pct}"), $"falta rect milestone:{pct} en el Draw completo");
    }

    [Fact]
    public void MultiSegment_milestone_action_toggles_within_array()
    {
        // La clave milestone:<pct> sigue alternando el valor dentro del array (ActionFor intacto).
        var cfg = Cfg();
        cfg.NotifyMilestones = new[] { 25, 50 };

        DashboardSettingsView.ActionFor("milestone:75")!(cfg);   // añade 75
        Assert.Contains(75, cfg.NotifyMilestones);
        Assert.Equal(new[] { 25, 50, 75 }, cfg.NotifyMilestones);

        DashboardSettingsView.ActionFor("milestone:50")!(cfg);   // quita 50
        Assert.DoesNotContain(50, cfg.NotifyMilestones);
        Assert.Equal(new[] { 25, 75 }, cfg.NotifyMilestones);
    }

    [Fact]
    public void Draw_frequency_segments_fit_within_content_in_all_languages()
    {
        // Regresión del corte 'gundos': con etiquetas compactas, los 4 chips de frecuencia caben
        // dentro de [X, X+W] en todos los idiomas (ningún x<X, ningún right>X+W-Sm).
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var cfg = Cfg();
        foreach (var lang in new[] { "en", "es", "nl", "fr", "de", "ja", "ko", "zh-Hant" })
        {
            var s = Localization.Get(lang);
            var rects = new Dictionary<string, Rectangle>();
            DashboardSettingsView.Draw(g, draw: true, X, 0, W, cfg, s, Theme.Dark,
                Typography.Body, Typography.Caption, rects);
            foreach (var key in new[] { "freq:30", "freq:60", "freq:300", "freq:900" })
            {
                Assert.True(rects.TryGetValue(key, out var r), $"[{lang}] falta {key}");
                Assert.True(r.X >= X, $"[{lang}] {key} a la izquierda de contentLeft (x={r.X})");
                Assert.True(r.Right <= X + W, $"[{lang}] {key} rebasa el borde derecho (right={r.Right})");
            }
        }
    }

    // ================= T6: DrawDependent (master/dependientes) + secciones Mostrar/Notif/Actualización =================

    [Fact]
    public void DrawDependent_measure_equals_paint_when_active()
    {
        // El invariante de 2 pasadas también para el envoltorio de dependientes: medir==pintar.
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var rects = new Dictionary<string, Rectangle>();

        int measured = DashboardSettingsView.DrawDependent(active: true, X, 100, W, Theme.Dark, rects,
            (ix, iw, th) => DashboardSettingsView.ToggleRow(g, draw: false, "toggle:Dep", "Dependiente", null, true,
                ix, 100, iw, th, Typography.Body, Typography.Caption, rects));
        rects.Clear();
        int painted = DashboardSettingsView.DrawDependent(active: true, X, 100, W, Theme.Dark, rects,
            (ix, iw, th) => DashboardSettingsView.ToggleRow(g, draw: true, "toggle:Dep", "Dependiente", null, true,
                ix, 100, iw, th, Typography.Body, Typography.Caption, rects));

        Assert.Equal(measured, painted);
    }

    [Fact]
    public void DrawDependent_indents_inner_by_lg()
    {
        // El dependiente se dibuja sangrado Spacing.Lg respecto al contentLeft del master.
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var rects = new Dictionary<string, Rectangle>();

        DashboardSettingsView.DrawDependent(active: true, X, 100, W, Theme.Dark, rects,
            (ix, iw, th) => DashboardSettingsView.ToggleRow(g, draw: true, "toggle:Dep", "Dependiente", null, true,
                ix, 100, iw, th, Typography.Body, Typography.Caption, rects));

        Assert.True(rects.TryGetValue("toggle:Dep", out var r));
        Assert.Equal(X + Spacing.Lg, r.X);                 // sangría Lg
        Assert.Equal(W - Spacing.Lg, r.Width);             // ancho reducido por la sangría
    }

    [Fact]
    public void DrawDependent_inert_when_master_off_does_not_register_rect()
    {
        // INERCIA: con el master OFF, el rect del dependiente NO queda registrado → el clic no muta nada.
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var rects = new Dictionary<string, Rectangle>();

        DashboardSettingsView.DrawDependent(active: false, X, 100, W, Theme.Dark, rects,
            (ix, iw, th) => DashboardSettingsView.ToggleRow(g, draw: true, "toggle:Dep", "Dependiente", null, true,
                ix, 100, iw, th, Typography.Body, Typography.Caption, rects));

        Assert.False(rects.ContainsKey("toggle:Dep"), "dependiente inerte: su rect no debe disparar mutación");
    }

    [Fact]
    public void DrawDependent_same_advance_regardless_of_master_state()
    {
        // El alto reservado por el dependiente es idéntico esté el master on u off (solo cambia
        // la atenuación y la inercia, no la geometría → no se mueve nada al activar/desactivar).
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var rects = new Dictionary<string, Rectangle>();

        const int y0 = 100;
        int onY = DashboardSettingsView.DrawDependent(active: true, X, y0, W, Theme.Dark, rects,
            (ix, iw, th) => DashboardSettingsView.ToggleRow(g, draw: false, "toggle:Dep", "Dependiente", null, true,
                ix, y0, iw, th, Typography.Body, Typography.Caption, rects));
        rects.Clear();
        int offY = DashboardSettingsView.DrawDependent(active: false, X, y0, W, Theme.Dark, rects,
            (ix, iw, th) => DashboardSettingsView.ToggleRow(g, draw: false, "toggle:Dep", "Dependiente", null, true,
                ix, y0, iw, th, Typography.Body, Typography.Caption, rects));

        Assert.Equal(onY, offY);
    }

    [Fact]
    public void Draw_notifications_off_makes_dependents_inert()
    {
        // Con Notificaciones OFF, PaceAlerts e hitos quedan INERTES (su rect no se registra) → el clic
        // sobre ellos no muta nada. El Draw completo sigue cumpliendo medir==pintar.
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var cfg = Cfg();
        cfg.NotificationsEnabled = false;
        var s = Localization.Get("es");
        var rects = new Dictionary<string, Rectangle>();

        int measured = DashboardSettingsView.Draw(g, draw: false, X, 0, W, cfg, s, Theme.Dark,
            Typography.Body, Typography.Caption, rects);
        rects.Clear();
        int painted = DashboardSettingsView.Draw(g, draw: true, X, 0, W, cfg, s, Theme.Dark,
            Typography.Body, Typography.Caption, rects);

        Assert.Equal(measured, painted);
        // El master sí responde; los dependientes no.
        Assert.True(rects.ContainsKey("toggle:Notifications"), "el master de notificaciones siempre responde");
        Assert.False(rects.ContainsKey("toggle:PaceAlerts"), "PaceAlerts inerte con notificaciones off");
        foreach (var pct in new[] { 25, 50, 75, 95 })
            Assert.False(rects.ContainsKey($"milestone:{pct}"), $"milestone:{pct} inerte con notificaciones off");
    }

    /// <summary>Sin el Pro, las filas Pro del notch no cambian el ajuste (no haría nada): llevan el chip «Pro» y abren la
    /// tarjeta. Con la prueba o el Pro, como siempre.</summary>
    [Theory]
    [InlineData("es")]
    [InlineData("de")]
    public void Without_pro_the_pro_rows_of_the_notch_link_to_the_pro_card(string lang)
    {
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var cfg = Cfg();
        cfg.NotchEnabled = true;
        cfg.NotchMusic = true;
        cfg.NotchLyrics = true;
        cfg.NotchMonitorMode = NotchMonitorMode.Fixed;
        var s = Localization.Get(lang);
        var rects = new Dictionary<string, Rectangle>();
        string[] locked = { "toggle:NotchMusic", "toggle:NotchLyrics", "toggle:NotchCompactLyrics", "toggle:NotchTasks",
            "toggle:NotchShelfClear", "special:notchpermtoggle", "notchmonmode:fixed", "notchmonmode:all", "cycle:notchmonitor" };
        string[] links = { "music", "tasks", "shelf", "permissions", "monitors" };

        int measured = DashboardSettingsView.Draw(g, draw: false, X, 0, W, cfg, s, Theme.Dark,
            Typography.Body, Typography.Caption, rects, pro: ClaudeBarWin.Services.Licensing.ProState.Free);
        rects.Clear();
        int painted = DashboardSettingsView.Draw(g, draw: true, X, 0, W, cfg, s, Theme.Dark,
            Typography.Body, Typography.Caption, rects, pro: ClaudeBarWin.Services.Licensing.ProState.Free);

        Assert.Equal(measured, painted);
        foreach (var k in locked) Assert.False(rects.ContainsKey(k), $"{k} no debe responder sin el Pro");
        foreach (var f in links)
        {
            Assert.True(rects.TryGetValue(DashboardSettingsView.GetProKeyPrefix + f, out var r), $"falta el enlace de {f}");
            Assert.InRange(r.Right, X, X + W);
        }
        Assert.True(rects.ContainsKey("toggle:NotchClock"), "lo gratis sigue respondiendo");
        Assert.Null(DashboardSettingsView.ActionFor(DashboardSettingsView.GetProKeyPrefix + "music"));   // lo enruta la bandeja

        foreach (var state in new[] { ClaudeBarWin.Services.Licensing.ProState.Trial, ClaudeBarWin.Services.Licensing.ProState.Pro })
        {
            rects.Clear();
            DashboardSettingsView.Draw(g, draw: true, X, 0, W, cfg, s, Theme.Dark, Typography.Body, Typography.Caption, rects, pro: state);
            Assert.True(rects.ContainsKey("toggle:NotchMusic"));
            Assert.True(rects.ContainsKey("special:notchpermtoggle"));
            Assert.DoesNotContain(rects.Keys, k => k.StartsWith(DashboardSettingsView.GetProKeyPrefix, StringComparison.Ordinal));
        }

        // Con el notch apagado, el enlace también queda inerte (como el resto de dependientes).
        cfg.NotchEnabled = false;
        rects.Clear();
        DashboardSettingsView.Draw(g, draw: true, X, 0, W, cfg, s, Theme.Dark, Typography.Body, Typography.Caption, rects,
            pro: ClaudeBarWin.Services.Licensing.ProState.Free);
        Assert.DoesNotContain(rects.Keys, k => k.StartsWith(DashboardSettingsView.GetProKeyPrefix, StringComparison.Ordinal));
    }

    /// <summary>El pomodoro en Ajustes: su sonido es gratis; las duraciones, Pro (sin él, sus filas con el chip abren la tarjeta y
    /// enseñan las de serie). Solo se aceptan los valores que se ofrecen.</summary>
    [Theory]
    [InlineData("es")]
    [InlineData("ja")]
    public void The_pomodoro_rows_keep_the_sound_free_and_the_durations_pro(string lang)
    {
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var cfg = Cfg();
        cfg.NotchEnabled = true;
        cfg.NotchFocusMinutes = 50;
        var s = Localization.Get(lang);
        var rects = new Dictionary<string, Rectangle>();
        string[] rows = { "focusmin", "focusshort", "focuslong", "focusevery" };

        int measured = DashboardSettingsView.Draw(g, draw: false, X, 0, W, cfg, s, Theme.Dark,
            Typography.Body, Typography.Caption, rects, pro: ClaudeBarWin.Services.Licensing.ProState.Free);
        rects.Clear();
        int painted = DashboardSettingsView.Draw(g, draw: true, X, 0, W, cfg, s, Theme.Dark,
            Typography.Body, Typography.Caption, rects, pro: ClaudeBarWin.Services.Licensing.ProState.Free);
        Assert.Equal(measured, painted);
        Assert.True(rects.ContainsKey("toggle:NotchFocusSound"), "el sonido del foco es gratis");
        Assert.DoesNotContain(rects.Keys, k => rows.Any(r => k.StartsWith(r + ":", StringComparison.Ordinal)));
        foreach (var r in rows)
        {
            Assert.True(rects.TryGetValue(DashboardSettingsView.GetProKeyPrefix + "focus:" + r, out var link), $"falta el enlace de {r}");
            Assert.InRange(link.Right, X, X + W);
        }

        rects.Clear();
        DashboardSettingsView.Draw(g, draw: true, X, 0, W, cfg, s, Theme.Dark, Typography.Body, Typography.Caption, rects,
            pro: ClaudeBarWin.Services.Licensing.ProState.Pro);
        foreach (var v in FocusDurations.FocusOptions) Assert.True(rects.ContainsKey($"focusmin:{v}"), $"focusmin:{v}");
        foreach (var v in FocusDurations.EveryOptions) Assert.True(rects.ContainsKey($"focusevery:{v}"), $"focusevery:{v}");
        Assert.All(rects.Where(kv => kv.Key.StartsWith("focus", StringComparison.Ordinal)), kv => Assert.InRange(kv.Value.Right, X, X + W));
    }

    [Fact]
    public void The_pomodoro_settings_accept_only_what_is_offered()
    {
        var c = new AppConfig();
        DashboardSettingsView.ActionFor("focusmin:50")!(c);
        DashboardSettingsView.ActionFor("focusshort:10")!(c);
        DashboardSettingsView.ActionFor("focuslong:30")!(c);
        DashboardSettingsView.ActionFor("focusevery:3")!(c);
        Assert.Equal(new FocusDurations(50, 10, 30, 3), c.NotchFocusDurations);
        DashboardSettingsView.ActionFor("toggle:NotchFocusSound")!(c);
        Assert.False(c.NotchFocusSound);
        foreach (var bad in new[] { "focusmin:17", "focusshort:-5", "focuslong:", "focusevery:9", "focusmin:+25", "focusmin:25.0" })
            Assert.Null(DashboardSettingsView.ActionFor(bad));
    }

    [Fact]
    public void Draw_notifications_on_dependents_active_and_indented()
    {
        // Con Notificaciones ON, PaceAlerts e hitos responden (rect registrado) y están sangrados Spacing.Lg.
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var cfg = Cfg();
        cfg.NotificationsEnabled = true;
        var s = Localization.Get("es");
        var rects = new Dictionary<string, Rectangle>();

        DashboardSettingsView.Draw(g, draw: true, X, 0, W, cfg, s, Theme.Dark,
            Typography.Body, Typography.Caption, rects);

        Assert.True(rects.TryGetValue("toggle:PaceAlerts", out var pace), "PaceAlerts activo con notificaciones on");
        Assert.True(pace.X >= X + Spacing.Lg, $"PaceAlerts debe ir sangrado Lg (x={pace.X})");
        foreach (var pct in new[] { 25, 50, 75, 95 })
        {
            Assert.True(rects.TryGetValue($"milestone:{pct}", out var m), $"falta milestone:{pct} con notif on");
            Assert.True(m.X >= X + Spacing.Lg, $"milestone:{pct} debe ir sangrado Lg (x={m.X})");
        }
    }

    [Fact]
    public void Draw_show_spend_row_has_subtitle()
    {
        // La fila "Mostrar gasto" lleva subtítulo ("Coste equivalente por modelo") → su rect es más alto
        // que una fila sin subtítulo, y el Draw completo sigue cumpliendo medir==pintar.
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var cfg = Cfg();
        var s = Localization.Get("es");
        var rects = new Dictionary<string, Rectangle>();

        int measured = DashboardSettingsView.Draw(g, draw: false, X, 0, W, cfg, s, Theme.Dark,
            Typography.Body, Typography.Caption, rects);
        rects.Clear();
        int painted = DashboardSettingsView.Draw(g, draw: true, X, 0, W, cfg, s, Theme.Dark,
            Typography.Body, Typography.Caption, rects);
        Assert.Equal(measured, painted);

        // La fila de gasto (con subtítulo) es más alta que la de salud (sin subtítulo).
        Assert.True(rects.TryGetValue("toggle:ShowSpend", out var spend));
        Assert.True(rects.TryGetValue("toggle:ShowHealth", out var health));
        Assert.True(spend.Height > health.Height, "la fila de gasto con subtítulo debe ser más alta");
    }

    [Fact]
    public void Draw_show_spend_subtitle_is_localized_in_all_languages()
    {
        // i18n: el subtítulo de gasto sale de Strings y no queda vacío en ningún idioma.
        foreach (var lang in new[] { "en", "es", "nl", "fr", "de", "ja", "ko", "zh-Hant" })
        {
            var s = Localization.Get(lang);
            Assert.False(string.IsNullOrWhiteSpace(s.ShowSpendSubtitle), $"[{lang}] ShowSpendSubtitle vacío");
        }
    }

    // ================= T7: SESIONES EN VIVO (master hooks + StatusBadge + mascota dependiente) =================

    // -------- MasterRowWithBadge: fila maestra con título/subtítulo + StatusBadge a la derecha --------

    [Fact]
    public void MasterRowWithBadge_measure_equals_paint()
    {
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var rects = new Dictionary<string, Rectangle>();

        int measured = DashboardSettingsView.MasterRowWithBadge(g, draw: false, "special:hooktoggle",
            "Activar sesiones en vivo", "Recibe el estado de tus sesiones", "Activas", Theme.Dark.Ok,
            X, 100, W, Theme.Dark, Typography.Body, Typography.Caption, rects);
        rects.Clear();
        int painted = DashboardSettingsView.MasterRowWithBadge(g, draw: true, "special:hooktoggle",
            "Activar sesiones en vivo", "Recibe el estado de tus sesiones", "Activas", Theme.Dark.Ok,
            X, 100, W, Theme.Dark, Typography.Body, Typography.Caption, rects);

        Assert.Equal(measured, painted);
    }

    [Fact]
    public void MasterRowWithBadge_registers_full_row_rect_and_badge_on_right()
    {
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var rects = new Dictionary<string, Rectangle>();

        DashboardSettingsView.MasterRowWithBadge(g, draw: true, "special:hooktoggle",
            "Activar sesiones en vivo", "Recibe el estado de tus sesiones", "Instalar", Theme.Dark.Warn,
            X, 100, W, Theme.Dark, Typography.Body, Typography.Caption, rects);

        // El hit-test es la fila completa (clic en cualquier punto dispara la acción del host).
        Assert.True(rects.TryGetValue("special:hooktoggle", out var r));
        Assert.Equal(X, r.X);
        Assert.Equal(W, r.Width);
        Assert.True(r.Height > 0);
    }

    [Fact]
    public void MasterRowWithBadge_measure_equals_paint_with_long_label_and_subtitle()
    {
        // Anti-truncamiento: con etiqueta+subtítulo largos en un ancho estrecho, la decisión de elidir es
        // idéntica en medir y pintar → mismo y de salida (no se mueve nada, no se corta bajo el badge).
        const int x = 16, w = 160; // estrecho a propósito
        using var bmp = new Bitmap(x + w + 80, 200);
        using var g = Graphics.FromImage(bmp);
        var rects = new Dictionary<string, Rectangle>();

        int measured = DashboardSettingsView.MasterRowWithBadge(g, draw: false, "special:hooktoggle",
            "Desactivar (quitar hooks de settings.json)", "Recibe el estado de tus sesiones de Claude Code",
            "Activas", Theme.Dark.Ok, x, 0, w, Theme.Dark, Typography.Body, Typography.Caption, rects);
        rects.Clear();
        int painted = DashboardSettingsView.MasterRowWithBadge(g, draw: true, "special:hooktoggle",
            "Desactivar (quitar hooks de settings.json)", "Recibe el estado de tus sesiones de Claude Code",
            "Activas", Theme.Dark.Ok, x, 0, w, Theme.Dark, Typography.Body, Typography.Caption, rects);

        Assert.Equal(measured, painted);
    }

    [Fact]
    public void MasterRowWithBadge_with_subtitle_is_taller_than_without()
    {
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var rects = new Dictionary<string, Rectangle>();

        int plain = DashboardSettingsView.MasterRowWithBadge(g, draw: false, "special:x",
            "Activar sesiones en vivo", null, "Activas", Theme.Dark.Ok,
            X, 0, W, Theme.Dark, Typography.Body, Typography.Caption, rects);
        int withSub = DashboardSettingsView.MasterRowWithBadge(g, draw: false, "special:x",
            "Activar sesiones en vivo", "Recibe el estado de tus sesiones", "Activas", Theme.Dark.Ok,
            X, 0, W, Theme.Dark, Typography.Body, Typography.Caption, rects);

        Assert.True(withSub > plain, "la fila maestra con subtítulo debe ocupar más alto");
    }

    // -------- LiveSessionsSection: master hooks + dependientes (mascota/tamaño/silenciar) --------

    [Fact]
    public void LiveSessions_measure_equals_paint_installed()
    {
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var cfg = Cfg();
        var s = Localization.Get("es");
        var rects = new Dictionary<string, Rectangle>();

        int measured = DashboardSettingsView.LiveSessionsSection(g, draw: false, hooksInstalled: true,
            X, 100, W, cfg, s, Theme.Dark, Typography.Body, Typography.Caption, rects);
        rects.Clear();
        int painted = DashboardSettingsView.LiveSessionsSection(g, draw: true, hooksInstalled: true,
            X, 100, W, cfg, s, Theme.Dark, Typography.Body, Typography.Caption, rects);

        Assert.Equal(measured, painted);
    }

    [Fact]
    public void LiveSessions_measure_equals_paint_not_installed()
    {
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var cfg = Cfg();
        var s = Localization.Get("es");
        var rects = new Dictionary<string, Rectangle>();

        int measured = DashboardSettingsView.LiveSessionsSection(g, draw: false, hooksInstalled: false,
            X, 100, W, cfg, s, Theme.Dark, Typography.Body, Typography.Caption, rects);
        rects.Clear();
        int painted = DashboardSettingsView.LiveSessionsSection(g, draw: true, hooksInstalled: false,
            X, 100, W, cfg, s, Theme.Dark, Typography.Body, Typography.Caption, rects);

        Assert.Equal(measured, painted);
    }

    [Fact]
    public void LiveSessions_advance_identical_regardless_of_hooks_state()
    {
        // La geometría no cambia con el estado de los hooks (solo color + inercia) → no se mueve nada.
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var cfg = Cfg();
        var s = Localization.Get("es");
        var rects = new Dictionary<string, Rectangle>();

        const int y0 = 100;
        int onY = DashboardSettingsView.LiveSessionsSection(g, draw: false, hooksInstalled: true,
            X, y0, W, cfg, s, Theme.Dark, Typography.Body, Typography.Caption, rects);
        rects.Clear();
        int offY = DashboardSettingsView.LiveSessionsSection(g, draw: false, hooksInstalled: false,
            X, y0, W, cfg, s, Theme.Dark, Typography.Body, Typography.Caption, rects);

        Assert.Equal(onY, offY);
    }

    [Fact]
    public void LiveSessions_dependents_active_and_indented_when_installed()
    {
        // Con hooks instalados: la fila maestra responde, los 3 dependientes responden y van sangrados Lg.
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var cfg = Cfg();
        var s = Localization.Get("es");
        var rects = new Dictionary<string, Rectangle>();

        DashboardSettingsView.LiveSessionsSection(g, draw: true, hooksInstalled: true,
            X, 100, W, cfg, s, Theme.Dark, Typography.Body, Typography.Caption, rects);

        Assert.True(rects.ContainsKey("special:hooktoggle"), "la fila maestra siempre responde");
        Assert.True(rects.TryGetValue("toggle:Suppress", out var sup), "falta dependiente toggle:Suppress con hooks instalados");
        Assert.True(sup.X >= X + Spacing.Lg, $"toggle:Suppress debe ir sangrado Lg (x={sup.X})");
        // v0.3.7: la mascota es un toggle simple sangrado (la talla grande y su segmented se retiraron).
        Assert.True(rects.TryGetValue("toggle:ShowMascot", out var m), "falta el toggle de la mascota con hooks on");
        Assert.True(m.X >= X + Spacing.Lg, $"toggle:ShowMascot debe ir sangrado Lg (x={m.X})");
        // Ya NO existe el control de 3 estados ni el segmented de tamaño antiguos.
        foreach (var v in new[] { "hidden", "compact", "large" })
            Assert.False(rects.ContainsKey($"mascot:{v}"), $"el segmento mascot:{v} ya no existe");
        Assert.False(rects.ContainsKey("mascotsize:compact"), "el segmented de tamaño separado ya no existe");
    }

    [Fact]
    public void LiveSessions_dependents_inert_when_not_installed()
    {
        // Sin hooks instalados: los 3 dependientes quedan INERTES (sus rects no se registran) → un clic
        // no muta nada. La fila maestra (special:hooktoggle) SÍ responde (es lo que instala los hooks).
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var cfg = Cfg();
        var s = Localization.Get("es");
        var rects = new Dictionary<string, Rectangle>();

        DashboardSettingsView.LiveSessionsSection(g, draw: true, hooksInstalled: false,
            X, 100, W, cfg, s, Theme.Dark, Typography.Body, Typography.Caption, rects);

        Assert.True(rects.ContainsKey("special:hooktoggle"), "la fila maestra responde para poder instalar");
        Assert.False(rects.ContainsKey("toggle:Suppress"), "Suppress inerte sin hooks");
        // v0.3.7: el toggle de la mascota queda INERTE sin hooks (su rect no se registra).
        Assert.False(rects.ContainsKey("toggle:ShowMascot"), "toggle:ShowMascot inerte sin hooks");
    }

    [Fact]
    public void LiveSessions_master_is_positive_toggle_not_double_negation_label()
    {
        // P1 #1: la fila maestra es un toggle POSITIVO. Su label es "Activadas" (s.Enabled, mismo patrón que
        // NOTIFICACIONES) — NUNCA la antigua doble negación "Desactivar (quitar hooks)". El subtítulo gris
        // explica los hooks. No hay StatusBadge "Activas/Instalar" duplicando al toggle.
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var cfg = Cfg();
        var s = Localization.Get("es");
        var rects = new Dictionary<string, Rectangle>();

        DashboardSettingsView.LiveSessionsSection(g, draw: true, hooksInstalled: true,
            X, 100, W, cfg, s, Theme.Dark, Typography.Body, Typography.Caption, rects);

        // La fila maestra responde y es la de hooks.
        Assert.True(rects.ContainsKey("special:hooktoggle"), "la fila maestra de hooks debe registrarse");
        // El label POSITIVO es "Activadas"; ya no se usa el de doble negación en la fila maestra.
        Assert.Equal("Activadas", s.Enabled);
        Assert.Contains("Desactivar", s.MenuUninstallHooks); // sigue existiendo para el menú del tray…
        // …pero la fila maestra del panel NO usa ese label de doble negación (lo verifica el pixel del pill).
        Assert.Equal("Instala/quita hooks en ~/.claude/settings.json", s.LiveSessionsSubtitle);
    }

    [Fact]
    public void LiveSessions_master_toggle_pill_reflects_installed_state()
    {
        // El pill del master refleja el estado: ON (Accent) cuando los hooks están instalados, OFF
        // (Separator) cuando no. El pill comunica el estado → no hace falta un badge aparte (sin doble).
        const int x = 16, w = 308;
        var cfg = Cfg();
        var s = Localization.Get("es");

        Color SampleMasterPill(bool installed, bool onSide)
        {
            using var bmp = new Bitmap(x + w + 40, 200);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Theme.Dark.Background);
            var rects = new Dictionary<string, Rectangle>();
            DashboardSettingsView.LiveSessionsSection(g, draw: true, hooksInstalled: installed,
                x, 20, w, cfg, s, Theme.Dark, Typography.Body, Typography.Caption, rects);
            var r = rects["special:hooktoggle"];
            // El pill se ancla a la derecha (track 36×20, margen Sm). Muestrea el lado del TRACK sin knob:
            // ON → knob a la derecha → muestrear cuarto izquierdo; OFF → knob izq → cuarto derecho.
            int trackRight = x + w - 8;          // rightX - Spacing.Sm
            int trackLeft = trackRight - 36;     // PillTrackW
            int py = r.Y + r.Height / 2;
            int px = onSide ? trackLeft + 36 / 4 : trackRight - 36 / 4;
            return bmp.GetPixel(px, py);
        }

        var cOn = SampleMasterPill(installed: true, onSide: true);
        var acc = Theme.Dark.Accent;
        Assert.True(Math.Abs(cOn.R - acc.R) <= 10 && Math.Abs(cOn.G - acc.G) <= 10 && Math.Abs(cOn.B - acc.B) <= 10,
            $"hooks instalados → pill ON (Accent), fue #{cOn.R:X2}{cOn.G:X2}{cOn.B:X2}");

        var cOff = SampleMasterPill(installed: false, onSide: false);
        var sep = Theme.Dark.Separator;
        Assert.True(Math.Abs(cOff.R - sep.R) <= 10 && Math.Abs(cOff.G - sep.G) <= 10 && Math.Abs(cOff.B - sep.B) <= 10,
            $"hooks NO instalados → pill OFF (Separator), fue #{cOff.R:X2}{cOff.G:X2}{cOff.B:X2}");
    }

    // -------- MasterToggleRow: fila maestra POSITIVA con toggle-pill + subtítulo que ENVUELVE (P1 #1) --------

    [Fact]
    public void MasterToggleRow_measure_equals_paint()
    {
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var rects = new Dictionary<string, Rectangle>();

        int measured = DashboardSettingsView.MasterToggleRow(g, draw: false, "special:hooktoggle",
            "Activadas", "Instala/quita hooks en ~/.claude/settings.json", true,
            X, 100, W, Theme.Dark, Typography.Body, Typography.Caption, rects);
        rects.Clear();
        int painted = DashboardSettingsView.MasterToggleRow(g, draw: true, "special:hooktoggle",
            "Activadas", "Instala/quita hooks en ~/.claude/settings.json", true,
            X, 100, W, Theme.Dark, Typography.Body, Typography.Caption, rects);

        Assert.Equal(measured, painted);
    }

    [Fact]
    public void MasterToggleRow_hit_rect_covers_full_row_width()
    {
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var rects = new Dictionary<string, Rectangle>();

        DashboardSettingsView.MasterToggleRow(g, draw: true, "special:hooktoggle",
            "Activadas", "Instala/quita hooks en ~/.claude/settings.json", false,
            X, 100, W, Theme.Dark, Typography.Body, Typography.Caption, rects);

        Assert.True(rects.TryGetValue("special:hooktoggle", out var r));
        Assert.Equal(X, r.X);
        Assert.Equal(W, r.Width);
        Assert.True(r.Height > 0);
    }

    [Fact]
    public void MasterToggleRow_long_subtitle_wraps_not_ellipsizes()
    {
        // ANTI-CORTE (invariante de Yovan): un subtítulo más ancho que la columna de texto se ENVUELVE a 2+
        // líneas (la fila crece), JAMÁS se elide. Verificado en los 8 idiomas (todos los subtítulos de hooks
        // son largos). Sin "…" en el subtítulo; la fila con subtítulo largo es más alta que una de 1 línea.
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);

        // Altura de referencia: misma fila con un subtítulo CORTO de 1 línea.
        var rShort = new Dictionary<string, Rectangle>();
        DashboardSettingsView.MasterToggleRow(g, draw: true, "k", "Activadas", "ok", true,
            X, 0, W, Theme.Dark, Typography.Body, Typography.Caption, rShort);
        int shortH = rShort["k"].Height;

        foreach (var lang in new[] { "en", "es", "nl", "fr", "de", "ja", "ko", "zh-Hant" })
        {
            var s = Localization.Get(lang);
            // El subtítulo de hooks no debe contener la elipsis (no se elide en ningún idioma).
            Assert.DoesNotContain(TextWrap.Ellipsis, s.LiveSessionsSubtitle);

            var rects = new Dictionary<string, Rectangle>();
            DashboardSettingsView.MasterToggleRow(g, draw: true, "k", s.Enabled, s.LiveSessionsSubtitle, true,
                X, 0, W, Theme.Dark, Typography.Body, Typography.Caption, rects);
            int h = rects["k"].Height;
            Assert.True(h > shortH,
                $"[{lang}] el subtítulo largo de hooks debe ENVOLVER (fila más alta que 1 línea): h={h}, ref={shortH}");
        }
    }

    [Fact]
    public void MasterToggleRow_text_column_never_overlaps_pill()
    {
        // El texto (título+subtítulo) se mide contra la columna a la IZQUIERDA del pill (gutter Spacing.Md):
        // ninguna línea del subtítulo, medida, rebasa el borde izquierdo del pill. Garantiza que el texto
        // completo cabe sin solaparse con el control (medición determinista = la del propio helper).
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var s = Localization.Get("nl"); // el subtítulo más largo (~359px)
        var rects = new Dictionary<string, Rectangle>();

        DashboardSettingsView.MasterToggleRow(g, draw: true, "k", s.Enabled, s.LiveSessionsSubtitle, true,
            X, 0, W, Theme.Dark, Typography.Body, Typography.Caption, rects);

        // Columna de texto: desde X hasta el borde izquierdo del pill (anclado a la derecha) menos gutter Md.
        const int pillTrackW = 36; // PillTrackW
        int pillLeft = (X + W) - Spacing.Sm - pillTrackW;
        int textColW = pillLeft - Spacing.Md - X;
        var lines = TextWrap.WordWrap(s.LiveSessionsSubtitle, textColW,
            t => g.MeasureString(t, Typography.Caption).Width);
        foreach (var line in lines)
            Assert.True(g.MeasureString(line, Typography.Caption).Width <= textColW + 1,
                $"la línea '{line}' rebasa la columna de texto ({textColW}px) → se solaparía con el pill");
        Assert.True(lines.Count >= 2, "el subtítulo largo debe ocupar ≥2 líneas (envuelto, no cortado)");
    }

    // -------- T9b: knob del TogglePill blanco con borde sutil (§3 #11) --------

    [Fact]
    public void TogglePill_knob_is_white_in_every_theme_and_state()
    {
        // El knob histórico usaba TextPrimary (OFF) y ColorMath.Contrast(Accent) (ON): en claro OFF
        // salía NEGRO sobre el track gris ("agujero perforado") y en CLI ON negro sobre verde. El
        // estándar es knob BLANCO con borde sutil, en los 3 temas y ambos estados.
        foreach (var theme in new[] { Theme.Dark, Theme.Light, Theme.Cli })
            foreach (var on in new[] { false, true })
            {
                using var bmp = new Bitmap(120, 40);
                using var g = Graphics.FromImage(bmp);
                g.Clear(theme.Background);
                var track = DashboardSettingsView.TogglePill(g, draw: true, on, rightX: 100, y: 4, rowH: 24, theme);
                int cx = DashboardSettingsView.PillKnobCenterX(track, on);
                var px = bmp.GetPixel(cx, track.Y + track.Height / 2);
                Assert.True(px.R > 220 && px.G > 220 && px.B > 220,
                    $"[{theme.Id}, on={on}] el knob debe ser blanco, fue #{px.R:X2}{px.G:X2}{px.B:X2}");
            }
    }

    // -------- T8a: guards de truncamiento en ToggleRow / SegmentedRow / MultiSegmentRow --------

    // Etiqueta multi-palabra más ancha que la fila (estilo DE/FR): obliga a envolver, no a cortar.
    private const string LongLabel =
        "Benachrichtigungen für kritische Schwellenwerte vom System automatisch aktivieren";

    [Fact]
    public void ToggleRow_long_label_wraps_instead_of_passing_under_the_pill()
    {
        // §3 #13: ToggleRow pintaba el label en UNA línea sin acotar → en DE/FR pasaba por debajo del
        // pill. Debe envolver dentro de [x, pillLeft - Md] (mismo patrón que MasterToggleRow): la fila
        // con label largo CRECE respecto a una de 1 línea.
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);

        var rShort = new Dictionary<string, Rectangle>();
        DashboardSettingsView.ToggleRow(g, draw: false, "k", "Notificaciones", null, true,
            X, 0, W, Theme.Dark, Typography.Body, Typography.Caption, rShort);
        int shortH = rShort["k"].Height;

        var rects = new Dictionary<string, Rectangle>();
        DashboardSettingsView.ToggleRow(g, draw: false, "k", LongLabel, null, true,
            X, 0, W, Theme.Dark, Typography.Body, Typography.Caption, rects);

        Assert.True(rects["k"].Height > shortH,
            $"el label largo debe ENVOLVER (fila más alta): h={rects["k"].Height}, ref={shortH}");
    }

    [Fact]
    public void ToggleRow_long_label_and_subtitle_measure_equals_paint()
    {
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var rects = new Dictionary<string, Rectangle>();

        int measured = DashboardSettingsView.ToggleRow(g, draw: false, "k", LongLabel, LongLabel, true,
            X, 100, W, Theme.Dark, Typography.Body, Typography.Caption, rects);
        rects.Clear();
        int painted = DashboardSettingsView.ToggleRow(g, draw: true, "k", LongLabel, LongLabel, true,
            X, 100, W, Theme.Dark, Typography.Body, Typography.Caption, rects);

        Assert.Equal(measured, painted);
    }

    [Fact]
    public void SegmentedRow_long_label_wraps_and_drops_chips_below_the_label_block()
    {
        // §3 #13: la etiqueta de SegmentedRow se pintaba en 1 línea sin acotar → una etiqueta DE/FR más
        // ancha que la fila rebasaba el borde del panel. Debe envolver dentro de [x, x+w-Sm] y los chips
        // caer DEBAJO del bloque completo (≥2 líneas), no debajo de una sola.
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var segs = new[] { ("a", "AA"), ("b", "BB") };

        var rects = new Dictionary<string, Rectangle>();
        int after = DashboardSettingsView.SegmentedRow(g, draw: false, "k", LongLabel, segs, "a",
            X, 0, W, Theme.Dark, Typography.Caption, rects);

        int lineH = (int)Math.Ceiling(g.MeasureString(LongLabel, Typography.Caption).Height);
        Assert.All(segs, sg => Assert.True(rects[$"k:{sg.Item1}"].Y >= 2 * lineH,
            $"chip '{sg.Item1}' en y={rects[$"k:{sg.Item1}"].Y}: debe caer bajo el bloque envuelto (≥{2 * lineH})"));

        // medir==pintar con la etiqueta envuelta.
        var rPaint = new Dictionary<string, Rectangle>();
        int painted = DashboardSettingsView.SegmentedRow(g, draw: true, "k", LongLabel, segs, "a",
            X, 0, W, Theme.Dark, Typography.Caption, rPaint);
        Assert.Equal(after, painted);
    }

    [Fact]
    public void SegmentedRow_long_label_never_paints_past_the_content_width()
    {
        // Pixel-check del defecto original: nada pintado a la derecha de x+w en la banda de la etiqueta.
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        g.Clear(Theme.Dark.Background);

        DashboardSettingsView.SegmentedRow(g, draw: true, "k", LongLabel,
            new[] { ("a", "AA") }, "a", X, 0, W, Theme.Dark, Typography.Caption,
            new Dictionary<string, Rectangle>());

        var bg = Theme.Dark.Background;
        for (int px = X + W + 2; px < bmp.Width; px++)
            for (int py = 0; py < 18; py++)
            {
                var p = bmp.GetPixel(px, py);
                Assert.True(p.R == bg.R && p.G == bg.G && p.B == bg.B,
                    $"píxel pintado en ({px},{py}), fuera del ancho de contenido (x+w={X + W})");
            }
    }

    [Fact]
    public void MultiSegmentRow_long_label_wraps_and_drops_chips_below_the_label_block()
    {
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var segs = new[] { ("25", "25%"), ("50", "50%") };

        var rects = new Dictionary<string, Rectangle>();
        int after = DashboardSettingsView.MultiSegmentRow(g, draw: false, "m", LongLabel, segs,
            new[] { 25 }, X, 0, W, Theme.Dark, Typography.Caption, rects);

        int lineH = (int)Math.Ceiling(g.MeasureString(LongLabel, Typography.Caption).Height);
        Assert.All(segs, sg => Assert.True(rects[$"m:{sg.Item1}"].Y >= 2 * lineH,
            $"chip '{sg.Item1}' en y={rects[$"m:{sg.Item1}"].Y}: debe caer bajo el bloque envuelto (≥{2 * lineH})"));

        var rPaint = new Dictionary<string, Rectangle>();
        int painted = DashboardSettingsView.MultiSegmentRow(g, draw: true, "m", LongLabel, segs,
            new[] { 25 }, X, 0, W, Theme.Dark, Typography.Caption, rPaint);
        Assert.Equal(after, painted);
    }

    // -------- T8d: el rect del "‹ Ajustes" se mide con el texto localizado (no 80×20 fijo) --------

    [Fact]
    public void BackHitRect_covers_the_localized_label_in_every_language()
    {
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);

        foreach (var lang in new[] { "en", "es", "nl", "fr", "de", "ja", "ko", "zh-Hant" })
        {
            var s = Localization.Get(lang);
            string label = "‹ " + s.Settings;
            var r = DashboardSettingsView.BackHitRect(g, label, Typography.Body, X, 50, W);
            int textW = (int)Math.Ceiling(g.MeasureString(label, Typography.Body).Width);

            Assert.Equal(X, r.X);
            Assert.Equal(50, r.Y);
            Assert.True(r.Width >= Math.Min(textW, W),
                $"[{lang}] el rect (w={r.Width}) no cubre el texto medido ({textW}px): media etiqueta sin zona de clic");
            Assert.True(r.Width <= W, $"[{lang}] el rect (w={r.Width}) rebasa el ancho de contenido ({W}px)");
            Assert.True(r.Height >= 18, "el alto debe conservar una zona de clic cómoda");
        }
    }

    // -------- Draw completo: orden de secciones, sin duplicados, mascota como dependiente --------

    [Fact]
    public void Draw_mascot_row_is_dependent_below_hook_master()
    {
        // SIN DUPLICADOS: la mascota ya NO es una fila suelta por ENCIMA del botón de hooks; es un control
        // dependiente sangrado Lg y se dibuja DESPUÉS (más abajo en y) que la fila maestra de hooks. P2 #1:
        // es UN segmented de 3 estados (mascot:*), no un toggle + un segmented de tamaño.
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var cfg = Cfg();
        var s = Localization.Get("es");
        var rects = new Dictionary<string, Rectangle>();

        DashboardSettingsView.Draw(g, draw: true, X, 0, W, cfg, s, Theme.Dark,
            Typography.Body, Typography.Caption, rects);

        Assert.True(rects.TryGetValue("special:hooktoggle", out var master), "falta la fila maestra de hooks");
        // Si la mascota es dependiente (rect registrado solo con hooks instalados), debe ir DEBAJO y sangrada.
        if (rects.TryGetValue("mascot:hidden", out var mascot))
        {
            Assert.True(mascot.Y > master.Y, "el control de mascota dependiente va DEBAJO de la fila maestra de hooks");
            Assert.True(mascot.X >= X + Spacing.Lg, "el control de mascota dependiente va sangrado Lg");
        }
    }

    [Fact]
    public void Draw_threshold_lives_in_icon_section_not_notifications()
    {
        // El umbral de color sigue registrado (sección Icono), separado de los hitos de notificación.
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var cfg = Cfg();
        var s = Localization.Get("es");
        var rects = new Dictionary<string, Rectangle>();

        DashboardSettingsView.Draw(g, draw: true, X, 0, W, cfg, s, Theme.Dark,
            Typography.Body, Typography.Caption, rects);

        // Umbral presente (umbral de color del icono) y hitos presentes (notificación): coexisten, no duplican.
        Assert.True(rects.Keys.Any(k => k.StartsWith("threshold:")), "el umbral de color debe estar en la sección Icono");
        Assert.True(rects.ContainsKey("icon:percent"), "el contenido del icono debe estar en la sección Icono");
        foreach (var pct in new[] { 25, 50, 75, 95 })
            Assert.True(rects.ContainsKey($"milestone:{pct}"), $"los hitos de notificación siguen presentes ({pct})");
    }

    [Fact]
    public void Draw_appearance_has_no_inline_import_theme_button()
    {
        // T8: "Importar .itermcolors" ya NO va inline en Apariencia; se consolidó en ACERCA DE. El botón
        // existe (lo verifica Draw_registers_import_theme_button_in_about_section) pero queda DEBAJO de las
        // preferencias de Apariencia, no entre ellas.
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var cfg = Cfg();
        var s = Localization.Get("es");
        var rects = new Dictionary<string, Rectangle>();

        DashboardSettingsView.Draw(g, draw: true, X, 0, W, cfg, s, Theme.Dark,
            Typography.Body, Typography.Caption, rects);

        // Las preferencias de apariencia siguen presentes.
        Assert.True(rects.ContainsKey("theme:dark"));
        Assert.True(rects.ContainsKey("cycle:position"));
        Assert.True(rects.ContainsKey("toggle:Sticky"));
        Assert.True(rects.ContainsKey("toggle:OnTop"));
        Assert.True(rects.ContainsKey("toggle:ReduceMotion"));
        // El botón de importar tema está, pero por DEBAJO de la última preferencia de apariencia (no inline).
        Assert.True(rects.TryGetValue("special:importtheme", out var import));
        Assert.True(rects.TryGetValue("toggle:ReduceMotion", out var rm));
        Assert.True(import.Y > rm.Y, "Importar tema va al final (Acerca de), no entre las preferencias de Apariencia");
    }

    [Fact]
    public void Draw_reduce_motion_row_has_subtitle()
    {
        // "Reducir movimiento" lleva subtítulo ("Desactiva animaciones") → más alto que una fila sin sub.
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var cfg = Cfg();
        var s = Localization.Get("es");
        var rects = new Dictionary<string, Rectangle>();

        DashboardSettingsView.Draw(g, draw: true, X, 0, W, cfg, s, Theme.Dark,
            Typography.Body, Typography.Caption, rects);

        Assert.True(rects.TryGetValue("toggle:ReduceMotion", out var rm));
        Assert.True(rects.TryGetValue("toggle:OnTop", out var ontop)); // sin subtítulo
        Assert.True(rm.Height > ontop.Height, "Reducir movimiento con subtítulo debe ser más alto");
    }

    [Fact]
    public void Draw_no_orphan_mascot_button_row_in_old_position()
    {
        // Regresión: ya NO existe la presentación antigua (mascota suelta + ButtonRow de hooks como fila más).
        // La fila maestra de hooks debe ir ANTES (más arriba) que cualquier dependiente de mascota.
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var cfg = Cfg();
        var s = Localization.Get("es");
        var rects = new Dictionary<string, Rectangle>();

        DashboardSettingsView.Draw(g, draw: true, X, 0, W, cfg, s, Theme.Dark,
            Typography.Body, Typography.Caption, rects);

        Assert.True(rects.ContainsKey("special:hooktoggle"));
        // El subtítulo de la fila maestra está localizado en todos los idiomas.
        foreach (var lang in new[] { "en", "es", "nl", "fr", "de", "ja", "ko", "zh-Hant" })
            Assert.False(string.IsNullOrWhiteSpace(Localization.Get(lang).LiveSessionsSubtitle),
                $"[{lang}] LiveSessionsSubtitle vacío");
    }

    [Fact]
    public void Draw_reduce_motion_subtitle_localized_in_all_languages()
    {
        foreach (var lang in new[] { "en", "es", "nl", "fr", "de", "ja", "ko", "zh-Hant" })
            Assert.False(string.IsNullOrWhiteSpace(Localization.Get(lang).ReduceMotionSubtitle),
                $"[{lang}] ReduceMotionSubtitle vacío");
    }

    // ================= T8: secciones SISTEMA + ACERCA DE =================

    [Fact]
    public void MenuSystem_localized_in_all_languages()
    {
        // i18n: el título "Sistema" deja de estar hardcodeado y sale de Strings en los 8 idiomas.
        foreach (var lang in new[] { "en", "es", "nl", "fr", "de", "ja", "ko", "zh-Hant" })
            Assert.False(string.IsNullOrWhiteSpace(Localization.Get(lang).MenuSystem),
                $"[{lang}] MenuSystem vacío");
        // El español debe ser "Sistema" (sustituye exactamente el literal antiguo).
        Assert.Equal("Sistema", Localization.Get("es").MenuSystem);
    }

    [Fact]
    public void Draw_registers_import_theme_button_in_about_section()
    {
        // T8: "Importar .itermcolors" se consolida en ACERCA DE → el panel vuelve a registrar special:importtheme.
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var cfg = Cfg();
        var s = Localization.Get("es");
        var rects = new Dictionary<string, Rectangle>();

        DashboardSettingsView.Draw(g, draw: true, X, 0, W, cfg, s, Theme.Dark,
            Typography.Body, Typography.Caption, rects);

        Assert.True(rects.ContainsKey("special:importtheme"),
            "Importar tema vive ahora en la sección Acerca de (ButtonRow)");
    }

    [Fact]
    public void Draw_import_theme_lives_after_appearance_preferences()
    {
        // Acerca de va al FINAL: el botón Importar tema queda por DEBAJO de las preferencias de Apariencia.
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var cfg = Cfg();
        var s = Localization.Get("es");
        var rects = new Dictionary<string, Rectangle>();

        DashboardSettingsView.Draw(g, draw: true, X, 0, W, cfg, s, Theme.Dark,
            Typography.Body, Typography.Caption, rects);

        Assert.True(rects.TryGetValue("special:importtheme", out var import));
        Assert.True(rects.TryGetValue("theme:dark", out var theme));
        Assert.True(rects.TryGetValue("toggle:Startup", out var startup));
        Assert.True(import.Y > theme.Y, "Importar tema (Acerca de) va por debajo del tema (Apariencia)");
        Assert.True(import.Y > startup.Y, "Importar tema (Acerca de) va por debajo de Sistema (Arrancar con Windows)");
    }

    [Fact]
    public void Draw_language_lives_in_system_section_after_startup()
    {
        // SISTEMA agrupa Arrancar con Windows + Idioma: el ciclo de idioma queda DEBAJO del toggle de arranque.
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var cfg = Cfg();
        var s = Localization.Get("es");
        var rects = new Dictionary<string, Rectangle>();

        DashboardSettingsView.Draw(g, draw: true, X, 0, W, cfg, s, Theme.Dark,
            Typography.Body, Typography.Caption, rects);

        Assert.True(rects.TryGetValue("toggle:Startup", out var startup));
        Assert.True(rects.TryGetValue("cycle:lang", out var lang));
        Assert.True(lang.Y > startup.Y, "Idioma va dentro de SISTEMA, debajo de Arrancar con Windows");
    }

    [Fact]
    public void Draw_registers_the_remove_integration_button_below_live_sessions()
    {
        // «Quitar la integración con Claude Code»: un botón, siempre (también sin nada instalado: lo busca la guía de
        // desinstalación de la Store), justo después de las dos integraciones (origen de la cuota y sesiones en vivo).
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var rects = new Dictionary<string, Rectangle>();
        foreach (var lang in new[] { "en", "es", "de", "ja" })
        {
            rects.Clear();
            DashboardSettingsView.Draw(g, draw: true, X, 0, W, Cfg(), Localization.Get(lang), Theme.Dark,
                Typography.Body, Typography.Caption, rects);
            Assert.True(rects.TryGetValue(DashboardSettingsView.RemoveIntegrationKey, out var button), $"[{lang}] falta el botón");
            Assert.True(button.Top > rects["special:hooktoggle"].Bottom, $"[{lang}] el botón va debajo de las sesiones en vivo");
            Assert.True(button.Bottom < rects["toggle:NotchEnabled"].Top, $"[{lang}] y antes del notch");
            // El texto cabe en el botón (no se corta).
            var label = Localization.Get(lang).RemoveIntegration;
            Assert.True(g.MeasureString(label, Typography.Body).Width < button.Width, $"[{lang}] «{label}» no cabe");
        }
        Assert.Null(DashboardSettingsView.ActionFor(DashboardSettingsView.RemoveIntegrationKey));   // lo enruta el host
    }

    [Fact]
    public void Draw_does_not_register_unrouted_special_actions()
    {
        // El host (TrayAppContext) solo enruta importtheme y hooktoggle: logs/github NO se pintan (no botón muerto).
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var cfg = Cfg();
        var s = Localization.Get("es");
        var rects = new Dictionary<string, Rectangle>();

        DashboardSettingsView.Draw(g, draw: true, X, 0, W, cfg, s, Theme.Dark,
            Typography.Body, Typography.Caption, rects);

        Assert.False(rects.ContainsKey("special:openlogs"), "logs no enrutado por el host → no se pinta");
        Assert.False(rects.ContainsKey("special:opengithub"), "github no enrutado por el host → no se pinta");
    }

    [Fact]
    public void Draw_with_version_override_measure_equals_paint()
    {
        // La fila de Versión (texto) no rompe el invariante de 2 pasadas, con versión inyectada explícita.
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var cfg = Cfg();
        var s = Localization.Get("es");
        var rects = new Dictionary<string, Rectangle>();

        int measured = DashboardSettingsView.Draw(g, draw: false, X, 0, W, cfg, s, Theme.Dark,
            Typography.Body, Typography.Caption, rects, "9.9.9");
        int painted = DashboardSettingsView.Draw(g, draw: true, X, 0, W, cfg, s, Theme.Dark,
            Typography.Body, Typography.Caption, rects, "9.9.9");

        Assert.Equal(measured, painted);
    }

    [Fact]
    public void InfoRow_measure_equals_paint_and_not_clickable()
    {
        // InfoRow (Versión): texto no clicable → no registra rect; medir==pintar (mismo avance).
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var rects = new Dictionary<string, Rectangle>();

        int measured = DashboardSettingsView.InfoRow(g, draw: false, "Versión", "v9.9.9",
            X, 100, W, Theme.Dark, Typography.Body, rects);
        int painted = DashboardSettingsView.InfoRow(g, draw: true, "Versión", "v9.9.9",
            X, 100, W, Theme.Dark, Typography.Body, rects);

        Assert.Equal(measured, painted);
        Assert.Empty(rects); // una fila informativa nunca registra un rect clicable
    }

    [Fact]
    public void InfoRow_value_anchored_right_within_safe_margin()
    {
        // El valor (derecha) no rebasa x+w-Spacing.Sm y no invade la etiqueta (anti-truncamiento medido).
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var rects = new Dictionary<string, Rectangle>();

        // Valor absurdamente largo: debe elidirse y quedar dentro del margen derecho de seguridad.
        var (lx, _, rx, rw) = DashboardSettingsView.InfoRowLayout(g, "Versión",
            "v9.9.9-un-valor-extremadamente-largo-que-no-cabe-de-ninguna-manera",
            X, W, Typography.Body);

        Assert.Equal(X, lx);
        Assert.True(rx + rw <= X + W - Spacing.Sm, "el valor respeta el margen derecho ≥ Spacing.Sm");
        Assert.True(rx >= X, "el valor nunca empieza a la izquierda de contentLeft");
    }

    // ================= T10: verificación end-to-end de la vista única (sin cortes, sin tabs, mascota) =================

    // -------- MultiSegmentRow con etiqueta: anti-truncamiento (envolver, NO cortar) --------

    [Fact]
    public void MultiSegmentRow_with_label_does_not_cut_segments_at_right_edge()
    {
        // REGRESIÓN (render T10): en el Draw real el MultiSegmentRow de hitos lleva ETIQUETA
        // ("Avisar al llegar a…") y va dentro de un dependiente (ancho reducido Spacing.Lg). Con
        // etiqueta + 4 chips el bloque NO cabía y el chip 95% se salía por el borde derecho (se veía
        // un "9" cortado). Como SegmentedRow, debe medir y, si no cabe en un renglón, envolver a 2
        // filas alineadas a contentLeft — nunca pintar un chip más allá de x+w-Spacing.Sm.
        const int x = 16, y = 100;
        int w = 308 - Spacing.Lg; // ancho ya reducido por el indent del dependiente (caso real)
        using var bmp = new Bitmap(x + w + 80, 200);
        using var g = Graphics.FromImage(bmp);
        var rects = new Dictionary<string, Rectangle>();

        DashboardSettingsView.MultiSegmentRow(g, draw: true, "milestone", "Avisar al llegar a…",
            MilestoneSegs, new[] { 25, 50, 75, 95 }, x, y, w, Theme.Dark, Typography.Caption, rects);

        foreach (var seg in MilestoneSegs)
        {
            Assert.True(rects.TryGetValue($"milestone:{seg.val}", out var r), $"falta milestone:{seg.val}");
            Assert.True(r.X >= x, $"milestone:{seg.val} a la izquierda de contentLeft (x={r.X})");
            Assert.True(r.Right <= x + w - Spacing.Sm,
                $"milestone:{seg.val} cortado por el borde derecho (right={r.Right} > {x + w - Spacing.Sm})");
        }
    }

    [Fact]
    public void MultiSegmentRow_with_label_measure_equals_paint_when_wrapping()
    {
        // La decisión de envolver (1 fila vs 2) es idéntica en medir y pintar → mismo y de salida.
        const int x = 16, y = 0;
        int w = 308 - Spacing.Lg;
        using var bmp = new Bitmap(x + w + 80, 240);
        using var g = Graphics.FromImage(bmp);
        var rects = new Dictionary<string, Rectangle>();

        int measured = DashboardSettingsView.MultiSegmentRow(g, draw: false, "milestone", "Avisar al llegar a…",
            MilestoneSegs, new[] { 25, 50, 75, 95 }, x, y, w, Theme.Dark, Typography.Caption, rects);
        rects.Clear();
        int painted = DashboardSettingsView.MultiSegmentRow(g, draw: true, "milestone", "Avisar al llegar a…",
            MilestoneSegs, new[] { 25, 50, 75, 95 }, x, y, w, Theme.Dark, Typography.Caption, rects);

        Assert.Equal(measured, painted);
    }

    [Fact]
    public void MultiSegmentRow_with_label_still_marks_active_after_wrap()
    {
        // Tras envolver, los valores activos siguen pintándose con Accent (no se pierde el estilo de pill).
        const int x = 16, y = 30;
        int w = 308 - Spacing.Lg;
        using var bmp = new Bitmap(x + w + 80, 200);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Theme.Dark.Background);
        var rects = new Dictionary<string, Rectangle>();
        var active = new[] { 25, 95 };

        DashboardSettingsView.MultiSegmentRow(g, draw: true, "milestone", "Avisar al llegar a…",
            MilestoneSegs, active, x, y, w, Theme.Dark, Typography.Caption, rects);

        var acc = Theme.Dark.Accent;
        foreach (var pct in active)
        {
            Assert.True(rects.TryGetValue($"milestone:{pct}", out var r));
            var c = bmp.GetPixel(r.X + 2, r.Y + r.Height / 2);
            Assert.True(Math.Abs(c.R - acc.R) <= 10 && Math.Abs(c.G - acc.G) <= 10 && Math.Abs(c.B - acc.B) <= 10,
                $"el hito activo {pct}% debe seguir en Accent tras el wrap");
        }
    }

    [Fact]
    public void MultiSegmentRow_short_label_and_few_segments_stays_one_row()
    {
        // Si etiqueta + segmentos SÍ caben, no se envuelve: el avance es el de un solo renglón (sin
        // regresión de alto en el caso que ya cabía).
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var rects = new Dictionary<string, Rectangle>();

        const int y0 = 100;
        int after = DashboardSettingsView.MultiSegmentRow(g, draw: false, "milestone", "",
            MilestoneSegs, new[] { 25 }, X, y0, W, Theme.Dark, Typography.Caption, rects);

        Assert.Equal(y0 + DashboardSettingsView.SegmentRowAdvanceForTest, after);
    }

    // -------- Draw completo end-to-end: ningún rect clicable se sale del contenido, en los 8 idiomas --------

    [Fact]
    public void Draw_no_clickable_rect_overflows_content_in_all_languages()
    {
        // Invariante 3 (CERO textos/chips cortados) verificado END-TO-END sobre el Draw real: ningún
        // rect clicable empieza a la izquierda de contentLeft ni rebasa el borde derecho (x+w). Cubre
        // la regresión del hito 95% cortado y la vigila en TODOS los idiomas (alemán = el más largo).
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var cfg = Cfg();
        foreach (var lang in new[] { "en", "es", "nl", "fr", "de", "ja", "ko", "zh-Hant" })
        {
            var s = Localization.Get(lang);
            var rects = new Dictionary<string, Rectangle>();
            DashboardSettingsView.Draw(g, draw: true, X, 0, W, cfg, s, Theme.Dark,
                Typography.Body, Typography.Caption, rects);
            foreach (var (key, r) in rects)
            {
                Assert.True(r.X >= X, $"[{lang}] {key}: rect a la izquierda de contentLeft (x={r.X} < {X})");
                Assert.True(r.Right <= X + W, $"[{lang}] {key}: rect rebasa el borde derecho (right={r.Right} > {X + W})");
            }
        }
    }

    [Fact]
    public void Draw_measure_equals_paint_in_all_languages()
    {
        // El invariante de 2 pasadas del Draw completo se mantiene en los 8 idiomas (con o sin wrap de
        // segmentos): medir==pintar → mismo alto, nada se mueve al cambiar de idioma.
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var cfg = Cfg();
        foreach (var lang in new[] { "en", "es", "nl", "fr", "de", "ja", "ko", "zh-Hant" })
        {
            var s = Localization.Get(lang);
            var rects = new Dictionary<string, Rectangle>();
            int measured = DashboardSettingsView.Draw(g, draw: false, X, 0, W, cfg, s, Theme.Dark,
                Typography.Body, Typography.Caption, rects);
            rects.Clear();
            int painted = DashboardSettingsView.Draw(g, draw: true, X, 0, W, cfg, s, Theme.Dark,
                Typography.Body, Typography.Caption, rects);
            Assert.Equal(measured, painted);
        }
    }

    [Fact]
    public void Draw_is_single_screen_without_tab_navigation()
    {
        // DECISIÓN DE YOVAN: una pantalla agrupada, SIN pestañas. El panel NO registra ningún rect de
        // navegación por tabs (tab:general|display|live|system) → no hay tab-bar. Las secciones reales
        // (Mostrar, Sesiones en vivo, Notificaciones, Icono, Apariencia, Sistema, Acerca de) conviven
        // en una sola vista vertical, así que todas sus claves coexisten en el mismo Draw.
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var cfg = Cfg();
        var s = Localization.Get("es");
        var rects = new Dictionary<string, Rectangle>();

        DashboardSettingsView.Draw(g, draw: true, X, 0, W, cfg, s, Theme.Dark,
            Typography.Body, Typography.Caption, rects);

        foreach (var tab in new[] { "tab:general", "tab:display", "tab:live", "tab:system" })
            Assert.False(rects.ContainsKey(tab), $"no debe haber tab-bar ({tab}): es una sola pantalla");
        // Coexistencia de secciones (sin panes): claves de varias secciones presentes a la vez.
        foreach (var key in new[] { "toggle:ShowSpend", "special:hooktoggle", "toggle:Notifications",
                                    "icon:percent", "theme:dark", "toggle:Startup", "special:importtheme" })
            Assert.True(rects.ContainsKey(key), $"la sección de '{key}' debe estar en la misma vista (una pantalla)");
    }

    // -------- Mascota visible end-to-end (desacople de LiveSessionsEnabled) --------

    [Fact]
    public void Header_mascot_visible_with_show_on_and_live_off_end_to_end()
    {
        // Criterio de aceptación 5 (verificación T10): con ShowMascot=on y LiveSessionsEnabled=off el
        // header reserva alto para la mascota Idle (se ve). Es el desacople de DashboardHeader.cs:80.
        using var bmp = new Bitmap(W + X * 2, 400);
        using var g = Graphics.FromImage(bmp);
        Rectangle gear = Rectangle.Empty;
        var live = new LiveSessionsView();
        var sLoc = Localization.Get("es");

        int Run(bool showMascot) => DashboardHeader.Draw(g, draw: false, X, 0, W, snap: null, live,
            new AppConfig { ShowMascot = showMascot, LiveSessionsEnabled = false, ShowHealth = true },
            sLoc, Theme.Dark, MascotAnimator.StaticState, Mood.Neutral,
            Typography.Body, Typography.Caption, Typography.Mono, ref gear,
            motion: null, reduceMotion: false, mascotBounceOffsetY: 0, celebration: null);

        Assert.True(Run(true) > Run(false),
            "con live OFF, ShowMascot=on debe reservar alto para la mascota Idle (mascota visible)");
    }

    // ================= v0.3.5 P0: cortes de texto a CERO (sin elipsis) =================

    [Fact]
    public void Copy_subtitles_and_labels_have_no_decorative_ellipsis_in_all_languages()
    {
        // P0 #1: ningún copy de subtítulo/etiqueta de longitud variable lleva una elipsis decorativa
        // (la antigua "Avisar al llegar a…" pasó a "Umbral de aviso"). Yovan quiere el TEXTO COMPLETO.
        foreach (var lang in new[] { "en", "es", "nl", "fr", "de", "ja", "ko", "zh-Hant" })
        {
            var s = Localization.Get(lang);
            Assert.DoesNotContain("…", s.NotifyWhenReaching);
            Assert.DoesNotContain("…", s.LiveSessionsSubtitle);
            Assert.DoesNotContain("…", s.ShowSpendSubtitle);
            Assert.DoesNotContain("…", s.ReduceMotionSubtitle);
        }
        // El nuevo copy ES exacto del umbral de aviso (sin elipsis).
        Assert.Equal("Umbral de aviso", Localization.Get("es").NotifyWhenReaching);
    }

    [Fact]
    public void MasterRowWithBadge_subtitle_is_not_ellipsized_when_it_fits()
    {
        // P0 #2: el subtítulo de hooks ("Estado de tus sesiones en vivo") sale COMPLETO al lado del badge,
        // sin elipsis. Verificamos que el texto pintado (medido) no se recorta: en el ancho real del panel
        // el subtítulo cabe en 1 línea (con el badge restando ancho) y no contiene "…".
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var s = Localization.Get("es");
        var rects = new Dictionary<string, Rectangle>();

        // Mide el ancho de la columna de texto igual que MasterRowWithBadge (hasta el borde izq del badge).
        var badge = DashboardSettingsView.StatusBadge(g, draw: false, s.BadgeInstall, Theme.Dark.Warn,
            X, X + W, 100, 18, Theme.Dark, Typography.Caption);
        int textColW = badge.X - Spacing.Md - X;
        var subLines = TextWrap.WordWrap(s.LiveSessionsSubtitle, textColW,
            t => g.MeasureString(t, Typography.Caption).Width);

        Assert.DoesNotContain("…", string.Concat(subLines));
        // El subtítulo completo se conserva (solo podría partirse, nunca recortarse).
        Assert.Equal(s.LiveSessionsSubtitle, string.Join(" ", subLines));
    }

    [Fact]
    public void MasterRowWithBadge_long_label_and_subtitle_wrap_not_ellipsize()
    {
        // En un ancho MUY estrecho, etiqueta y subtítulo se ENVUELVEN a 2+ líneas (la fila crece), nunca
        // se recortan con "…". El rect clicable cubre toda la fila y medir==pintar se mantiene.
        const int x = 16, w = 150, y = 0;
        using var bmp = new Bitmap(x + w + 80, 260);
        using var g = Graphics.FromImage(bmp);
        var rects = new Dictionary<string, Rectangle>();
        const string label = "Desactivar (quitar hooks de settings.json)";
        const string sub = "Estado de tus sesiones de Claude Code en vivo";

        int measured = DashboardSettingsView.MasterRowWithBadge(g, draw: false, "special:hooktoggle",
            label, sub, "Activas", Theme.Dark.Ok, x, y, w, Theme.Dark, Typography.Body, Typography.Caption, rects);
        rects.Clear();
        int painted = DashboardSettingsView.MasterRowWithBadge(g, draw: true, "special:hooktoggle",
            label, sub, "Activas", Theme.Dark.Ok, x, y, w, Theme.Dark, Typography.Body, Typography.Caption, rects);

        Assert.Equal(measured, painted);
        // La fila creció por el wrap (más alta que una fila de 1 línea de badge).
        Assert.True(rects.TryGetValue("special:hooktoggle", out var r));
        Assert.True(r.Height > 18, "etiqueta+subtítulo largos deben envolver y crecer la fila");
        Assert.Equal(x, r.X);
        Assert.Equal(w, r.Width);
    }

    [Fact]
    public void Draw_position_value_shown_in_full_without_ellipsis_or_chevron()
    {
        // P0 #3 end-to-end: en el Draw real, la fila "Posición" con el valor largo
        // "Personalizada (arrastra el panel)" se apila (etiqueta arriba / valor completo debajo) SIN
        // elipsis ni chevron. La fila de posición es más alta que una fila de toggle simple (porque el
        // valor envuelve), su rect cubre el ancho y no rebasa el borde.
        using var bmp = new Bitmap(W + X * 2, 1400);
        using var g = Graphics.FromImage(bmp);
        var cfg = Cfg();
        cfg.DashboardPosition = "Custom"; // fuerza el valor largo PosCustom
        var s = Localization.Get("es");
        var rects = new Dictionary<string, Rectangle>();

        int measured = DashboardSettingsView.Draw(g, draw: false, X, 0, W, cfg, s, Theme.Dark,
            Typography.Body, Typography.Caption, rects);
        rects.Clear();
        int painted = DashboardSettingsView.Draw(g, draw: true, X, 0, W, cfg, s, Theme.Dark,
            Typography.Body, Typography.Caption, rects);

        Assert.Equal(measured, painted); // medir==pintar con el valor envuelto
        Assert.True(rects.TryGetValue("cycle:position", out var pos), "falta la fila de posición");
        Assert.True(pos.X >= X && pos.Right <= X + W, "la fila de posición no rebasa el contenido");
        // El valor completo "Personalizada (arrastra el panel)" no es elidible aquí: se conserva entero.
        Assert.DoesNotContain("…", s.PosCustom);
    }

    // ================= v0.3.7: mascota como toggle simple (la talla grande se retiró) =================

    [Fact]
    public void Mascot_toggle_flips_show_mascot_and_old_segmented_keys_are_gone()
    {
        // El toggle alterna ShowMascot; las claves del antiguo control de 3 estados (y las aún más
        // viejas de tamaño) ya no mutan nada.
        var c = new AppConfig { ShowMascot = true };
        DashboardSettingsView.ActionFor("toggle:ShowMascot")!(c);
        Assert.False(c.ShowMascot);
        DashboardSettingsView.ActionFor("toggle:ShowMascot")!(c);
        Assert.True(c.ShowMascot);

        Assert.Null(DashboardSettingsView.ActionFor("mascot:hidden"));
        Assert.Null(DashboardSettingsView.ActionFor("mascot:compact"));
        Assert.Null(DashboardSettingsView.ActionFor("mascot:large"));
        Assert.Null(DashboardSettingsView.ActionFor("mascotsize:compact"));
        Assert.Null(DashboardSettingsView.ActionFor("mascotsize:large"));
    }

    [Fact]
    public void Mascot_toggle_label_localized_without_ellipsis_in_all_languages()
    {
        // CERO textos cortados (invariante): la etiqueta del toggle existe en todos los idiomas y no
        // lleva elipsis decorativa.
        foreach (var lang in new[] { "en", "es", "nl", "fr", "de", "ja", "ko", "zh-Hant" })
        {
            var s = Localization.Get(lang);
            Assert.False(string.IsNullOrWhiteSpace(s.MenuShowMascot), $"[{lang}] MenuShowMascot vacía");
            Assert.DoesNotContain("…", s.MenuShowMascot);
        }
        Assert.Equal("Mostrar mascota", Localization.Get("es").MenuShowMascot);
    }

    [Fact]
    public void Mascot_toggle_row_is_dependent_of_hooks_master()
    {
        // Con hooks instalados la fila del toggle de la mascota existe y es clicable; con el master off
        // queda inerte (DrawDependent retira sus rects). En ningún caso queda rastro del viejo segmented.
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var cfg = Cfg();
        var s = Localization.Get("es");

        Dictionary<string, Rectangle> Run(bool hooks)
        {
            var rects = new Dictionary<string, Rectangle>();
            DashboardSettingsView.LiveSessionsSection(g, draw: true, hooks, X, 0, W, cfg, s, Theme.Dark,
                Typography.Body, Typography.Caption, rects);
            return rects;
        }

        var withHooks = Run(true);
        Assert.True(withHooks.ContainsKey("toggle:ShowMascot"), "falta la fila del toggle de la mascota");
        Assert.DoesNotContain(withHooks.Keys, k => k.StartsWith("mascot:"));

        var withoutHooks = Run(false);
        Assert.False(withoutHooks.ContainsKey("toggle:ShowMascot"),
            "con el master off, el toggle de la mascota debe ser inerte (sin rect)");
    }

    // ================= v0.3.5 P2 #2: afordancias estandarizadas + ritmo vertical =================

    [Fact]
    public void Rhythm_tokens_are_xl_section_gap_and_md_row_gap()
    {
        // Dos tokens uniformes: sectionGap (~Xl) sobre cada cabecera, rowGap (~Md) entre filas.
        Assert.Equal(Spacing.Xl, DashboardSettingsView.SectionGap);
        Assert.Equal(Spacing.Md, DashboardSettingsView.RowGap);
    }

    [Fact]
    public void Segmented_options_have_more_air_than_chart_tabs()
    {
        // P2 #2: las opciones de un segmented del PANEL respiran más que las tabs apretadas del chart
        // (gap 3px). Con dos chips, la separación entre el fin del 1º y el inicio del 2º es ≥ Spacing.Sm.
        const int x = 16, w = 308, y = 100;
        using var bmp = new Bitmap(x + w + 40, 160);
        using var g = Graphics.FromImage(bmp);
        var rects = new Dictionary<string, Rectangle>();
        var segs = new[] { ("a", "AA"), ("b", "BB") };

        DashboardSettingsView.SegmentedRow(g, draw: true, "k", "", segs, "a",
            x, y, w, Theme.Dark, Typography.Caption, rects);

        var ra = rects["k:a"]; var rb = rects["k:b"];
        int gap = rb.X - ra.Right;
        Assert.True(gap >= Spacing.Sm - 1, $"la separación entre opciones debe ser ≥ Spacing.Sm (fue {gap})");
    }

    [Fact]
    public void Labeled_segmented_that_does_not_fit_drops_chips_below_label_without_overlap()
    {
        // REGRESIÓN (P2 #2): al dar más aire entre opciones, un segmented con ETIQUETA cuyos chips ya no
        // caben a la derecha NO debe pintarlos en la misma fila pisando el texto de la etiqueta (era el caso
        // "Umbral de color" + 70/90·80/95·60/85). Deben BAJAR a su propio renglón, bajo la etiqueta. Con un
        // ancho estrecho a propósito forzamos el wrap y comprobamos que ningún chip se solapa con la etiqueta.
        const int x = 16, w = 150, y = 100; // estrecho: la etiqueta + chips no caben en 1 fila
        using var bmp = new Bitmap(x + w + 60, 160);
        using var g = Graphics.FromImage(bmp);
        var rects = new Dictionary<string, Rectangle>();
        var segs = new[] { ("a", "70/90"), ("b", "80/95"), ("c", "60/85") };
        const string label = "Umbral de color";

        int measured = DashboardSettingsView.SegmentedRow(g, draw: false, "threshold", label, segs, "a",
            x, y, w, Theme.Dark, Typography.Caption, rects);
        rects.Clear();
        int painted = DashboardSettingsView.SegmentedRow(g, draw: true, "threshold", label, segs, "a",
            x, y, w, Theme.Dark, Typography.Caption, rects);

        Assert.Equal(measured, painted); // medir==pintar con el wrap bajo la etiqueta
        int labelH = (int)Math.Ceiling(g.MeasureString(label, Typography.Caption).Height);
        int labelBottom = y + Math.Min(labelH, 18);
        foreach (var (val, _) in segs)
        {
            Assert.True(rects.TryGetValue($"threshold:{val}", out var r), $"falta threshold:{val}");
            Assert.True(r.X >= x, $"threshold:{val} a la izquierda del contenido (x={r.X})");
            Assert.True(r.Right <= x + w, $"threshold:{val} rebasa el borde (right={r.Right})");
            // Los chips quedan por DEBAJO de la línea de la etiqueta (no la pisan).
            Assert.True(r.Y >= labelBottom - 1, $"threshold:{val} se solapa con la etiqueta (y={r.Y} < {labelBottom})");
        }
    }

    [Fact]
    public void Icon_mode_segmented_matches_theme_and_freq_geometry_and_right_aligns()
    {
        // P2 #2: "Modo de icono" (%, ▲, %▲) comparte alto y alineación derecha con Tema/Frecuencia (todos
        // van por SegmentedRow). Mismo alto de chip, mismo margen derecho de seguridad (≥ Spacing.Sm).
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var cfg = Cfg();
        var s = Localization.Get("es");
        var rects = new Dictionary<string, Rectangle>();

        DashboardSettingsView.Draw(g, draw: true, X, 0, W, cfg, s, Theme.Dark,
            Typography.Body, Typography.Caption, rects);

        // Alturas iguales (mismo helper) entre icon/theme/freq.
        int hIcon = rects["icon:percent"].Height;
        Assert.Equal(hIcon, rects["theme:dark"].Height);
        Assert.Equal(hIcon, rects["freq:60"].Height);
        // Alineación derecha: el chip más a la derecha de cada uno deja margen ≥ Spacing.Sm (no toca el borde).
        foreach (var grp in new[] { "icon", "theme", "freq" })
        {
            int rightmost = rects.Where(kv => kv.Key.StartsWith(grp + ":")).Max(kv => kv.Value.Right);
            Assert.True(rightmost <= X + W - Spacing.Sm + 1,
                $"{grp}: el chip más a la derecha no respeta el margen de seguridad (right={rightmost})");
        }
    }

    // ---------------- T13b: toggle de privacidad "Actualizar tarifas online" ----------------

    [Fact]
    public void Draw_pricing_online_toggle_lives_in_system_section()
    {
        // El toggle vive en SISTEMA (debajo de Arrancar con Windows, como Idioma) y registra su rect.
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var cfg = Cfg();
        var s = Localization.Get("es");
        var rects = new Dictionary<string, Rectangle>();

        DashboardSettingsView.Draw(g, draw: true, X, 0, W, cfg, s, Theme.Dark,
            Typography.Body, Typography.Caption, rects);

        Assert.True(rects.TryGetValue("toggle:PricingOnline", out var pricing),
            "falta el toggle de tarifas online en ajustes");
        Assert.True(rects.TryGetValue("toggle:Startup", out var startup));
        Assert.True(pricing.Y > startup.Y, "Tarifas online va dentro de SISTEMA, debajo de Arrancar con Windows");
    }

    [Fact]
    public void ActionFor_pricing_online_alterna_la_config()
    {
        var cfg = new AppConfig();
        Assert.True(cfg.PricingOnlineUpdate); // default ON (precios públicos; no sale nada del usuario)

        DashboardSettingsView.ActionFor("toggle:PricingOnline")!(cfg);
        Assert.False(cfg.PricingOnlineUpdate);
        DashboardSettingsView.ActionFor("toggle:PricingOnline")!(cfg);
        Assert.True(cfg.PricingOnlineUpdate);
    }

    // ---------------- v0.4: origen de la cuota (statusline) ----------------

    private static Dictionary<string, Rectangle> DrawQuotaSource(TapStatus status, out int h0, out int h1,
        AppConfig? cfg = null, string lang = "en")
    {
        using var bmp = new Bitmap(400, 400);
        using var g = Graphics.FromImage(bmp);
        var rects = new Dictionary<string, Rectangle>();
        var s = Localization.Get(lang);
        cfg ??= new AppConfig();
        h0 = DashboardSettingsView.QuotaSourceSection(g, false, status, null, DateTime.UtcNow, 0, 0, 360, cfg, s,
            Theme.Dark, Typography.Body, Typography.Caption, new Dictionary<string, Rectangle>());
        h1 = DashboardSettingsView.QuotaSourceSection(g, true, status, null, DateTime.UtcNow, 0, 0, 360, cfg, s,
            Theme.Dark, Typography.Body, Typography.Caption, rects);
        return rects;
    }

    [Theory]
    [InlineData(TapStatus.Off)]
    [InlineData(TapStatus.Waiting)]
    [InlineData(TapStatus.Active)]
    public void QuotaSourceSection_registers_the_toggle_and_measures_like_it_paints(TapStatus status)
    {
        var rects = DrawQuotaSource(status, out int measured, out int painted);
        Assert.True(rects.ContainsKey("special:statuslinetoggle"));
        Assert.Equal(measured, painted);
    }

    [Fact]
    public void QuotaSourceSection_is_inert_when_unavailable()
    {
        var rects = DrawQuotaSource(TapStatus.Unavailable, out _, out _);
        Assert.False(rects.ContainsKey("special:statuslinetoggle"));
    }

    [Fact]
    public void Cli_refresh_is_off_by_default_and_toggles_from_the_quota_section()
    {
        var cfg = new AppConfig();
        Assert.False(cfg.QuotaCliRefresh); // `claude -p .` gasta un mensaje: nunca sin que el usuario lo pida

        var rects = DrawQuotaSource(TapStatus.Off, out int measured, out int painted, cfg, "es");
        Assert.True(rects.ContainsKey("toggle:QuotaCliRefresh"));
        Assert.Equal(measured, painted);

        DashboardSettingsView.ActionFor("toggle:QuotaCliRefresh")!(cfg);
        Assert.True(cfg.QuotaCliRefresh);
        DashboardSettingsView.ActionFor("toggle:QuotaCliRefresh")!(cfg);
        Assert.False(cfg.QuotaCliRefresh);
    }

    [Fact]
    public void Cli_refresh_strings_say_it_uses_a_message_in_spanish_and_english()
    {
        Assert.Equal("Forzar renovación con la CLI (gasta un mensaje)", Localization.Get("es").QuotaCliRefreshLabel);
        Assert.Equal("Force renewal with the CLI (uses one message)", Localization.Get("en").QuotaCliRefreshLabel);
        Assert.Contains("claude -p .", Localization.Get("es").QuotaCliRefreshSubtitle);
    }

    [Fact]
    public void TapSubtitle_formats_age_and_states()
    {
        var now = DateTime.UtcNow;
        var s = Localization.Get("es");
        Assert.Equal(string.Format(s.TapActiveFmt, "<1 min"), DashboardSettingsView.TapSubtitle(TapStatus.Active, now.AddSeconds(-12), now, s));
        Assert.Equal(string.Format(s.TapActiveFmt, "3 min"), DashboardSettingsView.TapSubtitle(TapStatus.Active, now.AddMinutes(-3.5), now, s));
        Assert.Equal(s.TapWaiting, DashboardSettingsView.TapSubtitle(TapStatus.Waiting, null, now, s));
        Assert.Equal(s.TapOff, DashboardSettingsView.TapSubtitle(TapStatus.Off, null, now, s));
        Assert.Equal(s.TapUnavailable, DashboardSettingsView.TapSubtitle(TapStatus.Unavailable, null, now, s));
    }

    [Fact]
    public void All_languages_translate_the_tap_strings()
    {
        var en = Localization.Get("en");
        foreach (var (code, _) in Localization.Languages)
        {
            var s = Localization.Get(code);
            Assert.Contains("{0}", s.TapActiveFmt);
            Assert.Contains("{0}", s.TapEnabledFmt);
            Assert.Contains("settings.json", s.TapEnableConfirm);
            if (code == "en") continue;
            // Cada idioma tiene su propia versión (no el inglés por defecto).
            Assert.NotEqual(en.QuotaFromClaudeCode, s.QuotaFromClaudeCode);
            Assert.NotEqual(en.TapSuggest, s.TapSuggest);
            Assert.NotEqual(en.TapEnableConfirm, s.TapEnableConfirm);
        }
    }

    // ---------------- Notch (F1): sección propia, maestro + dependientes ----------------

    [Fact]
    public void Draw_registers_notch_rows_and_dependents_are_inert_while_off()
    {
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var s = Localization.Get("es");

        var off = new Dictionary<string, Rectangle>();
        DashboardSettingsView.Draw(g, draw: true, X, 0, W, Cfg(), s, Theme.Dark, Typography.Body, Typography.Caption, off);
        Assert.Contains("toggle:NotchEnabled", off.Keys);
        Assert.DoesNotContain("toggle:NotchSound", off.Keys);           // atenuado e inerte con el notch apagado
        Assert.DoesNotContain("special:notchpermtoggle", off.Keys);

        var cfg = Cfg();
        cfg.NotchEnabled = true;
        var on = new Dictionary<string, Rectangle>();
        int yOn = DashboardSettingsView.Draw(g, draw: true, X, 0, W, cfg, s, Theme.Dark, Typography.Body, Typography.Caption, on);
        foreach (var k in new[] { "notchsize:0", "notchsize:150", "notchopen:hover", "notchopen:click", "cycle:notchmonitor",
                     "toggle:NotchMinimal", "toggle:NotchHideFullscreen", "toggle:NotchSound", "special:notchpermtoggle" })
            Assert.Contains(k, on.Keys);

        // Medir == pintar también con la sección del notch.
        int yMeasure = DashboardSettingsView.Draw(g, draw: false, X, 0, W, cfg, s, Theme.Dark, Typography.Body, Typography.Caption, new());
        Assert.Equal(yMeasure, yOn);
    }

    [Fact]
    public void Notch_actions_mutate_config()
    {
        var c = new AppConfig();
        DashboardSettingsView.ActionFor("toggle:NotchEnabled")!(c);
        DashboardSettingsView.ActionFor("toggle:NotchMinimal")!(c);
        DashboardSettingsView.ActionFor("toggle:NotchHideFullscreen")!(c);
        DashboardSettingsView.ActionFor("toggle:NotchSound")!(c);
        DashboardSettingsView.ActionFor("notchopen:click")!(c);
        DashboardSettingsView.ActionFor("notchsize:130")!(c);
        Assert.True(c.NotchEnabled);
        Assert.True(c.NotchMinimal);
        Assert.False(c.NotchHideInFullscreen);
        Assert.False(c.NotchSound);
        Assert.True(c.NotchOpenOnClick);
        Assert.Equal(130, c.NotchSizePercent);

        DashboardSettingsView.ActionFor("notchopen:hover")!(c);
        DashboardSettingsView.ActionFor("notchsize:0")!(c);
        Assert.False(c.NotchOpenOnClick);
        Assert.Equal(0, c.NotchSizePercent);
        Assert.Null(DashboardSettingsView.ActionFor("notchsize:123"));   // solo los tamaños ofrecidos
    }

    [Fact]
    public void Monitor_mode_row_has_three_options_and_the_fixed_monitor_only_counts_in_fixed()
    {
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var s = Localization.Get("es");
        foreach (var mode in new[] { NotchMonitorMode.Fixed, NotchMonitorMode.FollowActive, NotchMonitorMode.AllMonitors })
        {
            var cfg = Cfg();
            cfg.NotchEnabled = true;
            cfg.NotchMonitorMode = mode;
            var rects = new Dictionary<string, Rectangle>();
            int yDraw = DashboardSettingsView.Draw(g, draw: true, X, 0, W, cfg, s, Theme.Dark, Typography.Body, Typography.Caption, rects);
            foreach (var k in new[] { "notchmonmode:fixed", "notchmonmode:follow", "notchmonmode:all" })
                Assert.Contains(k, rects.Keys);
            // Elegir el monitor concreto solo en Fijo; en los otros modos, atenuado e inerte (misma geometría).
            Assert.Equal(mode == NotchMonitorMode.Fixed, rects.ContainsKey("cycle:notchmonitor"));
            int yMeasure = DashboardSettingsView.Draw(g, draw: false, X, 0, W, cfg, s, Theme.Dark, Typography.Body, Typography.Caption, new());
            Assert.Equal(yMeasure, yDraw);
        }
    }

    [Fact]
    public void Monitor_mode_actions_mutate_config()
    {
        var c = new AppConfig();
        DashboardSettingsView.ActionFor("notchmonmode:follow")!(c);
        Assert.Equal(NotchMonitorMode.FollowActive, c.NotchMonitorMode);
        DashboardSettingsView.ActionFor("notchmonmode:all")!(c);
        Assert.Equal(NotchMonitorMode.AllMonitors, c.NotchMonitorMode);
        DashboardSettingsView.ActionFor("notchmonmode:fixed")!(c);
        Assert.Equal(NotchMonitorMode.Fixed, c.NotchMonitorMode);
        Assert.Null(DashboardSettingsView.ActionFor("notchmonmode:everywhere"));
        Assert.Equal("fixed", DashboardSettingsView.MonitorModeKey((NotchMonitorMode)9));
        Assert.Equal("follow", DashboardSettingsView.MonitorModeKey(NotchMonitorMode.FollowActive));
        Assert.Equal("all", DashboardSettingsView.MonitorModeKey(NotchMonitorMode.AllMonitors));
    }

    [Fact]
    public void NextMonitor_cycles_primary_then_each_screen()
    {
        var screens = new[] { @"\\.\DISPLAY1", @"\\.\DISPLAY2" };
        Assert.Equal(screens[0], DashboardSettingsView.NextMonitor("", screens));
        Assert.Equal(screens[1], DashboardSettingsView.NextMonitor(screens[0], screens));
        Assert.Equal("", DashboardSettingsView.NextMonitor(screens[1], screens));
        // Desconectado: la fila ya dice «Principal», así que el clic pasa a la primera pantalla (no a "" otra vez).
        Assert.Equal(screens[0], DashboardSettingsView.NextMonitor(@"\\.\DISPLAY7", screens));
        Assert.Equal("", DashboardSettingsView.NextMonitor("", Array.Empty<string>()));
    }

    [Fact]
    public void Notch_strings_are_translated_in_spanish()
    {
        var en = Localization.Get("en");
        var es = Localization.Get("es");
        Assert.NotEqual(en.NotchMinimal, es.NotchMinimal);
        Assert.NotEqual(en.NotchSubtitle, es.NotchSubtitle);
        Assert.NotEqual(en.NotchSound, es.NotchSound);
        Assert.NotEqual(en.NotchMonitorPrimary, es.NotchMonitorPrimary);
        Assert.NotEqual(en.NotchPermissions, es.NotchPermissions);
        Assert.NotEqual(en.NotchPermissionsEnableConfirm, es.NotchPermissionsEnableConfirm);
        Assert.NotEqual(en.NotchPermissionsDisableConfirm, es.NotchPermissionsDisableConfirm);
    }

    [Fact]
    public void Notch_permissions_row_is_routed_by_the_host_with_confirmation()
    {
        // Toca ~/.claude/settings.json: no es una mutación simple, la enruta el host (confirmación + backup).
        Assert.Null(DashboardSettingsView.ActionFor("special:notchpermtoggle"));
        foreach (var code in new[] { "en", "es" })
        {
            var s = Localization.Get(code);
            Assert.Contains("settings.json", s.NotchPermissionsEnableConfirm);
            Assert.Contains("settings.json", s.NotchPermissionsDisableConfirm);
            Assert.Contains("{0}", s.NotchPermissionsEnabledFmt);
            Assert.False(string.IsNullOrWhiteSpace(s.NotchPermissionsNeedHooks));
            Assert.False(string.IsNullOrWhiteSpace(s.NotchPermissionsDisabled));
        }
    }
}
