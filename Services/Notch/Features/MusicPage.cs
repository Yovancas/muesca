using System.Drawing;

namespace ClaudeBarWin.Services.Notch.Features;

/// <summary>Partes de la página Música que responden al ratón. Los de la vista de letra (letras v2) van al final:
/// desfase −0,5 s / +0,5 s y «Otra letra».</summary>
public enum MusicControl { None, Previous, PlayPause, Next, Lyrics, Progress, LyricLine, NudgeBack, NudgeForward, OtherLyrics }

/// <summary>Botones de la vista de letra que se ven en este frame (letras v2): el desfase solo con letra sincronizada;
/// «Otra letra» con cualquier letra a la vista. Lo que no se ve tampoco recibe el clic.</summary>
[Flags]
public enum LyricsTools { None = 0, Nudge = 1, Other = 2 }

/// <summary>Qué hay bajo el cursor: el control; en la barra, la fracción 0..1; en la letra, la línea relativa a la
/// actual (−1 anterior, 0 actual, +1 siguiente).</summary>
public readonly record struct MusicHit(MusicControl Control, float Fraction = 0f, int LineOffset = 0);

/// <summary>Rectángulos de la página Música (px físicos). La MISMA cuenta para pintar y para acertar el clic.</summary>
public readonly record struct MusicRects(RectangleF Art, RectangleF Title, RectangleF Artist, RectangleF Previous,
    RectangleF PlayPause, RectangleF Next, RectangleF LyricsButton, RectangleF Bar, RectangleF BarHit,
    RectangleF Elapsed, RectangleF Total, RectangleF LyricsColumn, float Scale)
{
    /// <summary>Fila de la letra: −1 anterior, 0 actual, +1 siguiente (vacía sin la vista de letra).</summary>
    public RectangleF LyricLine(int offset)
    {
        if (LyricsColumn.IsEmpty) return RectangleF.Empty;
        float h = MusicLayout.LyricLineHeight * Scale;
        return new RectangleF(LyricsColumn.Left, LyricsColumn.Top + (offset + 1) * h, LyricsColumn.Width, h);
    }

    /// <summary>Vista de letra (letras v2): botón −0,5 s, en la fila de los tiempos. Vacío sin la vista o si no cabe.</summary>
    public RectangleF NudgeBack { get; init; }

    /// <summary>Botón +0,5 s, a la derecha de <see cref="NudgeBack"/>.</summary>
    public RectangleF NudgeForward { get; init; }

    /// <summary>Botón «Otra letra» con la fuente («LRCLIB 1/3»), pegado al tiempo total.</summary>
    public RectangleF OtherLyrics { get; init; }

    /// <summary>Hueco del aviso pequeño («letra sin sincronizar» o el desfase), alineado a la derecha: hasta los botones
    /// del desfase si se ven (<paramref name="nudge"/>) o hasta «Otra letra» si no. Vacío sin la vista.</summary>
    public RectangleF LyricsNotice(bool nudge)
    {
        if (OtherLyrics.IsEmpty) return RectangleF.Empty;
        float right = (nudge ? NudgeBack.Left : OtherLyrics.Left) - MusicLayout.ToolGap * Scale;
        return RectangleF.FromLTRB(Title.Left, OtherLyrics.Top, Math.Max(Title.Left, right), OtherLyrics.Bottom);
    }
}

/// <summary>
/// Maquetación de la página Música del expandido (F5 · B3). PURA. Todo en px lógicos × escala dentro de la zona de
/// contenido de la página (<c>NotchContentPainter.PageInner</c>, 444 × 98 a escala 1):
/// <code>
/// ┌──────┐ Título                                    normal: título/artista a todo lo ancho, «Letra» a la derecha
/// │ arte │ Artista                                   letra:  título/artista estrechos y la letra (3 filas) a la
/// │ 64px │ ⏮  ⏯  ⏭                         [Letra]          derecha; controles y barra no se mueven
/// └──────┘
/// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━  barra (4 px; franja de clic de 14 px)
/// 1:23                                            3:45
/// </code>
/// En la vista de letra, la fila de los tiempos lleva además (letras v2), de derecha a izquierda pegados al total:
/// [LRCLIB 1/3] [+0,5 s] [−0,5 s] y, a su izquierda, el aviso pequeño. No tocan la letra (arriba) ni la barra.
/// </summary>
public static class MusicLayout
{
    public const float ArtSize = 64f, ArtGap = 14f, TitleLine = 20f, ArtistLine = 18f, Button = 28f, ButtonGap = 4f,
        LyricsButtonWidth = 48f, LyricsButtonHeight = 22f, LyricsButtonGap = 8f, SplitTextWidth = 150f, ColumnGap = 14f,
        LyricLineHeight = 22f, BarHeight = 4f, BarHitHeight = 14f, BarCenterY = 73f, TimeTop = 80f, TimeLine = 16f,
        NudgeWidth = 42f, OtherWidth = 88f, ToolGap = 4f;

