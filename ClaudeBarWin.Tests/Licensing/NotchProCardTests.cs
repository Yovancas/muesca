using System.Drawing.Imaging;
using ClaudeBarWin.Config;
using ClaudeBarWin.Models;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Licensing;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.State;
using ClaudeBarWin.UI.Notch;

namespace ClaudeBarWin.Tests.Licensing;

/// <summary>
/// La tarjeta del Pro dentro del notch: sus textos (<see cref="NotchProText"/>), su geometría (<see cref="NotchProLayout"/>,
/// la misma para pintar y para el clic), lo que pinta, el aviso del fin de la prueba en la máquina y su camino por la
/// ventana y <see cref="NotchHost"/>: todas tienen la oferta y solo la interactiva compra.
/// </summary>
public class NotchProCardTests
{
    private static readonly NotchOptions Opts = new(false, false, true, true, 0, "", false);
    private static readonly Strings Es = Localization.Get("es");
    private static readonly NotchProOffer StoreFree = new(ProState.Free, null, true, "9,99 €");
    private static readonly NotchProOffer GitHubFree = new(ProState.Free, null, false, null);

    // ---- textos

    [Fact]
    public void In_the_store_it_offers_buy_and_restore_with_the_price()
    {
        var v = NotchProText.Card(StoreFree, Es, interactive: true);
        Assert.Equal("Muesca Pro", v.Title);
        Assert.Equal("9,99 € · pago único", v.Subtitle);
        Assert.Equal("Comprar", v.Primary);
        Assert.Equal("Restaurar compra", v.Secondary);
        Assert.False(v.Wide);
        Assert.Null(v.Status);
        Assert.Equal(6, v.Features.Count);
        Assert.Contains("Permitir / Denegar desde el notch", v.Features);
    }

    [Fact]
    public void The_github_edition_only_links_to_the_store()
    {
        var v = NotchProText.Card(GitHubFree, Es, interactive: true);
        Assert.Equal("Pago único", v.Subtitle);
        Assert.Equal("Conseguir Pro en Microsoft Store", v.Primary);
        Assert.Null(v.Secondary);
        Assert.True(v.Wide);
    }

    [Fact]
    public void The_trial_says_how_many_days_are_left()
    {
        Assert.Equal("Prueba · quedan 5 días · 9,99 € · pago único",
            NotchProText.Card(StoreFree with { State = ProState.Trial, TrialDaysLeft = 5 }, Es, true).Subtitle);
        Assert.StartsWith("Prueba · último día", NotchProText.Card(StoreFree with { State = ProState.Trial, TrialDaysLeft = 1 }, Es, true).Subtitle);
        Assert.StartsWith("Trial · 3 days left",
            NotchProText.Card(StoreFree with { State = ProState.Trial, TrialDaysLeft = 3 }, Localization.Get("en"), true).Subtitle);
    }

    [Fact]
    public void With_pro_it_thanks_and_has_no_buttons()
    {
        var v = NotchProText.Card(StoreFree with { State = ProState.Pro }, Es, true);
        Assert.Equal("Activado. ¡Gracias!", v.Subtitle);
        Assert.Null(v.Primary);
        Assert.Null(v.Secondary);
        Assert.False(v.Has(ProCardButton.Primary));
    }

    [Fact]
    public void Only_the_interactive_window_has_the_buttons()
    {
        var v = NotchProText.Card(StoreFree, Es, interactive: false);
        Assert.Null(v.Primary);
        Assert.Null(v.Secondary);
        Assert.Equal("Haz clic para comprar desde esta pantalla", v.Status);
        Assert.False(v.StatusIsError);
    }

    [Theory]
    [InlineData(ProMessage.Busy, "Abriendo Microsoft Store…", false)]
    [InlineData(ProMessage.Failed, "La compra no se completó.", true)]
    [InlineData(ProMessage.Offline, "La Store no responde.", true)]
    [InlineData(ProMessage.Unavailable, "Pro aún no está en la Store.", true)]
    [InlineData(ProMessage.NotOwned, "Esta cuenta no tiene Pro.", true)]
    public void The_status_line_says_how_the_last_action_went(ProMessage message, string text, bool error)
    {
        var v = NotchProText.Card(StoreFree with { Message = message }, Es, true);
        Assert.Equal(text, v.Status);
        Assert.Equal(error, v.StatusIsError);
        Assert.Equal(message == ProMessage.Busy, v.Busy);
    }

