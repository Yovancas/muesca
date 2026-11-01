using ClaudeBarWin.Services.Tutorial;
using ClaudeBarWin.UI.Onboarding;

namespace ClaudeBarWin.UI.Tutorial;

/// <summary>Un control del tutorial que recibe clics y foco. <see cref="TutorialHit.Index"/> dice cuál en los que son varios
/// (capítulo de la barra de progreso, paso del pie, entrada del menú o fila de los ajustes de mentira).</summary>
public enum TutorialControl
{
    None, Exit, Back, Skip, Next, Chapter, StepDot, ProLink,
    /// <summary>El notch de demostración (lo acierta la escena con las geometrías del notch de verdad).</summary>
    Notch,
    /// <summary>El archivo de ejemplo del escritorio (se arrastra al notch).</summary>
    SampleFile,
    /// <summary>El icono de Muesca en la bandeja de mentira.</summary>
    TrayIcon,
    /// <summary>Entrada del menú de la bandeja de mentira (<see cref="TutorialMenuItem"/>).</summary>
    MenuItem,
    /// <summary>Fila de los ajustes de mentira (<see cref="TutorialSettingsRow"/>).</summary>
    SettingsRow,
}

/// <summary>Control bajo el ratón o con el foco.</summary>
public readonly record struct TutorialHit(TutorialControl Control, int Index = 0)
{
    public static readonly TutorialHit None = new(TutorialControl.None);

    /// <summary>Los botones del pie y el enlace del Pro (Enter los pulsa).</summary>
    public bool IsButton => Control is TutorialControl.Exit or TutorialControl.Back or TutorialControl.Skip
        or TutorialControl.Next or TutorialControl.ProLink or TutorialControl.Chapter;
}

/// <summary>Entradas del menú de la bandeja de mentira, en el orden del de verdad (<c>TrayAppContext.BuildMenu</c>).</summary>
public enum TutorialMenuItem { Dashboard, Settings, Reconfigure, Monitor, LiveSessions, Updates, Pro, Tutorial, Exit }

/// <summary>Filas de los ajustes de mentira: lo que el capítulo 10 enseña de Ajustes.</summary>
public enum TutorialSettingsRow { Monitor, Size, ReduceMotion, UsageStats, Reconfigure, Tutorial }

/// <summary>
/// Geometría del tutorial dentro del notch grande. PURA (sin Win32 ni estado): la usan el painter para pintar y la ventana para
/// acertar el clic y mover el foco, así pintar y acertar salen de la misma cuenta (como <see cref="OnboardingGeometry"/>). Todo
/// en px lógicos × escala y relativo a la pastilla, que va centrada arriba.
///
/// <code>
///  ┌──────────────────────────────────── 760 ────────────────────────────────────┐
///  │  Capítulo 3 de 10                                              Salir · Esc  │
///  │  Permisos                                                                   │
///  │  ▬▬▬ ▬▬▬ ▬▬▬ ─── ─── ─── ─── ─── ─── ───   (capítulos)                      │
///  │  ┌─ escenario: escritorio de mentira con el notch de demostración ────────┐ │ 548
///  │  └────────────────────────────────────────────────────────────────────────┘ │
///  │  Paso 1 de 2 · Botones que se arman  [Pro]                       ✓ Hecho    │
///  │  La frase del paso (hasta dos líneas)                                       │
///  │  Teclado: Intro lo hace                                                     │
///  │  [ Atrás ]           ● ━            [ Saltar paso ]  [ Siguiente ]          │
///  └─────────────────────────────────────────────────────────────────────────────┘
/// </code>
/// </summary>
public static class TutorialGeometry
{
    /// <summary>Tamaño del tutorial abierto (px lógicos): con la sombra cabe en 1366 × 768 a 100 % con la barra de tareas.</summary>
    public const float Width = 760f, Height = 548f;

    public const float PadX = 28f, StageTop = 84f, StageHeight = 284f;
    public const float ButtonHeight = 32f, BackWidth = 104f, NextWidth = 140f, SkipWidth = 136f, FooterBottom = 20f, ButtonGap = 8f;
    public const float ChapterTop = 66f, ChapterBar = 5f, ChapterGap = 6f;

    /// <summary>Escala del notch de demostración respecto a la del tutorial: la página Uso (560 × 256) cabe en el escenario.</summary>
    public const float DemoScale = 1f;

