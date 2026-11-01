using ClaudeBarWin.Services;
using ClaudeBarWin.UI;

namespace ClaudeBarWin.Tests;

public class TrayShapeTests
{
    [Fact]
    public void ShapeFor_ok_is_circle()
        => Assert.Equal(TrayShape.Circle, Tray.ShapeFor(UsageStatus.Ok));

    [Fact]
    public void ShapeFor_warn_is_triangle()
        => Assert.Equal(TrayShape.Triangle, Tray.ShapeFor(UsageStatus.Warn));

    [Fact]
    public void ShapeFor_critical_is_rhombus()
        => Assert.Equal(TrayShape.Rhombus, Tray.ShapeFor(UsageStatus.Critical));

    [Fact]
    public void Each_status_maps_to_a_distinct_shape()
    {
        var shapes = new[]
        {
            Tray.ShapeFor(UsageStatus.Ok),
            Tray.ShapeFor(UsageStatus.Warn),
            Tray.ShapeFor(UsageStatus.Critical)
        };
        Assert.Equal(3, shapes.Distinct().Count());
    }

    [Fact]
    public void Glyph_returns_a_single_char_for_each_status()
    {
        // El glifo de forma del dashboard es de 1 carácter junto al %.
        Assert.Equal(1, Tray.ShapeGlyph(TrayShape.Circle).Length);
        Assert.Equal(1, Tray.ShapeGlyph(TrayShape.Triangle).Length);
        Assert.Equal(1, Tray.ShapeGlyph(TrayShape.Rhombus).Length);
    }

    [Fact]
    public void TaskbarIsLight_does_not_throw()
    {
        // Lee el registro con fallback; nunca debe lanzar.
        var ex = Record.Exception(() => ThemeResolver.TaskbarIsLight());
        Assert.Null(ex);
    }

    [Fact]
    public void Render_with_status_and_stale_does_not_throw()
    {
        // La nueva firma de Render (status + stale) debe producir un icono sin lanzar.
        var ex = Record.Exception(() =>
        {
            using var ico = TrayIconRenderer.Render(
                68, Theme.Dark, 70, 90, UsageStatus.Warn, stale: true);
        });
        Assert.Null(ex);
    }

    // --- T9d (§3 #15): badge "pend" — forma suprimida, ámbar de paleta, sin tapar el dígito ---

    private const double Warn = 70, Crit = 90;

    private static (Bitmap b0, Bitmap b1) RenderPair(int pct, UsageStatus st)
    {
        using var icoBase = TrayIconRenderer.Render(pct, Theme.Dark, Warn, Crit, st, stale: false, pending: false);
        using var icoPend = TrayIconRenderer.Render(pct, Theme.Dark, Warn, Crit, st, stale: false, pending: true);
        return (icoBase.ToBitmap(), icoPend.ToBitmap());
    }

    private static double Dist(Color a, Color b)
        => Math.Sqrt((a.R - b.R) * (a.R - b.R) + (a.G - b.G) * (a.G - b.G) + (a.B - b.B) * (a.B - b.B));

    [Fact]
    public void Pending_suppresses_the_a11y_shape_overlay()
    {
        // Con el punto de notificación la forma a11y (rombo/triángulo) se SUPRIME: un solo elemento
        // decorativo por esquina — dígito + forma + punto a 16-32px se solapaban en ruido (§3 #15).
        var (b0, b1) = RenderPair(95, UsageStatus.Critical);
        using (b0)
        using (b1)
        {
            var fill = ColorMath.RiskColor(95, Theme.Dark, Warn, Crit);
            var ink = ColorMath.Contrast(fill);
            // Centro del rombo (s=14 anclado a la esquina inferior derecha del lienzo de 48).
            var shapePx = b0.GetPixel(40, 40);
            Assert.True(Dist(shapePx, ink) < 60, $"sin pending el rombo debe pintarse; fue {shapePx}");
            var pendPx = b1.GetPixel(40, 40);
            Assert.True(Dist(pendPx, ink) >= 60, $"con pending el rombo debe SUPRIMIRSE; fue {pendPx}");
        }
    }

