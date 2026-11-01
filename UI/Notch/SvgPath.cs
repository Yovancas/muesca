using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ClaudeBarWin.UI.Notch;

/// <summary>Tipo de tramo de un trazado SVG ya en coordenadas absolutas.</summary>
public enum SvgSegmentKind { Move, Line, Cubic, Close }

/// <summary>Un tramo absoluto: <see cref="P"/> = punto final; <see cref="C1"/>/<see cref="C2"/> = puntos de control de
/// la Bézier cúbica (solo en <see cref="SvgSegmentKind.Cubic"/>). En <see cref="SvgSegmentKind.Close"/>, <see cref="P"/>
/// es el inicio de la figura (el punto actual tras cerrarla).</summary>
public readonly record struct SvgSegment(SvgSegmentKind Kind, PointF P, PointF C1 = default, PointF C2 = default);

/// <summary>
/// Lector mínimo del atributo <c>d</c> de un <c>&lt;path&gt;</c> SVG, para pintar iconos vectoriales sin dependencias
/// (el de la letra del notch, de Icons8). PURO: texto → lista de tramos absolutos; aparte, tramos → <see cref="GraphicsPath"/>
/// escalado a un rectángulo. Admite M/L/H/V/C/S/Z en absoluto y relativo, la repetición implícita de parámetros (tras M
/// los pares siguientes son L) y números pegados («-0.245-1.068», «.5.5», exponentes). S/s reflejan el segundo punto de
/// control del tramo anterior si era C/S (si no, el control es el punto actual), como manda la especificación. Q/T/A no
/// hacen falta para ningún icono del repo: lanzan <see cref="FormatException"/> en vez de pintar algo a medias.
/// </summary>
public static class SvgPath
{
    /// <summary>Tramos absolutos del trazado <paramref name="d"/>. Lanza <see cref="FormatException"/> si está mal formado.</summary>
    public static IReadOnlyList<SvgSegment> Parse(string d)
    {
        ArgumentNullException.ThrowIfNull(d);
        var segs = new List<SvgSegment>();
        int i = 0;
        char cmd = '\0';
        PointF cur = default, start = default, lastC2 = default;
        bool lastCubic = false;
        while (true)
        {
            SkipSeparators(d, ref i);
            if (i >= d.Length) break;
            char c = d[i];
            if (char.IsLetter(c))
            {
                cmd = c;
                i++;
            }
            else if (cmd == '\0') throw new FormatException($"El trazado empieza sin comando en {i}.");
            // Sin letra nueva: se repite el comando anterior (tras M/m, como L/l).

            bool rel = char.IsLower(cmd);
            PointF Rel(float x, float y) => rel ? new PointF(cur.X + x, cur.Y + y) : new PointF(x, y);
            switch (char.ToUpperInvariant(cmd))
            {
                case 'M':
                {
                    var p = Rel(Number(d, ref i), Number(d, ref i));
                    segs.Add(new SvgSegment(SvgSegmentKind.Move, p));
                    cur = start = p;
                    cmd = rel ? 'l' : 'L';
                    lastCubic = false;
                    break;
                }
                case 'L':
                {
                    var p = Rel(Number(d, ref i), Number(d, ref i));
                    segs.Add(new SvgSegment(SvgSegmentKind.Line, p));
                    cur = p;
                    lastCubic = false;
                    break;
                }
                case 'H':
                {
                    float x = Number(d, ref i);
                    var p = new PointF(rel ? cur.X + x : x, cur.Y);
                    segs.Add(new SvgSegment(SvgSegmentKind.Line, p));
                    cur = p;
                    lastCubic = false;
                    break;
                }
                case 'V':
                {
                    float y = Number(d, ref i);
                    var p = new PointF(cur.X, rel ? cur.Y + y : y);
                    segs.Add(new SvgSegment(SvgSegmentKind.Line, p));
                    cur = p;
                    lastCubic = false;
                    break;
                }
                case 'C':
                {
                    var c1 = Rel(Number(d, ref i), Number(d, ref i));
                    var c2 = Rel(Number(d, ref i), Number(d, ref i));
                    var p = Rel(Number(d, ref i), Number(d, ref i));
                    segs.Add(new SvgSegment(SvgSegmentKind.Cubic, p, c1, c2));
                    cur = p; lastC2 = c2; lastCubic = true;
                    break;
                }
                case 'S':
                {
                    var c1 = lastCubic ? new PointF(2 * cur.X - lastC2.X, 2 * cur.Y - lastC2.Y) : cur;
                    var c2 = Rel(Number(d, ref i), Number(d, ref i));
                    var p = Rel(Number(d, ref i), Number(d, ref i));
                    segs.Add(new SvgSegment(SvgSegmentKind.Cubic, p, c1, c2));
                    cur = p; lastC2 = c2; lastCubic = true;
                    break;
                }
                case 'Z':
                    segs.Add(new SvgSegment(SvgSegmentKind.Close, start));
                    cur = start;
                    lastCubic = false;
                    cmd = '\0';   // tras Z hace falta un comando nuevo
                    break;
                default:
                    throw new FormatException($"Comando SVG no admitido: «{cmd}».");
            }
        }
        if (segs.Count > 0 && segs[0].Kind != SvgSegmentKind.Move)
            throw new FormatException("El trazado debe empezar por M.");
        return segs;
    }

