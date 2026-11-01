using System.Drawing.Imaging;
using ClaudeBarWin.Config;
using ClaudeBarWin.Models;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Notch.Features;
using ClaudeBarWin.Services.State;
using ClaudeBarWin.Services.Updates;
using ClaudeBarWin.UI.Notch;

namespace ClaudeBarWin.Tests;

/// <summary>
/// El aviso de actualización en el notch: sus textos (<see cref="NotchUpdateText"/>), su geometría (<see cref="NotchUpdateLayout"/>,
/// la misma para pintar y para el clic), lo que pinta el painter (punto en el reposo, línea con anillo o botón en el
/// expandido) y su camino por <see cref="NotchHost"/>: todas las ventanas lo enseñan y solo la interactiva lleva el botón.
/// </summary>
public class NotchUpdateTests
{
    private static readonly NotchOptions Opts = new(false, false, true, true, 0, "", false);
    private static readonly UpdateOfferView Downloading = new("0.6", 0.42, false);
    private static readonly UpdateOfferView Ready = new("0.6", 1, true);

    // ---- de IAppUpdates a la oferta

    [Fact]
    public void The_offer_comes_from_the_updater_only_while_downloading_or_ready()
    {
        Assert.Equal(new UpdateOfferView("0.6", 0.42, false), UpdateOfferView.From(UpdateStage.Downloading, "0.6", 0.42));
        Assert.Equal(new UpdateOfferView("0.6", 1, true), UpdateOfferView.From(UpdateStage.ReadyToApply, "0.6", 0.97));
        Assert.Equal(0, UpdateOfferView.From(UpdateStage.Downloading, "0.6", double.NaN)!.Progress);
        Assert.Equal(1, UpdateOfferView.From(UpdateStage.Downloading, "0.6", 3)!.Progress);
        // Al día, comprobando o tras un fallo, el notch no enseña nada (el reintento sigue en el menú de la bandeja).
        Assert.Null(UpdateOfferView.From(UpdateStage.Idle, null, 0));
        Assert.Null(UpdateOfferView.From(UpdateStage.Checking, "0.6", 0));
        Assert.Null(UpdateOfferView.From(UpdateStage.Failed, "0.6", 0.5));
        Assert.Null(UpdateOfferView.From(UpdateStage.ReadyToApply, null, 1));
        Assert.Null(UpdateOfferView.From(UpdateStage.Downloading, "", 0.1));
    }

    // ---- textos

    [Fact]
    public void While_downloading_it_says_how_far_it_goes_and_has_no_button()
    {
        var es = NotchUpdateText.Build(Downloading, Localization.Get("es"), interactive: true);
        Assert.Equal("Nueva versión 0.6", es.Label);
        Assert.StartsWith("Descargando…", es.Status);
        Assert.Contains("42", es.Status);
        Assert.False(es.Button);
        Assert.Equal(0.42f, es.Progress, 3);

        var en = NotchUpdateText.Build(Downloading with { Progress = double.NaN }, Localization.Get("en"), interactive: true);
        Assert.Equal("New version 0.6", en.Label);
        Assert.Equal(0f, en.Progress);
    }

    [Fact]
    public void When_ready_only_the_interactive_window_has_the_button()
    {
        var here = NotchUpdateText.Build(Ready, Localization.Get("es"), interactive: true);
        Assert.True(here.Button);
        Assert.Equal("Actualizar", here.Action);
        var elsewhere = NotchUpdateText.Build(Ready, Localization.Get("es"), interactive: false);
        Assert.False(elsewhere.Button);
        Assert.Null(elsewhere.Action);
        Assert.Equal("Lista para instalar", elsewhere.Status);
    }

    // ---- geometría

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    [InlineData(2f)]
    public void The_row_button_and_dot_fit_where_they_are_painted(float s)
    {
        var surface = NotchPlacement.Surface(s);
        var expanded = NotchLayout.PageSize(NotchPage.Claude, new PageRows(2, 0, 0), withAlert: true, withDots: true, s);
        var pill = NotchGeometry.PillRect(expanded.Width, expanded.Height, surface);
        var row = NotchUpdateLayout.Row(pill, s);
        var button = NotchUpdateLayout.Button(row, s);
        Assert.False(button.IsEmpty);
        Assert.True(pill.Contains(row));
        Assert.True(row.Contains(button), $"{button} fuera de {row}");
        Assert.True(row.Contains(NotchUpdateLayout.Ring(row, s)));
        Assert.True(NotchUpdateLayout.HitButton(new Point(button.Left + button.Width / 2, button.Top + button.Height / 2), pill, s));
        Assert.False(NotchUpdateLayout.HitButton(new Point(button.Left - 3, button.Top + 2), pill, s));
        // La línea es la del aviso integrado: el contenido de la página empieza debajo.
        Assert.True(row.Bottom <= NotchContentPainter.PageInner(pill, s, withAlert: true, withDots: true).Top + 0.01f);

        var idle = NotchGeometry.PillRect(NotchLayout.TargetSize(NotchMode.Idle, s, 0f).Width,
            NotchLayout.TargetSize(NotchMode.Idle, s, 0f).Height, surface);
        Assert.True(idle.Contains(NotchUpdateLayout.Dot(idle, s)));
    }

    // ---- painter