    public static MusicRects Compute(RectangleF inner, float s, bool lyrics)
    {
        float l = inner.Left, t = inner.Top, r = inner.Right;
        var art = new RectangleF(l, t, ArtSize * s, ArtSize * s);
        float x0 = l + (ArtSize + ArtGap) * s;
        float textRight = lyrics ? Math.Min(r, x0 + SplitTextWidth * s) : r;

        var title = new RectangleF(x0, t + 2f * s, Math.Max(1f, textRight - x0), TitleLine * s);
        var artist = new RectangleF(x0, t + 22f * s, Math.Max(1f, textRight - x0), ArtistLine * s);

        float b = Button * s, by = t + (ArtSize - Button) * s;
        var prev = new RectangleF(x0, by, b, b);
        var play = new RectangleF(prev.Right + ButtonGap * s, by, b, b);
        var next = new RectangleF(play.Right + ButtonGap * s, by, b, b);
        float lw = LyricsButtonWidth * s, lh = LyricsButtonHeight * s, ly = by + (b - lh) / 2f;
        var lyricsBtn = lyrics
            ? new RectangleF(next.Right + LyricsButtonGap * s, ly, lw, lh)
            : new RectangleF(r - lw, ly, lw, lh);

        float barY = t + BarCenterY * s;
        var bar = new RectangleF(l, barY - BarHeight * s / 2f, r - l, BarHeight * s);
        var barHit = new RectangleF(l, barY - BarHitHeight * s / 2f, r - l, BarHitHeight * s);
        float timeW = 60f * s;
        var elapsed = new RectangleF(l, t + TimeTop * s, timeW, TimeLine * s);
        var total = new RectangleF(r - timeW, t + TimeTop * s, timeW, TimeLine * s);

        var column = RectangleF.Empty;
        RectangleF back = RectangleF.Empty, forward = RectangleF.Empty, other = RectangleF.Empty;
        if (lyrics)
        {
            float cx = Math.Max(textRight, lyricsBtn.Right) + ColumnGap * s;
            column = new RectangleF(cx, t, Math.Max(1f, r - cx), 3f * LyricLineHeight * s);

            // Botones de la letra en la fila de los tiempos, de derecha a izquierda desde el total. Si no caben
            // (página muy estrecha), no hay botones: nunca encima del tiempo transcurrido.
            float gap = ToolGap * s, nw = NudgeWidth * s, ow = OtherWidth * s, th = TimeLine * s, ty = t + TimeTop * s;
            float right = total.Left - gap;
            if (right - ow - 2f * (nw + gap) >= x0)
            {
                other = new RectangleF(right - ow, ty, ow, th);
                forward = new RectangleF(other.Left - gap - nw, ty, nw, th);
                back = new RectangleF(forward.Left - gap - nw, ty, nw, th);
            }
        }
        return new MusicRects(art, title, artist, prev, play, next, lyricsBtn, bar, barHit, elapsed, total, column, s)
        {
            NudgeBack = back,
            NudgeForward = forward,
            OtherLyrics = other,
        };
    }

