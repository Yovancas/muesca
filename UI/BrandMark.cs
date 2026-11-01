using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using ClaudeBarWin.Services;

namespace ClaudeBarWin.UI;

/// <summary>
/// Geometría del logo de Muesca (branding/muesca.svg, lienzo de 512) proyectada a un cuadrado cualquiera. Pura: la usa
/// <see cref="BrandMark"/> para pintar y los tests para comprobar que sigue al SVG. En el SVG:
/// <list type="bullet">
/// <item>el cuadrado redondeado: x = y = 24, lado 464, radio 112, con el degradado violeta → azul en diagonal;</item>
/// <item>el notch negro colgando del borde de arriba: x de 96 a 416, hasta y = 360, esquinas de abajo con radio 110, recortado
/// por el cuadrado (sus esquinas de arriba siguen el arco de las del cuadrado);</item>
/// <item>dos anillos con centro (256, 212): el grande de radio 96 y trazo 34 (degradado lavanda → azul, 410 de 603 de
/// perímetro) y el pequeño de radio 48 y trazo 26 (blanco, 110 de 302), los dos desde las 12 en el sentido del reloj y con
/// los extremos redondos, sobre su pista #24243A.</item>
/// </list>
/// </summary>
public readonly record struct MuescaMarkLayout(
    RectangleF Tile, float TileRadius,
    float NotchLeft, float NotchRight, float NotchBottom, float NotchRadius, float NotchTop,
    PointF RingCenter, float OuterRadius, float OuterStroke, float OuterSweep,
    float InnerRadius, float InnerStroke, float InnerSweep)
{
    /// <summary>Lado del lienzo del SVG.</summary>
    public const float Canvas = 512f;

    // Medidas del SVG (unidades del lienzo).
    private const float TileInset = 24f, TileSide = 464f, TileCorner = 112f;
    private const float NotchX0 = 96f, NotchX1 = 416f, NotchY1 = 360f, NotchCorner = 110f;
    private const float RingCx = 256f, RingCy = 212f, OuterR = 96f, OuterW = 34f, OuterDash = 410f, InnerR = 48f, InnerW = 26f,
        InnerDash = 110f;

    /// <summary>El logo dentro de <paramref name="box"/> (si no es cuadrado, el cuadrado mayor centrado).</summary>
    public static MuescaMarkLayout For(RectangleF box)
    {
        float side = Math.Min(box.Width, box.Height);
        float k = side / Canvas;
        float ox = box.Left + (box.Width - side) / 2f, oy = box.Top + (box.Height - side) / 2f;
        float X(float v) => ox + v * k;
        float Y(float v) => oy + v * k;

        // Donde el borde vertical del notch (x = 96) corta el arco de la esquina de arriba del cuadrado: ahí empieza su borde
        // de arriba, que sigue el arco hasta la parte recta (y = 24).
        float cornerCx = TileInset + TileCorner, dx = cornerCx - NotchX0;
        float notchTop = cornerCx - MathF.Sqrt(TileCorner * TileCorner - dx * dx);

        return new MuescaMarkLayout(
            new RectangleF(X(TileInset), Y(TileInset), TileSide * k, TileSide * k), TileCorner * k,
            X(NotchX0), X(NotchX1), Y(NotchY1), NotchCorner * k, Y(notchTop),
            new PointF(X(RingCx), Y(RingCy)), OuterR * k, OuterW * k, Sweep(OuterDash, OuterR),
            InnerR * k, InnerW * k, Sweep(InnerDash, InnerR));
    }

    /// <summary>Grados de arco que cubre un trazo discontinuo de <paramref name="dash"/> sobre un círculo de radio
    /// <paramref name="r"/> (sin contar los extremos redondos, que lo alargan medio trazo por cada lado).</summary>
    public static float Sweep(float dash, float r) => 360f * dash / (2f * MathF.PI * r);

    /// <summary>Grado (sentido de GDI+: desde +x, hacia abajo) en que el borde vertical izquierdo del notch corta el arco de la
    /// esquina de arriba a la izquierda del cuadrado. El de la derecha es su simétrico (540° − este).</summary>
    public float NotchCornerAngle
    {
        get
        {
            float cx = Tile.Left + TileRadius, cy = Tile.Top + TileRadius;
            return MathF.Atan2(NotchTop - cy, NotchLeft - cx) * 180f / MathF.PI + 360f;
        }
    }

    /// <summary>Caja del anillo grande (para el degradado de su trazo y para dibujarlo).</summary>
    public RectangleF OuterRing => new(RingCenter.X - OuterRadius, RingCenter.Y - OuterRadius, 2f * OuterRadius, 2f * OuterRadius);

    /// <summary>Caja del anillo pequeño.</summary>
    public RectangleF InnerRing => new(RingCenter.X - InnerRadius, RingCenter.Y - InnerRadius, 2f * InnerRadius, 2f * InnerRadius);
}

