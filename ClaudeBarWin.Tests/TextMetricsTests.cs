using ClaudeBarWin.Services;

namespace ClaudeBarWin.Tests;

/// <summary>
/// T10 (auditoría §3 #16): medida central de texto para los valores right-aligned.
/// <c>MeasureString</c> con el StringFormat por defecto añade un padding (~1/6 em por lado) que no es
/// tinta, y cada call site lo truncaba distinto ((int), Ceiling o float crudo): la columna derecha del
/// panel quedaba "dentada" (3-6px). <see cref="TextMetrics"/> mide con GenericTypographic + Ceiling y
/// pinta con el MISMO formato, así el texto termina exacto en el borde pedido.
/// </summary>
public class TextMetricsTests
{
    /// <summary>Columna X de la tinta más a la derecha dentro de la banda [yFrom, yTo); -1 si no hay.</summary>
    internal static int RightmostInk(Bitmap bmp, Color bg, int yFrom, int yTo)
    {
        for (int px = bmp.Width - 1; px >= 0; px--)
            for (int py = yFrom; py < yTo; py++)
            {
                var p = bmp.GetPixel(px, py);
                if (p.R != bg.R || p.G != bg.G || p.B != bg.B) return px;
            }
        return -1;
    }

    [Fact]
    public void MeasureWidth_is_tighter_than_default_MeasureString()
    {
        // GenericTypographic quita el padding del formato por defecto: la medida tipográfica de un
        // valor real de la columna derecha debe ser estrictamente menor que la acolchada.
        using var bmp = new Bitmap(10, 10);
        using var g = Graphics.FromImage(bmp);
        const string text = "◆ 87%";

        int typographic = TextMetrics.MeasureWidth(g, text, Typography.Mono);
        int padded = (int)Math.Ceiling(g.MeasureString(text, Typography.Mono).Width);

        Assert.InRange(typographic, 1, padded - 1);
    }

    [Fact]
    public void MeasureWidth_always_rounds_up()
    {
        // Un solo criterio de redondeo (Ceiling) para TODOS los right-align: nada de mezclar (int)
        // truncado con Ceiling según el call site.
        using var bmp = new Bitmap(10, 10);
        using var g = Graphics.FromImage(bmp);
        const string text = "$420.50";

        float raw = g.MeasureString(text, Typography.Mono, int.MaxValue, TextMetrics.Typographic).Width;
        int w = TextMetrics.MeasureWidth(g, text, Typography.Mono);

        Assert.Equal((int)Math.Ceiling(raw), w);
        Assert.True(w >= raw, "la medida entera nunca puede quedar por debajo del ancho real (recortaría tinta)");
    }

    [Fact]
    public void DrawRight_returns_x_consistent_with_MeasureWidth()
    {
        using var bmp = new Bitmap(300, 40);
        using var g = Graphics.FromImage(bmp);
        using var brush = new SolidBrush(Color.White);
        const string text = "12%";
        const int right = 200;

        int x = TextMetrics.DrawRight(g, text, Typography.Mono, brush, right, 10);

        Assert.Equal(right - TextMetrics.MeasureWidth(g, text, Typography.Mono), x);
    }

    [Fact]
    public void DrawRight_ink_is_flush_with_the_right_edge()
    {
        // El defecto original: medir con padding y pintar con el default dejaba la tinta 3-6px antes
        // del borde (y cada fila a una distancia distinta). Con medida+pintado tipográficos la tinta
        // termina pegada al borde pedido (≤3px de side bearing) y JAMÁS lo rebasa.
        using var bmp = new Bitmap(300, 40);
        using var g = Graphics.FromImage(bmp);
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        var bg = Color.Black;
        g.Clear(bg);
        using var brush = new SolidBrush(Color.White);
        const int right = 200;

        TextMetrics.DrawRight(g, "◆ 87%", Typography.Mono, brush, right, 10);

        int ink = RightmostInk(bmp, bg, 10, 30);
        Assert.True(ink >= 0, "DrawRight no pintó nada");
        Assert.InRange(ink, right - 3, right);
    }

    [Fact]
    public void DrawRight_aligns_different_strings_to_the_same_edge()
    {
        // Columna derecha de verdad: valores de distinta longitud ("◆ 87%" vs "12%") terminan en la
        // misma columna de píxeles (±1 por el redondeo del side bearing).
        using var bmp = new Bitmap(300, 80);
        using var g = Graphics.FromImage(bmp);
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        var bg = Color.Black;
        g.Clear(bg);
        using var brush = new SolidBrush(Color.White);
        const int right = 200;

        TextMetrics.DrawRight(g, "◆ 87%", Typography.Mono, brush, right, 10);
        TextMetrics.DrawRight(g, "12%", Typography.Mono, brush, right, 45);

        int inkA = RightmostInk(bmp, bg, 10, 30);
        int inkB = RightmostInk(bmp, bg, 45, 65);
        Assert.True(inkA >= 0 && inkB >= 0, "alguna de las dos cadenas no se pintó");
        Assert.True(Math.Abs(inkA - inkB) <= 1,
            $"columna dentada: '◆ 87%' termina en {inkA} y '12%' en {inkB} (borde pedido {right})");
    }
}
