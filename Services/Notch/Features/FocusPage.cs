using System.Drawing;

namespace ClaudeBarWin.Services.Notch.Features;

/// <summary>Control de la página Foco bajo el ratón o pulsado.</summary>
public enum FocusControl
{
    None,
    /// <summary>Empezar / Pausa / Reanudar (o, con la oferta, «Empezar descanso» / «Empezar foco»).</summary>
    Primary,
    Skip,
    Reset,
    /// <summary>La fila de la tarea enlazada (Pro): un clic pasa a la siguiente tarea pendiente.</summary>
    Task,
    /// <summary>Sin el Pro: la fila de la tarea o la de las estadísticas, con el chip «Pro» (abren la tarjeta).</summary>
    ProChip,
}

/// <summary>Control de la página Foco.</summary>
public readonly record struct FocusHit(FocusControl Control);

/// <summary>
/// Rectángulos de la página Foco en px físicos (de <see cref="FocusLayout.Compute"/>): a la izquierda el anillo grande con la
/// cuenta atrás; a la derecha la cabecera (título y focos de hoy), la línea de la fase, los botones, la tarea enlazada y la
/// semana.
/// </summary>
public readonly record struct FocusRects(RectangleF Ring, RectangleF Header, RectangleF Phase, RectangleF Primary,
    RectangleF Skip, RectangleF Reset, RectangleF Task, RectangleF Stats, float Scale)
{
    /// <summary>El reloj dentro del anillo (centrado, algo por encima del centro: debajo van los puntos del ciclo).</summary>
    public RectangleF Clock
    {
        get
        {
            float h = FocusLayout.ClockHeight * Scale;
            return new RectangleF(Ring.Left, Ring.Top + (Ring.Height - h) / 2f - FocusLayout.ClockLift * Scale, Ring.Width, h);
        }
    }

    /// <summary>Punto <paramref name="i"/> de <paramref name="count"/> del ciclo hacia el descanso largo, bajo el reloj.</summary>
    public RectangleF CycleDot(int i, int count)
    {
        float d = FocusLayout.CycleDot * Scale, gap = FocusLayout.CycleDotGap * Scale;
        float w = count * d + Math.Max(0, count - 1) * gap;
        float x = Ring.Left + (Ring.Width - w) / 2f + i * (d + gap);
        return new RectangleF(x, Clock.Bottom + FocusLayout.CycleDotTop * Scale, d, d);
    }

    /// <summary>Chip «Pro» al final de una fila (la de la tarea o la de la semana).</summary>
    public RectangleF Chip(RectangleF row)
    {
        float w = Math.Min(FocusLayout.ChipWidth * Scale, row.Width), h = Math.Min(FocusLayout.ChipHeight * Scale, row.Height);
        return new RectangleF(row.Right - w, row.Top + (row.Height - h) / 2f, w, h);
    }

    /// <summary>Barra del día <paramref name="i"/> (0 = hace seis días, 6 = hoy) de la semana, a la izquierda de su fila.</summary>
    public RectangleF StatsBar(int i)
    {
        float w = FocusLayout.StatsBarWidth * Scale, gap = FocusLayout.StatsBarGap * Scale;
        return new RectangleF(Stats.Left + i * (w + gap), Stats.Top + FocusLayout.StatsBarInset * Scale, w,
            Stats.Height - 2 * FocusLayout.StatsBarInset * Scale);
    }

    /// <summary>El texto de la semana, tras las barras.</summary>
    public RectangleF StatsLabel
    {
        get
        {
            float x = StatsBar(PomodoroTimer.WeekDays - 1).Right + FocusLayout.StatsLabelGap * Scale;
            return RectangleF.FromLTRB(Math.Min(x, Stats.Right), Stats.Top, Stats.Right, Stats.Bottom);
        }
    }
}

/// <summary>
/// Geometría de la página Foco. PURA: la MISMA para pintar y para acertar el ratón. Va a su tamaño final anclada arriba (como
/// la página Uso): mientras el expandido crece, se destapa en vez de estirarse. Todo en px lógicos × escala. Cabe en el
/// expandido de siempre (480 de ancho) con el aviso integrado y los puntos de página.
/// </summary>
public static class FocusLayout
{
    /// <summary>Anillo grande: diámetro, trazo y hueco hasta la columna de la derecha.</summary>
    public const float Ring = 100f, RingStroke = 6f, RingGap = 20f;

