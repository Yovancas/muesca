using System.Reflection;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Licensing;
using ClaudeBarWin.Services.Tutorial;
using ClaudeBarWin.UI.Tutorial;

namespace ClaudeBarWin.Tests.Tutorial;

/// <summary>Los textos del tutorial: la vista ya formateada (<see cref="TutorialText"/>) y <see cref="TutorialStrings"/> en
/// TODOS los idiomas (nada cae al inglés, cada lista tiene un texto por capítulo o paso y cabe donde se pinta).</summary>
public sealed class TutorialTextTests
{
    private static readonly DateTime Local = new(2026, 10, 30, 18, 55, 0);

    public static IEnumerable<object[]> Languages => Localization.Languages.Select(l => new object[] { l.Code });

    private static TutorialView View(TutorialMachine m, string lang = "es", ProState pro = ProState.Free) =>
        TutorialText.Build(m, Localization.Get(lang), pro, Local);

    [Fact]
    public void A_step_shows_its_chapter_step_and_keys()
    {
        var m = new TutorialMachine();
        var v = View(m);
        Assert.Equal(TutorialPhase.Step, v.Phase);
        Assert.StartsWith("Capítulo 1 de 10", v.ChapterLabel);
        Assert.Contains("0 de 10 capítulos hechos", v.ChapterLabel);
        Assert.Equal("El notch", v.ChapterTitle);
        Assert.Equal("Paso 1 de 7", v.StepLabel);
        Assert.Equal("En reposo", v.StepTitle);
        Assert.StartsWith("Teclado: Intro lo hace", v.KeyHint);
        Assert.False(v.Done);
        Assert.False(v.Pro);
        Assert.False(v.CanGoBack);
        Assert.True(v.CanSkip);
        Assert.False(v.NextPrimary);
        Assert.Null(v.ProLine);
        Assert.Null(v.Effect);
        Assert.Equal(7, v.StepStates.Count);
        Assert.True(v.Chapters[0].Current);
        Assert.All(v.Chapters, c => Assert.False(c.Done));

        m.HandleKey(TutorialKey.Enter);
        v = View(m);
        Assert.True(v.Done);
        Assert.StartsWith("Intro para el siguiente paso", v.KeyHint);
        Assert.True(v.NextPrimary);
        Assert.False(v.CanSkip);
        Assert.Equal(TutorialStepStatus.Done, v.StepStates[0]);
    }

    [Fact]
    public void Keys_say_arrows_on_steps_with_a_direction()
    {
        var m = new TutorialMachine();
        m.GoTo(TutorialChapter.Pages);
        Assert.StartsWith("Teclado: Intro o ← → lo hacen", View(m).KeyHint);
        m.GoTo(TutorialChapter.Sessions);
        m.GoToStep(1);
        Assert.StartsWith("Teclado: Intro o ↑ ↓ lo hacen", View(m).KeyHint);
    }

    [Fact]
    public void Pro_steps_carry_the_chip()
    {
        var m = new TutorialMachine();
        m.GoTo(TutorialChapter.Permissions);
        Assert.True(View(m).Pro);
        m.GoTo(TutorialChapter.Focus);
        Assert.False(View(m).Pro);
        m.GoToStep(TutorialScript.StepsOf(TutorialChapter.Focus).Count - 1);
        Assert.True(View(m).Pro);
    }

    [Theory]
    [InlineData(ProState.Free, true)]
    [InlineData(ProState.Trial, false)]
    [InlineData(ProState.Pro, false)]
    public void The_chapter_end_offers_pro_only_without_it(ProState pro, bool offered)
    {
        var m = new TutorialMachine();
        m.GoTo(TutorialChapter.Music);
        while (m.Phase == TutorialPhase.Step) m.Skip();
        var v = View(m, pro: pro);
        Assert.Equal(TutorialPhase.ChapterEnd, v.Phase);
        Assert.Equal("«Música y letras», hecho", v.StepTitle);
        Assert.StartsWith("Lo siguiente: Tareas y recordatorios.", v.StepText);
        Assert.True(v.Done);
        Assert.Equal(offered, v.ProLine is not null);
        Assert.Equal(offered, v.ProLink is not null);
        Assert.True(v.NextPrimary);
    }

