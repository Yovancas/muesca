using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Licensing;
using ClaudeBarWin.Services.Tutorial;
using ClaudeBarWin.UI.Onboarding;

namespace ClaudeBarWin.UI.Tutorial;

/// <summary>
/// La ventana del tutorial interactivo: el notch grande de la bienvenida (<see cref="LayeredNotchForm"/>: superficie layered,
/// bucle a 0 % de CPU quieta, DPI) con <see cref="TutorialScene"/> dentro, pintada por <see cref="OnboardingPainter"/>. Solo
/// traduce el ratón y el teclado a la escena y pinta; la lógica está en <see cref="TutorialMachine"/>. Nunca toca los datos
/// del usuario ni manda nada a Claude: todo es el notch de demostración con datos de ejemplo. Esc, «Salir» o Alt+F4 la cierran
/// (el progreso ya está guardado); una sola a la vez (la bandeja la trae al frente si se vuelve a pedir).
/// </summary>
public sealed class TutorialForm : LayeredNotchForm
{
    private readonly TutorialScene _scene;
    private readonly OnboardingPainter _painter;

    /// <param name="chaptersDone">Capítulos ya hechos (de la config), para retomar en el primero que falta.</param>
    /// <param name="pro">Estado del Pro: sin él, al acabar un capítulo con pasos Pro, la línea con el enlace a la tarjeta.</param>
    /// <param name="monitorDevice">Monitor donde sale (el del notch); "" o desconectado = el principal.</param>
    public TutorialForm(IEnumerable<TutorialChapter> chaptersDone, Strings strings, ProState pro, bool reduceMotion,
        string? monitorDevice = null) : base(monitorDevice)
    {
        ArgumentNullException.ThrowIfNull(strings);
        Text = strings.Tut.Title;
        _painter = new OnboardingPainter(strings);
        _scene = new TutorialScene(new TutorialMachine(chaptersDone), strings, _painter, pro, reduceMotion);
        _scene.ProgressChanged += () => ProgressChanged?.Invoke(this);
        _scene.ProRequested += () => ProRequested?.Invoke(this);
        InitPlacement();
    }

    /// <summary>Un capítulo pasó a hecho: la bandeja guarda <see cref="ChaptersDone"/> en la config.</summary>
    public event Action<TutorialForm>? ProgressChanged;

    /// <summary>«Ver Pro ›»: la bandeja abre la tarjeta del Pro que ya existe.</summary>
    public event Action<TutorialForm>? ProRequested;

    /// <summary>Se encoge y se va, como con «Salir» (la bandeja lo pide antes de abrir la tarjeta del Pro).</summary>
    public void LeaveAnimated()
    {
        if (IsDisposed) return;
        _scene.RequestExit(Now);
        AfterInput();
    }

    /// <summary>Los capítulos hechos ahora (para guardarlos).</summary>
    public IReadOnlyCollection<TutorialChapter> ChaptersDone => _scene.Machine.ChaptersDone;

    // ---------------------------------------------------------------- escena

    protected override SizeF LogicalSize => new(TutorialGeometry.Width, TutorialGeometry.Height);
    protected override bool SceneAnimating => _scene.IsAnimating;
    protected override bool SceneSlowOnly => _scene.SlowOnly;
    protected override TimeSpan? SceneNextWake(TimeSpan now) => _scene.NextWakeIn(now);
    protected override bool SceneGone => _scene.IsGone;
    protected override float SceneAlpha => _scene.WindowAlpha;
    protected override void ScenePlaced(float scale, Size surface) => _scene.Place(scale, surface);
    protected override void SceneStart(TimeSpan now) => _scene.Start(now);
    protected override void SceneStep(TimeSpan now) => _scene.Step(now);
    protected override RectangleF PaintScene(Graphics g, Size surface) => _painter.PaintTutorial(g, surface, _scene.Frame());
    protected override RectangleF PaintSceneShadow(Graphics g, Size surface) => _painter.PaintTutorialShadow(g, surface, _scene.Frame());

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Alt+F4 o la X del sistema: se encoge y se va (no hay nada que confirmar). Al salir de la app, sin más.
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
        if (_scene.PointerMove(e.Location, Now)) AfterInput();
        Cursor = _scene.OverControl ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_scene.PointerLeave(Now)) AfterInput();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button is not (MouseButtons.Left or MouseButtons.Right)) return;
        // El arrastre del archivo de ejemplo sigue aunque el ratón salga de la ventana.
        Capture = true;
        _scene.PointerDown(e.Location, Now, right: e.Button == MouseButtons.Right);
        Frame();
        Pump();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button is not (MouseButtons.Left or MouseButtons.Right)) return;
        Capture = false;
        _scene.PointerUp(e.Location, Now, right: e.Button == MouseButtons.Right);
        AfterInput();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_scene.Wheel(e.Delta, e.Location, Now)) AfterInput();
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_MOUSEHWHEEL = 0x020E;
        if (m.Msg == WM_MOUSEHWHEEL)
        {
            // Deslizamiento horizontal (trackpad o rueda inclinable): cambia de página, como en el notch.
            int delta = (short)(((long)m.WParam >> 16) & 0xFFFF);
            var p = PointToClient(new Point((short)((long)m.LParam & 0xFFFF), (short)(((long)m.LParam >> 16) & 0xFFFF)));
            if (_scene.Wheel(delta, p, Now, horizontal: true)) AfterInput();
            m.Result = IntPtr.Zero;
            return;
        }
        base.WndProc(ref m);
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
}
