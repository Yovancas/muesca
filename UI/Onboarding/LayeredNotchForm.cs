using System.Diagnostics;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.UI.Notch;
using static ClaudeBarWin.UI.Notch.NotchNative;

namespace ClaudeBarWin.UI.Onboarding;

/// <summary>
/// La ventana del notch grande, común a la bienvenida (<see cref="OnboardingForm"/>) y al tutorial: una superficie layered
/// pegada arriba del monitor, sin botón en la barra de tareas, siempre encima, con la sombra en su propia ventana que no
/// recibe clics (<see cref="NotchShadowWindow"/>). La hija decide la escena y la pinta; aquí vive lo de Win32:
/// <list type="bullet">
/// <item><b>Foco:</b> a diferencia del notch, SÍ se activa (se maneja con el teclado).</item>
/// <item><b>0 % CPU quieta:</b> el bucle (<c>Application.Idle</c> + <c>DwmFlush</c>, o un timer de 16 ms si DWM no compone)
/// solo vive mientras la escena se mueve (<see cref="SceneAnimating"/>); lo próximo que cambia sin moverse despierta con un
/// timer de un disparo (<see cref="SceneNextWake"/>). Si lo único que se mueve va a pocos fotogramas (<see cref="SceneSlowOnly"/>,
/// el spinner), un timer a <see cref="NotchMotion.SpinnerFps"/>.</item>
/// <item><b>DPI/monitor:</b> <c>WM_DPICHANGED</c> y <c>WM_DISPLAYCHANGE</c> ⇒ 400 ms después se recalculan escala y superficie
/// (<see cref="OnboardingPlacement"/> con el tamaño lógico de la hija).</item>
/// </list>
/// </summary>
public abstract class LayeredNotchForm : Form
{
    private const int RelayoutDebounceMs = 400;

    private readonly LayeredSurface _pill = new(), _shadowSurface = new();
    private readonly NotchShadowWindow _shadow = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly System.Windows.Forms.Timer _frameTimer = new();
    private readonly System.Windows.Forms.Timer _wake = new();
    private readonly System.Windows.Forms.Timer _relayout = new() { Interval = RelayoutDebounceMs };
    private string _device;
    private Rectangle _monitor, _work;
    private float _scale = 1f;
    private Size _surface;
    private Point _topLeft;
    private bool _idleHooked, _timerLoop, _failureLogged;
    private int _fastFlushes;