/// <summary>
/// Pinta el logo de Muesca en vector con GDI+ (nítido a cualquier escala y DPI), con la geometría de
/// <see cref="MuescaMarkLayout"/> y los colores de <see cref="Brand"/>. Sin estado: cada llamada crea y libera sus pinceles,
/// lápices y trazados (sin fugas GDI). Quien lo pinta en cada frame (la bienvenida, el notch que baja al empezar y el tutorial)
/// usa <see cref="BrandMarkCache"/>, que lo pinta así una vez por tamaño y DPI y luego solo copia el bitmap.
/// </summary>
public static class BrandMark
{
    /// <summary>Pinta el logo en <paramref name="box"/> con <paramref name="alpha"/> (0..1). Deja el Graphics como estaba.</summary>
    public static void Paint(Graphics g, RectangleF box, float alpha = 1f)
    {
        ArgumentNullException.ThrowIfNull(g);
        alpha = Math.Clamp(alpha, 0f, 1f);
        if (alpha <= 0f || box.Width < 2f || box.Height < 2f) return;
        var m = MuescaMarkLayout.For(box);
        var state = g.Save();
        try
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            // Cuadrado con el degradado en diagonal.
            using (var tile = RoundedSquare(m.Tile, m.TileRadius))
            using (var grad = PadGradient(m.Tile, 1f, A(Brand.Violet, alpha), A(Brand.Blue, alpha), LinearGradientMode.ForwardDiagonal))
                g.FillPath(grad, tile);

            using (var notch = NotchPath(m))
            using (var ink = new SolidBrush(A(Brand.Ink, alpha)))
                g.FillPath(ink, notch);

            // Los dos anillos: la pista entera y, encima, el arco desde las 12 en el sentido del reloj con los extremos redondos.
            using (var track = new Pen(A(Brand.RingTrack, alpha), m.OuterStroke))
            {
                g.DrawEllipse(track, m.OuterRing);
                track.Width = m.InnerStroke;
                g.DrawEllipse(track, m.InnerRing);
            }
            // El degradado del trazo va sobre la caja del círculo (la del SVG) y el trazo sale de ella medio grosor. En el SVG el
            // círculo lleva rotate(-90), que también gira su degradado: lavanda abajo a la izquierda, azul arriba a la derecha.
            using (var ringBrush = PadGradient(m.OuterRing, m.OuterStroke, A(Brand.RingEnd, alpha), A(Brand.RingStart, alpha),
                       LinearGradientMode.BackwardDiagonal))
            using (var outer = new Pen(ringBrush, m.OuterStroke) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawArc(outer, m.OuterRing, -90f, m.OuterSweep);
            using (var inner = new Pen(A(Color.White, alpha), m.InnerStroke) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawArc(inner, m.InnerRing, -90f, m.InnerSweep);
        }
        finally { g.Restore(state); }
    }

    /// <summary>El notch del logo: arriba sigue el arco de las esquinas del cuadrado (y la parte recta entre ellas), baja por
    /// los lados y redondea las dos esquinas de abajo.</summary>
    internal static GraphicsPath NotchPath(MuescaMarkLayout m)
    {
        var p = new GraphicsPath();
        float d = 2f * m.TileRadius, a = m.NotchCornerAngle, sweep = 270f - a;
        // Arriba a la izquierda: del corte con el borde del notch hasta lo alto de la esquina del cuadrado.
        p.AddArc(m.Tile.Left, m.Tile.Top, d, d, a, sweep);
        // Arriba a la derecha: la esquina simétrica, desde lo alto hasta el corte con el otro borde.
        p.AddArc(m.Tile.Right - d, m.Tile.Top, d, d, 270f, sweep);
        float r = m.NotchRadius, dd = 2f * r;
        p.AddArc(m.NotchRight - dd, m.NotchBottom - dd, dd, dd, 0f, 90f);
        p.AddArc(m.NotchLeft, m.NotchBottom - dd, dd, dd, 90f, 90f);
        p.CloseFigure();
        return p;
    }

    private static GraphicsPath RoundedSquare(RectangleF r, float radius)
    {
        float d = Math.Min(2f * radius, Math.Min(r.Width, r.Height));
        var p = new GraphicsPath();
        p.AddArc(r.Left, r.Top, d, d, 180f, 90f);
        p.AddArc(r.Right - d, r.Top, d, d, 270f, 90f);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0f, 90f);
        p.AddArc(r.Left, r.Bottom - d, d, d, 90f, 90f);
        p.CloseFigure();
        return p;
    }