    private static RectangleF R(RectangleF pill, float s, float x, float y, float w, float h) =>
        new(pill.Left + x * s, pill.Top + y * s, w * s, h * s);

    // ---------------------------------------------------------------- cabecera

    public static RectangleF ChapterLabel(RectangleF pill, float s) => R(pill, s, PadX, 20f, 420f, 16f);
    public static RectangleF ChapterTitle(RectangleF pill, float s) => R(pill, s, PadX, 36f, 520f, 26f);
    public static RectangleF Exit(RectangleF pill, float s) => R(pill, s, Width - PadX - 150f, 22f, 150f, 22f);

    /// <summary>La marca del capítulo <paramref name="index"/> de <paramref name="count"/> en la barra de progreso.</summary>
    public static RectangleF ChapterMark(RectangleF pill, float s, int count, int index)
    {
        count = Math.Max(1, count);
        float w = (Width - 2 * PadX - (count - 1) * ChapterGap) / count;
        return R(pill, s, PadX + index * (w + ChapterGap), ChapterTop, w, ChapterBar);
    }

    /// <summary>Celda de clic de una marca de capítulo: más alta que la barra (5 px serían imposibles de acertar).</summary>
    public static RectangleF ChapterCell(RectangleF pill, float s, int count, int index)
    {
        var m = ChapterMark(pill, s, count, index);
        return RectangleF.FromLTRB(m.Left - ChapterGap * s / 2f, m.Top - 7f * s, m.Right + ChapterGap * s / 2f, m.Bottom + 7f * s);
    }

    // ---------------------------------------------------------------- escenario

    /// <summary>El escritorio de mentira donde cuelga el notch de demostración (de su borde de arriba, como en la pantalla).</summary>
    public static RectangleF Stage(RectangleF pill, float s) => R(pill, s, PadX, StageTop, Width - 2 * PadX, StageHeight);

    /// <summary>Superficie del notch de demostración: el escenario en px enteros (el painter del notch centra arriba).</summary>
    public static Size StageSurface(RectangleF stage) =>
        new((int)Math.Round(stage.Width), (int)Math.Round(stage.Height));

    /// <summary>La línea de lo que «pasaría» de verdad, abajo en el escenario.</summary>
    public static RectangleF Effect(RectangleF stage, float s) =>
        new(stage.Left + 60f * s, stage.Bottom - 40f * s, stage.Width - 120f * s, 26f * s);

    /// <summary>El archivo de ejemplo (icono, nombre y la pista de arrastrar), abajo a la izquierda del escritorio.</summary>
    public static RectangleF SampleFile(RectangleF stage, float s) =>
        new(stage.Left + 36f * s, stage.Bottom - 118f * s, 120f * s, 96f * s);

    /// <summary>El icono del archivo de ejemplo dentro de su zona (o siguiendo al ratón mientras se arrastra).</summary>
    public static RectangleF FileIcon(RectangleF file, float s) =>
        new(file.Left + (file.Width - 40f * s) / 2f, file.Top + 4f * s, 40f * s, 50f * s);

    // ---------------------------------------------------------------- bandeja de mentira (capítulo 10)

    public const float TaskbarHeight = 32f, MenuWidth = 260f, MenuRow = 22f, MenuPad = 6f, PanelWidth = 230f, PanelHeight = 150f,
        SettingsWidth = 300f, SettingsRowHeight = 30f, MonitorWidth = 230f;

    public static RectangleF Taskbar(RectangleF stage, float s) =>
        RectangleF.FromLTRB(stage.Left, stage.Bottom - TaskbarHeight * s, stage.Right, stage.Bottom);

    public static RectangleF TrayIcon(RectangleF stage, float s)
    {
        var bar = Taskbar(stage, s);
        float d = 22f * s;
        return new RectangleF(bar.Right - 104f * s, bar.Top + (bar.Height - d) / 2f, d, d);
    }

    public static RectangleF TrayClock(RectangleF stage, float s)
    {
        var bar = Taskbar(stage, s);
        return RectangleF.FromLTRB(bar.Right - 72f * s, bar.Top, bar.Right - 10f * s, bar.Bottom);
    }

    /// <summary>El panel de la cuota encima del icono.</summary>
    public static RectangleF Panel(RectangleF stage, float s)
    {
        var icon = TrayIcon(stage, s);
        float w = PanelWidth * s, h = PanelHeight * s;
        return new RectangleF(Math.Min(stage.Right - 8f * s - w, icon.Left + icon.Width / 2f - w / 2f),
            Taskbar(stage, s).Top - 8f * s - h, w, h);
    }