    /// <summary>Reloj dentro del anillo (alto de su línea y cuánto sube sobre el centro) y los puntos del ciclo debajo.</summary>
    public const float ClockHeight = 28f, ClockLift = 6f, CycleDot = 5f, CycleDotGap = 4f, CycleDotTop = 2f;

    /// <summary>Filas de la columna derecha.</summary>
    public const float Header = 20f, HeaderGap = 2f, PhaseLine = 16f, PhaseGap = 6f, Buttons = 26f, ButtonsGap = 8f,
        TaskRow = 18f, TaskGap = 4f, StatsRow = 16f;

    /// <summary>Botones: el principal con su texto; saltar y reiniciar, redondos con su icono.</summary>
    public const float PrimaryWidth = 150f, IconButton = 26f, ButtonGap = 8f;

    /// <summary>Chip «Pro» de las filas de la tarea y de la semana.</summary>
    public const float ChipWidth = 30f, ChipHeight = 16f;

    /// <summary>Barras de la semana.</summary>
    public const float StatsBarWidth = 6f, StatsBarGap = 3f, StatsBarInset = 2f, StatsLabelGap = 10f;

    /// <summary>Alto del contenido (sin márgenes, aviso ni puntos): la columna derecha; el anillo va centrado en ella.</summary>
    public const float ContentHeight = Header + HeaderGap + PhaseLine + PhaseGap + Buttons + ButtonsGap + TaskRow + TaskGap + StatsRow;

    /// <summary>Rectángulos de la página dentro de <paramref name="inner"/> (la zona de contenido del expandido).</summary>
    public static FocusRects Compute(RectangleF inner, float s)
    {
        float top = inner.Top, x = inner.Left;
        float d = Ring * s;
        var ring = new RectangleF(x, top + (ContentHeight * s - d) / 2f, d, d);
        float cx = ring.Right + RingGap * s, right = Math.Max(cx, inner.Right);
        float y = top;
        RectangleF Row(float h, float gapAfter)
        {
            var r = RectangleF.FromLTRB(cx, y, right, y + h * s);
            y += (h + gapAfter) * s;
            return r;
        }
        var header = Row(Header, HeaderGap);
        var phase = Row(PhaseLine, PhaseGap);
        var buttons = Row(Buttons, ButtonsGap);
        var task = Row(TaskRow, TaskGap);
        var stats = Row(StatsRow, 0f);
        var primary = new RectangleF(cx, buttons.Top, Math.Min(PrimaryWidth * s, buttons.Width), buttons.Height);
        float icon = IconButton * s, gap = ButtonGap * s;
        var skip = new RectangleF(primary.Right + gap, buttons.Top + (buttons.Height - icon) / 2f, icon, icon);
        var reset = new RectangleF(skip.Right + gap, skip.Top, icon, icon);
        return new FocusRects(ring, header, phase, primary, skip, reset, task, stats, s);
    }

    /// <summary>Tooltip de saltar y reiniciar (px lógicos): alto, margen del texto a los lados y hueco sobre el icono. Cabe en
    /// la línea de la fase, encima de los botones, sin tapar la cabecera.</summary>
    public const float TipHeight = 18f, TipPadX = 7f, TipGap = 4f;

