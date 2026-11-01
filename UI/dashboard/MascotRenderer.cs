using System.Drawing.Drawing2D;
using ClaudeBarWin.Config;
using ClaudeBarWin.Models;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Mascot;

namespace ClaudeBarWin.UI;

/// <summary>Dibuja la mascota ASCII (color por fase/humor) + spinner de trabajo. Sin estado.</summary>
public static class MascotRenderer
{
    /// <summary>Barrido (grados) del arco del spinner: deja un hueco para que se lea como anillo girando.</summary>
    internal const float SpinnerSweepDeg = 280f;

    /// <summary>
    /// Dibuja el frame elegido por el <see cref="MascotAnimator"/> en (x,y) y devuelve el tamaño
    /// ocupado. El <paramref name="state"/> trae el <c>FrameIndex</c> ya resuelto (tempo/blink) y la
    /// señal/ángulo del spinner (Processing/Compacting). El color sale de la fase, salvo que el
    /// <paramref name="mood"/> lo tiña (Alert/Happy/Focused). El spinner se pinta a la derecha de la
    /// fila de la CARA y NO cambia el tamaño reservado (cabe en el ancho de la mascota o se ignora en la medida).
    /// <paramref name="color"/> sustituye al color de fase/humor (crossfade animado del panel, F3 Task 6).
    /// </summary>
    public static Size Draw(Graphics g, bool draw, int x, int y, SessionPhase phase,
                            MascotState state, Theme theme, Font mono, Mood mood = Mood.Neutral,
                            Color? color = null, Action<Rectangle>? spinnerBand = null,
                            Action<Point>? faceCenter = null)
    {
        var frames = MascotSprite.Frames(phase);
        var frame = state.Face is { } face ? MascotSprite.FrameWithFace(face) : frames[state.FrameIndex % frames.Count];
        float lineH = mono.GetHeight(g);
        float maxW = 0;
        var ink = color ?? MoodColor(theme, phase, mood);
        for (int i = 0; i < frame.Length; i++)
        {
            if (draw)
            {
                using var b = GdiCache.BrushFor(ink);
                if (i == FaceRow && (state.EyeDx != 0 || state.EyeDy != 0) && frame[i].Length >= 7)
                    DrawLookingFace(g, frame[i], mono, b, x, y + i * lineH, state.EyeDx, state.EyeDy);
                else
                    g.DrawString(frame[i], mono, b, x, y + i * lineH);
            }
            var w = g.MeasureString(frame[i], mono).Width;
            if (w > maxW) maxW = w;
        }
        faceCenter?.Invoke(new Point(x + (int)(maxW / 2), y + (int)(lineH * (FaceRow + 0.5f))));

        // Spinner de "trabajo vivo": ARCO GDI+ que gira (T-v039 F2), no el viejo glifo braille a ~7px
        // (se veía como 1-3 píxeles muertos colgando de la oreja). Color = fase, grosor ~2px, ángulo
        // de arranque elapsed-driven (state.SpinnerAngleDeg). Anclado a la fila de la CARA (y+lineH),
        // no a la oreja. Solo pintado y dentro de la celda → no ensancha el tamaño reservado.
        if (draw && state.SpinnerGlyph != '\0')
        {
            float ax = x + maxW + Spacing.Scaled.Sm, rowTop = y + lineH;
            DrawSpinnerArc(g, ax, rowTop, lineH, state.SpinnerAngleDeg, PhaseColor(theme, phase));
            // §4 #8: cuando solo gira el spinner, el panel invalida solo su arco (en coordenadas de
            // dispositivo: incluye el desplazamiento del Salto que lleva el Graphics).
            if (spinnerBand is not null)
            {
                var r = SpinnerArcRect(ax, rowTop, lineH);
                var pts = new[] { new PointF(r.Left, r.Top), new PointF(r.Right, r.Bottom) };
                using (var m = g.Transform) m.TransformPoints(pts);
                spinnerBand(Rectangle.FromLTRB((int)Math.Floor(pts[0].X) - 2, (int)Math.Floor(pts[0].Y) - 2,
                    (int)Math.Ceiling(pts[1].X) + 2, (int)Math.Ceiling(pts[1].Y) + 2));
            }
        }

        return new Size((int)Math.Ceiling(maxW), (int)Math.Ceiling(frame.Length * lineH));
    }