    [Fact]
    public void Pending_dot_uses_palette_warn_amber_with_knockout_ring()
    {
        // El punto era #F5A623 (fuera de paleta; el .ico antiguo lo aplanaba a oliva) con aro
        // #1A1A1A. Ahora: ámbar Warn del tema + aro KNOCKOUT (borrado a transparente) que lo separa
        // del relleno del badge sobre cualquier barra de tareas. Geometría espejo: d=10 en
        // (48-10-1, 1) → centro (42, 6), aro borrado a +2px.
        var (b0, b1) = RenderPair(10, UsageStatus.Ok);
        using (b0)
        using (b1)
        {
            var dotPx = b1.GetPixel(42, 6);
            var warn = Theme.Dark.Warn;
            Assert.True(Dist(dotPx, warn) < 25,
                $"el punto debe ser el ámbar Warn de la paleta {warn}, fue {dotPx}");
            // Aro knockout: entre el borde del punto (r=5) y el del borrado (r=7) queda TRANSPARENTE.
            var ringPx = b1.GetPixel(42 - 6, 6);
            Assert.True(ringPx.A < 64, $"el aro debe quedar perforado (alpha {ringPx.A})");
            // En el badge base esa zona era relleno opaco (el aro se NOTA).
            Assert.True(b0.GetPixel(42 - 6, 6).A > 200, "sin pending esa zona es relleno opaco");
        }
    }

    [Fact]
    public void Pending_dot_does_not_cover_the_digit()
    {
        // §3 #15: el punto de 18px tapaba el dígito. Ahora todo píxel que el modo pending CAMBIA
        // (punto + aro) debe caer fuera de la tinta del dígito: ningún píxel alterado era tinta
        // (ni su anti-aliasing fuerte) en el badge base. "88" = el par de dígitos más ancho.
        var (b0, b1) = RenderPair(88, UsageStatus.Ok);
        using (b0)
        using (b1)
        {
            var fill = ColorMath.RiskColor(88, Theme.Dark, Warn, Crit);
            var ink = ColorMath.Contrast(fill);
            var violations = new List<string>();
            for (int y = 0; y < b0.Height; y++)
                for (int x = 0; x < b0.Width; x++)
                {
                    var p0 = b0.GetPixel(x, y);
                    var p1 = b1.GetPixel(x, y);
                    if (p0.ToArgb() == p1.ToArgb()) continue;          // sin cambio
                    if (p0.A < 200) continue;                           // base transparente: no es tinta
                    if (Dist(p0, ink) < 100)                            // tinta o su AA fuerte
                        violations.Add($"({x},{y}) base={p0} pend={p1}");
                }
            Assert.True(violations.Count == 0,
                $"el punto pending pisa la tinta del dígito en {violations.Count} px: " +
                string.Join("; ", violations.Take(8)));
        }
    }

    // --- F8 (v039 g4): el punto "pend" recupera la forma daltónica del estado dentro del punto ---

    [Fact]
    public void Pending_critical_draws_a_shape_inside_the_attention_dot()
    {
        // Antes el punto de atención SUPRIMÍA toda forma → un Critical+pend (caso real: status+pending a
        // la vez) era distinguible SOLO por color, rompiendo la redundancia no-cromática que el resto de
        // estados sí tienen. Ahora la silueta del estado (◆ Critical) se dibuja DENTRO del punto, en el
        // color de contraste del ámbar. El centro del punto (42,6) debe pintarse con ese color de tinta,
        // no con el ámbar liso.
        var (b0, b1) = RenderPair(95, UsageStatus.Critical);
        using (b0)
        using (b1)
        {
            var ink = ColorMath.Contrast(Theme.Dark.Warn);
            var center = b1.GetPixel(42, 6);
            Assert.True(Dist(center, ink) < 60,
                $"el centro del punto debe llevar la silueta en color de contraste {ink}, fue {center}");
        }
    }