    [Theory]
    [InlineData(PurchaseResult.Purchased, ProMessage.None)]
    [InlineData(PurchaseResult.AlreadyOwned, ProMessage.None)]
    [InlineData(PurchaseResult.Cancelled, ProMessage.None)]
    [InlineData(PurchaseResult.NotOwned, ProMessage.NotOwned)]
    [InlineData(PurchaseResult.NotAvailable, ProMessage.Unavailable)]
    [InlineData(PurchaseResult.NetworkError, ProMessage.Offline)]
    [InlineData(PurchaseResult.Failed, ProMessage.Failed)]
    public void Purchase_results_become_card_messages(PurchaseResult result, ProMessage expected) =>
        Assert.Equal(expected, NotchProOffer.MessageFor(result));

    [Fact]
    public void The_tray_entry_says_the_state()
    {
        Assert.Equal("Muesca Pro… (Gratis)", NotchProText.Menu(new ProSummary(ProState.Free, null, false), Es));
        Assert.Equal("Muesca Pro… (Prueba: 5 días)", NotchProText.Menu(new ProSummary(ProState.Trial, 5, true), Es));
        Assert.Equal("Muesca Pro… (Prueba: 1 día)", NotchProText.Menu(new ProSummary(ProState.Trial, 1, true), Es));
        Assert.Equal("Muesca Pro… (Pro)", NotchProText.Menu(new ProSummary(ProState.Pro, null, true), Es));
        Assert.Equal("Muesca Pro… (Trial: 7 days)", NotchProText.Menu(new ProSummary(ProState.Trial, null, true), Localization.Get("en")));
    }

    /// <summary>Sin notch a la vista, «Muesca Pro…» no abre la compra: un globo con el estado y dónde se compra.</summary>
    [Fact]
    public void Without_the_notch_the_tray_entry_shows_the_state_instead_of_buying()
    {
        Assert.Equal("Prueba · quedan 5 días\nPro: pago único, desde la tarjeta Pro del notch.",
            NotchProText.Balloon(new ProSummary(ProState.Trial, 5, true), Es));
        Assert.Equal("Gratis\nPro: pago único, desde la tarjeta Pro del notch.",
            NotchProText.Balloon(new ProSummary(ProState.Free, null, true), Es));
        Assert.Equal(Es.ProThanks, NotchProText.Balloon(new ProSummary(ProState.Pro, null, true), Es));
    }

    // ---- geometría

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    [InlineData(2f)]
    public void The_card_and_its_buttons_fit_where_they_are_painted(float s)
    {
        var surface = NotchPlacement.Surface(s);
        var size = NotchProLayout.CardSize(s);
        Assert.True(size.Height <= NotchLayout.ExpandedMaxHeight * s);
        var pill = NotchGeometry.PillRect(size.Width, size.Height, surface);
        var inner = NotchProLayout.CardInner(pill, s);
        var store = NotchProText.Card(StoreFree, Es, true);
        var buy = NotchProLayout.CardPrimary(inner, s, wide: false);
        var restore = NotchProLayout.CardSecondary(inner, s);
        var wide = NotchProLayout.CardPrimary(inner, s, wide: true);
        foreach (var b in new[] { buy, restore, wide })
        {
            Assert.False(b.IsEmpty);
            Assert.True(pill.Contains(b), $"{b} fuera de {pill}");
        }
        Assert.True(restore.Right < buy.Left, "Restaurar a la izquierda de Comprar, sin pisarse");
        Assert.True(NotchProLayout.CardStatus(inner, s, store).Right <= restore.Left);
        // Lo que incluye cabe entre el título y los botones.
        var lastFeature = NotchProLayout.CardFeature(inner, s, 2 * NotchProLayout.CardFeatureRows - 1);
        Assert.True(lastFeature.Bottom <= NotchProLayout.CardButtonsRow(inner, s).Top + 0.01f);
        Assert.True(NotchProLayout.CardFeature(inner, s, 0).Top >= NotchProLayout.CardTitle(inner, s).Bottom - 0.01f);

        Assert.Equal(ProCardButton.Primary, NotchProLayout.HitCard(Center(buy), pill, s, store));
        Assert.Equal(ProCardButton.Secondary, NotchProLayout.HitCard(Center(restore), pill, s, store));
        Assert.Equal(ProCardButton.None, NotchProLayout.HitCard(new Point(buy.Left - 2, (int)inner.Top + 3), pill, s, store));
        // Sin el botón, su sitio no responde (Pro activo, u otra ventana).
        var pro = NotchProText.Card(StoreFree with { State = ProState.Pro }, Es, true);
        Assert.Equal(ProCardButton.None, NotchProLayout.HitCard(Center(buy), pill, s, pro));
        var github = NotchProText.Card(GitHubFree, Es, true);
        Assert.Equal(ProCardButton.Primary, NotchProLayout.HitCard(new Point(wide.Left + 4, wide.Top + 4), pill, s, github));
    }