    /// <summary>
    /// Arco del spinner: anillo de <paramref name="rowH"/> de alto (el de una línea de la cara),
    /// centrado en la fila, con grosor ~2px (escala con DPI) y un hueco para que se lea girando. El
    /// arranque <paramref name="startDeg"/> viene del animador (elapsed-driven). AA on para que el
    /// arco no se vea dentado.
    /// </summary>
    private static void DrawSpinnerArc(Graphics g, float x, float rowTop, float rowH, float startDeg, Color color)
    {
        float thickness = Math.Max(1.5f, Dpi.Scale(2));
        float pad = thickness / 2f + 0.5f;            // margen para que el trazo no se recorte
        var cell = SpinnerArcRect(x, rowTop, rowH);
        var rect = RectangleF.Inflate(cell, -pad, -pad);
        if (rect.Width <= 0 || rect.Height <= 0) return;

        var sm = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        try
        {
            using var pen = GdiCache.PenFor(color, thickness, LineCap.Round);
            g.DrawArc(pen, rect, startDeg, SpinnerSweepDeg);
        }
        finally { g.SmoothingMode = sm; }
    }

    /// <summary>Fila del sprite con la cara (<c>"( o.o )"</c>).</summary>
    private const int FaceRow = 1;

    /// <summary>
    /// Ojos que miran el cursor (§5): los paréntesis se quedan quietos y la cara (ojo · nariz · ojo) se
    /// desplaza (dx, dy) px enteros. Ambas partes se pintan con el formato por defecto; la cara arranca
    /// dos avances de carácter mono a la derecha, así en reposo coincide con la fila entera.
    /// </summary>
    private static void DrawLookingFace(Graphics g, string row, Font mono, Brush b, float x, float y, int dx, int dy)
    {
        string face = row.Substring(2, row.Length - 4);
        string frame = "(" + new string(' ', face.Length + 2) + ")";
        g.DrawString(frame, mono, b, x, y);
        float adv = g.MeasureString("00", mono, int.MaxValue, TextMetrics.Typographic).Width
                  - g.MeasureString("0", mono, int.MaxValue, TextMetrics.Typographic).Width;
        g.DrawString(face, mono, b, x + 2 * adv + dx, y + dy);
    }

    /// <summary>
    /// Celda del arco del spinner: cuadrado de ~70 % del alto de fila (mínimo 6 px), centrado en la fila
    /// de la cara. El trazo vive dentro (con su margen); fuera no se pinta nada del spinner.
    /// </summary>
    internal static RectangleF SpinnerArcRect(float x, float rowTop, float rowH)
    {
        float d = Math.Max(6f, rowH * 0.70f);
        return new RectangleF(x, rowTop + (rowH - d) / 2f, d, d);
    }

    /// <summary>Color base por fase (sin humor). Compat con consumidores antiguos.</summary>
    public static Color PhaseColor(Theme theme, SessionPhase phase) => phase switch
    {
        SessionPhase.WaitingForApproval => theme.Warn,
        SessionPhase.WaitingForInput => theme.Warn,
        SessionPhase.Processing => theme.Ok,
        SessionPhase.Compacting => theme.Ok,
        SessionPhase.Ended => theme.Critical,
        // Idle/default: el gris Neutral caía a ~2.3:1 sobre el fondo oscuro (glifo no textual < 3:1,
        // T-v039 F1). MascotIdle → TextMuted (≥3:1, ~5:1 como texto) en los 3 temas. NO toca las fases
        // activas (Warn/Ok/Critical), que tienen su propio color semántico.
        _ => theme.MascotIdle,
    };

    /// <summary>Color con tinte de humor: el humor manda cuando es expresivo; si no, cae a la fase.</summary>
    public static Color MoodColor(Theme theme, SessionPhase phase, Mood mood) => mood switch
    {
        Mood.Alert => theme.Warn,
        // Celebración de reset (T-v039 F3): el gato contento se tiñe del ACENTO de marca (el violeta de Muesca),
        // no del verde Ok. Antes Happy heredaba Ok (verde) y la cara de Processing → "contento" se
        // confundía con "trabajando". El acento lo distingue de cualquier estado de cuota/fase.
        Mood.Happy => theme.Accent,
        _ => PhaseColor(theme, phase),  // Focused/Neutral siguen el color de la fase
    };
}
