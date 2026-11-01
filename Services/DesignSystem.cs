namespace ClaudeBarWin.Services;

/// <summary>Espaciado en rejilla de 8pt (múltiplos de 4). Sustituye los offsets ad-hoc.</summary>
public static class Spacing
{
    public const int Xs = 4;
    public const int Sm = 8;
    public const int Md = 12;
    public const int Lg = 16;
    public const int Xl = 24;
    public const int Xxl = 32;

    /// <summary>
    /// La rejilla proyectada a un factor de escala (F3 Task 5): las constantes son px de diseño a 96 DPI
    /// y se escalan en el punto de uso. A factor 1.0 es idéntica a las constantes.
    /// </summary>
    public static Scale At(float factor) => new(
        Dpi.Scale(Xs, factor), Dpi.Scale(Sm, factor), Dpi.Scale(Md, factor),
        Dpi.Scale(Lg, factor), Dpi.Scale(Xl, factor), Dpi.Scale(Xxl, factor));

    /// <summary>La rejilla al factor DPI ambiente del hilo (<see cref="Dpi.Factor"/>).</summary>
    public static Scale Scaled => At(Dpi.Factor);

    /// <summary>Valores de la rejilla ya escalados (px de dispositivo).</summary>
    public readonly record struct Scale(int Xs, int Sm, int Md, int Lg, int Xl, int Xxl);
}

/// <summary>
/// Colores asumidos de la barra de tareas de Win11 (no hay API pública para leer el color real).
/// Los usa el badge stale del tray para PRE-COMPONER su velo a opaco (T6b) y el QA visual de
/// <c>--render-test</c> como swatch de fondo de cada fila. <see cref="ThemeResolver.TaskbarIsLight"/>
/// decide qué lado aplica.
/// </summary>
public static class TaskbarColors
{
    public static readonly Color Dark = Color.FromArgb(0x2A, 0x2A, 0x2A);
    public static readonly Color Light = Color.FromArgb(0xE8, 0xE8, 0xE8);
}

/// <summary>Helpers de color: interpolación lineal y color de cuota por riesgo.</summary>
public static class ColorMath
{
    /// <summary>Interpola por canal ARGB. t se recorta a [0,1].</summary>
    public static Color Lerp(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        int L(int x, int y) => (int)Math.Round(x + (y - x) * t);
        return Color.FromArgb(L(a.A, b.A), L(a.R, b.R), L(a.G, b.G), L(a.B, b.B));
    }

    /// <summary>
    /// Color de cuota interpolado de forma continua: Ok→Warn hasta el umbral 'warn',
    /// Warn→Critical hasta 'crit', y Critical a partir de ahí. pct se recorta a [0,100].
    /// </summary>
    public static Color RiskColor(double pct, Theme t, double warn, double crit)
    {
        pct = Math.Clamp(pct, 0.0, 100.0);
        if (warn <= 0) warn = 70;
        if (crit <= warn) crit = Math.Max(warn + 1, 90);
        if (pct >= crit) return t.Critical;
        if (pct <= warn) return Lerp(t.Ok, t.Warn, pct / warn);
        return Lerp(t.Warn, t.Critical, (pct - warn) / (crit - warn));
    }

    /// <summary>
    /// Luminancia relativa a la que negro y blanco contrastan IGUAL sobre el fondo:
    /// <c>(1.05)/(L+0.05) = (L+0.05)/0.05 ⇒ L = √(1.05·0.05) − 0.05 ≈ 0.1791</c>.
    /// Por encima gana el negro; por debajo, el blanco.
    /// </summary>
    private const double TextFlipLuminance = 0.1791287;

    /// <summary>
    /// Color de texto legible sobre <paramref name="bg"/>: el lado (negro/blanco) que más contrasta
    /// según la luminancia relativa WCAG (<see cref="RelativeLuminance"/>, T6a). La heurística antigua
    /// (luma 0.299/0.587/0.114, umbral 140) elegía BLANCO sobre el verde CLI #00D959 (1.9:1, ilegible)
    /// y sobre los rellenos Warn/Ok oscuros; con el punto de cruce WCAG (~0.179) el lado elegido nunca
    /// contrasta peor que el descartado (negro 11.1:1 sobre el verde CLI).
    /// </summary>
    public static Color Contrast(Color bg)
        => RelativeLuminance(bg) > TextFlipLuminance ? Color.Black : Color.White;

    /// <summary>
    /// Realce de hover de un relleno OPACO que lleva texto <see cref="Contrast"/> encima, con la intensidad animada
    /// <paramref name="k"/> (0..1). Con texto negro (relleno claro: el verde del CLI, los de las fases del foco) se aclara un
    /// 10 % hacia el blanco, como siempre; con texto blanco (el violeta de Muesca) se OSCURECE un 8 %: aclararlo bajaría el
    /// blanco de ~4.8:1 a ~4.1:1 (por debajo de AA) justo mientras se apunta al botón.
    /// </summary>
    public static Color HoverFill(Color fill, double k = 1.0)
    {
        k = Math.Clamp(k, 0.0, 1.0);
        return RelativeLuminance(fill) > TextFlipLuminance
            ? Lerp(fill, Color.FromArgb(fill.A, 255, 255, 255), 0.10 * k)
            : Lerp(fill, Color.FromArgb(fill.A, 0, 0, 0), 0.08 * k);
    }