    /// <summary>El menú de la bandeja, encima del icono (como el de Windows).</summary>
    public static RectangleF Menu(RectangleF stage, float s)
    {
        int n = Enum.GetValues<TutorialMenuItem>().Length;
        float w = MenuWidth * s, h = (n * MenuRow + 2 * MenuPad) * s;
        var icon = TrayIcon(stage, s);
        return new RectangleF(Math.Min(stage.Right - 8f * s - w, icon.Left), Taskbar(stage, s).Top - 4f * s - h, w, h);
    }

    public static RectangleF MenuItemRect(RectangleF stage, float s, TutorialMenuItem item)
    {
        var m = Menu(stage, s);
        return new RectangleF(m.Left + 4f * s, m.Top + (MenuPad + (int)item * MenuRow) * s, m.Width - 8f * s, MenuRow * s);
    }

    /// <summary>El submenú «Monitor del notch», a la izquierda del menú, a la altura de su entrada.</summary>
    public static RectangleF MonitorMenu(RectangleF stage, float s, int rows)
    {
        var item = MenuItemRect(stage, s, TutorialMenuItem.Monitor);
        float w = MonitorWidth * s, h = (Math.Max(1, rows) * MenuRow + 2 * MenuPad) * s;
        return new RectangleF(Menu(stage, s).Left - w + 2f * s, Math.Max(stage.Top + 4f * s, item.Top - MenuPad * s), w, h);
    }

    /// <summary>Los ajustes de mentira, a la izquierda del icono.</summary>
    public static RectangleF Settings(RectangleF stage, float s)
    {
        int n = Enum.GetValues<TutorialSettingsRow>().Length;
        float w = SettingsWidth * s, h = (34f + n * SettingsRowHeight + 8f) * s;
        return new RectangleF(stage.Right - 12f * s - w - 120f * s, Taskbar(stage, s).Top - 8f * s - h, w, h);
    }

    public static RectangleF SettingsRowRect(RectangleF stage, float s, TutorialSettingsRow row)
    {
        var p = Settings(stage, s);
        return new RectangleF(p.Left + 8f * s, p.Top + (34f + (int)row * SettingsRowHeight) * s, p.Width - 16f * s,
            SettingsRowHeight * s);
    }

    // ---------------------------------------------------------------- paso

    public static RectangleF StepLabel(RectangleF pill, float s) => R(pill, s, PadX, 378f, Width - 2 * PadX, 16f);
    public static RectangleF StepTitle(RectangleF pill, float s) => R(pill, s, PadX, 394f, 560f, 24f);
    public static RectangleF DoneBadge(RectangleF pill, float s) => R(pill, s, Width - PadX - 140f, 396f, 140f, 20f);
    public static RectangleF StepText(RectangleF pill, float s) => R(pill, s, PadX, 422f, Width - 2 * PadX, 38f);
    public static RectangleF KeyHint(RectangleF pill, float s) => R(pill, s, PadX, 464f, Width - 2 * PadX - 190f, 18f);

    /// <summary>El enlace «Ver Pro ›» del cierre de capítulo, a la derecha de la línea de las teclas.</summary>
    public static RectangleF ProLink(RectangleF pill, float s) => R(pill, s, Width - PadX - 180f, 462f, 180f, 22f);

    // ---------------------------------------------------------------- pie

    public static RectangleF Back(RectangleF pill, float s) =>
        R(pill, s, PadX, Height - FooterBottom - ButtonHeight, BackWidth, ButtonHeight);

    public static RectangleF Next(RectangleF pill, float s) =>
        R(pill, s, Width - PadX - NextWidth, Height - FooterBottom - ButtonHeight, NextWidth, ButtonHeight);

    public static RectangleF Skip(RectangleF pill, float s) =>
        R(pill, s, Width - PadX - NextWidth - ButtonGap - SkipWidth, Height - FooterBottom - ButtonHeight, SkipWidth, ButtonHeight);

    /// <summary>Centro del punto del paso <paramref name="index"/> de <paramref name="count"/>, entre «Atrás» y «Saltar paso».</summary>
    public static PointF StepDotCenter(RectangleF pill, float s, int count, float index)
    {
        float pitch = Notch.NotchGeometry.PageDotPitch * s;
        float mid = (Back(pill, s).Right + Skip(pill, s).Left) / 2f;
        float x = mid + (index - (count - 1) / 2f) * pitch;
        return new PointF(x, pill.Top + (Height - FooterBottom - ButtonHeight / 2f) * s);
    }