    private static Bitmap Paint(NotchFrame f, Size surface)
    {
        var bmp = new Bitmap(surface.Width, surface.Height, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        using var painter = new NotchContentPainter(Localization.Get("es"));
        painter.Paint(g, surface, f);
        return bmp;
    }

    private static bool Near(Color a, Color b, int tol = 28) =>
        Math.Abs(a.R - b.R) <= tol && Math.Abs(a.G - b.G) <= tol && Math.Abs(a.B - b.B) <= tol;

    [Fact]
    public void Idle_shows_a_discreet_accent_dot_only_with_an_offer()
    {
        var surface = NotchPlacement.Surface(1f);
        var size = NotchLayout.TargetSize(NotchMode.Idle, 1f, 0f);
        var frame = new NotchFrame(size.Width, size.Height, 1f, NotchMode.Idle, null, Array.Empty<SessionState>(), null, 0f, 1f);
        var pill = NotchGeometry.PillRect(size.Width, size.Height, surface);
        var dot = NotchUpdateLayout.Dot(pill, 1f);
        var c = new Point((int)(dot.Left + dot.Width / 2f), (int)(dot.Top + dot.Height / 2f));

        using (var without = Paint(frame, surface)) Assert.True(Near(without.GetPixel(c.X, c.Y), Color.Black, 10));
        var view = NotchUpdateText.Build(Downloading, Localization.Get("es"), true);
        using (var with = Paint(frame with { Update = view }, surface)) Assert.True(Near(with.GetPixel(c.X, c.Y), Theme.Dark.Accent));
    }

    [Fact]
    public void The_expanded_row_paints_the_button_when_ready_and_the_ring_while_downloading()
    {
        var surface = NotchPlacement.Surface(1f);
        var size = NotchLayout.PageSize(NotchPage.Claude, new PageRows(1, 0, 0), withAlert: true, withDots: false, 1f);
        var frame = new NotchFrame(size.Width, size.Height, 1f, NotchMode.Expanded, null, Array.Empty<SessionState>(), null, 0f, 1f,
            Update: NotchUpdateText.Build(Ready, Localization.Get("es"), true));
        var pill = NotchGeometry.PillRect(size.Width, size.Height, surface);
        var row = NotchUpdateLayout.Row(pill, 1f);
        var b = NotchUpdateLayout.Button(row, 1f);
        using (var ready = Paint(frame, surface))
            Assert.True(Near(ready.GetPixel(b.Left + 3, b.Top + b.Height / 2), Theme.Dark.Accent), "botón con el acento");

        var ring = NotchUpdateLayout.Ring(row, 1f);
        using (var loading = Paint(frame with { Update = NotchUpdateText.Build(new("0.6", 1, false), Localization.Get("es"), true) }, surface))
        {
            // El arco empieza arriba (las 12): ese punto del anillo, con el acento.
            Assert.True(Near(loading.GetPixel((int)(ring.Left + ring.Width / 2f), (int)(ring.Top + 1.5f)), Theme.Dark.Accent, 60));
            Assert.False(Near(loading.GetPixel(b.Left + 3, b.Top + b.Height / 2), Theme.Dark.Accent), "sin botón mientras descarga");
        }
    }

    // ---- ventana y host

    [Fact]
    public void The_window_keeps_the_offer_and_only_the_interactive_one_asks_to_update()
    {
        var hub = new ClaudeStateHub();
        using var form = new NotchForm(hub, Opts, Localization.Get("es"));
        int clicks = 0;
        form.UpdateClicked += _ => clicks++;

        form.SetUpdateOffer(Downloading);
        Assert.Equal(Downloading, form.UpdateOffer);
        Assert.False(form.RequestUpdate());   // descargando: sin botón
        form.SetUpdateOffer(Ready);
        Assert.True(form.UpdateView!.Button);
        Assert.True(form.RequestUpdate());
        Assert.Equal(1, clicks);

        form.Interactive = false;
        Assert.False(form.UpdateView!.Button);
        Assert.False(form.RequestUpdate());
        Assert.Equal(1, clicks);

        form.Strings = Localization.Get("en");
        Assert.Equal("New version 0.6", form.UpdateView!.Label);
        form.SetUpdateOffer(null);
        Assert.Null(form.UpdateView);
    }

    [Fact]
    public void The_host_hands_the_offer_to_every_window_and_relays_the_interactive_click()
    {
        RunSta(() =>
        {
            var hub = new ClaudeStateHub();
            using var host = new NotchHost(hub, Opts, NotchMonitorMode.AllMonitors, Localization.Get("es"), show: false);
            int requests = 0;
            host.UpdateRequested += () => requests++;
            host.SetUpdateOffer(Ready);
            Assert.All(host.Windows, w => Assert.Equal(Ready, w.UpdateOffer));
            Assert.Single(host.Windows, w => w.UpdateView!.Button);

            foreach (var w in host.Windows.Where(w => !w.Interactive)) w.RequestUpdate();
            Assert.Equal(0, requests);
            host.Windows.Single(w => w.Interactive).RequestUpdate();
            Assert.Equal(1, requests);

            // Un modo nuevo rehace ventanas: la oferta sigue en todas.
            host.Apply(Opts, NotchMonitorMode.Fixed, Localization.Get("es"));
            Assert.Equal(Ready, Assert.Single(host.Windows).UpdateOffer);
            host.SetUpdateOffer(null);
            Assert.Null(Assert.Single(host.Windows).UpdateOffer);
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
