using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Licensing;
using ClaudeBarWin.Config;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Notch.Features;
using ClaudeBarWin.Services.Tutorial;
using ClaudeBarWin.UI.Notch;

namespace ClaudeBarWin.UI.Tutorial;

/// <summary>Una marca de la barra de capítulos: hecho, el actual, y su nombre (para el foco del teclado).</summary>
public readonly record struct TutorialChapterMark(bool Done, bool Current);

/// <summary>Una barra del panel de mentira: «5h», «42 %» (ya formateado) y cuánto se llena.</summary>
public readonly record struct TutorialPanelRow(string Label, string Pct, float Fraction);

/// <summary>Los textos de la bandeja de mentira del capítulo 10 (los del menú y los ajustes de verdad, en el idioma).</summary>
public sealed record TutorialTrayView(IReadOnlyList<string> Menu, IReadOnlyList<string> Monitors, IReadOnlyList<string> Settings,
    string SettingsTitle, string Clock, string PanelTitle, IReadOnlyList<TutorialPanelRow> Panel);

/// <summary>
/// Todo lo que pinta el tutorial en su estado actual, con los textos YA formateados: la escena lo rehace cuando cambia algo (un
/// gesto, un paso, otro idioma) y el painter lo pinta tal cual en cada frame, sin formatear nada.
/// </summary>
public sealed record TutorialView(
    TutorialPhase Phase, TutorialChapter Chapter, TutorialStep Step,
    string ChapterLabel, string ChapterTitle, string Exit, IReadOnlyList<TutorialChapterMark> Chapters,
    string StepLabel, string StepTitle, string StepText, bool Pro, bool Done, string DoneLabel, string KeyHint,
    int StepIndex, IReadOnlyList<TutorialStepStatus> StepStates,
    string Back, bool CanGoBack, string Skip, bool CanSkip, string Next, bool NextPrimary,
    string? ProLine, string? ProLink, string? Effect, string SampleFile, string DragHint, TutorialTrayView Tray);

/// <summary>
/// Construye <see cref="TutorialView"/> desde la máquina y los textos del idioma. PURO: el estado del Pro (para la línea del
/// cierre de capítulo) y la hora del reloj de la bandeja llegan por parámetro.
/// </summary>
public static class TutorialText
{
    public static TutorialView Build(TutorialMachine m, Strings s, ProState pro, DateTime localNow)
    {
        ArgumentNullException.ThrowIfNull(m);
        ArgumentNullException.ThrowIfNull(s);
        var t = s.Tut;
        var chapters = TutorialScript.Chapters.Select(c => new TutorialChapterMark(m.IsChapterDone(c), c == m.Chapter)).ToArray();
        var states = m.Steps.Select(m.Status).ToArray();
        string chapterName = Pick(t.ChapterNames, (int)m.Chapter);
        string label = Fmt(s, t.ChapterFmt, m.ChapterNumber, TutorialMachine.ChapterCount) + " · "
            + Fmt(s, t.ProgressFmt, m.ChaptersDone.Count, TutorialMachine.ChapterCount);

        string stepLabel, title, text, keys;
        bool done = false, proStep = false;
        string? proLine = null, proLink = null;
        switch (m.Phase)
        {
            case TutorialPhase.ChapterEnd:
            {
                stepLabel = "";
                title = Fmt(s, t.ChapterDoneFmt, chapterName);
                text = (int)m.Chapter + 1 < TutorialMachine.ChapterCount
                    ? Fmt(s, t.ChapterNextFmt, Pick(t.ChapterNames, (int)m.Chapter + 1)) + " " + t.ChapterEndText
                    : t.ChapterEndText;
                keys = t.KeyNext;
                done = true;
                // Sin el Pro, una línea discreta con el enlace a la tarjeta (solo si el capítulo enseñó algo del Pro).
                if (TutorialScript.StepsOf(m.Chapter).Select(TutorialScript.Info)
                    .Any(i => i.Pro is { } f && !ProGate.IsUnlocked(f, pro)))
                {
                    proLine = t.ProLine;
                    proLink = t.ProLink;
                }
                break;
            }
            case TutorialPhase.Finished:
                stepLabel = "";
                title = t.FinishedTitle;
                text = t.FinishedText;
                keys = t.KeysFooter;
                done = true;
                break;
            default:
            {
                var info = m.Info;
                stepLabel = Fmt(s, t.StepFmt, m.StepNumber, m.StepCount);
                title = Pick(t.StepTitles, (int)m.Step);
                text = Pick(t.StepTexts, (int)m.Step);
                done = m.StepDone;
                proStep = info.IsPro;
                keys = done ? t.KeyNext
                    : m.Step == TutorialStep.SessionsStates ? t.KeyUpDown
                    : info.Arrows ? t.KeyArrows : t.KeyEnter;
                break;
            }
        }
        keys = m.Phase == TutorialPhase.Finished ? keys : keys + " · " + t.KeysFooter;

        string next = m.Phase == TutorialPhase.Finished ? t.Close : t.Next;
        string skip = m.Phase == TutorialPhase.Finished ? t.Restart : t.Skip;
        bool canSkip = m.Phase == TutorialPhase.Finished || (m.Phase == TutorialPhase.Step && !m.CanAdvance);

        return new TutorialView(m.Phase, m.Chapter, m.Step, label, chapterName, t.Exit, chapters,
            stepLabel, title, text, proStep, done, t.Done, keys, m.StepIndex, states,
            t.Back, m.CanGoBack, skip, canSkip, next, m.CanAdvance || m.Phase != TutorialPhase.Step,
            proLine, proLink, Effect(m, s), t.SampleFile, t.DragHint, Tray(s, localNow));
    }