    [Fact]
    public void A_free_chapter_end_has_no_pro_line()
    {
        var m = new TutorialMachine();
        m.GoTo(TutorialChapter.Pages);
        while (m.Phase == TutorialPhase.Step) m.Skip();
        Assert.Null(View(m).ProLine);
    }

    [Fact]
    public void The_finish_offers_close_and_start_over()
    {
        var m = new TutorialMachine();
        m.GoTo(TutorialChapter.Tray);
        while (m.Phase != TutorialPhase.Finished) m.Skip();
        var v = View(m);
        Assert.Equal("Eso es todo", v.StepTitle);
        Assert.Equal("Cerrar", v.Next);
        Assert.Equal("Empezar de nuevo", v.Skip);
        Assert.True(v.CanSkip);
        Assert.True(v.CanGoBack);
    }

    [Fact]
    public void Effects_say_what_would_happen()
    {
        var m = new TutorialMachine();
        m.GoTo(TutorialChapter.Sessions);
        m.GoToStep(2);
        m.HandleKey(TutorialKey.Enter);
        Assert.Equal("Aquí se traería al frente la terminal de «api».", View(m).Effect);
        m.GoTo(TutorialChapter.Shelf);
        m.GoToStep(1);
        m.HandleKey(TutorialKey.Enter);
        Assert.Equal("Aquí se abriría informe.pdf.", View(m).Effect);
        Assert.Equal("Here, report.pdf would open.", View(m, "en").Effect);
    }

    [Fact]
    public void The_mock_tray_uses_the_real_menu_texts()
    {
        var s = Localization.Get("es");
        var tray = TutorialText.Tray(s, Local);
        Assert.Equal(Enum.GetValues<TutorialMenuItem>().Length, tray.Menu.Count);
        Assert.All(tray.Menu, t => Assert.False(string.IsNullOrWhiteSpace(t)));
        Assert.Equal(s.Settings, tray.Menu[(int)TutorialMenuItem.Settings]);
        Assert.Equal(s.Tut.Menu, tray.Menu[(int)TutorialMenuItem.Tutorial]);
        Assert.Equal(s.MenuReconfigure, tray.Menu[(int)TutorialMenuItem.Reconfigure]);
        Assert.Equal(Enum.GetValues<TutorialSettingsRow>().Length, tray.Settings.Count);
        Assert.Equal(s.UsageStatsLabel, tray.Settings[(int)TutorialSettingsRow.UsageStats]);
        Assert.Equal(s.Tut.SettingsLink, tray.Settings[(int)TutorialSettingsRow.Tutorial]);
        Assert.Equal(4, tray.Monitors.Count);
        Assert.Equal("18:55", tray.Clock);
        Assert.Equal("42 %", tray.Panel[0].Pct);
    }

    // ---------------------------------------------------------------- idiomas

    /// <summary>Iguales en algún idioma por ser iguales de verdad («Tutorial» en español y alemán).</summary>
    private static readonly HashSet<string> MaySame = new()
    {
        nameof(TutorialStrings.Title), nameof(TutorialStrings.Menu), nameof(TutorialStrings.SettingsLink),
    };