    /// <summary>
    /// Ratio de contraste WCAG 2.x entre dos colores: <c>(L1 + 0.05) / (L2 + 0.05)</c> con el más
    /// claro arriba (rango 1..21). AA pide ≥ 4.5:1 para texto pequeño. Simétrico. Lo usa el test del
    /// tema claro para garantizar que el gris tenue y el verde de éxito son legibles sobre el fondo.
    /// </summary>
    public static double ContrastRatio(Color a, Color b)
    {
        double la = RelativeLuminance(a), lb = RelativeLuminance(b);
        double hi = Math.Max(la, lb), lo = Math.Min(la, lb);
        return (hi + 0.05) / (lo + 0.05);
    }

    /// <summary>Luminancia relativa WCAG (sRGB linearizado). 0 = negro, 1 = blanco.</summary>
    public static double RelativeLuminance(Color c)
    {
        static double Chan(int v)
        {
            double s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Chan(c.R) + 0.7152 * Chan(c.G) + 0.0722 * Chan(c.B);
    }
}

/// <summary>
/// Medida de texto centralizada para los valores right-aligned (T10, auditoría §3 #16: QuotaBar %,
/// $ de gasto, % de modelo, glance del header, fase de sesión en vivo). <c>MeasureString</c> con el
/// StringFormat por defecto añade un padding (~1/6 em por lado) que NO es tinta, y cada call site lo
/// truncaba con un criterio distinto ((int), Ceiling o float crudo): la columna derecha quedaba
/// "dentada" (3-6px). Aquí se mide el avance tipográfico real (GenericTypographic) con un ÚNICO
/// redondeo (Ceiling) y se pinta con el MISMO formato, así la tinta termina exacta en el borde pedido.
/// </summary>
public static class TextMetrics
{
    /// <summary>
    /// Clon estático de <see cref="StringFormat.GenericTypographic"/> (vive toda la app, como las
    /// fuentes de <see cref="Typography"/>). Medir y PINTAR deben compartirlo: medir tipográfico y
    /// pintar con el default desplazaría la tinta ~1/6 em a la derecha del borde calculado.
    /// </summary>
    public static readonly StringFormat Typographic = (StringFormat)StringFormat.GenericTypographic.Clone();

    /// <summary>Ancho tipográfico de <paramref name="text"/> en px, redondeado SIEMPRE hacia arriba.</summary>
    public static int MeasureWidth(Graphics g, string text, Font font)
        => (int)Math.Ceiling(g.MeasureString(text, font, int.MaxValue, Typographic).Width);

    /// <summary>
    /// Pinta <paramref name="text"/> right-aligned terminando en <paramref name="right"/> y devuelve la
    /// X donde arrancó (<c>right - MeasureWidth</c>), por si el llamador acota el contenido izquierdo.
    /// </summary>
    public static int DrawRight(Graphics g, string text, Font font, Brush brush, int right, int y)
    {
        int x = right - MeasureWidth(g, text, font);
        g.DrawString(text, font, brush, x, y, Typographic);
        return x;
    }
}

/// <summary>
/// Fuentes del sistema de diseño: una familia (Segoe UI Variable) en 4 pasos + mono para números.
/// Cacheadas estáticamente (viven toda la app); con fallback si la familia no está instalada.
/// </summary>
public static class Typography
{
    public static readonly Font Hero    = Ui("Segoe UI Variable Display", 28f, FontStyle.Bold);
    public static readonly Font Title   = Ui("Segoe UI Variable Text", 15f, FontStyle.Bold);
    public static readonly Font Body    = Ui("Segoe UI Variable Text", 12f, FontStyle.Regular);
    public static readonly Font Caption = Ui("Segoe UI Variable Text", 11f, FontStyle.Regular);
    public static readonly Font Mono    = MonoFont(12f);

    // Crea la fuente pedida; si el sistema sustituye por otra familia (no instalada), cae a "Segoe UI".
    private static Font Ui(string family, float size, FontStyle style)
    {
        try
        {
            var f = new Font(family, size, style);
            if (f.Name.StartsWith("Segoe UI", StringComparison.OrdinalIgnoreCase)) return f;
            f.Dispose();
        }
        catch { }
        return new Font("Segoe UI", size, style);
    }

    private static Font MonoFont(float size)
    {
        foreach (var family in new[] { "Cascadia Mono", "Consolas" })
        {
            try
            {
                var f = new Font(family, size, FontStyle.Regular, GraphicsUnit.Point);
                if (f.Name.Equals(family, StringComparison.OrdinalIgnoreCase)) return f;
                f.Dispose();
            }
            catch { }
        }
        return new Font("Consolas", size, FontStyle.Regular, GraphicsUnit.Point);
    }
}
