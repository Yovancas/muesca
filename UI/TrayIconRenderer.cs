using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using ClaudeBarWin.Services;

namespace ClaudeBarWin.UI;

public enum UsageStatus
{
    Ok = 0,
    Warn = 1,
    Critical = 2
}

/// <summary>
/// Forma del indicador de estado, redundante con el color para accesibilidad (daltónicos):
/// Ok→círculo, Warn→triángulo, Critical→rombo. Misma semántica en el tray (overlay) y en el
/// dashboard (glifo de 1 carácter junto al %).
/// </summary>
public enum TrayShape
{
    Circle = 0,
    Triangle = 1,
    Rhombus = 2
}

/// <summary>
/// Mapeos puros y testeables del estado a forma/glifo. El estado por <b>forma</b> acompaña siempre
/// al color para que la señal sea legible también por quien no distingue colores.
/// </summary>
public static class Tray
{
    /// <summary>Forma del indicador para cada estado de cuota (Ok→círculo, Warn→triángulo, Critical→rombo).</summary>
    public static TrayShape ShapeFor(UsageStatus status) => status switch
    {
        UsageStatus.Critical => TrayShape.Rhombus,
        UsageStatus.Warn => TrayShape.Triangle,
        _ => TrayShape.Circle
    };

    /// <summary>Glifo de 1 carácter para pintar la forma junto al % en el dashboard.</summary>
    public static string ShapeGlyph(TrayShape shape) => shape switch
    {
        TrayShape.Rhombus => "◆",
        TrayShape.Triangle => "▲",
        _ => "●"
    };
}

/// <summary>
/// Todo lo que decide los píxeles de un badge (F3 Task 7), incluido su tamaño en px de dispositivo.
/// Es la clave de <see cref="TrayIconCache{T}"/>: misma especificación ⇒ mismo icono, sin otro HICON.
/// </summary>
public readonly record struct BadgeSpec(string Text, Color Bg, UsageStatus Status, bool Stale, bool Pending,
    Color PendingDot, bool TaskbarLight, int SizePx);