    /// <summary>Lo que «pasaría» de verdad con el último gesto (traer la terminal, abrir el archivo…), o null.</summary>
    public static string? Effect(TutorialMachine m, Strings s) => m.Demo.Effect switch
    {
        TutorialEffect.Terminal => Fmt(s, s.Tut.EffectTerminalFmt, TutorialSamples.PermissionProject),
        TutorialEffect.OpenFile => Fmt(s, s.Tut.EffectOpenFmt, s.Tut.SampleFile),
        TutorialEffect.TutorialHere => s.Tut.EffectTutorial,
        _ => null,
    };

    /// <summary>El menú, el submenú de monitores y los ajustes de mentira con los textos de verdad del idioma.</summary>
    public static TutorialTrayView Tray(Strings s, DateTime localNow)
    {
        ArgumentNullException.ThrowIfNull(s);
        var menu = new string[Enum.GetValues<TutorialMenuItem>().Length];
        menu[(int)TutorialMenuItem.Dashboard] = s.Dashboard;
        menu[(int)TutorialMenuItem.Settings] = s.Settings;
        menu[(int)TutorialMenuItem.Reconfigure] = s.MenuReconfigure;
        menu[(int)TutorialMenuItem.Monitor] = s.NotchMonitorMenu + "  ›";
        menu[(int)TutorialMenuItem.LiveSessions] = s.MenuInstallHooks;
        menu[(int)TutorialMenuItem.Updates] = s.CheckUpdates;
        menu[(int)TutorialMenuItem.Pro] = NotchProText.Menu(new ProSummary(ProState.Free, null, false), s);
        menu[(int)TutorialMenuItem.Tutorial] = s.Tut.Menu;
        menu[(int)TutorialMenuItem.Exit] = s.Exit;
        var monitors = new List<string> { s.ProMonitorsLocked };
        monitors.AddRange(NotchMonitorMenu.Modes(NotchMonitorMode.Fixed, s).Select(x => (x.Checked ? "✓ " : "   ") + x.Text));
        var settings = new string[Enum.GetValues<TutorialSettingsRow>().Length];
        settings[(int)TutorialSettingsRow.Monitor] = s.NotchMonitorMenu;
        settings[(int)TutorialSettingsRow.Size] = s.OnbSize;
        settings[(int)TutorialSettingsRow.ReduceMotion] = s.ReduceMotion;
        settings[(int)TutorialSettingsRow.UsageStats] = s.UsageStatsLabel;
        settings[(int)TutorialSettingsRow.Reconfigure] = s.MenuReconfigure;
        settings[(int)TutorialSettingsRow.Tutorial] = s.Tut.SettingsLink;
        var (time, _) = ClockText.Format(localNow, s.Culture, true);
        var panel = new[]
        {
            new TutorialPanelRow("5h", string.Format(s.Culture, "{0:0} %", 42), 0.42f),
            new TutorialPanelRow("7d", string.Format(s.Culture, "{0:0} %", 18), 0.18f),
        };
        return new TutorialTrayView(menu, monitors, settings, s.Settings, time, Brand.Name, panel);
    }

    private static string Pick(string[] values, int i) => values is { Length: > 0 } && i >= 0 && i < values.Length ? values[i] : "";

    private static string Fmt(Strings s, string format, params object[] args) => string.Format(s.Culture, format, args);
}