    /// <param name="monitorDevice">Monitor donde sale; "" o desconectado = el principal.</param>
    protected LayeredNotchForm(string? monitorDevice)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        TopMost = true;
        KeyPreview = true;
        Text = Brand.Name;
        _device = monitorDevice ?? "";
        _frameTimer.Tick += (_, _) => { Frame(); Pump(); };
        _wake.Tick += (_, _) => { _wake.Stop(); Frame(); Pump(); };
        _relayout.Tick += (_, _) => { _relayout.Stop(); Relayout(); };
    }

    /// <summary>Se está cerrando a propósito (sin preguntar): Alt+F4 ya no se intercepta.</summary>
    protected bool ClosingOnPurpose { get; private set; }

    /// <summary>Monitor donde está (nombre GDI).</summary>
    public string MonitorDevice => _device;

    /// <summary>Instante del reloj de la ventana (el que reciben la escena y sus muelles).</summary>
    protected TimeSpan Now => _clock.Elapsed;

    protected float SceneScale => _scale;
    protected Size SurfaceSize => _surface;
    protected Point TopLeft => _topLeft;

    // ---------------------------------------------------------------- lo que pone la hija

    /// <summary>Tamaño de la pastilla abierta en px lógicos (para la escala y la superficie).</summary>
    protected abstract SizeF LogicalSize { get; }
    protected abstract bool SceneAnimating { get; }
    /// <summary>¿Lo único que se mueve va a pocos fotogramas (el spinner)? Entonces basta un timer a su ritmo.</summary>
    protected virtual bool SceneSlowOnly => false;
    protected abstract TimeSpan? SceneNextWake(TimeSpan now);
    protected abstract bool SceneGone { get; }
    /// <summary>Alfa de toda la ventana (va como alfa constante de <c>UpdateLayeredWindow</c>).</summary>
    protected abstract float SceneAlpha { get; }
    /// <summary>Escala y superficie: al mostrarse y con cada cambio de DPI o de pantalla.</summary>
    protected abstract void ScenePlaced(float scale, Size surface);
    /// <summary>Empieza la entrada (ya colocada y visible).</summary>
    protected abstract void SceneStart(TimeSpan now);
    /// <summary>Avanza la escena hasta <paramref name="now"/> (y lo que la hija haga entonces, como el relevo).</summary>
    protected abstract void SceneStep(TimeSpan now);
    protected abstract RectangleF PaintScene(Graphics g, Size surface);
    protected abstract RectangleF PaintSceneShadow(Graphics g, Size surface);

    /// <summary>La hija la llama al final de su constructor (con la escena ya creada): escala, superficie y sitio.</summary>
    protected void InitPlacement()
    {
        ComputePlacement();
        Bounds = new Rectangle(_topLeft, _surface);
    }

    // ---------------------------------------------------------------- ventana

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_TOPMOST;
            return cp;
        }
    }

    // La ventana layered se pinta entera con UpdateLayeredWindow: nada de WM_PAINT.
    protected override void OnPaint(PaintEventArgs e) { }
    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _shadow.Create(Bounds);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _shadow.SetVisible(true);
        ReassertTopmost();
        Activate();
        ScenePlaced(_scale, _surface);
        SceneStart(Now);
        Frame();
        Pump();
    }

    /// <summary>Se cierra ya, sin preguntar (la app sale).</summary>
    public void Dismiss()
    {
        if (IsDisposed) return;
        ClosingOnPurpose = true;
        StopLoop();
        Close();
    }

    /// <summary>Lo trae al frente con el foco (la bandeja lo pide si se vuelve a abrir mientras está abierto).</summary>
    public void BringToFront(bool activate)
    {
        if (!IsHandleCreated || !Visible) return;
        ReassertTopmost();
        if (activate) Activate();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        StopLoop();
        _wake.Stop();
        _relayout.Stop();
        _shadow.SetVisible(false);
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopLoop();
            _frameTimer.Dispose();
            _wake.Dispose();
            _relayout.Dispose();
            _shadow.Dispose();
            _pill.Dispose();
            _shadowSurface.Dispose();
        }
        base.Dispose(disposing);
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WM_DPICHANGED:
                // Se ignora el rect sugerido: la ventana se recoloca sola, centrada arriba y a su escala.
                m.Result = IntPtr.Zero;
                ScheduleRelayout();
                return;
            case WM_DISPLAYCHANGE:
                ScheduleRelayout();
                break;
            case WM_SETTINGCHANGE:
                if ((long)m.WParam == SPI_SETWORKAREA) ScheduleRelayout();
                break;
        }
        base.WndProc(ref m);
    }

    /// <summary>¿Es la autorrepetición de una tecla mantenida? Bit 30 del lParam de <c>WM_KEYDOWN</c>: la tecla ya estaba
    /// abajo antes de este mensaje.</summary>
    internal static bool IsAutoRepeat(IntPtr lParam) => ((long)lParam & (1L << 30)) != 0;

    /// <summary>Tras un clic o una tecla: un frame, el motor que toque y el próximo despertar.</summary>
    protected void AfterInput()
    {
        Frame();
        Pump();
        ScheduleWake();
    }

    // ---------------------------------------------------------------- frame y bucle

    protected void Frame()
    {
        if (IsDisposed || !IsHandleCreated) return;
        SceneStep(Now);
        Render();
        if (SceneGone) BeginInvoke(CloseSelf);
    }

    private void CloseSelf()
    {
        if (ClosingOnPurpose || IsDisposed) return;
        ClosingOnPurpose = true;
        StopLoop();
        Hide();
        Close();
    }

    private void Render()
    {
        if (!Visible || _surface.Width <= 0 || _surface.Height <= 0) return;
        try
        {
            byte alpha = (byte)Math.Round(Math.Clamp(SceneAlpha, 0f, 1f) * 255f);
            var g = _pill.Begin(_surface, _scale);
            _pill.End(PaintScene(g, _surface), _surface);
            _pill.Present(Handle, _topLeft, _surface, alpha);
            if (_shadow.Handle != IntPtr.Zero)
            {
                var gs = _shadowSurface.Begin(_surface, _scale);
                _shadowSurface.End(PaintSceneShadow(gs, _surface), _surface);
                _shadowSurface.Present(_shadow.Handle, _topLeft, _surface, alpha);
            }
        }
        catch (Exception ex)
        {
            // Un frame perdido no debe tumbar la app; se registra la primera vez.
            if (!_failureLogged) AppLog.Error($"{GetType().Name}: falló un frame", ex);
            _failureLogged = true;
        }
    }

    /// <summary>Elige el motor del siguiente frame: nada (quieto), DwmFlush en Idle, o timer (sin DWM, o solo el spinner).</summary>
    protected void Pump()
    {
        if (IsDisposed || !Visible || !SceneAnimating)
        {
            StopLoop();
            _timerLoop = false;
            _fastFlushes = 0;
            ScheduleWake();
            return;
        }
        bool slow = SceneSlowOnly;
        if (!_timerLoop && !slow)
        {
            _frameTimer.Stop();
            if (!_idleHooked) { Application.Idle += OnIdle; _idleHooked = true; }
            return;
        }
        Unhook();
        int interval = slow && !_timerLoop ? 1000 / NotchMotion.SpinnerFps : NotchMotion.FallbackFrameMs;
        if (_frameTimer.Interval != interval) _frameTimer.Interval = interval;
        if (!_frameTimer.Enabled) _frameTimer.Start();
    }

    private void OnIdle(object? sender, EventArgs e)
    {
        while (_idleHooked && !PeekMessage(out _, IntPtr.Zero, 0, 0, PM_NOREMOVE))
        {
            Frame();
            if (!SceneAnimating || SceneSlowOnly) { Pump(); return; }
            var t0 = Now;
            int hr = DwmFlush();
            if (hr < 0 || (Now - t0).TotalMilliseconds < 1)
            {
                if (++_fastFlushes >= NotchMotion.DwmFlushFastReturns)
                {
                    _timerLoop = true;   // DWM no compone (pantalla apagada, sesión bloqueada, RDP…)
                    Pump();
                    return;
                }
            }
            else _fastFlushes = 0;
        }
    }

    /// <summary>Lo próximo que cambia sin moverse: un disparo (entre medias, nada se mueve).</summary>
    protected void ScheduleWake()
    {
        _wake.Stop();
        if (IsDisposed || SceneNextWake(Now) is not { } d) return;
        _wake.Interval = (int)Math.Clamp(Math.Ceiling(d.TotalMilliseconds), 1, int.MaxValue);
        _wake.Start();
    }

    private void StopLoop()
    {
        Unhook();
        _frameTimer.Stop();
    }

    private void Unhook()
    {
        if (!_idleHooked) return;
        Application.Idle -= OnIdle;
        _idleHooked = false;
    }

    private void ReassertTopmost()
    {
        if (!IsHandleCreated) return;
        const uint flags = SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE;
        if (_shadow.Handle != IntPtr.Zero) SetWindowPos(_shadow.Handle, HWND_TOPMOST, 0, 0, 0, 0, flags);
        SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, flags);
    }

    // ---------------------------------------------------------------- colocación

    private void ScheduleRelayout()
    {
        _relayout.Stop();
        _relayout.Start();
    }

    private void Relayout()
    {
        if (IsDisposed) return;
        var (scale, surface, topLeft) = (_scale, _surface, _topLeft);
        ComputePlacement();
        if (Math.Abs(scale - _scale) > 0.0001f || surface != _surface || topLeft != _topLeft)
        {
            SetBounds(_topLeft.X, _topLeft.Y, _surface.Width, _surface.Height);
            ScenePlaced(_scale, _surface);
        }
        Frame();
        Pump();
    }

    private void ComputePlacement()
    {
        var screen = ScreenFor(_device);
        _device = screen.DeviceName;
        _monitor = screen.Bounds;
        _work = screen.WorkingArea;
        var size = LogicalSize;
        _scale = OnboardingPlacement.Scale(DpiOf(screen), _monitor, _work, size.Width, size.Height);
        _surface = OnboardingPlacement.Surface(_scale, size.Width, size.Height);
        _topLeft = OnboardingPlacement.TopLeft(_monitor, _surface);
    }

    protected static Screen ScreenFor(string? device) =>
        Screen.AllScreens.FirstOrDefault(s => string.Equals(s.DeviceName, device, StringComparison.OrdinalIgnoreCase))
        ?? Screen.PrimaryScreen ?? Screen.AllScreens[0];

    /// <summary>DPI efectivo del monitor (96 si no se puede leer).</summary>
    protected static int DpiOf(Screen screen)
    {
        try
        {
            var b = screen.Bounds;
            var h = MonitorFromPoint(new POINT { X = b.Left + b.Width / 2, Y = b.Top + b.Height / 2 }, MONITOR_DEFAULTTONEAREST);
            if (h != IntPtr.Zero && GetDpiForMonitor(h, MDT_EFFECTIVE_DPI, out uint dx, out _) == 0 && dx > 0) return (int)dx;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { /* sin shcore: 96 */ }
        return 96;
    }
}