    [Fact]
    public void Pending_warn_draws_a_shape_inside_the_attention_dot()
    {
        // Warn+pend: triángulo dentro del punto (misma redundancia que el Critical).
        var (b0, b1) = RenderPair(75, UsageStatus.Warn);
        using (b0)
        using (b1)
        {
            var ink = ColorMath.Contrast(Theme.Dark.Warn);
            var center = b1.GetPixel(42, 6);
            Assert.True(Dist(center, ink) < 60,
                $"el centro del punto Warn debe llevar la silueta {ink}, fue {center}");
        }
    }

    [Fact]
    public void Pending_ok_keeps_the_dot_a_plain_amber_circle()
    {
        // Ok: el punto YA es un círculo → la redundancia no-cromática está cubierta y NO se dibuja glifo.
        // El centro del punto debe seguir siendo el ámbar liso (sin tinta de silueta encima).
        var (b0, b1) = RenderPair(10, UsageStatus.Ok);
        using (b0)
        using (b1)
        {
            var warn = Theme.Dark.Warn;
            var center = b1.GetPixel(42, 6);
            Assert.True(Dist(center, warn) < 25,
                $"el punto Ok no debe llevar silueta: centro debe ser ámbar {warn}, fue {center}");
        }
    }

    [Fact]
    public void Pending_shape_does_not_cover_the_digit()
    {
        // La silueta vive DENTRO del punto (esquina superior derecha), nunca sobre el dígito. El dígito
        // ocupa la banda central; las dos esquinas derechas son zonas de overlay legítimas (arriba: el
        // punto + su silueta; abajo: la forma a11y que el modo pending SUPRIME — pasa de tinta a relleno,
        // un cambio esperado que no es "tapar el dígito"). Verificamos que en la BANDA CENTRAL (fuera de
        // ambas esquinas) ningún píxel que pending cambia respecto al base era tinta del dígito.
        // "88" = el par de dígitos más ancho, con Critical para que la silueta ◆ esté presente.
        var (b0, b1) = RenderPair(88, UsageStatus.Critical);
        using (b0)
        using (b1)
        {
            var fill = ColorMath.RiskColor(88, Theme.Dark, Warn, Crit);
            var ink = ColorMath.Contrast(fill);
            var violations = new List<string>();
            for (int y = 0; y < b0.Height; y++)
                for (int x = 0; x < b0.Width; x++)
                {
                    bool topRightDot = x >= 34 && y <= 15;     // punto de atención + su silueta
                    bool bottomRightOverlay = x >= 32 && y >= 32; // forma a11y de esquina (suprimida con pend)
                    if (topRightDot || bottomRightOverlay) continue;
                    var p0 = b0.GetPixel(x, y);
                    var p1 = b1.GetPixel(x, y);
                    if (p0.ToArgb() == p1.ToArgb()) continue;
                    if (p0.A < 200) continue;
                    if (Dist(p0, ink) < 100)
                        violations.Add($"({x},{y}) base={p0} pend={p1}");
                }
            Assert.True(violations.Count == 0,
                $"la silueta pending pisa la tinta del dígito en {violations.Count} px: " +
                string.Join("; ", violations.Take(8)));
        }
    }

    // --- F9 (v039 g4): tamaño de fuente del badge tokenizado + fit-to-box para "99+" ---

    private static (int fontHeightPx, bool fits) GlyphMetrics(string text)
    {
        // Renderiza el badge y mide la caja de tinta del dígito (filas/columnas con tinta fuerte) sobre
        // el lienzo nativo de 48px. Sirve para comprobar que "99+" llena más caja que el 18px histórico.
        using var ico = TrayIconRenderer.Render(text == "99+" ? 120 : int.Parse(text),
            Theme.Dark, Warn, Crit, UsageStatus.Ok, stale: false, pending: false);
        using var b = ico.ToBitmap();
        var fill = ColorMath.RiskColor(text == "99+" ? 100 : int.Parse(text), Theme.Dark, Warn, Crit);
        var ink = ColorMath.Contrast(fill);
        int top = -1, bottom = -1;
        for (int y = 0; y < b.Height; y++)
            for (int x = 0; x < b.Width; x++)
                if (Dist(b.GetPixel(x, y), ink) < 90 && b.GetPixel(x, y).A > 180)
                {
                    if (top < 0) top = y;
                    bottom = y;
                    break;
                }
        int h = (top < 0) ? 0 : bottom - top + 1;
        return (h, h > 0 && top >= 0 && bottom < b.Height);
    }