    /// <summary>
    /// Degradado en diagonal (<paramref name="mode"/>: <see cref="LinearGradientMode.ForwardDiagonal"/> de arriba a la izquierda
    /// abajo a la derecha, <see cref="LinearGradientMode.BackwardDiagonal"/> de arriba a la derecha abajo a la izquierda) de
    /// <paramref name="from"/> a <paramref name="to"/> sobre el cuadrado <paramref name="box"/> que, como
    /// el <c>spreadMethod="pad"</c> del SVG, se queda en el color del extremo fuera de él. GDI+ repite el degradado fuera de su
    /// caja (y el último píxel del borde ya sale con el color del otro extremo), así que el pincel abarca
    /// <paramref name="pad"/> más por cada lado y los colores se fijan en los tramos de fuera.
    /// </summary>
    internal static LinearGradientBrush PadGradient(RectangleF box, float pad, Color from, Color to, LinearGradientMode mode)
    {
        var outer = RectangleF.Inflate(box, pad, pad);
        var brush = new LinearGradientBrush(outer, from, to, mode);
        float side = Math.Max(1f, outer.Width), t0 = pad / side, t1 = 1f - t0;
        brush.InterpolationColors = new ColorBlend
        {
            Colors = new[] { from, from, to, to },
            Positions = new[] { 0f, t0, t1, 1f },
        };
        return brush;
    }

    private static Color A(Color c, float alpha) => alpha >= 1f ? c : Color.FromArgb((int)Math.Round(c.A * alpha), c);
}

/// <summary>
/// El logo de <see cref="BrandMark"/> ya pintado, en un bitmap por (tamaño en píxeles, DPI): la bienvenida y el tutorial lo
/// pintan en cada frame de sus animaciones (60 por segundo), y en vector eso son dos degradados, cuatro lápices y dos trazados
/// nuevos cada vez. Aquí se rasteriza una vez y cada frame es una copia 1:1, alineada al píxel (con su alfa, si lo lleva).
/// <para>Sin fugas: el bitmap anterior se libera en cuanto cambia el tamaño o el DPI (<see cref="Invalidate"/> al cambiar de
/// escala) y al cerrar (<see cref="Dispose"/>). Con una transformación que no sea solo un desplazamiento (escala o giro), o ya
/// cerrado, pinta en vector como siempre: copiar el bitmap ahí lo emborronaría (o crearía otro sin dueño).</para>
/// </summary>
public sealed class BrandMarkCache : IDisposable
{
    private Bitmap? _bitmap;
    private (int Width, int Height, int Dpi) _key;
    private readonly ImageAttributes _faded = new();
    private readonly PointF[] _dest = new PointF[3];
    private float _fadedAlpha = -1f;
    private bool _disposed;