    /// <summary>Control bajo <paramref name="p"/> (coordenadas de la superficie, centro del píxel ya sumado o no: se
    /// compara con rectángulos de coma flotante). Los botones, la barra y las filas de la letra; nada más. Los botones de
    /// la vista de letra solo cuentan si se ven (<paramref name="tools"/>).</summary>
    public static MusicHit HitTest(PointF p, RectangleF inner, float s, bool lyrics, LyricsTools tools = LyricsTools.None)
    {
        if (!inner.Contains(p)) return default;
        var r = Compute(inner, s, lyrics);
        if (r.Previous.Contains(p)) return new(MusicControl.Previous);
        if (r.PlayPause.Contains(p)) return new(MusicControl.PlayPause);
        if (r.Next.Contains(p)) return new(MusicControl.Next);
        if (r.LyricsButton.Contains(p)) return new(MusicControl.Lyrics);
        if (r.BarHit.Contains(p))
            return new(MusicControl.Progress, r.Bar.Width > 0 ? Math.Clamp((p.X - r.Bar.Left) / r.Bar.Width, 0f, 1f) : 0f);
        if (lyrics)
        {
            if ((tools & LyricsTools.Nudge) != 0)
            {
                if (r.NudgeBack.Contains(p)) return new(MusicControl.NudgeBack);
                if (r.NudgeForward.Contains(p)) return new(MusicControl.NudgeForward);
            }
            if ((tools & LyricsTools.Other) != 0 && r.OtherLyrics.Contains(p)) return new(MusicControl.OtherLyrics);
            for (int i = -1; i <= 1; i++)
                if (r.LyricLine(i).Contains(p)) return new(MusicControl.LyricLine, 0f, i);
        }
        return default;
    }
}

/// <summary>Formato del tiempo de la página Música. PURO.</summary>
public static class MusicFormat
{
    /// <summary>«m:ss» (o «h:mm:ss» desde una hora), truncado al segundo; negativo → «0:00».</summary>
    public static string Time(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        long total = (long)t.TotalSeconds;
        long h = total / 3600, m = total / 60 % 60, sec = total % 60;
        return h > 0 ? $"{h}:{m:00}:{sec:00}" : $"{total / 60}:{sec:00}";
    }

    /// <summary>Etiqueta de «Otra letra» (letras v2): la fuente de la letra que se ve y, con los candidatos ya pedidos,
    /// «n/total» («LRCLIB 1/3»); mientras se piden, «…» detrás. Sin fuente conocida, <paramref name="fallback"/>
    /// («Otra letra»).</summary>
    public static string SourceLabel(string? source, int index, int count, bool loading, string fallback)
    {
        string name = string.IsNullOrWhiteSpace(source) ? fallback : source;
        if (loading) return name + " …";
        return count > 0 && index >= 0 ? $"{name} {index + 1}/{count}" : name;
    }

    /// <summary>Desfase con signo y una cifra decimal en la cultura de la interfaz («+1,5 s», «−0,5 s»); «0 s» sin
    /// desfase. El menos es el signo tipográfico (U+2212), como en los botones.</summary>
    public static string Offset(double seconds, System.Globalization.CultureInfo culture)
    {
        if (Math.Abs(seconds) < 0.05) return "0 s";
        string n = Math.Abs(seconds).ToString("0.0", culture);
        return (seconds > 0 ? "+" : "\u2212") + n + " s";
    }
}

/// <summary>
/// Letra PLANA en la página Música (letras v2). PURO: reparte las líneas por la canción para que la vista se desplace
/// sola al ritmo de la reproducción (posición / duración). Es una estimación: la primera línea al empezar y la última
/// al <see cref="Span"/> de la duración (las canciones suelen acabar con un final instrumental). Las líneas vacías
/// (separación entre estrofas) se quitan. Sin duración conocida, una línea cada <see cref="FallbackLine"/>.
/// Con estas líneas la vista usa el mismo motor que la sincronizada (índice, cruce, plazo del repintado).
/// </summary>
public static class PlainLyricsTiming
{
    public const double Span = 0.92;
    public static readonly TimeSpan FallbackLine = TimeSpan.FromSeconds(3.5);

    public static IReadOnlyList<LyricLine> Estimate(string? text, TimeSpan duration)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<LyricLine>();
        var rows = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
            .Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (rows.Count == 0) return Array.Empty<LyricLine>();
        var lines = new LyricLine[rows.Count];
        for (int i = 0; i < rows.Count; i++)
        {
            var at = duration > TimeSpan.Zero
                ? TimeSpan.FromTicks((long)(duration.Ticks * Span * i / rows.Count))
                : TimeSpan.FromTicks(FallbackLine.Ticks * i);
            lines[i] = new LyricLine(at, rows[i]);
        }
        return lines;
    }
}

