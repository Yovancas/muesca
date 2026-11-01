using ClaudeBarWin.Models;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.State;
using ClaudeBarWin.UI.Notch;

namespace ClaudeBarWin.Tests;

/// <summary><see cref="NotchForm"/> sin mostrarla: no crea ventana hasta <c>Show</c>, aguanta eventos del hub
/// y ajustes antes de tener handle, y al liberarse se desengancha del hub. El resto (hover, DPI, topmost,
/// pantalla completa) se verifica en vivo en la Task 9.</summary>
public class NotchFormTests
{
    private static readonly NotchOptions Opts = new(false, false, true, true, 0, "", false);

    private static SessionState Working(string id) => new()
    {
        SessionId = id, Cwd = @"C:\p\web", ProjectName = "web", Phase = SessionPhase.Processing,
        LastTool = "Bash", LastActivityUtc = DateTime.UtcNow,
    };

    [Fact]
    public void Constructing_and_feeding_it_creates_no_window()
    {
        var hub = new ClaudeStateHub();
        using var form = new NotchForm(hub, Opts, Localization.Get("es"));
        hub.PublishSessions(new[] { Working("a") }, DateTime.UtcNow);
        hub.PublishQuota(new RealUsage { FiveHour = new UsageWindow(97, null) });
        form.ApplyOptions(Opts with { SizePercent = 130, OpenOnClick = true, ReduceMotion = true });
        Assert.False(form.IsHandleCreated);
        Assert.True(form.Width > 0 && form.Height > 0);
    }

    // ---- Revisión API (punto 7): oculto por pantalla completa no hay despertares visuales ----

    private static readonly TimeSpan S1 = TimeSpan.FromSeconds(1), S5 = TimeSpan.FromSeconds(5), S30 = TimeSpan.FromSeconds(30);

    [Fact]
    public void NextWake_takes_the_nearest_deadline_while_visible()
    {
        Assert.Equal(S1, NotchForm.NextWake(S5, S30, S1, hiddenForFullscreen: false));
        Assert.Equal(S5, NotchForm.NextWake(S5, null, S30, hiddenForFullscreen: false));
        Assert.Equal(S30, NotchForm.NextWake(null, S30, null, hiddenForFullscreen: false));
        Assert.Equal(S30, NotchForm.NextWake(null, null, S30, hiddenForFullscreen: false));   // minuto del reloj / carrusel
        Assert.Null(NotchForm.NextWake(null, null, null, hiddenForFullscreen: false));
    }

    [Fact]
    public void NextWake_ignores_clock_carousel_mascot_and_idle_music_while_hidden_for_fullscreen()
    {
        Assert.Null(NotchForm.NextWake(null, S1, S1, hiddenForFullscreen: true));    // nada de ticks durante el juego
        Assert.Equal(S5, NotchForm.NextWake(S5, S1, S1, hiddenForFullscreen: true)); // la máquina sí cuenta
    }

    [Fact]
    public void Disposing_unhooks_it_from_the_hub()
    {
        var hub = new ClaudeStateHub();
        var form = new NotchForm(hub, Opts);
        Assert.All(HubEvents, e => Assert.Contains(Handlers(hub, e), d => ReferenceEquals(d.Target, form)));

        form.Dispose();
        // Sin manejadores del form en ningún evento (Alive evitaría la excepción aunque siguiera enganchado).
        Assert.All(HubEvents, e => Assert.DoesNotContain(Handlers(hub, e), d => ReferenceEquals(d.Target, form)));
        hub.PublishSessions(new[] { Working("a") }, DateTime.UtcNow);
        hub.PublishQuota(new RealUsage { FiveHour = new UsageWindow(99, null) });
        Assert.True(form.IsDisposed);
    }

    /// <summary>
    /// Beta del 28-oct: con «Abrir con: Clic», un clic en el notch dejaba el ratón CAPTURADO para siempre
    /// (WinForms captura en WM_LBUTTONDOWN y el notch se tragaba el WM_LBUTTONUP sin soltarla): el panel y el
    /// resto de ventanas de ClaudeBar dejaban de recibir clics y arrastres.
    /// </summary>
    [Fact]
    public void Click_on_the_notch_never_leaves_the_mouse_captured()
    {
        RunSta(() =>
        {
            using var form = new NotchForm(new ClaudeStateHub(), Opts with { OpenOnClick = true });
            var hwnd = form.Handle;
            var lp = (IntPtr)((10 << 16) | 10);

            SendMessage(hwnd, WM_LBUTTONDOWN, (IntPtr)1, lp);
            SendMessage(hwnd, WM_LBUTTONUP, IntPtr.Zero, lp);

            Assert.False(form.Capture);
            Assert.NotEqual(hwnd, GetCapture());
        });
    }

    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetCapture();

    private static void RunSta(Action body)
    {
        Exception? error = null;
        var t = new Thread(() => { try { body(); } catch (Exception ex) { error = ex; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error is not null) throw new Xunit.Sdk.XunitException(error.ToString());
    }

    private static readonly string[] HubEvents =
        { nameof(ClaudeStateHub.SessionsChanged), nameof(ClaudeStateHub.Signal),
          nameof(ClaudeStateHub.QuotaChanged), nameof(ClaudeStateHub.QuotaThresholdCrossed) };

    /// <summary>Manejadores suscritos a un evento de campo del hub (campo privado con el mismo nombre).</summary>
    private static Delegate[] Handlers(ClaudeStateHub hub, string evt)
    {
        var field = typeof(ClaudeStateHub).GetField(evt,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (field!.GetValue(hub) as Delegate)?.GetInvocationList() ?? Array.Empty<Delegate>();
    }
}