    [Fact]
    public void NinetyNinePlus_fills_more_than_the_old_18px_literal()
    {
        // Antes "99+" se pintaba a un literal fijo de 18px (el peor caso de legibilidad). El fit-to-box
        // lo reescala (~21-24px) para llenar la caja del badge. La altura de tinta resultante debe
        // superar la que daría el 18px histórico: a 18px la cap-height de "99+" ≈ 12-13px; el fit la
        // sube por encima de 14px. Exigimos ≥ 14px de tinta y que quepa entera en el lienzo.
        var (h, fits) = GlyphMetrics("99+");
        Assert.True(fits, "la tinta de '99+' debe caber dentro del lienzo");
        Assert.True(h >= 14, $"'99+' debe llenar la caja (altura de tinta {h}px) por encima del 18px fijo");
    }

    /// <summary>Por cada columna x del lienzo nativo (48px), la EXTENSIÓN VERTICAL de tinta fuerte
    /// (bottom-top+1, ó 0 si no hay). Permite distinguir la silueta de un dígito (banda alta ≈ cap
    /// height) de la de un '+' (banda baja: solo el grosor del brazo/aspa).</summary>
    private static int[] ColumnInkSpans(Bitmap b, Color ink)
    {
        var spans = new int[b.Width];
        for (int x = 0; x < b.Width; x++)
        {
            int top = -1, bottom = -1;
            for (int y = 0; y < b.Height; y++)
            {
                var p = b.GetPixel(x, y);
                if (p.A > 180 && Dist(p, ink) < 90)
                {
                    if (top < 0) top = y;
                    bottom = y;
                }
            }
            spans[x] = top < 0 ? 0 : bottom - top + 1;
        }
        return spans;
    }

    private static (Bitmap bmp, Color ink) RenderBadgeBitmap(string text)
    {
        int pct = text == "99+" ? 120 : int.Parse(text);
        int fillPct = text == "99+" ? 100 : int.Parse(text);
        var ico = TrayIconRenderer.Render(pct, Theme.Dark, Warn, Crit, UsageStatus.Ok,
            stale: false, pending: false);
        using (ico)
        {
            var fill = ColorMath.RiskColor(fillPct, Theme.Dark, Warn, Crit);
            return (ico.ToBitmap(), ColorMath.Contrast(fill));
        }
    }