/// <summary>
/// Cuándo hay que repintar la página Música (F5 · B3). PURO. Sin música o en pausa no hay nada que mover ⇒ null (0 %
/// CPU). Sonando, el primero de: siguiente línea de la letra (con su adelanto de 80 ms), siguiente segundo del texto
/// «1:23» y siguiente px de la barra; acotado a [<see cref="MinTick"/>, <see cref="MaxTick"/>] (ni un bucle apretado ni
/// una barra a saltos). La ventana solo lo usa con la página visible.
/// </summary>
public static class MusicClock
{
    public static readonly TimeSpan MinTick = TimeSpan.FromMilliseconds(15);
    public static readonly TimeSpan MaxTick = TimeSpan.FromMilliseconds(250);

    /// <summary>Adelanto de la letra (compensa el repintado), el de <see cref="LrcParser.IndexAt"/>.</summary>
    public static readonly TimeSpan LyricLead = TimeSpan.FromMilliseconds(80);

    /// <param name="lyricsShift">Desfase manual de la letra (letras v2; positivo = la letra va más tarde): la línea i
    /// toca en el instante del reproductor <c>lines[i].At + lyricsShift</c>.</param>
    public static TimeSpan? NextTickIn(NowPlaying? track, DateTime utcNow, IReadOnlyList<LyricLine>? lines, float barWidthPx,
        TimeSpan lyricsShift = default)
    {
        if (track is not { Playing: true }) return null;
        var pos = track.PositionAt(utcNow);
        long next = TimeSpan.TicksPerSecond - pos.Ticks % TimeSpan.TicksPerSecond;   // siguiente segundo

        if (track.Duration > TimeSpan.Zero && barWidthPx >= 1f)
        {
            long perPx = (long)(track.Duration.Ticks / (double)barWidthPx);
            if (perPx > 0) next = Math.Min(next, perPx - pos.Ticks % perPx);
        }

        if (lines is { Count: > 0 })
        {
            var at = pos - lyricsShift;   // instante de la letra
            int i = LrcParser.IndexAt(lines, at, LyricLead) + 1;
            if (i < lines.Count)
            {
                long toLine = (lines[i].At - LyricLead - at).Ticks;
                if (toLine > 0) next = Math.Min(next, toLine);
            }
        }

        return TimeSpan.FromTicks(Math.Clamp(next, MinTick.Ticks, MaxTick.Ticks));
    }
}

/// <summary>
/// ¿El reposo enseña la canción (carátula mini + título) en vez de los anillos? (F5 · B3). PURO, solo eventos: sí
/// mientras suena algo y durante <see cref="PauseGrace"/> tras pausarlo (el hueco entre dos canciones no hace parpadear
/// la maquetación). Una canción que nunca sonó (en pausa desde el arranque) no cuenta; sin reproductor, fuera al instante.
/// </summary>
public sealed class IdleMusic
{
    public static readonly TimeSpan PauseGrace = TimeSpan.FromSeconds(3);

    private DateTime? _pausedAt;
    private bool _played;

    /// <summary>La última pista vista (null si no hay reproductor o nunca sonó).</summary>
    public NowPlaying? Track { get; private set; }

    public void Observe(NowPlaying? track, DateTime utcNow)
    {
        if (track is null)
        {
            Track = null;
            _played = false;
            _pausedAt = null;
            return;
        }
        if (track.Playing)
        {
            Track = track;
            _played = true;
            _pausedAt = null;
            return;
        }
        if (!_played) return;                  // en pausa sin haber sonado: nada que enseñar
        if (Track is { Playing: true } || _pausedAt is null) _pausedAt = utcNow;
        Track = track;
    }

    public bool Show(DateTime utcNow) =>
        Track is not null && (Track.Playing || (_pausedAt is { } p && utcNow - p < PauseGrace));