    private static Point Center(Rectangle r) => new(r.Left + r.Width / 2, r.Top + r.Height / 2);

    // ---- painter

    private static Bitmap Paint(NotchFrame f, Size surface)
    {
        var bmp = new Bitmap(surface.Width, surface.Height, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        using var painter = new NotchContentPainter(Es);
        painter.Paint(g, surface, f);
        return bmp;
    }

    private static bool Near(Color a, Color b, int tol = 28) =>
        Math.Abs(a.R - b.R) <= tol && Math.Abs(a.G - b.G) <= tol && Math.Abs(a.B - b.B) <= tol;

    [Fact]
    public void The_card_paints_buy_with_the_accent_and_replaces_the_page()
    {
        var surface = NotchPlacement.Surface(1f);
        var size = NotchProLayout.CardSize(1f);
        var pill = NotchGeometry.PillRect(size.Width, size.Height, surface);
        var buy = NotchProLayout.CardPrimary(NotchProLayout.CardInner(pill, 1f), 1f, false);
        var frame = new NotchFrame(size.Width, size.Height, 1f, NotchMode.Expanded, null, Array.Empty<SessionState>(), null, 0f, 1f,
            ProCard: NotchProText.Card(StoreFree, Es, true));
        using (var bmp = Paint(frame, surface))
            Assert.True(Near(bmp.GetPixel(buy.Left + 3, buy.Top + buy.Height / 2), Theme.Dark.Accent), "Comprar con el acento");
        using (var pro = Paint(frame with { ProCard = NotchProText.Card(StoreFree with { State = ProState.Pro }, Es, true) }, surface))
            Assert.False(Near(pro.GetPixel(buy.Left + 3, buy.Top + buy.Height / 2), Theme.Dark.Accent), "sin botón con el Pro");
        using (var busy = Paint(frame with { ProCard = NotchProText.Card(StoreFree with { Message = ProMessage.Busy }, Es, true) }, surface))
            Assert.False(Near(busy.GetPixel(buy.Left + 3, buy.Top + buy.Height / 2), Theme.Dark.Accent), "atenuado mientras compra");
    }

    // ---- máquina: aviso del fin de la prueba y abrir el expandido

    private static readonly DateTime T0 = new(2026, 10, 30, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void The_trial_notice_shows_once_quietly_and_goes_away_by_itself()
    {
        var sm = new NotchStateMachine(new NotchSettings(false, false, true));
        sm.OnNotice(NotchForm.ProTrialNoticeId, T0);
        Assert.Equal(NotchMode.Completed, sm.Mode);
        Assert.Equal(NotchAlertKind.Notice, sm.ActiveAlert!.Kind);
        Assert.False(NotchSound.ShouldPlay(sm.ActiveAlert, soundEnabled: true, fullscreen: false));   // nunca insistente
        Assert.Equal(NotchTiming.Notice, sm.NextWakeIn(T0));
        sm.Tick(T0 + NotchTiming.Notice);
        Assert.Null(sm.ActiveAlert);
        Assert.Equal(NotchMode.Idle, sm.Mode);
    }

    [Fact]
    public void The_notice_never_covers_claude()
    {
        var sm = new NotchStateMachine(new NotchSettings(false, false, true));
        sm.OnNotice(NotchForm.ProTrialNoticeId, T0);
        sm.OnSignal(new TaskCompleted("s1", "claudebar", T0), T0);
        Assert.Equal(NotchAlertKind.Completed, sm.ActiveAlert!.Kind);
        sm.ClearNotice(NotchForm.ProTrialNoticeId, T0);
        sm.Tick(T0 + NotchTiming.Completed);
        Assert.Null(sm.ActiveAlert);
    }

    /// <summary>El host avisa cuando el aviso del fin de la prueba SE VE (la bandeja lo apunta entonces), una vez por
    /// ventana; meterlo otra vez en la cola no vuelve a avisar.</summary>
    [Fact]
    public void The_host_reports_the_trial_notice_when_it_is_shown_not_when_queued()
    {
        RunSta(() =>
        {
            var hub = new ClaudeStateHub();
            using var host = new NotchHost(hub, Opts, NotchMonitorMode.Fixed, Es, show: false);
            int shown = 0;
            host.ProNoticeShown += () => shown++;
            Assert.Equal(0, shown);
            Assert.True(host.ShowProNotice());
            Assert.Equal(1, shown);
            host.ShowProNotice();
            Assert.Equal(1, shown);
        });
    }

    [Fact]
    public void Opening_from_the_tray_lingers_and_the_pointer_keeps_it_open()
    {
        var sm = new NotchStateMachine(new NotchSettings(false, false, true));
        sm.OpenExpanded(T0, NotchTiming.ProCardLinger);
        Assert.Equal(NotchMode.Expanded, sm.Mode);
        sm.Tick(T0 + TimeSpan.FromSeconds(5));
        Assert.Equal(NotchMode.Expanded, sm.Mode);
        sm.Tick(T0 + NotchTiming.ProCardLinger);
        Assert.Equal(NotchMode.Idle, sm.Mode);

        sm.OpenExpanded(T0, NotchTiming.ProCardLinger);
        sm.OnPointerEnter(T0 + TimeSpan.FromSeconds(1));
        sm.Tick(T0 + TimeSpan.FromSeconds(30));
        Assert.Equal(NotchMode.Expanded, sm.Mode);
        sm.OnPointerLeave(T0 + TimeSpan.FromSeconds(30));
        sm.Tick(T0 + TimeSpan.FromSeconds(30) + NotchTiming.CollapseGrace);
        Assert.NotEqual(NotchMode.Expanded, sm.Mode);
    }

    [Fact]
    public void The_end_notice_is_due_only_once_after_a_trial_without_purchase()
    {
        Assert.True(ProTrial.EndNoticeDue(ProState.Free, trialEnded: true, alreadyShown: false));
        Assert.False(ProTrial.EndNoticeDue(ProState.Free, trialEnded: true, alreadyShown: true));
        Assert.False(ProTrial.EndNoticeDue(ProState.Free, trialEnded: false, alreadyShown: false));   // GitHub: nunca hubo prueba
        Assert.False(ProTrial.EndNoticeDue(ProState.Pro, trialEnded: true, alreadyShown: false));
        Assert.False(ProTrial.EndNoticeDue(ProState.Trial, trialEnded: false, alreadyShown: false));
    }

    // ---- ventana y host

    [Fact]
    public void The_window_keeps_the_offer_and_only_the_interactive_one_buys()
    {
        var hub = new ClaudeStateHub();
        using var form = new NotchForm(hub, Opts, Es);
        var asked = new List<NotchProAction>();
        form.ProActionRequested += (_, a) => asked.Add(a);

        Assert.False(form.OpenProCard());   // sin oferta no hay tarjeta
        form.SetProOffer(StoreFree);
        Assert.False(form.RequestProAction(ProCardButton.Primary));   // cerrada
        Assert.True(form.OpenProCard());
        Assert.True(form.ProCardOpen);
        Assert.True(form.RequestProAction(ProCardButton.Primary));
        Assert.True(form.RequestProAction(ProCardButton.Secondary));
        Assert.Equal(new[] { NotchProAction.Purchase, NotchProAction.Restore }, asked);

        form.SetProOffer(StoreFree with { Message = ProMessage.Busy });
        Assert.False(form.RequestProAction(ProCardButton.Primary));   // la ventana de compra ya está abierta

        form.SetProOffer(GitHubFree);
        Assert.True(form.RequestProAction(ProCardButton.Primary));
        Assert.Equal(NotchProAction.OpenStore, asked[^1]);
        Assert.False(form.RequestProAction(ProCardButton.Secondary));   // la edición GitHub no tiene «Restaurar»

        form.Interactive = false;
        Assert.Null(form.ProCard!.Primary);
        Assert.False(form.RequestProAction(ProCardButton.Primary));
        form.Interactive = true;

        form.Strings = Localization.Get("en");
        Assert.Equal("Get Pro in Microsoft Store", form.ProCard!.Primary);
        form.CloseProCard();
        Assert.False(form.ProCardOpen);
        form.OpenProCard();
        form.SetProOffer(null);
        Assert.False(form.ProCardOpen);
        Assert.Null(form.ProCard);
    }

    [Fact]
    public void The_host_hands_the_offer_to_every_window_and_buys_only_from_the_interactive_one()
    {
        RunSta(() =>
        {
            var hub = new ClaudeStateHub();
            using var host = new NotchHost(hub, Opts, NotchMonitorMode.AllMonitors, Es, show: false);
            var asked = new List<NotchProAction>();
            host.ProActionRequested += asked.Add;
            Assert.False(host.ShowProCard());   // sin oferta
            host.SetProOffer(StoreFree);
            Assert.All(host.Windows, w => Assert.Equal(StoreFree, w.ProOffer));
            Assert.Single(host.Windows, w => w.ProCard!.Primary is not null);

            Assert.True(host.ShowProCard());
            var interactive = host.Windows.Single(w => w.Interactive);
            Assert.True(interactive.ProCardOpen);
            Assert.All(host.Windows.Where(w => !w.Interactive), w => Assert.False(w.ProCardOpen));

            foreach (var w in host.Windows.Where(w => !w.Interactive))
            {
                w.OpenProCard();
                w.RequestProAction(ProCardButton.Primary);
            }
            Assert.Empty(asked);
            interactive.RequestProAction(ProCardButton.Primary);
            Assert.Equal(new[] { NotchProAction.Purchase }, asked);

            Assert.True(host.ShowProNotice());

            // Un modo nuevo rehace ventanas: la oferta sigue en todas y la ventana cerrada ya no compra.
            var before = host.Windows.ToList();
            host.Apply(Opts, NotchMonitorMode.Fixed, Es);
            Assert.Equal(StoreFree, Assert.Single(host.Windows).ProOffer);
            foreach (var w in before.Where(w => !host.Windows.Contains(w))) w.RequestProAction(ProCardButton.Primary);
            Assert.Single(asked);
        });
    }

    /// <summary>
    /// Desbloqueo en caliente, con el mismo cálculo que la bandeja (<see cref="TrayAppContext.PlanNotch"/>, lo único que decide
    /// su <c>ApplyNotch</c>, al que llama <c>OnProChanged</c>): al comprar, las opciones sin candados llegan al host sin rehacer
    /// las ventanas y el pipe de permisos pasa a abrirse.
    /// </summary>
    [Fact]
    public void Buying_unlocks_the_running_notch_without_restarting()
    {
        RunSta(() =>
        {
            var license = new FakeProLicense(ProState.Free, store: true);
            var cfg = new AppConfig
            {
                NotchEnabled = true, NotchMusic = true, NotchTasks = true, NotchPermissions = true, LiveSessionsEnabled = true,
                NotchMonitorMode = NotchMonitorMode.AllMonitors, NotchMonitor = @"\\.\DISPLAY2",
            };
            var plan = TrayAppContext.PlanNotch(cfg, false, false, license.State);
            Assert.False(plan.PermissionPipe);   // sin el Pro, el pipe ni se abre
            Assert.Equal(NotchMonitorMode.Fixed, plan.Mode);
            var hub = new ClaudeStateHub();
            using var host = new NotchHost(hub, plan.Options, plan.Mode, Es, show: false);
            host.SetProOffer(NotchProOffer.From(license));
            license.Changed += () =>
            {
                plan = TrayAppContext.PlanNotch(cfg, false, false, license.State);
                // Los monitores aparte (los tests no tienen varios): lo que se pide de ellos se comprueba en el plan.
                host.Apply(plan.Options with { MonitorDeviceName = "" }, NotchMonitorMode.Fixed, Es);
                host.SetProOffer(NotchProOffer.From(license));
            };
            var window = Assert.Single(host.Windows);
            Assert.Equal(NotchProLocks.Music | NotchProLocks.Tasks | NotchProLocks.Shelf | NotchProLocks.UsageCharts
                | NotchProLocks.FocusPro, window.Options.Locked);
            Assert.True(host.ShowProCard());
            Assert.Equal("Comprar", window.ProCard!.Primary);

            license.PurchaseAsync().GetAwaiter().GetResult();   // el Changed de la licencia, en el mismo hilo

            Assert.Same(window, Assert.Single(host.Windows));   // la misma ventana, sin reiniciar
            Assert.Equal(NotchProLocks.None, window.Options.Locked);
            Assert.True(window.Options.Music);
            Assert.True(window.Options.Tasks);
            Assert.Equal("Activado. ¡Gracias!", window.ProCard!.Subtitle);
            Assert.Null(window.ProCard.Primary);
            Assert.Equal(1, license.Purchases);
            Assert.True(plan.PermissionPipe);   // el pipe de permisos se abre al comprar
            Assert.Equal(NotchMonitorMode.AllMonitors, plan.Mode);
            Assert.Equal(@"\\.\DISPLAY2", plan.Options.MonitorDeviceName);
        });
    }

    private static void RunSta(Action body)
    {
        Exception? error = null;
        var t = new Thread(() => { try { body(); } catch (Exception ex) { error = ex; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error is not null) throw new Xunit.Sdk.XunitException(error.ToString());
    }
}