    /// <summary>
    /// Trazado GDI+ de <paramref name="segs"/> (coordenadas en <paramref name="viewBox"/>) escalado SIN deformar y centrado
    /// en <paramref name="target"/>. Relleno «nonzero» (<see cref="FillMode.Winding"/>), el de SVG por defecto: los huecos
    /// del icono son figuras con el giro contrario.
    /// </summary>
    public static GraphicsPath ToGraphicsPath(IReadOnlyList<SvgSegment> segs, RectangleF viewBox, RectangleF target)
    {
        float k = viewBox.Width > 0 && viewBox.Height > 0 ? Math.Min(target.Width / viewBox.Width, target.Height / viewBox.Height) : 0f;
        float ox = target.Left + (target.Width - viewBox.Width * k) / 2f - viewBox.Left * k;
        float oy = target.Top + (target.Height - viewBox.Height * k) / 2f - viewBox.Top * k;
        PointF T(PointF p) => new(ox + p.X * k, oy + p.Y * k);

        var path = new GraphicsPath(FillMode.Winding);
        PointF cur = default;
        foreach (var s in segs)
        {
            switch (s.Kind)
            {
                case SvgSegmentKind.Move:
                    path.StartFigure();
                    cur = s.P;
                    break;
                case SvgSegmentKind.Line:
                    path.AddLine(T(cur), T(s.P));
                    cur = s.P;
                    break;
                case SvgSegmentKind.Cubic:
                    path.AddBezier(T(cur), T(s.C1), T(s.C2), T(s.P));
                    cur = s.P;
                    break;
                case SvgSegmentKind.Close:
                    path.CloseFigure();
                    cur = s.P;
                    break;
            }
        }
        return path;
    }

    private static readonly Regex PathAttr = new("<path\\b[^>]*?\\sd\\s*=\\s*\"([^\"]*)\"", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex ViewBoxAttr = new("viewBox\\s*=\\s*\"([^\"]*)\"", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>El <c>d</c> del primer <c>&lt;path&gt;</c> y el <c>viewBox</c> de un SVG sencillo (un solo trazado, como
    /// los iconos de Icons8); <c>null</c> si no los tiene.</summary>
    public static (string Data, RectangleF ViewBox)? ReadIcon(string svg)
    {
        var p = PathAttr.Match(svg);
        var v = ViewBoxAttr.Match(svg);
        if (!p.Success || !v.Success) return null;
        var parts = v.Groups[1].Value.Split(new[] { ' ', ',', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4) return null;
        var n = new float[4];
        for (int j = 0; j < 4; j++)
            if (!float.TryParse(parts[j], NumberStyles.Float, CultureInfo.InvariantCulture, out n[j])) return null;
        return (p.Groups[1].Value, new RectangleF(n[0], n[1], n[2], n[3]));
    }

    private static void SkipSeparators(string d, ref int i)
    {
        while (i < d.Length && (char.IsWhiteSpace(d[i]) || d[i] == ',')) i++;
    }

    /// <summary>Lee un número SVG: signo, dígitos, UN punto decimal y exponente opcional. Un segundo punto o un signo
    /// empiezan el número siguiente («.5.5» = 0,5 y 0,5; «-1-2» = −1 y −2).</summary>
    private static float Number(string d, ref int i)
    {
        SkipSeparators(d, ref i);
        int start = i;
        if (i < d.Length && (d[i] == '+' || d[i] == '-')) i++;
        bool digits = false, dot = false;
        while (i < d.Length)
        {
            char c = d[i];
            if (c >= '0' && c <= '9') { digits = true; i++; }
            else if (c == '.' && !dot) { dot = true; i++; }
            else break;
        }
        if (!digits) throw new FormatException($"Se esperaba un número en {start}.");
        if (i < d.Length && (d[i] == 'e' || d[i] == 'E'))
        {
            int save = i++;
            if (i < d.Length && (d[i] == '+' || d[i] == '-')) i++;
            int expStart = i;
            while (i < d.Length && d[i] >= '0' && d[i] <= '9') i++;
            if (i == expStart) i = save;   // «e» suelta: no era exponente
        }
        return float.Parse(d.AsSpan(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