/// <summary>
/// Draws a small badge icon (percentage + status colour) for the system tray.
/// </summary>
public static class TrayIconRenderer
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int nIndex, uint dpi);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(Point pt, uint dwFlags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    private const int SM_CXSMICON = 49;
    private const uint MONITOR_DEFAULTTOPRIMARY = 1;
    private const int MDT_EFFECTIVE_DPI = 0;

    /// <summary>
    /// Lado (px) del icono pequeño que pinta la bandeja al DPI del monitor principal (donde vive la
    /// barra de tareas): <c>GetSystemMetricsForDpi(SM_CXSMICON, dpi)</c> — 16 a 100 %, 20 a 125 %, 24 a
    /// 150 %… Se consulta en cada refresco, así un cambio de escala sin reiniciar cambia la clave del
    /// icono. Si la API no está (o falla), 16.
    /// </summary>
    public static int SmallIconSizePx()
    {
        try
        {
            IntPtr mon = MonitorFromPoint(new Point(0, 0), MONITOR_DEFAULTTOPRIMARY);
            uint dpi = 96;
            if (mon != IntPtr.Zero && GetDpiForMonitor(mon, MDT_EFFECTIVE_DPI, out uint dx, out _) == 0 && dx > 0)
                dpi = dx;
            int px = GetSystemMetricsForDpi(SM_CXSMICON, dpi);
            if (px > 0) return px;
        }
        catch
        {
            // fuera de Windows o sin shcore: fallback abajo
        }
        return 16;
    }

    public static Icon Render(int percent, Color bg, bool pending = false)
    {
        percent = Math.Clamp(percent, 0, 999);
        // Sin tema a mano: el punto de atención usa el ámbar Warn de la paleta base oscura (T9d).
        return Render(new BadgeSpec(percent >= 100 ? "99+" : percent.ToString(), bg, UsageStatus.Ok, Stale: false,
            pending, Theme.Dark.Warn, TaskbarLight: false, BadgeSize));
    }

    /// <summary>Colorea el badge de forma continua por riesgo (Ok→Warn→Critical) según el tema y los umbrales.</summary>
    public static Icon Render(int percent, Theme theme, double warn, double crit, bool pending = false)
    {
        percent = Math.Clamp(percent, 0, 999);
        return Render(new BadgeSpec(percent >= 100 ? "99+" : percent.ToString(),
            ColorMath.RiskColor(percent, theme, warn, crit), UsageStatus.Ok, Stale: false, pending, theme.Warn,
            TaskbarLight: false, BadgeSize));
    }

    /// <summary>
    /// Especificación del badge con estado por forma + stale (la misma decisión que
    /// <see cref="Render(int, Theme, double, double, UsageStatus, bool, bool, bool)"/>) a
    /// <paramref name="sizePx"/> px. Sirve de clave de caché (F3 Task 7).
    /// </summary>
    public static BadgeSpec Spec(int percent, Theme theme, double warn, double crit,
        UsageStatus status, bool stale = false, bool pending = false, bool taskbarLight = false, int sizePx = BadgeSize)
    {
        percent = Math.Clamp(percent, 0, 999);
        var bg = ColorMath.RiskColor(percent, theme, warn, crit);
        return new BadgeSpec(percent >= 100 ? "99+" : percent.ToString(), bg, status, stale, pending, theme.Warn,
            taskbarLight, sizePx);
    }

    /// <summary>Especificación del badge neutro de error ("!") a <paramref name="sizePx"/> px.</summary>
    public static BadgeSpec SpecError(Color bg, bool pending = false, int sizePx = BadgeSize)
        => new("!", bg, UsageStatus.Ok, Stale: false, pending, Theme.Dark.Warn, TaskbarLight: false, sizePx);

    /// <summary>Pinta el badge de <paramref name="spec"/> al tamaño exacto que pide.</summary>
    public static Icon Render(BadgeSpec spec)
        => RenderBadge(spec.Text, spec.Bg, spec.Status, spec.Stale, spec.Pending, spec.PendingDot,
            spec.TaskbarLight, spec.SizePx);

    /// <summary>
    /// Badge con estado por forma (overlay) + estado stale. El texto adapta su contraste al fondo
    /// (<see cref="ColorMath.Contrast"/>), de modo que es legible en barra de tareas clara u oscura.
    /// <paramref name="taskbarLight"/> (<see cref="Services.ThemeResolver.TaskbarIsLight"/> en la app)
    /// indica contra qué barra se compone el velo stale; los badges frescos no dependen de él.
    /// </summary>
    public static Icon Render(int percent, Theme theme, double warn, double crit,
        UsageStatus status, bool stale = false, bool pending = false, bool taskbarLight = false)
        => Render(Spec(percent, theme, warn, crit, status, stale, pending, taskbarLight));

    /// <summary>Neutral badge for "no data / auth expired / offline".</summary>
    public static Icon RenderError(Color bg, bool pending = false)
        => Render(SpecError(bg, pending));

    /// <summary>Opacidad del velo de atenuación del badge stale (sobre 255).</summary>
    private const int StaleAlpha = 150;

    /// <summary>
    /// Lado del lienzo de DISEÑO del badge en px: toda la geometría está pensada a 48. F3 Task 7: la
    /// bandeja recibe el badge pintado (vectorial) al tamaño exacto de su icono pequeño, escalando el
    /// dibujo; antes se le daba a 48 y la shell lo reescalaba (borroso).
    /// </summary>
    internal const int BadgeSize = 48;

    /// <summary>
    /// Tamaño de fuente (px) de un número de 1-2 dígitos: llena la altura del badge sin desbordar.
    /// El caso de 3 caracteres ("99+") NO usa un literal fijo (antes 18px, el peor caso de
    /// legibilidad): se calcula por fit-to-box (<see cref="FitFontPx"/>) para llenar la caja igual
    /// que un número normal en vez de quedar diminuto.
    /// </summary>
    private const float BadgeFontPx = 30f;

    /// <summary>
    /// Caja útil (px) que la ALTURA del texto del badge debe llenar dentro del lienzo de
    /// <see cref="BadgeSize"/>: deja margen para el redondeo del relleno y los overlays de esquina
    /// (forma/punto). El fit-to-box de "99+" ajusta el tamaño de fuente para que la tinta ocupe ~este
    /// alto sin desbordar el ancho del rectángulo de dibujo.
    /// </summary>
    private const float BadgeTextBox = 40f;

    /// <summary>
    /// Ancho máximo (px) que la cadena DIBUJADA puede ocupar = el lado del rectángulo de dibujo
    /// (<see cref="BadgeSize"/>) menos un pequeño margen de seguridad. Es el límite que evita que GDI+
    /// recorte el '+' de "99+": el ancho se mide con el MISMO formato del dibujo (side bearing real).
    /// </summary>
    private const float BadgeTextMaxWidth = BadgeSize - 2f;

    /// <summary>Suelo del fit-to-box: nunca por debajo del 18px histórico aunque la caja sea estrecha.</summary>
    private const float BadgeFontMinPx = 18f;

    /// <summary>
    /// Mayor tamaño de fuente (px) con el que <paramref name="text"/> llena una caja de alto
    /// <paramref name="box"/> px SIN que la cadena, al dibujarse, desborde el ancho del rectángulo del
    /// badge. Parte de <see cref="BadgeFontPx"/> hacia abajo y nunca baja de <see cref="BadgeFontMinPx"/>.
    /// Para 1-2 dígitos cabe de sobra y devuelve <see cref="BadgeFontPx"/>; para "99+" se reescala para
    /// llenar la caja (~22-24px) en lugar del 18px fijo histórico.
    /// <para>
    /// CRÍTICO (regresión v039 g4): el ANCHO se mide con el MISMO <see cref="StringFormat"/> con el que
    /// luego se DIBUJA (<see cref="DrawBadgeFormat"/>, derivado de <c>GenericDefault</c> → incluye el
    /// side bearing real ≈1/6 em por lado) y se limita al ancho del rectángulo de dibujo
    /// (<see cref="BadgeTextMaxWidth"/>), NO a una caja artificial de 40px. Antes el ancho se medía con
    /// <c>GenericTypographic</c> (sin side bearing) contra 40px: al subir la fuente, la cadena dibujada
    /// desbordaba el rectángulo por la derecha y GDI+ RECORTABA el '+', dejando "99" indistinguible de un
    /// 99% real y anulando el indicador de overflow. El ALTO se mide tipográficamente (tinta ceñida) para
    /// que "llenar la caja" no quede a merced del leading de la fuente.
    /// </para>
    /// </summary>
    public static float FitFontPx(Graphics g, string text, float box)
    {
        using var sf = DrawBadgeFormat();
        return FitFontPx(g, text, box, sf);
    }

    /// <summary>
    /// Variante que mide el ANCHO con el <paramref name="drawFormat"/> exacto del dibujo (mismas
    /// métricas de side bearing) para que el tamaño elegido no haga que GDI+ recorte el texto, y el
    /// ALTO tipográficamente (ceñido a la tinta) para llenar la caja del badge.
    /// </summary>
    public static float FitFontPx(Graphics g, string text, float box, StringFormat drawFormat)
    {
        using var probe = new Font("Segoe UI", BadgeFontPx, FontStyle.Bold, GraphicsUnit.Pixel);
        // Ancho: con el formato REAL del dibujo → refleja lo que GDI+ va a pintar (side bearing incl.).
        float drawnWidth = g.MeasureString(text, probe, int.MaxValue, drawFormat).Width;
        // Alto: tipográfico (ceñido a la tinta) → llenar la caja sin que el leading lo encoja de más.
        float inkHeight = g.MeasureString(text, probe, int.MaxValue, StringFormat.GenericTypographic).Height;
        if (drawnWidth <= 0 || inkHeight <= 0) return BadgeFontPx;
        // El factor limitante: o el alto no llena la caja, o el ancho dibujado desbordaría el badge.
        float scale = Math.Min(box / inkHeight, BadgeTextMaxWidth / drawnWidth);
        // Solo encogemos: un número de 1-2 dígitos ya cabe (scale ≥ 1) y conserva BadgeFontPx.
        float fontPx = scale >= 1f ? BadgeFontPx : BadgeFontPx * scale;
        return Math.Max(BadgeFontMinPx, fontPx);
    }

    /// <summary>
    /// <see cref="StringFormat"/> con el que se MIDE el ancho y se DIBUJA el texto del badge (deben
    /// coincidir): centrado H/V y NoWrap (deja "99+" en una sola línea, no "99 / +"). Basado en
    /// <c>GenericDefault</c> — el que usa <c>DrawString</c> por defecto, con el side bearing que el
    /// fit-to-box debe contemplar para no recortar. El llamador lo libera (<c>using</c>).
    /// </summary>
    private static StringFormat DrawBadgeFormat() => new(StringFormat.GenericDefault)
    {
        Alignment = StringAlignment.Center,
        LineAlignment = StringAlignment.Center,
        FormatFlags = StringFormatFlags.NoWrap
    };

    /// <summary>
    /// Relleno EFECTIVO del badge stale: el velo translúcido (<see cref="StaleAlpha"/>) PRE-COMPUESTO
    /// a opaco contra el color asumido de la barra de tareas (<see cref="TaskbarColors"/>). Pintar con
    /// alpha y decidir el texto sobre el color fresco (regresión de 5ef80c1, T6) dejaba el dígito a
    /// ~2:1 en barra oscura: el compuesto real caía al otro lado del flip WCAG. A opaco, el color
    /// pintado ES el color en pantalla (sin recomposición del pipeline HICON), y el lado que elige
    /// <see cref="ColorMath.Contrast"/> garantiza ≥4.58:1 sobre cualquier relleno (en el punto de
    /// cruce L≈0.179 negro y blanco empatan a (L+0.05)/0.05 ≈ 4.58).
    /// </summary>
    public static Color StaleFill(Color bg, bool taskbarLight)
        => ColorMath.Lerp(taskbarLight ? TaskbarColors.Light : TaskbarColors.Dark, bg, StaleAlpha / 255.0);

    private static Icon RenderBadge(string text, Color bg, UsageStatus status, bool stale, bool pending,
        Color pendingDot, bool taskbarLight, int sizePx)
    {
        // Geometría de diseño a 48 px; el bitmap tiene el tamaño EXACTO pedido y el dibujo (vectorial) se
        // escala a él: la shell ya no reescala un 48 px a 16/20/24 (F3 Task 7). A 48 la escala es 1.
        const int size = BadgeSize;
        sizePx = Math.Max(1, sizePx);
        using var bmp = new Bitmap(sizePx, sizePx);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.Clear(Color.Transparent);
            if (sizePx != size) g.ScaleTransform(sizePx / (float)size, sizePx / (float)size);

            // Estado "stale": dato envejecido → badge atenuado hacia el color de la barra para que se
            // note de un vistazo que no es fresco. Atenuado SIN alpha (pre-compuesto a opaco, T6b):
            // sobre la barra asumida se ve igual que el velo translúcido, pero el color pintado es el
            // color real en pantalla y la decisión de contraste del texto deja de mentir.
            Color fillColor = stale ? StaleFill(bg, taskbarLight) : bg;
            using (var brush = new SolidBrush(fillColor))
                Shapes.FillRounded(g, brush, new Rectangle(0, 0, size - 1, size - 1), 11);

            // Texto con contraste calculado sobre el relleno EFECTIVO (no el fresco): legible en barra
            // clara/oscura también cuando el badge está atenuado por stale.
            Color textColor = ColorMath.Contrast(fillColor);
            // Larger glyph that fills more of the badge → readable even when shrunk into the tray.
            // Fit-to-box: 1-2 dígitos llenan a BadgeFontPx; "99+" (3 chars) se REESCALA para llenar la
            // caja (~22-24px) en vez del 18px fijo histórico — su peor caso de legibilidad. NoWrap deja
            // "99+" en una sola línea (no "99 / +"). El tamaño ya no es un literal mágico (F9).
            // El MISMO sf mide y dibuja: el fit contempla el side bearing real, así "99+" no se recorta
            // por la derecha (regresión v039 g4: con métricas distintas GDI+ comía el '+').
            using var sf = DrawBadgeFormat();
            float fontPx = FitFontPx(g, text, BadgeTextBox, sf);
            using var font = new Font("Segoe UI", fontPx, FontStyle.Bold, GraphicsUnit.Pixel);
            using (var textBrush = new SolidBrush(textColor))
                g.DrawString(text, font, textBrush, new RectangleF(0, -2f, size, size), sf);

            // Estado por forma (a11y): overlay de forma en la esquina inferior derecha para Warn/Critical.
            // Ok→círculo = sin overlay (el badge ya es el indicador). T9d (§3 #15): con el punto de
            // atención la forma se SUPRIME — un solo elemento decorativo por esquina; dígito + forma
            // + punto a 16-32px se solapaban en ruido ilegible.
            if (!pending)
                DrawShapeOverlay(g, Tray.ShapeFor(status), bg, textColor, size);

            if (pending)
            {
                // Punto de atención (T9d): ámbar Warn de la paleta del tema (antes #F5A623 fuera de
                // paleta con aro #1A1A1A). Más pequeño (d=10, antes 18: tapaba el dígito) y pegado a
                // la esquina superior derecha, separado del relleno por un aro KNOCKOUT: se BORRA a
                // transparente (SourceCopy), así delimita sobre cualquier color de badge y de barra
                // de tareas sin introducir un color nuevo. La geometría deja libre la caja de tinta
                // del dígito (verificado por test píxel a píxel con "88").
                const int d = 10;
                var dot = new Rectangle(size - d - 1, 1, d, d);
                var gap = Rectangle.Inflate(dot, 2, 2);
                var cm = g.CompositingMode;
                g.CompositingMode = CompositingMode.SourceCopy;   // escribe transparencia real (borra)
                using (var clear = new SolidBrush(Color.Transparent))
                    g.FillEllipse(clear, gap);
                g.CompositingMode = cm;
                using (var fill = new SolidBrush(pendingDot))
                    g.FillEllipse(fill, dot);

                // F8 (§3 #15 redux): el punto de atención SUPRIMÍA la forma daltónica, así que un
                // Critical+pend (caso real: TrayAppContext pasa status + pending a la vez) quedaba
                // distinguible SOLO por color. Recuperamos la redundancia no-cromática dibujando la
                // silueta del estado (▲ Warn / ◆ Critical) DENTRO del punto, en el color de contraste
                // del ámbar. Ok→círculo = sin glifo (el punto ya es un círculo). Pequeña (cabe en d=10)
                // y centrada en el punto: no toca el dígito (vive en la esquina superior, fuera de la
                // caja de tinta) ni descuadra el badge.
                DrawPendShape(g, Tray.ShapeFor(status), pendingDot, dot);
            }
        }

        // Clonamos el icono a 32bpp ARGB completo. El round-trip anterior (Icon.Save→new Icon(ms))
        // recodificaba el .ico a la paleta VGA de 16 colores: el verde (22,163,74) caía a teal
        // (0,128,128) y el verde-oliva del badge a oliva puro (128,128,0). En la bandeja a 16px casi no
        // se nota, pero al MAGNIFICAR el icono para el GIF el aplanado de color se veía amateur. Con
        // Icon.FromHandle preservamos color y alfa reales; clonamos para poder liberar el HICON.
        IntPtr hIcon = bmp.GetHicon();
        try
        {
            using var tmp = Icon.FromHandle(hIcon);
            return (Icon)tmp.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    /// <summary>Pinta la forma de estado en la esquina inferior derecha. Circle (Ok) no dibuja overlay.</summary>
    private static void DrawShapeOverlay(Graphics g, TrayShape shape, Color bg, Color fg, int size)
    {
        if (shape == TrayShape.Circle) return; // Ok: el propio badge es el indicador, sin overlay.

        // Forma en el color de texto (mismo contraste que el %), centrada en una esquina.
        int s = 14;
        int cx = size - s - 1;
        int cy = size - s - 1;
        using var brush = new SolidBrush(fg);
        if (shape == TrayShape.Triangle)
        {
            var tri = new[]
            {
                new Point(cx + s / 2, cy),
                new Point(cx + s, cy + s),
                new Point(cx, cy + s),
            };
            g.FillPolygon(brush, tri);
        }
        else // Rhombus
        {
            var rho = new[]
            {
                new Point(cx + s / 2, cy),
                new Point(cx + s, cy + s / 2),
                new Point(cx + s / 2, cy + s),
                new Point(cx, cy + s / 2),
            };
            g.FillPolygon(brush, rho);
        }
    }

    /// <summary>
    /// F8: dibuja la silueta del estado (▲ Warn / ◆ Critical) DENTRO del punto de atención, en el color
    /// que contrasta con el ámbar del punto, para que un badge "pending" siga siendo distinguible por
    /// forma y no solo por color. Circle (Ok) no dibuja glifo: el propio punto ya es un círculo, así que
    /// la redundancia no-cromática ya existe. La forma es pequeña (cabe holgada en el punto de d≈10) y se
    /// centra en él con anti-aliasing en float; queda en la esquina superior derecha, lejos del dígito.
    /// </summary>
    private static void DrawPendShape(Graphics g, TrayShape shape, Color dotColor, Rectangle dot)
    {
        if (shape == TrayShape.Circle) return; // Ok: el punto ya ES un círculo, redundancia cubierta.

        float cx = dot.Left + dot.Width / 2f;
        float cy = dot.Top + dot.Height / 2f;
        float r = dot.Width * 0.30f; // radio de la silueta: ~3px en un punto de 10px → cabe sin tocar el aro
        using var brush = new SolidBrush(ColorMath.Contrast(dotColor));
        if (shape == TrayShape.Triangle)
        {
            var tri = new[]
            {
                new PointF(cx, cy - r),
                new PointF(cx + r, cy + r),
                new PointF(cx - r, cy + r),
            };
            g.FillPolygon(brush, tri);
        }
        else // Rhombus
        {
            var rho = new[]
            {
                new PointF(cx, cy - r),
                new PointF(cx + r, cy),
                new PointF(cx, cy + r),
                new PointF(cx - r, cy),
            };
            g.FillPolygon(brush, rho);
        }
    }
}
