using ClaudeBarWin.Config;
using ClaudeBarWin.Models;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Licensing;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Notch.Platform;
using ClaudeBarWin.Services.Onboarding;
using ClaudeBarWin.UI.Notch;

namespace ClaudeBarWin.UI.Onboarding;

/// <summary>
/// Ventana del asistente de bienvenida: el notch, en grande. Capa Win32 fina (la superficie, el bucle y la colocación son de
/// <see cref="LayeredNotchForm"/>, como los del tutorial): decide <see cref="OnboardingScene"/> y pinta
/// <see cref="OnboardingPainter"/> en una superficie layered pegada arriba del monitor (<see cref="OnboardingPlacement"/>), con
/// la sombra en su propia ventana que no recibe clics (<see cref="NotchShadowWindow"/>).
///
/// <list type="bullet">
/// <item><b>Foco:</b> a diferencia del notch, esta ventana SÍ se activa (el asistente se maneja con el teclado: Tab, Enter,
/// Espacio, flechas y Esc). Sin botón en la barra de tareas y siempre encima.</item>
/// <item><b>0 % CPU quieta:</b> el bucle (<c>Application.Idle</c> + <c>DwmFlush</c>, o un timer de 16 ms si DWM no compone)
/// solo vive mientras algo se mueve; la vista previa despierta con un timer de un disparo en cada cambio de escena.</item>
/// <item><b>DPI/monitor:</b> <c>WM_DPICHANGED</c> y <c>WM_DISPLAYCHANGE</c> ⇒ 400 ms después se recalculan escala y superficie.</item>
/// <item><b>Relevo:</b> al terminar se encoge hasta el notch de reposo que va a aparecer en este monitor
/// (<see cref="Landing"/>, con su escala y su centro exactos) y avisa con <see cref="HandOff"/>: entonces la bandeja crea el
/// notch de verdad, que queda encima, y esta ventana se cierra poco después.</item>
/// <item>Alt+F4 no la cierra de golpe: es como Esc (pide confirmar dentro del propio notch).</item>
/// </list>
/// </summary>
public sealed class OnboardingForm : LayeredNotchForm
{
    private readonly OnboardingScene _scene;
    private readonly OnboardingPainter _painter;
    private readonly OnboardingServices _services;
    private readonly Func<AppConfig> _config;
    // Estado del Pro al abrir: sin varios monitores, el notch saldrá en el principal (NotchProGating.Placement).
    private readonly ProState _pro;

    /// <param name="config">La config de ahora (ya aplicado lo elegido): de ella sale dónde aparece el notch.</param>
    /// <param name="monitorDevice">Monitor donde sale (el del notch); "" o desconectado = el principal.</param>
    public OnboardingForm(OnboardingContext context, Strings strings, OnboardingServices services, Func<AppConfig> config,
        bool reduceMotion, string? monitorDevice = null) : base(monitorDevice)
    {
        ArgumentNullException.ThrowIfNull(context);
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _pro = context.Pro?.State ?? ProState.Pro;

        _painter = new OnboardingPainter(strings ?? throw new ArgumentNullException(nameof(strings)));
        _scene = new OnboardingScene(new OnboardingWizard(context), strings, _painter, services, reduceMotion, MonitorLabels)
        {
            Landing = ComputeLanding,
        };
        InitPlacement();
    }

    /// <summary>El notch de reposo ya está entero (o el asistente se va sin notch): es el momento de crear el notch de verdad.</summary>
    public event Action<OnboardingForm>? HandOff;

    /// <summary>Cómo acabó (lo aplicado o nada); <c>null</c> mientras sigue abierto.</summary>
    public OnboardingResult? Result => _scene.Result;

    /// <summary>Lo que el asistente está enseñando (para traerlo al frente si se vuelve a pedir).</summary>
    public OnboardingPage Page => _scene.Wizard.Page;

    /// <summary>¿Aterrizó en el notch de reposo de este monitor? (si no, se metió arriba: no hay notch aquí).</summary>
    public bool Landed => _scene.Landed;

    // ---------------------------------------------------------------- escena

    protected override SizeF LogicalSize => new(OnboardingGeometry.Width, OnboardingGeometry.Height);
    protected override bool SceneAnimating => _scene.IsAnimating;
    protected override TimeSpan? SceneNextWake(TimeSpan now) => _scene.NextWakeIn(now);
    protected override bool SceneGone => _scene.IsGone;
    protected override float SceneAlpha => _scene.WindowAlpha;
    protected override void ScenePlaced(float scale, Size surface) => _scene.Place(scale, surface);
    protected override void SceneStart(TimeSpan now) => _scene.Start(now);