    [Theory]
    [MemberData(nameof(Languages))]
    public void Every_language_has_every_text(string code)
    {
        var t = Localization.Get(code).Tut;
        var en = Localization.Get("en").Tut;
        Assert.Equal(Enum.GetValues<TutorialChapter>().Length, t.ChapterNames.Length);
        Assert.Equal(Enum.GetValues<TutorialStep>().Length, t.StepTitles.Length);
        Assert.Equal(Enum.GetValues<TutorialStep>().Length, t.StepTexts.Length);
        Assert.Equal(TutorialDemo.TaskCount, t.SampleTasks.Length);
        foreach (var p in typeof(TutorialStrings).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var value = p.GetValue(t);
            if (value is string[] list)
            {
                var enList = (string[])p.GetValue(en)!;
                Assert.All(list, x => Assert.False(string.IsNullOrWhiteSpace(x), $"{code}.{p.Name}: vacío"));
                if (code != "en")
                {
                    // Alguno puede coincidir de verdad («Sessions», «Pages» en francés), pero no la mayoría.
                    int same = list.Where((x, i) => i < enList.Length && x == enList[i]).Count();
                    Assert.True(same * 10 <= list.Length * 3, $"{code}.{p.Name}: {same} caen al inglés");
                }
                continue;
            }
            var text = (string)value!;
            Assert.False(string.IsNullOrWhiteSpace(text), $"{code}.{p.Name}: vacío");
            if (code != "en" && !MaySame.Contains(p.Name)) Assert.True(text != (string)p.GetValue(en)!, $"{code}.{p.Name} cae al inglés");
        }
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Formats_take_their_arguments(string code)
    {
        var t = Localization.Get(code).Tut;
        foreach (var fmt in new[] { t.ChapterFmt, t.StepFmt, t.ProgressFmt })
        {
            Assert.Contains("{0}", fmt);
            Assert.Contains("{1}", fmt);
        }
        foreach (var fmt in new[] { t.ChapterDoneFmt, t.ChapterNextFmt, t.EffectTerminalFmt, t.EffectOpenFmt })
            Assert.Contains("{0}", fmt);
        // Que formatear no lance en ningún idioma.
        _ = string.Format(t.ChapterFmt, 1, 10) + string.Format(t.StepFmt, 1, 7) + string.Format(t.ProgressFmt, 0, 10);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Every_step_text_fits_in_two_lines_and_titles_in_their_row(string code)
    {
        // Con las fuentes del painter a 1x: la frase en dos líneas del ancho del bloque; el título (con sitio para el chip
        // «Pro») antes del «Hecho»; las teclas en su línea.
        var s = Localization.Get(code);
        var t = s.Tut;
        var pill = UI.Onboarding.OnboardingGeometry.PillRect(TutorialGeometry.Width, TutorialGeometry.Height, 400f);
        var textRect = TutorialGeometry.StepText(pill, 1f);
        var titleRect = TutorialGeometry.StepTitle(pill, 1f);
        float titleRoom = Math.Min(titleRect.Right, TutorialGeometry.DoneBadge(pill, 1f).Left - 8f) - titleRect.Left
            - UI.Notch.NotchProLayout.ChipWidth - 14f;
        using var bmp = new Bitmap(10, 10);
        using var g = Graphics.FromImage(bmp);
        string family = Typography.Body.FontFamily.Name;
        using var body = new Font(family, 13f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var title = new Font(family, 19f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var small = new Font(family, 10.5f, FontStyle.Regular, GraphicsUnit.Pixel);
        var fmt = StringFormat.GenericTypographic;
        for (int i = 0; i < t.StepTexts.Length; i++)
        {
            var size = g.MeasureString(t.StepTexts[i], body, (int)textRect.Width, fmt);
            Assert.True(size.Height <= textRect.Height + 0.5f, $"{code} paso {(TutorialStep)i}: {size.Height} > {textRect.Height}");
            float w = g.MeasureString(t.StepTitles[i], title, int.MaxValue, fmt).Width;
            Assert.True(w <= titleRoom, $"{code} título {(TutorialStep)i}: {w} > {titleRoom}");
        }
        string keys = t.KeyArrows + " · " + t.KeysFooter;
        Assert.True(g.MeasureString(keys, small, int.MaxValue, fmt).Width <= TutorialGeometry.KeyHint(pill, 1f).Width, $"{code}: teclas");
        string next = t.KeyNext + " · " + t.KeysFooter;
        Assert.True(g.MeasureString(next, small, int.MaxValue, fmt).Width <= TutorialGeometry.StepLabel(pill, 1f).Width, $"{code}: siguiente");
    }
}