    /// <summary>Cuándo deja de enseñarse la canción pausada (para el plazo de la ventana); null si no hay nada que vigilar.</summary>
    public TimeSpan? NextChangeIn(DateTime utcNow)
    {
        if (Track is null || Track.Playing || _pausedAt is not { } p) return null;
        var left = p + PauseGrace - utcNow;
        return left > TimeSpan.Zero ? left : null;
    }
}

/// <summary>
/// Movimiento de la página Música (F5 · B3; lenguaje de movimiento §3.0). PURO, el instante llega por parámetro:
/// <list type="bullet">
/// <item>Abrir/cerrar la vista de letra: cruce lineal de <see cref="Duration"/> entre la maquetación normal y la dividida
/// (<see cref="SplitBlend"/> 0 → 1); al invertirlo a medias sigue desde donde está.</item>
/// <item>Cambio de línea de la letra: texto que cambia dentro del mismo diseño ⇒ cruce simultáneo de 120 ms, sin
/// desplazamiento (<see cref="LineBlend"/>: alfa de la nueva; la anterior lleva 1 − blend).</item>
/// </list>
/// Quieto ⇒ <see cref="IsAnimating"/> = false. Con reduce-motion todo salta.
/// </summary>
public sealed class MusicMotion
{
    public static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(120);

    private float _splitFrom;
    private TimeSpan _splitAt;
    private bool _splitMoving;
    private TimeSpan _lineAt;

    public bool ReduceMotion { get; set; }

    /// <summary>Vista de letra pedida (objetivo del cruce).</summary>
    public bool ShowLyrics { get; private set; }

    /// <summary>0 = maquetación normal, 1 = dividida con la letra.</summary>
    public float SplitBlend { get; private set; }

    /// <summary>Línea actual de la letra (−1 antes de la primera).</summary>
    public int Line { get; private set; } = -1;

    /// <summary>Línea que se va en el cruce.</summary>
    public int PreviousLine { get; private set; } = -1;

    /// <summary>Alfa de <see cref="Line"/>; 1 sin cruce.</summary>
    public float LineBlend { get; private set; } = 1f;

    public bool IsAnimating => _splitMoving || LineBlend < 1f;

    public void SetLyrics(bool on, TimeSpan now)
    {
        if (on == ShowLyrics && !_splitMoving) return;
        ShowLyrics = on;
        if (ReduceMotion)
        {
            SplitBlend = on ? 1f : 0f;
            _splitMoving = false;
            return;
        }
        _splitFrom = SplitBlend;
        _splitAt = now;
        _splitMoving = SplitBlend != (on ? 1f : 0f);
    }

    /// <summary>Cambia de línea con cruce (si es otra).</summary>
    public void OnLine(int line, TimeSpan now)
    {
        if (line == Line) return;
        PreviousLine = Line;
        Line = line;
        if (ReduceMotion) { LineBlend = 1f; return; }
        LineBlend = 0f;
        _lineAt = now;
    }

    /// <summary>Salta a una línea sin cruce (letra nueva, otra pista).</summary>
    public void JumpLine(int line)
    {
        Line = line;
        PreviousLine = -1;
        LineBlend = 1f;
    }

    /// <summary>Todo al estado final sin animar (página oculta: al volver no se ve un cruce viejo).</summary>
    public void Settle()
    {
        SplitBlend = ShowLyrics ? 1f : 0f;
        _splitMoving = false;
        LineBlend = 1f;
    }

    public void Step(TimeSpan now)
    {
        if (_splitMoving)
        {
            float k = (float)((now - _splitAt).TotalMilliseconds / Duration.TotalMilliseconds);
            float target = ShowLyrics ? 1f : 0f;
            float v = _splitFrom + (target > _splitFrom ? k : -k);
            if (ReduceMotion || (target > _splitFrom ? v >= target : v <= target)) { SplitBlend = target; _splitMoving = false; }
            else SplitBlend = Math.Clamp(v, 0f, 1f);
        }
        if (LineBlend < 1f)
        {
            float k = (float)((now - _lineAt).TotalMilliseconds / Duration.TotalMilliseconds);
            LineBlend = ReduceMotion || k >= 1f ? 1f : Math.Max(0f, k);
        }
    }
}