    /// <summary>Caja del tooltip de <paramref name="button"/> con un texto de <paramref name="textWidth"/> px: justo encima,
    /// centrada sobre el icono y, si no cabe, pegada al borde de <paramref name="bounds"/>.</summary>
    public static RectangleF TipBox(RectangleF button, float textWidth, RectangleF bounds, float s)
    {
        float w = Math.Min(textWidth + 2f * TipPadX * s, bounds.Width), h = TipHeight * s;
        float left = button.Left + button.Width / 2f - w / 2f;
        left = Math.Clamp(left, bounds.Left, Math.Max(bounds.Left, bounds.Right - w));
        return new RectangleF(left, button.Top - TipGap * s - h, w, h);
    }
    /// <summary>
    /// Control bajo <paramref name="p"/>. Los botones del pomodoro valen siempre (es gratis); la fila de la tarea, con el Pro
    /// (sin él, <paramref name="locked"/>, la fila de la tarea y la de la semana abren la tarjeta del Pro). El anillo no hace
    /// nada (no es un botón: un clic suelto sobre él no debe pausar sin querer).
    /// </summary>
    public static FocusHit HitTest(PointF p, FocusRects r, bool locked)
    {
        if (r.Primary.Contains(p)) return new(FocusControl.Primary);
        if (r.Skip.Contains(p)) return new(FocusControl.Skip);
        if (r.Reset.Contains(p)) return new(FocusControl.Reset);
        if (locked) return r.Task.Contains(p) || r.Stats.Contains(p) ? new(FocusControl.ProChip) : default;
        return r.Task.Contains(p) ? new(FocusControl.Task) : default;
    }
}

/// <summary>
/// Los mini anillos del pomodoro con el notch cerrado (px lógicos): el reposo con el foco (anillo y «25 min»), la vista previa
/// (eso a la izquierda y el anillo de 5 h con su % a la derecha) y la ranura junto al % de Trabajando. PURO.
/// </summary>
public static class FocusFace
{
    /// <summary>Reposo con el foco en marcha: un poco más ancho que el de los anillos (el texto «25 min» lo pide).</summary>
    public const float IdleWidth = 124f;

    /// <summary>Anillo del reposo y de la vista previa, su trazo y el hueco hasta el texto.</summary>
    public const float MiniRing = 16f, MiniStroke = 2.5f, MiniGap = 6f;

    /// <summary>Ranura del mini anillo en Trabajando (sin texto: Claude manda), a la izquierda del %.</summary>
    public const float WorkingSlot = 22f, WorkingRing = 13f;

    /// <summary>Alto del reposo con el foco: el del reposo de siempre (30).</summary>
    public const float IdleHeight = 30f;

    /// <summary>Lo que se ensancha el reposo con el foco cuando la cuota de 5 h está en aviso: el hueco y el anillo de 5 h
    /// (<see cref="IdleQuotaRing"/>), a la derecha del texto; la cuota alta no se esconde tras el mini anillo.</summary>
    public const float IdleQuotaGap = 10f, IdleQuotaRing = 16f;

    /// <summary>Tamaño del reposo con el foco (con <paramref name="quota"/>, más el anillo de 5 h en aviso).</summary>
    public static SizeF IdleSize(float scale, bool quota = false) =>
        new((IdleWidth + (quota ? IdleQuotaGap + IdleQuotaRing : 0f)) * scale, IdleHeight * scale);

    /// <summary>Lo que el foco suma al ancho de Trabajando (su ranura).</summary>
    public static float WorkingExtra(bool shown) => shown ? WorkingSlot : 0f;

    /// <summary>¿Lleva el reposo (y la vista previa sobre él) el mini anillo del pomodoro en vez de la canción? En marcha, sí;
    /// en pausa, solo si no suena música (<paramref name="song"/>: la canción se enseñaría). Los avisos mandan por encima.</summary>
    public static bool OnIdle(FocusRun run, bool song) => run == FocusRun.Running || (run == FocusRun.Paused && !song);
}

/// <summary>A qué tarea pasa el enlace del foco con cada clic en su fila. PURO.</summary>
public static class FocusTasks
{
    /// <summary>
    /// La siguiente de <paramref name="pending"/> (las pendientes, en el orden de la página Tareas): ninguna → la primera → … →
    /// la última → ninguna. Un enlace que ya no está entre las pendientes (hecha o quitada) pasa a la primera.
    /// </summary>
    public static Guid? Next(IReadOnlyList<Guid> pending, Guid? current)
    {
        ArgumentNullException.ThrowIfNull(pending);
        if (pending.Count == 0) return null;
        int i = -1;
        if (current is { } c)
            for (int k = 0; k < pending.Count; k++)
                if (pending[k] == c) { i = k; break; }
        if (i < 0) return pending[0];
        return i + 1 < pending.Count ? pending[i + 1] : null;
    }
}