    protected override void SceneStep(TimeSpan now)
    {
        _scene.Step(now);
        if (_scene.TakeHandoff())
        {
            try { HandOff?.Invoke(this); }
            catch (Exception ex) { AppLog.Error("asistente del notch: el relevo al notch falló", ex); }
        }
    }

    protected override RectangleF PaintScene(Graphics g, Size surface) => _painter.Paint(g, surface, _scene.Frame());

    protected override RectangleF PaintSceneShadow(Graphics g, Size surface) => _painter.PaintShadow(g, surface, _scene.Frame());

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Alt+F4 o la X del sistema: como Esc. Al apagar o salir de la app, se cierra sin más.
        if (!ClosingOnPurpose && e.CloseReason == CloseReason.UserClosing && !_scene.IsGone)
        {
            e.Cancel = true;
            _scene.RequestClose(Now);
            Frame();
            Pump();
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _painter.Dispose();
        base.Dispose(disposing);
    }

    // ---------------------------------------------------------------- entrada

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        // Un frame siempre que cambie el control bajo el ratón: de una fila a la de al lado el realce ya está entero (o, con
        // reducir movimiento, salta) y no queda nada animándose que repinte.
        if (_scene.PointerMove(e.Location)) { Frame(); Pump(); }
        Cursor = _scene.OverControl ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_scene.PointerLeave()) { Frame(); Pump(); }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        _scene.PointerDown(e.Location);
        Frame();
        Pump();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        _scene.PointerUp(e.Location, Now);
        AfterInput();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        bool shift = (keyData & Keys.Shift) != 0;
        if ((keyData & (Keys.Control | Keys.Alt)) == 0 && _scene.Key(key, shift, Now, IsAutoRepeat(msg.LParam)))
        {
            AfterInput();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ---------------------------------------------------------------- aterrizaje

    /// <summary>
    /// Dónde aparecerá el notch de verdad en ESTE monitor con la config ya aplicada: su modo (Trabajando si Claude está
    /// trabajando; si no, la raya o el reposo), su tamaño a su escala (DPI × tamaño del notch) y su centro exacto en esta
    /// superficie. Sin notch, o si va a otro monitor (fijo en otro), null: el asistente se mete arriba.
    /// </summary>
    private OnboardingLanding? ComputeLanding()
    {
        var cfg = _config();
        if (!cfg.NotchEnabled) return null;
        var here = ScreenFor(MonitorDevice);
        // Fijo y siguiendo a la ventana activa empiezan en su monitor de casa (las ventanas de ClaudeBar no cuentan como la
        // activa: este asistente tampoco); con uno por monitor, también hay uno aquí. Sin el Pro, uno solo en el principal.
        var (monitorMode, monitor) = NotchProGating.Placement(cfg.NotchMonitorMode, cfg.NotchMonitor, _pro);
        var target = monitorMode == NotchMonitorMode.AllMonitors ? here : ScreenFor(monitor);
        if (!string.Equals(target.DeviceName, here.DeviceName, StringComparison.OrdinalIgnoreCase)) return null;
        float ns = NotchPlacement.Scale(DpiOf(target), cfg.NotchSizePercent, target.Bounds.Width);
        var surface = NotchPlacement.Surface(ns);
        var topLeft = NotchPlacement.TopLeft(target.Bounds, surface);
        float centerX = topLeft.X + surface.Width / 2f - TopLeft.X;
        IReadOnlyList<SessionState> sessions;
        try { sessions = _services.Sessions(); }
        catch { sessions = Array.Empty<SessionState>(); }
        bool working = sessions.Any(x => x.Phase.IsActive());
        var mode = working ? NotchMode.Working : cfg.NotchMinimal ? NotchMode.Minimal : NotchMode.Idle;
        float extra = working ? _painter.Notch.MeasureWorkingExtra(sessions, ns) : 0f;
        return new OnboardingLanding(mode, NotchLayout.TargetSize(mode, ns, extra), ns, centerX);
    }

    /// <summary>Nombres de los monitores como los de la bandeja («Monitor 2 · 2560×1440 · arriba»), en el idioma dado.</summary>
    private static IReadOnlyDictionary<string, string> MonitorLabels(Strings s)
    {
        try
        {
            var monitors = MonitorCatalog.Current();
            var labels = MonitorNames.Labels(monitors, s);
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < monitors.Count; i++) map[monitors[i].DeviceName] = labels[i];
            return map;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"asistente del notch: sin nombres de monitores: {ex.Message}");
            return new Dictionary<string, string>();
        }
    }
}