    /// <summary>Cuántas veces se ha rasterizado el logo (tests: una por tamaño y DPI, no una por frame).</summary>
    internal int Renders { get; private set; }

    /// <summary>¿Hay un bitmap guardado? (tests: se libera al cambiar el DPI y al cerrar.)</summary>
    internal bool HasBitmap => _bitmap is not null;

    /// <summary>La clave del bitmap: lado en píxeles de <paramref name="box"/> (redondeado) y DPI. PURO.</summary>
    internal static (int Width, int Height, int Dpi) KeyFor(RectangleF box, float scale) =>
        ((int)MathF.Round(box.Width), (int)MathF.Round(box.Height), (int)MathF.Round(96f * Math.Max(0.01f, scale)));

    /// <summary>Pinta el logo en <paramref name="box"/> con <paramref name="alpha"/> (0..1) a la escala <paramref name="scale"/>
    /// (DPI / 96). Deja el Graphics como estaba.</summary>
    public void Paint(Graphics g, RectangleF box, float alpha = 1f, float scale = 1f)
    {
        ArgumentNullException.ThrowIfNull(g);
        alpha = Math.Clamp(alpha, 0f, 1f);
        if (alpha <= 0f || box.Width < 2f || box.Height < 2f) return;
        if (_disposed || !TranslationOnly(g, out float dx, out float dy))
        {
            BrandMark.Paint(g, box, alpha);
            return;
        }

        var key = KeyFor(box, scale);
        if (_bitmap is null || key != _key)
        {
            Invalidate();
            _bitmap = Render(key.Width, key.Height);
            _key = key;
            Renders++;
        }

        // Alineado al píxel del dispositivo (el Graphics puede llevar un desplazamiento): una copia exacta, sin remuestrear.
        float left = MathF.Round(box.X + dx) - dx, top = MathF.Round(box.Y + dy) - dy;
        _dest[0] = new PointF(left, top);
        _dest[1] = new PointF(left + key.Width, top);
        _dest[2] = new PointF(left, top + key.Height);
        var state = g.Save();
        try
        {
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.CompositingMode = CompositingMode.SourceOver;
            ImageAttributes? attributes = null;
            if (alpha < 1f)
            {
                if (alpha != _fadedAlpha)
                {
                    _faded.SetColorMatrix(new ColorMatrix { Matrix33 = alpha });
                    _fadedAlpha = alpha;
                }
                attributes = _faded;
            }
            g.DrawImage(_bitmap, _dest, new RectangleF(0, 0, key.Width, key.Height), GraphicsUnit.Pixel, attributes);
        }
        finally { g.Restore(state); }
    }

    /// <summary>Libera el bitmap (al cambiar de DPI: el de antes ya no vale). El siguiente <see cref="Paint"/> lo rehace.</summary>
    public void Invalidate()
    {
        _bitmap?.Dispose();
        _bitmap = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Invalidate();
        _faded.Dispose();
    }

    private static Bitmap Render(int width, int height)
    {
        var bmp = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        try
        {
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.Transparent);
            BrandMark.Paint(g, new RectangleF(0, 0, width, height));
            return bmp;
        }
        catch
        {
            bmp.Dispose();
            throw;
        }
    }

    /// <summary>¿Lleva el Graphics solo un desplazamiento? (Con escala o giro, el bitmap no se copiaría 1:1.)</summary>
    private static bool TranslationOnly(Graphics g, out float dx, out float dy)
    {
        using var m = g.Transform;
        var e = m.Elements;
        dx = e[4];
        dy = e[5];
        return e[0] == 1f && e[1] == 0f && e[2] == 0f && e[3] == 1f;
    }
}