    [Fact]
    public void NinetyNinePlus_renders_the_plus_glyph_and_is_not_clipped()
    {
        // REGRESIÓN v039 g4 (el blocker): al subir la fuente de "99+" con el fit, se medía con un
        // StringFormat distinto del de dibujo (GenericTypographic vs el derivado de GenericDefault), la
        // cadena desbordaba el rectángulo de dibujo y GDI+ RECORTABA el '+' → el badge mostraba "99",
        // indistinguible de un 99% real (el peor resultado: anula el indicador de overflow). El test
        // viejo solo medía ALTURA de tinta y no lo detectaba.
        //
        // Detectamos el '+' por su FIRMA geométrica frente a un dígito: el '+' es mucho más BAJO que un
        // dígito (sus aspas no llegan a la cap-height). En un "99+" centrado, las columnas de tinta más
        // a la derecha son el aspa derecha del '+'; su extensión vertical debe ser CLARAMENTE menor que
        // la de los dígitos (≈ cap-height). Si el '+' estuviera recortado/ausente, la tinta más a la
        // derecha sería el borde de un '9' (banda alta) ó tocaría el límite del lienzo.
        var (b, ink) = RenderBadgeBitmap("99+");
        using (b)
        {
            var spans = ColumnInkSpans(b, ink);
            int right = Array.FindLastIndex(spans, s => s > 0);
            int left = Array.FindIndex(spans, s => s > 0);
            int maxSpan = spans.Max();                       // alto de los dígitos (cap-height ≈)

            Assert.True(right >= 0, "'99+' debe tener tinta");
            // No recortado: la tinta no llega al borde del lienzo (si GDI+ recortara, llegaría a x=47).
            Assert.True(right < b.Width - 1,
                $"el '+' no debe recortarse contra el borde del lienzo (tinta hasta x={right})");
            // El '+' presente a la derecha: la columna de tinta más a la derecha es un aspa del '+',
            // de banda BAJA (< 60% de la altura de un dígito). Un '9' recortado dejaría banda alta aquí.
            Assert.True(spans[right] < maxSpan * 0.6,
                $"la tinta más a la derecha debe ser el aspa baja del '+', no un dígito " +
                $"(span derecho {spans[right]} vs dígito {maxSpan})");
            // Y hay un hueco entre los dígitos y el '+': alguna columna en la mitad derecha del rango de
            // tinta tiene span pequeño/cero (el espacio antes del '+') → confirma glifo separado, no un
            // borde de dígito que llega al filo. Buscamos en [mid..right] una columna de span < 30% max.
            int mid = (left + right) / 2;
            bool gapBeforePlus = false;
            for (int x = mid; x <= right; x++)
                if (spans[x] < maxSpan * 0.3) { gapBeforePlus = true; break; }
            Assert.True(gapBeforePlus,
                "debe existir un hueco entre los dígitos y el '+' (glifo '+' separado, no recorte)");
        }
    }

    [Fact]
    public void Badge_measures_and_draws_with_the_same_metrics()
    {
        // La causa raíz del recorte fue medir el fit con un StringFormat distinto del de dibujo. La
        // sobrecarga pública FitFontPx(g, text, box) debe coincidir con la que recibe el sf real del
        // badge: si divergen, el fit miente sobre el ancho pintado. Aquí comprobamos que el tamaño
        // calculado con el sf por defecto (el que usa internamente) es el mismo que el de la firma larga.
        using var bmp = new Bitmap(48, 48);
        using var g = Graphics.FromImage(bmp);
        using var sf = new StringFormat(StringFormat.GenericDefault)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap
        };
        Assert.Equal(
            TrayIconRenderer.FitFontPx(g, "99+", 40f),
            TrayIconRenderer.FitFontPx(g, "99+", 40f, sf));
    }

    [Fact]
    public void FitFontPx_keeps_full_size_for_one_or_two_digits()
    {
        // Un número de 1-2 dígitos cabe de sobra en la caja → conserva el tamaño completo (no encoge).
        using var bmp = new Bitmap(48, 48);
        using var g = Graphics.FromImage(bmp);
        Assert.Equal(30f, TrayIconRenderer.FitFontPx(g, "8", 40f));
        Assert.Equal(30f, TrayIconRenderer.FitFontPx(g, "88", 40f));
    }

    [Fact]
    public void FitFontPx_shrinks_three_chars_but_stays_above_eighteen()
    {
        // "99+" no cabe a 30px en una caja de 40px → encoge, pero por encima del 18px histórico y por
        // debajo del tamaño de un número normal (queda en la franja legible ~22-24px).
        using var bmp = new Bitmap(48, 48);
        using var g = Graphics.FromImage(bmp);
        float px = TrayIconRenderer.FitFontPx(g, "99+", 40f);
        Assert.True(px > 18f, $"'99+' debe superar el 18px fijo histórico, fue {px:0.0}");
        Assert.True(px < 30f, $"'99+' debe encoger respecto a un número normal, fue {px:0.0}");
    }

    [Fact]
    public void FitFontPx_never_drops_below_the_eighteen_floor()
    {
        // Suelo de seguridad: aunque la caja sea minúscula, nunca por debajo de 18px.
        using var bmp = new Bitmap(48, 48);
        using var g = Graphics.FromImage(bmp);
        Assert.Equal(18f, TrayIconRenderer.FitFontPx(g, "99+", 2f));
    }
}