    public static RectangleF StepDotCell(RectangleF pill, float s, int count, int index)
    {
        var c = StepDotCenter(pill, s, count, index);
        float pitch = Notch.NotchGeometry.PageDotPitch * s;
        return new RectangleF(c.X - pitch / 2f, c.Y - ButtonHeight * s / 2f, pitch, ButtonHeight * s);
    }

    // ---------------------------------------------------------------- controles, clic y foco

    /// <summary>
    /// Los controles del marco que reciben clic, con su rectángulo, en orden de tabulación: el notch de demostración (para
    /// llevar el foco a él: Intro hace el gesto del paso), los capítulos, el enlace del Pro (si se ve), Atrás, los puntos,
    /// Saltar, Siguiente y Salir. Lo del escenario (notch, archivo, bandeja) lo acierta la escena aparte.
    /// </summary>
    public static IReadOnlyList<(TutorialHit Hit, RectangleF Rect)> Controls(RectangleF pill, float s, int chapters, int steps,
        TutorialPhase phase, bool proLink)
    {
        var list = new List<(TutorialHit, RectangleF)>(chapters + steps + 8)
        {
            (new(TutorialControl.Notch), Stage(pill, s)),
        };
        for (int i = 0; i < chapters; i++) list.Add((new(TutorialControl.Chapter, i), ChapterCell(pill, s, chapters, i)));
        if (proLink) list.Add((new(TutorialControl.ProLink), ProLink(pill, s)));
        list.Add((new(TutorialControl.Back), Back(pill, s)));
        if (phase == TutorialPhase.Step)
            for (int i = 0; i < steps; i++) list.Add((new(TutorialControl.StepDot, i), StepDotCell(pill, s, steps, i)));
        list.Add((new(TutorialControl.Skip), Skip(pill, s)));
        list.Add((new(TutorialControl.Next), Next(pill, s)));
        list.Add((new(TutorialControl.Exit), Exit(pill, s)));
        return list;
    }

    /// <summary>Control del marco bajo <paramref name="p"/> (del último al primero, como la bienvenida); el escenario cuenta
    /// como <see cref="TutorialControl.Notch"/> (la escena afina qué hay debajo).</summary>
    public static TutorialHit HitTest(Point p, RectangleF pill, float s, int chapters, int steps, TutorialPhase phase, bool proLink)
    {
        if (!pill.Contains(p.X + 0.5f, p.Y + 0.5f)) return TutorialHit.None;
        var controls = Controls(pill, s, chapters, steps, phase, proLink);
        for (int i = controls.Count - 1; i >= 0; i--)
            if (controls[i].Rect.Contains(p.X + 0.5f, p.Y + 0.5f)) return controls[i].Hit;
        return TutorialHit.None;
    }

    /// <summary>Lo que recorre Tab: los controles usables (<paramref name="usable"/>) sin los puntos de los pasos (atajos del
    /// ratón; ← → y Atrás/Siguiente ya mueven entre pasos).</summary>
    public static IReadOnlyList<TutorialHit> FocusOrder(int chapters, int steps, TutorialPhase phase, bool proLink,
        Func<TutorialHit, bool> usable)
    {
        ArgumentNullException.ThrowIfNull(usable);
        var order = new List<TutorialHit>(chapters + 8);
        foreach (var (hit, _) in Controls(new RectangleF(0, 0, Width, Height), 1f, chapters, steps, phase, proLink))
            if (hit.Control != TutorialControl.StepDot && usable(hit)) order.Add(hit);
        return order;
    }

    /// <summary>El siguiente (o anterior) de <paramref name="order"/> tras <paramref name="current"/>, dando la vuelta.</summary>
    public static TutorialHit MoveFocus(IReadOnlyList<TutorialHit> order, TutorialHit current, int delta)
    {
        if (order.Count == 0) return TutorialHit.None;
        int i = -1;
        for (int k = 0; k < order.Count; k++)
            if (order[k] == current) { i = k; break; }
        if (i < 0) return delta < 0 ? order[^1] : order[0];
        int n = order.Count;
        return order[((i + Math.Sign(delta == 0 ? 1 : delta)) % n + n) % n];
    }
}
