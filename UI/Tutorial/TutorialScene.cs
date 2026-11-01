using ClaudeBarWin.Models;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Licensing;
using ClaudeBarWin.Services.Motion;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Notch.Features;
using ClaudeBarWin.Services.Onboarding;
using ClaudeBarWin.Services.State;
using ClaudeBarWin.Services.Tutorial;
using ClaudeBarWin.UI.Notch;
using ClaudeBarWin.UI.Onboarding;

namespace ClaudeBarWin.UI.Tutorial;

/// <summary>Lo del escenario en este frame: la bandeja de mentira, el archivo de ejemplo (en su sitio o arrastrándose) y qué
/// entrada del menú o de los ajustes está bajo el ratón y cuál es la del paso (va con el acento).</summary>
public sealed record TutorialStageView(TutorialTray Tray, bool File, PointF? Drag, int MenuHover, int SettingsHover,
    int MenuGoal, int SettingsGoal, bool IconHover);

/// <summary>
/// Una foto de lo que pinta el tutorial en este frame (la construye <see cref="TutorialScene"/> con los valores ANIMADOS; el
/// painter la pinta tal cual). Como <see cref="OnboardingFrame"/>: la forma y sus alfas, el cruce de pasos
/// (<see cref="Previous"/> con <c>1 − Blend</c>), los realces y el foco; además el «hecho» (<see cref="DonePop"/>: escala del
/// visto, que entra con el muelle de atención, y <see cref="DoneAlpha"/>), el notch de demostración (<see cref="Notch"/>, en
/// coordenadas del escenario, lo pinta el painter del notch de verdad) y el escenario.
/// </summary>
public sealed record TutorialFrame(float Width, float Height, float Scale, float CenterX, TutorialView View,
    float Squash = 0f, float ContentAlpha = 1f, float ContentOffsetY = 0f, float LogoAlpha = 0f, float ShadowAlpha = 0f,
    TutorialView? Previous = null, float Blend = 1f, float Shift = 0f, float PreviousShift = 0f, float IndicatorPos = 0f,
    TutorialHit Hover = default, float HoverIntensity = 0f, TutorialHit Pressed = default, TutorialHit Focus = default,
    bool ShowFocus = false, float DonePop = 1f, float DoneAlpha = 0f, NotchFrame? Notch = null, TutorialStageView? Stage = null);

/// <summary>
/// El tutorial en movimiento, sin Win32: la máquina de pasos (<see cref="TutorialMachine"/>), la entrada y la salida del notch
/// grande (<see cref="OnboardingMotion"/>, la de la bienvenida), el cruce de pasos, el «hecho», el foco del teclado y el notch
/// de demostración: un <see cref="NotchAnimator"/> de verdad con las páginas, las flechas y las geometrías del notch real para
/// acertar el clic, alimentado con <see cref="TutorialSamples"/>. La ventana le pasa el instante, el ratón y las teclas; los
/// tests de capturas lo conducen igual, sin ventana. Nunca toca los datos del usuario ni manda nada a Claude.
/// </summary>
public sealed class TutorialScene
{
    private static readonly TimeSpan StepCross = PageTransition.Duration;
    private static readonly SpringPreset IndicatorPreset = Motion.SpringAdjust with { EpsX = 0.02, EpsV = 0.2 };
    private static readonly SpringPreset PopPreset = Motion.SpringAttention with { EpsX = 0.002, EpsV = 0.02 };

    private readonly TutorialMachine _m;
    private readonly OnboardingPainter _painter;
    private readonly ProState _pro;
    private readonly OnboardingMotion _motion = new();
    private readonly NotchAnimator _notch = new();
    private readonly PageTransition _pages;
    private readonly PageArrows _arrows = new();
    private readonly PageCarousel _carousel = new(TutorialDemo.Pages);
    private readonly SpringValue _indicator = new(0, IndicatorPreset);
    private readonly SpringValue _hoverIntensity = new(0, Motion.HoverIn);
    private readonly SpringValue _donePop = new(1, PopPreset);
    private readonly SpringValue _doneAlpha = new(0, Motion.Rapido);
    private readonly SpringValue _split = new(0, Motion.SpringAdjust);
    private readonly Func<DateTime> _utcNow;
    private TutorialSamples _samples;
    private TutorialView _view;
    private TutorialView? _previous;
    private TimeSpan _stepAt = TimeSpan.MinValue, _now, _last;
    private int _stepDirection = 1;
    private float _scale = 1f, _centerX;
    private TutorialHit _hover, _pressed, _focus;
    private bool _showFocus, _started, _leaving;

    // El notch de demostración: el ratón encima, los plazos del hover (abrir) y de la gracia (cerrar), el armado de los botones.
    private bool _overNotch, _dragging, _dragOver;
    // Pasar el ratón abre el notch solo si ENTRA en él con el tutorial ya desplegado: si al empezar el paso (o mientras se
    // desplegaba la ventana) ya estaba encima, tiene que salir y volver. Así cada paso empieza con su notch como en el guion
    // (el primero, en reposo) y no se abre «solo» porque el cursor estaba donde aparece. Salir siempre cuenta.
    private bool _hoverArmed = true, _wasInteractive;
    private Point? _pointer;
    private PointF _dragAt;
    private TimeSpan? _expandAt, _collapseAt, _armSince;
    private PageArrow _arrowHover, _arrowHeld;
    private PermissionVerdict _held;
    private NotchPage _shownPage;
    private int _menuHover = -1, _settingsHover = -1;
    private bool _iconHover;
    // El pomodoro de ejemplo: lo que queda, desde cuándo corre y la vista cacheada (se rehace al cambiar el segundo que se ve).
    private TimeSpan _focusLeft, _focusSince;
    private (FocusRun, FocusPhase, int, int, bool)? _focusKey;
    private FocusView? _focusView;

    /// <param name="localNow">Hora local (el reloj del reposo y de la bandeja; los tests de capturas la fijan).</param>
    /// <param name="utcNow">Reloj UTC de los datos de ejemplo (reinicios de la cuota…).</param>
    public TutorialScene(TutorialMachine machine, Strings strings, OnboardingPainter painter, ProState pro, bool reduceMotion,
        Func<DateTime>? localNow = null, Func<DateTime>? utcNow = null)
    {
        _m = machine ?? throw new ArgumentNullException(nameof(machine));
        _painter = painter ?? throw new ArgumentNullException(nameof(painter));
        ArgumentNullException.ThrowIfNull(strings);
        _pro = pro;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        ReduceMotion = reduceMotion;
        _motion.ReduceMotion = reduceMotion;
        _notch.ReduceMotion = reduceMotion;
        _arrows.ReduceMotion = reduceMotion;
        _painter.Strings = strings;
        _samples = new TutorialSamples(strings, _utcNow(), (localNow ?? (() => DateTime.Now))());
        _pages = new PageTransition(_m.Demo.Page, _m.Demo.PageIndex) { ReduceMotion = reduceMotion };
        _shownPage = _m.Demo.Page;
        _m.ProgressChanged += () => ProgressChanged?.Invoke();
        _view = Build();
        _indicator.Snap(_m.StepIndex);
        ResetStage(TimeSpan.Zero);
    }

    /// <summary>Un capítulo pasó a hecho (la ventana lo guarda en la config).</summary>
    public event Action? ProgressChanged;

    /// <summary>«Ver Pro ›» del cierre de capítulo (la bandeja abre la tarjeta del Pro que ya existe).</summary>
    public event Action? ProRequested;

    public TutorialMachine Machine => _m;
    public TutorialView View => _view;
    public OnboardingMotion Choreography => _motion;
    public bool ReduceMotion { get; }
    public float Scale => _scale;
    public Strings Strings => _samples.Strings;

    /// <summary>Tamaño del tutorial abierto en px físicos.</summary>
    public SizeF OpenSize => new(TutorialGeometry.Width * _scale, TutorialGeometry.Height * _scale);

    /// <summary>La pastilla tal como está ahora (la que se pinta y donde se acierta el clic).</summary>
    public RectangleF Pill => OnboardingGeometry.PillRect(_motion.Width, _motion.Height, _centerX);

    /// <summary>La pastilla abierta (donde está maquetado el contenido).</summary>
    private RectangleF Open => OnboardingGeometry.PillRect(TutorialGeometry.Width * _scale, TutorialGeometry.Height * _scale, _centerX);

    private RectangleF Stage => TutorialGeometry.Stage(Open, _scale);
    private float DemoScale => _scale * TutorialGeometry.DemoScale;

    public bool IsGone => _motion.IsGone;
    public float WindowAlpha => _motion.PillAlpha;

    /// <summary>¿Hay un control usable bajo el ratón? (la ventana pone la mano).</summary>
    public bool OverControl => _hover.Control is not (TutorialControl.None or TutorialControl.Notch) || _overNotch
        || _menuHover >= 0 || _settingsHover >= 0 || _iconHover;

    // ---------------------------------------------------------------- ciclo

    public void Place(float scale, Size surface)
    {
        bool changed = Math.Abs(scale - _scale) > 0.0001f;
        _scale = scale > 0 ? scale : 1f;
        _centerX = surface.Width / 2f;
        if (changed && _started)
        {
            _motion.Resize(OpenSize, _scale);
            SyncNotch(_now);
        }
    }

    /// <summary>Empieza la entrada (el notch baja y se despliega, como la bienvenida).</summary>
    public void Start(TimeSpan now)
    {
        _started = true;
        _last = _now = now;
        _motion.Start(NotchLayout.TargetSize(NotchMode.Idle, _scale, 0f), OpenSize, _scale, now);
        ResetStage(now);
        SyncNotch(now);
    }

    /// <summary>Salir (Esc, «Salir» o «Cerrar»): se encoge y se mete arriba. El progreso ya está guardado.</summary>
    public void RequestExit(TimeSpan now)
    {
        if (_leaving) return;
        _leaving = true;
        _hover = _pressed = _focus = TutorialHit.None;
        _showFocus = false;
        _hoverIntensity.SetTarget(0);
        _motion.Finish(SizeF.Empty, now);
    }

    public bool IsAnimating => FastAnimating || SpinnerOn;

    /// <summary>¿Lo único que se mueve es el spinner del notch de demostración? (la ventana lo mueve a pocos fotogramas).</summary>
    public bool SlowOnly => !FastAnimating && SpinnerOn;

    private bool FastAnimating => _motion.IsAnimating || !_indicator.IsSettled || !_hoverIntensity.IsSettled
        || !_donePop.IsSettled || !_doneAlpha.IsSettled || StepCrossing(_now) || !_split.IsSettled
        || (DemoVisible && (_notch.IsAnimating || _pages.IsAnimating || _arrows.IsAnimating || Arming));

    private bool SpinnerOn => DemoVisible && !ReduceMotion && _notch.SpinnerVisible(ActiveOnScreen,
        sessionsOnScreen: _pages.Current == NotchPage.Claude || (_pages.IsAnimating && _pages.Previous == NotchPage.Claude));

    private bool ActiveOnScreen => _m.Demo.Face is TutorialFace.Working;

    /// <summary>Los botones del Permiso o del recordatorio rellenándose (sin reducir movimiento: con él, saltan al armarse).</summary>
    private bool Arming => FaceWithButtons && !ReduceMotion && ArmProgress(_now) < 1f;

    private bool FaceWithButtons => _m.Phase == TutorialPhase.Step && _m.Demo.Mode is NotchMode.Permission or NotchMode.Reminder
        && _m.Demo.Answer == TutorialAnswer.None;

    private bool DemoVisible => _motion.Interactive || _motion.Stage == OnboardingStage.Unfolding;

    /// <summary>Lo próximo que cambia sin moverse: abrir por hover, cerrar tras la gracia, armar (con reducir movimiento) o el
    /// segundo del pomodoro; null si nada.</summary>
    public TimeSpan? NextWakeIn(TimeSpan now)
    {
        TimeSpan? next = null;
        void Min(TimeSpan? at)
        {
            if (at is not { } a) return;
            var d = a - now;
            if (d < TimeSpan.Zero) d = TimeSpan.Zero;
            if (next is null || d < next) next = d;
        }
        if (_leaving) return null;
        Min(_expandAt);
        Min(_collapseAt);
        if (FaceWithButtons && _armSince is { } arm) Min(arm + NotchTiming.PermissionArm);
        if (FocusTicking) Min(now + (FocusClock.NextSecondIn(FocusLeft(now)) ?? TimeSpan.FromSeconds(1)));
        return next;
    }

    public void Step(TimeSpan now)
    {
        _now = now;
        double dt = Math.Clamp((now - _last).TotalSeconds, 0.0, 1.0 / 20.0);
        _last = now;
        _motion.Step(now);
        if (_motion.Interactive && !_wasInteractive)
        {
            // Acaba de desplegarse: un cursor que ya estaba sobre el notch de demostración no lo abre hasta salir y volver.
            if (_pointer is { } at && OverNotch(at)) _hoverArmed = false;
        }
        _wasInteractive = _motion.Interactive;
        if (dt > 0)
        {
            _indicator.Advance(dt);
            _hoverIntensity.Advance(dt);
            _donePop.Advance(dt);
            _doneAlpha.Advance(dt);
            _split.Advance(dt);
            _arrows.Advance(dt);
        }
        if (ReduceMotion)
        {
            _indicator.Snap(_indicator.Target);
            _donePop.Snap(_donePop.Target);
            _doneAlpha.Snap(_doneAlpha.Target);
            _split.Snap(_split.Target);
        }
        _notch.Step(now);
        _pages.Step(now);
        if (_expandAt is { } e && now >= e)
        {
            _expandAt = null;
            if (_overNotch) Act(new TutorialInput(TutorialAct.ExpandNotch), now);
        }
        if (_collapseAt is { } c && now >= c)
        {
            _collapseAt = null;
            if (!_overNotch && !_dragging) Act(new TutorialInput(TutorialAct.LeaveNotch), now);
        }
        // El armado cuenta desde que el aviso con botones se ve entero y quieto (como el notch de verdad).
        if (FaceWithButtons && _armSince is null && _notch.ShownMode == _m.Demo.Mode && _notch.ShapeSettled
            && _notch.ContentAlpha >= 0.95f)
            _armSince = now;
        UpdateArrows();
    }

    // ---------------------------------------------------------------- frame

    public TutorialFrame Frame()
    {
        float blend = StepBlend(_now);
        float shift = MathF.Round(_stepDirection * PageTransition.Shift * (1f - blend));
        float prevShift = MathF.Round(-_stepDirection * PageTransition.Shift * blend);
        return new TutorialFrame(_motion.Width, _motion.Height, _scale, _centerX, _view, _motion.Squash, _motion.ContentAlpha,
            _motion.ContentOffsetY, _motion.LogoAlpha, _motion.ShadowAlpha, blend < 1f ? _previous : null, blend, shift, prevShift,
            (float)_indicator.Value, _motion.Interactive ? _hover : TutorialHit.None, (float)Math.Clamp(_hoverIntensity.Value, 0, 1),
            _pressed, _focus, _showFocus, (float)_donePop.Value, (float)Math.Clamp(_doneAlpha.Value, 0, 1),
            DemoVisible ? NotchFrameNow() : null, StageView());
    }

    private bool StepCrossing(TimeSpan now) => _previous is not null && StepBlend(now) < 1f;

    private float StepBlend(TimeSpan now) => ReduceMotion || _stepAt == TimeSpan.MinValue ? 1f
        : (float)Math.Clamp((now - _stepAt).TotalMilliseconds / StepCross.TotalMilliseconds, 0, 1);

    private TutorialStageView StageView()
    {
        var d = _m.Demo;
        bool file = _m.Phase == TutorialPhase.Step && _m.Step == TutorialStep.ShelfDrop && !d.ShelfFile;
        int menuGoal = _m.Phase != TutorialPhase.Step ? -1 : _m.Step switch
        {
            TutorialStep.TrayMonitor => (int)TutorialMenuItem.Monitor,
            TutorialStep.TraySettings => (int)TutorialMenuItem.Settings,
            _ => -1,
        };
        int settingsGoal = _m.Phase == TutorialPhase.Step && _m.Step == TutorialStep.TrayTutorial ? (int)TutorialSettingsRow.Tutorial : -1;
        return new TutorialStageView(d.Tray, file, _dragging ? _dragAt : null, _menuHover, _settingsHover, menuGoal, settingsGoal,
            _iconHover);
    }

    // ---------------------------------------------------------------- ratón y teclado

    /// <summary>El ratón se movió a <paramref name="p"/> (superficie). True si cambió lo que se ve.</summary>
    public bool PointerMove(Point p, TimeSpan now)
    {
        bool changed = false;
        _pointer = p;
        if (_dragging)
        {
            _dragAt = p;
            bool over = OverNotch(p);
            if (over && !_dragOver)
            {
                // Algo aceptable encima abre el expandido YA en la Bandeja (como el notch de verdad), con su contorno.
                _dragOver = true;
                Act(new TutorialInput(TutorialAct.ExpandNotch), now);
                Act(new TutorialInput(TutorialAct.DotPage, IndexOf(NotchPage.Shelf)), now);
            }
            else if (!over && _dragOver) _dragOver = false;
            return true;
        }
        var hit = _motion.Interactive && !_leaving
            ? TutorialGeometry.HitTest(p, Pill, _scale, TutorialMachine.ChapterCount, _m.StepCount, _m.Phase, _view.ProLink is not null)
            : TutorialHit.None;
        if (!Usable(hit)) hit = TutorialHit.None;
        changed |= SetHover(hit);
        changed |= StageHover(p, now);
        return changed;
    }

    public bool PointerLeave(TimeSpan now)
    {
        _pointer = null;
        _hoverArmed = true;   // fuera de la ventana: la próxima vez que entre en el notch, cuenta
        _pressed = TutorialHit.None;
        bool changed = SetHover(TutorialHit.None);
        if (!_dragging) changed |= StageHover(new Point(int.MinValue / 2, int.MinValue / 2), now);
        return changed;
    }

    public void PointerDown(Point p, TimeSpan now, bool right = false)
    {
        PointerMove(p, now);
        if (right || !_motion.Interactive || _leaving) return;
        _pressed = _hover;
        var d = _m.Demo;
        if (_m.Phase == TutorialPhase.Step && _m.Step == TutorialStep.ShelfDrop && !d.ShelfFile
            && TutorialGeometry.SampleFile(Stage, _scale).Contains(p))
        {
            _dragging = true;
            _dragAt = p;
            _pressed = TutorialHit.None;
            return;
        }
        if (_overNotch && FaceWithButtons && Armed) _held = ButtonAt(p);
        if (_overNotch && d.Mode == NotchMode.Expanded)
        {
            var arrow = NotchGeometry.PageArrowAt(Local(p), DemoPill, DemoScale, TutorialDemo.Pages.Count);
            _arrowHeld = PageArrows.Target(d.PageIndex, TutorialDemo.Pages.Count, arrow) >= 0 ? arrow : PageArrow.None;
        }
    }

    /// <summary>Soltar: los controles van si se suelta sobre el MISMO que se pulsó (como un botón de verdad); el notch de
    /// demostración, con las geometrías del notch real.</summary>
    public void PointerUp(Point p, TimeSpan now, bool right = false)
    {
        PointerMove(p, now);
        if (_dragging)
        {
            _dragging = false;
            bool over = OverNotch(p);
            _dragOver = false;
            Act(new TutorialInput(over ? TutorialAct.DropFile : TutorialAct.LeaveNotch), now);
            return;
        }
        var pressed = _pressed;
        _pressed = TutorialHit.None;
        var held = _held;
        _held = PermissionVerdict.None;
        var arrowHeld = _arrowHeld;
        _arrowHeld = PageArrow.None;
        if (!_motion.Interactive || _leaving) return;
        if (right)
        {
            if (_m.Demo.OnTray && TutorialGeometry.TrayIcon(Stage, _scale).Contains(p)) Act(new TutorialInput(TutorialAct.TrayRight), now);
            return;
        }
        if (pressed.Control is not (TutorialControl.None or TutorialControl.Notch))
        {
            if (pressed == _hover)
            {
                _showFocus = false;
                Activate(pressed, now);
            }
            return;
        }
        if (Stage.Contains(p)) StageClick(p, held, arrowHeld, now);
    }

    /// <summary>Rueda (o deslizamiento horizontal, <paramref name="horizontal"/>) sobre el notch de demostración abierto.</summary>
    public bool Wheel(int delta, Point p, TimeSpan now, bool horizontal = false)
    {
        if (!_motion.Interactive || _leaving || !OverNotch(p) || _m.Demo.Mode != NotchMode.Expanded) return false;
        var d = _m.Demo;
        var pill = DemoPill;
        bool withAlert = WithAlert(d);
        bool overList = NotchContentPainter.SessionColumn(pill, DemoScale, withAlert, true).Contains(Local(p));
        int listed = _samples.Sessions.Count;
        int rows = NotchContentPainter.ExpandedRows(pill.Height, DemoScale, withAlert, true);
        if (!horizontal && NotchPages.RouteWheel(d.Page, TutorialDemo.Pages.Count, overList, listed > rows) == WheelTarget.SessionList)
            return Act(new TutorialInput(TutorialAct.WheelSessions, delta < 0 ? 1 : -1), now);
        if (_carousel.Current != d.Page) _carousel.GoTo(d.Page);
        int before = _carousel.Index;
        if (!_carousel.OnWheel(horizontal ? NotchPages.HorizontalToCarouselDelta(delta) : delta, _utcNow())) return false;
        int dir = _carousel.Index == (before + 1) % TutorialDemo.Pages.Count ? 1 : -1;
        return Act(new TutorialInput(TutorialAct.WheelPage, dir), now);
    }

    /// <summary>
    /// Una tecla. Tab / Mayús+Tab mueven el foco; Intro (o Espacio) pulsa el control con el foco o, sin él, hace el gesto del
    /// paso (su alternativa de teclado) y, con el paso hecho, pasa al siguiente; ← → hacen el gesto en los pasos con dirección
    /// y, si no, mueven entre pasos; Retroceso es «Atrás»; Esc sale. False si no es una tecla del tutorial.
    /// </summary>
    public bool Key(Keys key, bool shift, TimeSpan now, bool repeat = false)
    {
        if (repeat && key is Keys.Enter or Keys.Space or Keys.Escape) return true;
        if (!_motion.Interactive || _leaving) return key is Keys.Escape or Keys.Enter or Keys.Tab or Keys.Space;
        switch (key)
        {
            case Keys.Escape:
                RequestExit(now);
                return true;
            case Keys.Tab:
                _showFocus = true;
                _focus = TutorialGeometry.MoveFocus(FocusOrder(), _focus, shift ? -1 : 1);
                return true;
            case Keys.Enter:
            case Keys.Space:
                if (_showFocus && _focus.IsButton && Usable(_focus)) Activate(_focus, now);
                else if (key == Keys.Space && _showFocus && _focus.Control == TutorialControl.Notch) KeyAct(TutorialKey.Enter, now);
                else if (_m.CanAdvance) Advance(now);
                else KeyAct(TutorialKey.Enter, now);
                return true;
            case Keys.Back:
                Activate(new TutorialHit(TutorialControl.Back), now);
                return true;
            case Keys.Left:
            case Keys.Right:
            {
                var k = key == Keys.Left ? TutorialKey.Left : TutorialKey.Right;
                if (!_m.CanAdvance && _m.KeyActs(k)) KeyAct(k, now);
                else if (key == Keys.Left) Activate(new TutorialHit(TutorialControl.Back), now);
                else if (_m.CanAdvance) Advance(now);
                return true;
            }
            case Keys.Up:
            case Keys.Down:
            {
                var k = key == Keys.Up ? TutorialKey.Up : TutorialKey.Down;
                if (_m.KeyActs(k)) KeyAct(k, now);
                return true;
            }
        }
        return false;
    }

    /// <summary>Alt+F4 o cerrar la ventana: salir (el progreso está guardado; no hay nada que confirmar).</summary>
    public void RequestClose(TimeSpan now) => RequestExit(now);

    /// <summary>Lo que hace cada control del marco (clic, Espacio o Intro).</summary>
    public void Activate(TutorialHit hit, TimeSpan now)
    {
        if (_leaving || !Usable(hit)) return;
        switch (hit.Control)
        {
            case TutorialControl.Exit:
                RequestExit(now);
                return;
            case TutorialControl.Back:
                _m.Back();
                break;
            case TutorialControl.Skip:
                if (_m.Phase == TutorialPhase.Finished) _m.Restart();
                else _m.Skip();
                break;
            case TutorialControl.Next:
                Advance(now);
                return;
            case TutorialControl.Chapter:
                _m.GoTo((TutorialChapter)hit.Index);
                break;
            case TutorialControl.StepDot:
                _m.GoToStep(hit.Index);
                break;
            case TutorialControl.ProLink:
                ProRequested?.Invoke();
                return;
            case TutorialControl.Notch:
                KeyAct(TutorialKey.Enter, now);
                return;
        }
        Rebuild(now);
    }

    /// <summary>¿Se puede usar ahora? (lo que no aplica se pinta atenuado o no se pinta, y no responde).</summary>
    public bool Usable(TutorialHit hit) => hit.Control switch
    {
        TutorialControl.None => false,
        TutorialControl.Back => _view.CanGoBack,
        TutorialControl.Skip => _view.CanSkip,
        TutorialControl.ProLink => _view.ProLink is not null,
        TutorialControl.StepDot => _m.Phase == TutorialPhase.Step && hit.Index >= 0 && hit.Index < _m.StepCount && hit.Index != _m.StepIndex,
        TutorialControl.Chapter => hit.Index >= 0 && hit.Index < TutorialMachine.ChapterCount,
        TutorialControl.Notch => _m.Phase == TutorialPhase.Step && !_m.StepDone,
        _ => true,
    };

    private IReadOnlyList<TutorialHit> FocusOrder() =>
        TutorialGeometry.FocusOrder(TutorialMachine.ChapterCount, _m.StepCount, _m.Phase, _view.ProLink is not null, Usable);

    /// <summary>«Siguiente» (o «Cerrar» al final).</summary>
    private void Advance(TimeSpan now)
    {
        if (_m.Phase == TutorialPhase.Finished)
        {
            RequestExit(now);
            return;
        }
        if (_m.Next()) Rebuild(now);
    }

    private void KeyAct(TutorialKey key, TimeSpan now)
    {
        if (_m.Phase != TutorialPhase.Step) return;
        // Sin ratón: lo que el gesto deja a la vista se ve igual (el hover de la gráfica, el archivo soltado…).
        Outcome(_m.HandleKey(key), now);
    }

    /// <summary>Un gesto sobre la demostración: la mueve y, si completa el paso, el «hecho».</summary>
    private bool Act(TutorialInput input, TimeSpan now) => Outcome(_m.Handle(input), now);

    private bool Outcome(TutorialOutcome outcome, TimeSpan now)
    {
        if (outcome == TutorialOutcome.Ignored) return false;
        if (outcome == TutorialOutcome.Completed) PopDone();
        Rebuild(now);
        return true;
    }

    private void PopDone()
    {
        _donePop.Snap(NotchMotion.FocusPopFrom);
        _donePop.SetTarget(1);
        _doneAlpha.Snap(0);
        _doneAlpha.SetTarget(1);
        if (ReduceMotion)
        {
            _donePop.Snap(1);
            _doneAlpha.Snap(1);
        }
    }

    private bool SetHover(TutorialHit hit)
    {
        if (hit == _hover) return false;
        _hover = hit;
        bool on = hit.Control is not (TutorialControl.None or TutorialControl.Notch);
        _hoverIntensity.SetPreset(on ? Motion.HoverIn : Motion.Rapido);
        _hoverIntensity.SetTarget(on ? 1 : 0);
        if (ReduceMotion) _hoverIntensity.Snap(_hoverIntensity.Target);
        return true;
    }

    // ---------------------------------------------------------------- estado → vista

    /// <summary>Tras cada cambio: los textos, el cruce de paso, el «hecho» y el notch de demostración.</summary>
    private void Rebuild(TimeSpan now)
    {
        var old = _view;
        _view = Build();
        bool moved = old.Step != _view.Step || old.Phase != _view.Phase || old.Chapter != _view.Chapter;
        if (moved)
        {
            _previous = old;
            _stepAt = now;
            _stepDirection = Order(_view) >= Order(old) ? 1 : -1;
            _indicator.SetTarget(_m.StepIndex);
            if (ReduceMotion || old.Chapter != _view.Chapter) _indicator.Snap(_m.StepIndex);
            _focus = _showFocus ? TutorialGeometry.MoveFocus(FocusOrder(), TutorialHit.None, 1) : TutorialHit.None;
            _doneAlpha.Snap(_view.Done && _view.Phase == TutorialPhase.Step ? 1 : 0);
            _donePop.Snap(1);
            ResetStage(now);
        }
        if (_focus.Control != TutorialControl.None && !Usable(_focus)) _focus = TutorialHit.None;
        SyncNotch(now);
    }

    private TutorialView Build() => TutorialText.Build(_m, _samples.Strings, _pro, _samples.LocalNow);

    private static int Order(TutorialView v) => (int)v.Chapter * 1000 + (v.Phase == TutorialPhase.Step ? v.StepIndex : 500)
        + (v.Phase == TutorialPhase.Finished ? 999 : 0);

    /// <summary>Al entrar en un paso: el ratón «fuera» del notch de demostración, los plazos a cero, el armado y el pomodoro
    /// como en su demostración inicial.</summary>
    private void ResetStage(TimeSpan now)
    {
        _expandAt = _collapseAt = _armSince = null;
        _held = PermissionVerdict.None;
        _dragging = _dragOver = false;
        _arrowHover = _arrowHeld = PageArrow.None;
        _focusLeft = TutorialSamples.Length(_m.Demo.FocusPhase);
        if (_m.Demo.FocusRun != FocusRun.Ready) _focusLeft -= TimeSpan.FromMinutes(6.5);
        _focusSince = now;
        _focusState = (_m.Demo.FocusRun, _m.Demo.FocusPhase);
        _focusKey = null;
        _split.Snap(_m.Demo.Lyrics ? 1 : 0);
        _pages.Jump(_m.Demo.Page, _m.Demo.PageIndex);
        _shownPage = _m.Demo.Page;
        // Si el ratón ya está encima del notch, cuenta como fuera hasta que se mueva, y entonces no lo abre: tiene que salir y
        // volver a entrar (el paso empieza con su notch como en el guion).
        _overNotch = false;
        _hoverArmed = _pointer is not { } at || !OverNotch(at);
    }

    // ---------------------------------------------------------------- escenario

    private bool StageHover(Point p, TimeSpan now)
    {
        bool changed = false;
        var d = _m.Demo;
        if (d.OnTray || !DemoVisible || _m.Phase != TutorialPhase.Step)
        {
            if (_overNotch) { _overNotch = false; changed = true; }
            changed |= TrayHover(p);
            return changed;
        }
        bool over = OverNotch(p);
        // Fuera del notch se rearma siempre, también si nunca llegó a contar como dentro (el notch encogió bajo un cursor
        // quieto, o el paso empezó con el cursor encima de lo que ya no es notch).
        if (!over) _hoverArmed = true;
        if (over != _overNotch)
        {
            _overNotch = over;
            changed = true;
            if (over)
            {
                _collapseAt = null;
                if (d.Pointer == TutorialPointer.Away && _hoverArmed && _motion.Interactive)
                {
                    Act(new TutorialInput(TutorialAct.HoverNotch), now);
                    if (_m.Demo.Mode is not (NotchMode.Permission or NotchMode.Reminder)) _expandAt = now + NotchTiming.HoverExpand;
                }
            }
            else
            {
                _expandAt = null;
                if (_m.Demo.Pointer != TutorialPointer.Away) _collapseAt = now + NotchTiming.CollapseGrace;
                if (_arrowHover != PageArrow.None) _arrowHover = PageArrow.None;
                if (_m.Demo.ChartAt >= 0) Act(new TutorialInput(TutorialAct.ChartHover, -1), now);
            }
        }
        if (!_overNotch || _m.Demo.Mode != NotchMode.Expanded) return changed;
        var local = Local(p);
        var arrow = NotchGeometry.PageArrowAt(local, DemoPill, DemoScale, TutorialDemo.Pages.Count);
        if (arrow != _arrowHover) { _arrowHover = arrow; changed = true; }
        if (_m.Demo.Page == NotchPage.Usage) changed |= ChartHover(local, now);
        return changed;
    }

    private bool TrayHover(Point p)
    {
        var d = _m.Demo;
        var stage = Stage;
        int menu = -1, settings = -1;
        bool icon = d.OnTray && TutorialGeometry.TrayIcon(stage, _scale).Contains(p);
        if (d.Tray is TutorialTray.Menu or TutorialTray.Monitor)
            foreach (var item in Enum.GetValues<TutorialMenuItem>())
                if (TutorialGeometry.MenuItemRect(stage, _scale, item).Contains(p)) menu = (int)item;
        if (d.Tray == TutorialTray.Settings)
            foreach (var row in Enum.GetValues<TutorialSettingsRow>())
                if (TutorialGeometry.SettingsRowRect(stage, _scale, row).Contains(p)) settings = (int)row;
        if (menu == _menuHover && settings == _settingsHover && icon == _iconHover) return false;
        (_menuHover, _settingsHover, _iconHover) = (menu, settings, icon);
        return true;
    }

    /// <summary>La raya del hover de la gráfica: la casilla bajo el ratón, en milésimas del ancho (como la del notch).</summary>
    private bool ChartHover(Point local, TimeSpan now)
    {
        var d = _m.Demo;
        var r = UsageLayout.Compute(DemoInner(d), DemoScale);
        var (_, chart) = _samples.Usage(d.UsageMode, d.UsageFocus);
        var hit = UsageLayout.HitTest(new PointF(local.X + 0.5f, local.Y + 0.5f), r, chart.Series.Count, true, false,
            UsageLayout.LegendSlotsFor(chart.Mode, chart.Series.Count));
        int at = -1;
        if (hit.Control == UsageControl.Plot && r.Plot.Width > 1f && chart.Count > 1)
        {
            float k = Math.Clamp((local.X + 0.5f - r.Plot.Left) / r.Plot.Width, 0f, 1f);
            int bucket = (int)MathF.Round(k * (chart.Count - 1));
            at = (int)MathF.Round(bucket / (float)(chart.Count - 1) * TutorialDemo.ChartSpan);
        }
        if (at == d.ChartAt) return false;
        return Act(new TutorialInput(TutorialAct.ChartHover, at), now);
    }

    private void StageClick(Point p, PermissionVerdict held, PageArrow arrowHeld, TimeSpan now)
    {
        var d = _m.Demo;
        var stage = Stage;
        if (d.OnTray)
        {
            if (TutorialGeometry.TrayIcon(stage, _scale).Contains(p)) { Act(new TutorialInput(TutorialAct.TrayLeft), now); return; }
            if (d.Tray is TutorialTray.Menu or TutorialTray.Monitor)
            {
                foreach (var item in Enum.GetValues<TutorialMenuItem>())
                {
                    if (!TutorialGeometry.MenuItemRect(stage, _scale, item).Contains(p)) continue;
                    var act = item switch
                    {
                        TutorialMenuItem.Monitor => TutorialAct.MenuMonitor,
                        TutorialMenuItem.Settings => TutorialAct.MenuSettings,
                        TutorialMenuItem.Tutorial => TutorialAct.MenuTutorial,
                        _ => TutorialAct.None,
                    };
                    Act(new TutorialInput(act), now);
                    return;
                }
            }
            if (d.Tray == TutorialTray.Settings
                && TutorialGeometry.SettingsRowRect(stage, _scale, TutorialSettingsRow.Tutorial).Contains(p))
                Act(new TutorialInput(TutorialAct.MenuTutorial), now);
            return;
        }
        if (_m.Phase != TutorialPhase.Step || !OverNotch(p)) return;
        var local = Local(p);
        var pill = DemoPill;
        float s = DemoScale;
        switch (d.Mode)
        {
            case NotchMode.Permission:
            case NotchMode.Reminder:
            {
                if (d.Answer != TutorialAnswer.None) return;
                var over = ButtonAt(p);
                if (over == PermissionVerdict.None)
                {
                    // Fuera de los botones (y sin haber pulsado en uno): el Permiso trae su terminal.
                    if (held == PermissionVerdict.None && d.Mode == NotchMode.Permission)
                        Act(new TutorialInput(TutorialAct.ClickPermissionText), now);
                    return;
                }
                if (over != held || !Armed) return;
                bool allow = over == PermissionVerdict.Allow;
                Act(new TutorialInput(d.Mode == NotchMode.Permission
                    ? allow ? TutorialAct.PermissionAllow : TutorialAct.PermissionDeny
                    : allow ? TutorialAct.ReminderDone : TutorialAct.ReminderSnooze), now);
                return;
            }
            case NotchMode.Expanded:
                ExpandedClick(local, pill, s, arrowHeld, now);
                return;
            default:
                Act(new TutorialInput(TutorialAct.ExpandNotch), now);
                return;
        }
    }

    private void ExpandedClick(Point local, RectangleF pill, float s, PageArrow arrowHeld, TimeSpan now)
    {
        var d = _m.Demo;
        int n = TutorialDemo.Pages.Count;
        var arrow = NotchGeometry.PageArrowAt(local, pill, s, n);
        if (arrow != PageArrow.None)
        {
            if (arrow == arrowHeld && PageArrows.Target(d.PageIndex, n, arrow) >= 0)
            {
                _arrows.Press(arrow);
                Act(new TutorialInput(TutorialAct.ArrowPage, (int)arrow), now);
            }
            return;
        }
        int dot = NotchGeometry.PageDotAt(local, pill, s, n);
        if (dot >= 0)
        {
            Act(new TutorialInput(TutorialAct.DotPage, dot), now);
            return;
        }
        var inner = DemoInner(d);
        var pt = new PointF(local.X + 0.5f, local.Y + 0.5f);
        switch (d.Page)
        {
            case NotchPage.Music:
            {
                var hit = MusicLayout.HitTest(pt, inner, s, _split.Value >= 0.999);
                var act = hit.Control switch
                {
                    MusicControl.PlayPause => TutorialAct.PlayPause,
                    MusicControl.Next => TutorialAct.NextTrack,
                    MusicControl.Previous => TutorialAct.PreviousTrack,
                    MusicControl.Lyrics => TutorialAct.ToggleLyrics,
                    _ => TutorialAct.None,
                };
                Act(new TutorialInput(act), now);
                return;
            }
            case NotchPage.Tasks:
            {
                var items = _samples.Tasks(d.TasksDone);
                var hit = TasksLayout.HitTest(pt, inner, s, items.Count, 0, d.Drafting);
                if (hit.Control == TaskControl.Add) Act(new TutorialInput(TutorialAct.AddTask), now);
                else if (hit.Control == TaskControl.Check)
                    Act(new TutorialInput(TutorialAct.CheckTask, _samples.SampleAt(hit.Index, d.TasksDone)), now);
                return;
            }
            case NotchPage.Shelf:
            {
                var hit = ShelfLayout.HitTest(pt, inner, s, d.ShelfFile ? 1 : 0, 0f);
                if (hit.Control == ShelfControl.Card) Act(new TutorialInput(TutorialAct.OpenShelfItem), now);
                return;
            }
            case NotchPage.Usage:
            {
                var r = UsageLayout.Compute(inner, s);
                var (_, chart) = _samples.Usage(d.UsageMode, d.UsageFocus);
                var hit = UsageLayout.HitTest(pt, r, chart.Series.Count, true, false,
                    UsageLayout.LegendSlotsFor(chart.Mode, chart.Series.Count));
                if (hit.Control == UsageControl.Segment) Act(new TutorialInput(TutorialAct.UsageMode, hit.Index), now);
                else if (hit.Control == UsageControl.Tile) Act(new TutorialInput(TutorialAct.UsageTile, hit.Index), now);
                return;
            }
            case NotchPage.Focus:
            {
                var hit = FocusLayout.HitTest(pt, FocusLayout.Compute(inner, s), locked: false);
                var act = hit.Control switch
                {
                    FocusControl.Primary => TutorialAct.FocusPrimary,
                    FocusControl.Skip => TutorialAct.FocusSkip,
                    FocusControl.Reset => TutorialAct.FocusReset,
                    FocusControl.Task => TutorialAct.FocusTask,
                    _ => TutorialAct.None,
                };
                Act(new TutorialInput(act), now);
                return;
            }
        }
    }

    // ---------------------------------------------------------------- notch de demostración

    private Point Local(Point p)
    {
        var stage = Stage;
        return new Point(p.X - (int)MathF.Round(stage.Left), p.Y - (int)MathF.Round(stage.Top));
    }

    private Size StageSurface => TutorialGeometry.StageSurface(Stage);

    /// <summary>La pastilla del notch de demostración, en coordenadas del escenario.</summary>
    private RectangleF DemoPill => NotchGeometry.PillRect(_notch.Width, _notch.Height, StageSurface);

    private RectangleF DemoInner(TutorialDemo d) => NotchContentPainter.PageInner(DemoPill, DemoScale, WithAlert(d), true);

    private bool OverNotch(Point p)
    {
        if (!Stage.Contains(p) || _notch.IsGone) return false;
        return NotchGeometry.HitTest(Local(p), _notch.Width, _notch.Height, DemoScale, StageSurface);
    }

    private PermissionVerdict ButtonAt(Point p)
    {
        var (deny, allow) = NotchGeometry.PermissionButtons(DemoPill, DemoScale);
        var local = Local(p);
        if (allow.Contains(local)) return PermissionVerdict.Allow;
        if (deny.Contains(local)) return PermissionVerdict.Deny;
        return PermissionVerdict.None;
    }

    private float ArmProgress(TimeSpan now) => ArmProgress(_armSince, now);

    /// <summary>Armado de los botones: de 0 a 1 en <see cref="NotchTiming.PermissionArm"/> desde que el aviso se ve entero y
    /// quieto (<paramref name="since"/>; null = aún no). PURO.</summary>
    internal static float ArmProgress(TimeSpan? since, TimeSpan now) => since is not { } at ? 0f
        : (float)Math.Clamp((now - at).TotalMilliseconds / NotchTiming.PermissionArm.TotalMilliseconds, 0, 1);

    private bool Armed => ArmProgress(_now) >= 1f;

    private static bool WithAlert(TutorialDemo d) => d.Face is TutorialFace.Done or TutorialFace.QuotaHigh;

    /// <summary>El objetivo del notch de demostración según la demostración: modo, tamaño (con las medidas del painter del
    /// notch), maquetación y aviso; y la página que se ve (con su cruce).</summary>
    private void SyncNotch(TimeSpan now)
    {
        var d = _m.Demo;
        float s = DemoScale;
        if (d.Page != _shownPage)
        {
            // La dirección del cruce: la de la rueda (también al dar la vuelta) o la del salto.
            int n = TutorialDemo.Pages.Count, from = IndexOf(_shownPage);
            int dir = d.PageIndex == (from + 1) % n ? 1 : d.PageIndex == (from + n - 1) % n ? -1 : Math.Sign(d.PageIndex - from);
            if (_m.Demo.Mode == NotchMode.Expanded && _notch.ShownMode == NotchMode.Expanded) _pages.Go(d.Page, d.PageIndex, dir, now);
            else _pages.Jump(d.Page, d.PageIndex);
            _shownPage = d.Page;
        }
        if (d.Lyrics != _split.Target >= 0.5)
        {
            _split.SetTarget(d.Lyrics ? 1 : 0);
            if (ReduceMotion) _split.Snap(_split.Target);
        }
        UpdateFocusClock(now);
        var mode = d.Mode;
        NotchMode? content = null;
        if (mode == NotchMode.Peek && d.Face == TutorialFace.CompactLyrics)
        {
            // Con la letra compacta, la vista previa sigue enseñando el reposo: solo crece el anillo de la cuota (como el notch).
            content = NotchMode.Idle;
        }
        var size = TargetSize(d, mode, s);
        int layout = mode == NotchMode.Expanded ? 0 : (int)d.Face + 1 + (FocusFace(d) ? 100 : 0);
        _notch.SetTarget(mode, AlertFor(d, mode), size, s, now, layout, content);
    }

    private SizeF TargetSize(TutorialDemo d, NotchMode mode, float s)
    {
        var n = _painter.Notch;
        switch (mode)
        {
            case NotchMode.Expanded:
                return NotchLayout.PageSize(d.Page,
                    new PageRows(_samples.Sessions.Count, TutorialDemo.TaskCount + (d.Drafting ? 1 : 0), d.ShelfFile ? 1 : 0),
                    WithAlert(d), withDots: true, s);
            case NotchMode.Permission:
                return NotchLayout.PermissionButtonsSize(s, Math.Max(1, n.PermissionDetailLines(TutorialSamples.PermissionDetail, s)));
            case NotchMode.Working:
                return NotchLayout.TargetSize(NotchMode.Working, s, n.MeasureWorkingExtra(_samples.Sessions, s));
            case NotchMode.Idle or NotchMode.Peek when d.Face == TutorialFace.CompactLyrics:
            {
                var slots = new CompactLyricsSlots(Art: false, Quota: true);
                return CompactLyrics.Size(CompactLyrics.Extra(n.MeasureCompactLine(TutorialSamples.Lyrics[TutorialSamples.LyricNow].Text, s),
                    slots), s);
            }
            case NotchMode.Idle when FocusFace(d):
                return Services.Notch.Features.FocusFace.IdleSize(s);
            case NotchMode.Idle when d.Face == TutorialFace.Clock:
            {
                var (time, date) = ClockNow();
                int layout = IdleScene.LayoutClock | IdleScene.LayoutMascot;
                return IdleScene.SizeFor(NotchMode.Idle, layout, n.MeasureClock(s, time, date) / s, s);
            }
            default:
                return NotchLayout.TargetSize(mode, s, 0f);
        }
    }

    private NotchAlert? AlertFor(TutorialDemo d, NotchMode mode) => mode switch
    {
        NotchMode.Permission => _samples.Permission,
        NotchMode.Reminder => _samples.Reminder,
        NotchMode.Completed => _samples.Done,
        NotchMode.QuotaAlert => _samples.QuotaAlert,
        NotchMode.Expanded when d.Face == TutorialFace.Done => _samples.Done,
        NotchMode.Expanded when d.Face == TutorialFace.QuotaHigh => _samples.QuotaAlert,
        _ => null,
    };

    /// <summary>¿El notch cerrado enseña el mini anillo del pomodoro? (en marcha o en pausa, como el de verdad).</summary>
    private static bool FocusFace(TutorialDemo d) => d.FocusRun != FocusRun.Ready && d.Face is TutorialFace.Idle;

    private (string Time, string Date)? _clockText;

    /// <summary>Hora y fecha del reloj del ejemplo (fijas: se formatean una vez, no en cada frame).</summary>
    private (string Time, string Date) ClockNow() => _clockText ??=
        ClockText.Format(_samples.LocalNow, _samples.Strings.Culture, ClockText.Prefers24h(System.Globalization.CultureInfo.CurrentCulture));

    /// <summary>El frame del notch de demostración, como lo monta <c>NotchForm.RenderFrame</c>, con los datos de ejemplo.</summary>
    private NotchFrame? NotchFrameNow()
    {
        if (!_started) return null;
        var d = _m.Demo;
        var shown = _notch.ShownMode;
        int layout = _notch.ShownLayout;
        bool expanded = shown == NotchMode.Expanded;
        var face = expanded ? d.Face : layout is > 0 and < 100 ? (TutorialFace)(layout - 1)
            : layout >= 100 ? (TutorialFace)(layout - 101) : d.Face;
        var sessions = face is TutorialFace.Working or TutorialFace.Permission
            ? expanded ? _samples.SessionsFrom(d.SessionOffset) : _samples.Sessions
            : expanded && d.SessionOffset > 0 ? _samples.Quiet.Skip(d.SessionOffset).ToArray() : _samples.Quiet;
        var quota = face == TutorialFace.QuotaHigh ? _samples.HighQuota
            : expanded && d.Page == NotchPage.Usage ? _samples.UsageQuota : _samples.Quota;
        float arm = FaceWithButtons ? ArmProgress(_now) : 1f;
        var answer = d.Answer switch
        {
            TutorialAnswer.Allow => PermissionVerdict.Allow,
            TutorialAnswer.Deny => PermissionVerdict.Deny,
            _ => PermissionVerdict.None,
        };
        NotchPagesView? pages = expanded
            ? new NotchPagesView(TutorialDemo.Pages, _pages.Current, _pages.Previous, _pages.Blend, _pages.CurrentShift,
                _pages.PreviousShift, _pages.IndicatorPos, _arrows.View)
            : null;
        bool peek = d.Pointer == TutorialPointer.Peek && !expanded;
        MusicView? music = null;
        if (expanded) music = MusicFrame(d);
        else if (face == TutorialFace.CompactLyrics && shown == NotchMode.Idle)
        {
            var track = _samples.Track(d.Track, d.Playing);
            float e = peek ? 1f : 0f;
            music = new MusicView(track, track.Position, IdleFace: true,
                Compact: new CompactLyricsView(TutorialSamples.Lyrics[TutorialSamples.LyricNow].Text, Art: false, Quota: true,
                    QuotaEmphasis: e, QuotaReveal: e));
        }
        IdleView? idle = null;
        if (face == TutorialFace.Clock && shown is NotchMode.Idle or NotchMode.Peek)
        {
            var (time, date) = ClockNow();
            idle = new IdleView(shown == NotchMode.Idle, time, date, new MascotLook(MascotState.Awake, Point.Empty, false), _notch.ContentAlpha);
        }
        ShelfView? shelf = expanded
            ? new ShelfView(d.ShelfFile ? new[] { _samples.File } : Array.Empty<ShelfItem>(), d.ShelfFile ? new[] { true } : Array.Empty<bool>(),
                Array.Empty<Guid>(), DropTarget: _dragOver, Details: d.ShelfFile ? new[] { _samples.FileDetail } : null)
            : null;
        TasksView? tasks = expanded
            ? new TasksView(_samples.Tasks(d.TasksDone), _samples.LocalNow, true, d.Drafting ? _samples.Draft : null)
            : null;
        return new NotchFrame(_notch.Width, _notch.Height, DemoScale, _notch.IsGone ? NotchMode.Hidden : shown, _notch.ShownAlert,
            sessions, quota, SpinnerPhase(), _notch.ContentAlpha, _notch.PillAlpha, _notch.ShadowAlpha, _notch.HaloAlpha, _notch.Squash,
            _notch.ContentOffsetY, ReduceMotion, arm, answer, _held, pages, music, shelf, tasks, idle,
            Usage: expanded ? UsageFrame(d) : null, Focus: FocusFrame(d, shown), Clock: _samples.Clock);
    }

    private float SpinnerPhase() => (float)(_now.TotalMilliseconds / NotchMotion.SpinnerPeriod.TotalMilliseconds % 1.0);

    private MusicView MusicFrame(TutorialDemo d)
    {
        var track = _samples.Track(d.Track, d.Playing);
        return new MusicView(track, track.Position, false, LyricsState.Found, TutorialSamples.Lyrics, TutorialSamples.LyricNow, -1, 1f,
            (float)Math.Clamp(_split.Value, 0, 1), LyricsEnabled: true,
            Equalizer: EqualizerBars.Heights(0, track.Playing, still: true), ReduceMotion: ReduceMotion);
    }

    private UsageView UsageFrame(TutorialDemo d)
    {
        var (numbers, chart) = _samples.Usage(d.UsageMode, d.UsageFocus);
        int bucket = d.ChartAt < 0 || chart.Count < 1 ? -1
            : (int)MathF.Round(d.ChartAt / (float)TutorialDemo.ChartSpan * (chart.Count - 1));
        return new UsageView(numbers, chart, d.UsageMode, chart.Range, Interactive: true, Locked: false,
            Bucket: bucket, TipIndex: bucket, TipAlpha: bucket >= 0 ? 1f : 0f);
    }

    // ---- pomodoro

    private bool FocusTicking => _m.Demo.FocusRun == FocusRun.Running && DemoVisible && _m.Phase == TutorialPhase.Step
        && (_m.Demo.Page == NotchPage.Focus || _m.Demo.Pointer != TutorialPointer.Expanded);

    private TimeSpan FocusLeft(TimeSpan now)
    {
        if (_m.Demo.FocusRun != FocusRun.Running) return _focusLeft;
        var left = _focusLeft - (now - _focusSince);
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    private (FocusRun Run, FocusPhase Phase) _focusState = (FocusRun.Ready, FocusPhase.Focus);

    /// <summary>Lo que queda del pomodoro de ejemplo sigue al botón: empezar corre desde lo que quedaba; pausar lo congela;
    /// saltar o reiniciar vuelven a la fase entera.</summary>
    private void UpdateFocusClock(TimeSpan now)
    {
        var d = _m.Demo;
        var (run, phase) = _focusState;
        if (run == d.FocusRun && phase == d.FocusPhase) return;
        if (phase != d.FocusPhase || d.FocusRun == FocusRun.Ready) _focusLeft = TutorialSamples.Length(d.FocusPhase);
        else if (run == FocusRun.Running) _focusLeft = FocusLeft(now);
        if (d.FocusRun == FocusRun.Running) _focusSince = now;
        _focusState = (d.FocusRun, d.FocusPhase);
        _focusKey = null;
    }

    private FocusView? FocusFrame(TutorialDemo d, NotchMode shown)
    {
        bool page = shown == NotchMode.Expanded && (d.Page == NotchPage.Focus || _pages.Previous == NotchPage.Focus);
        bool face = shown is NotchMode.Idle or NotchMode.Peek or NotchMode.Working or NotchMode.Minimal && d.FocusRun != FocusRun.Ready;
        if (!page && !face) return null;
        var left = FocusLeft(_now);
        var key = (d.FocusRun, d.FocusPhase, d.FocusTask, FocusClock.ShownSeconds(left), face);
        if (_focusKey != key || _focusView is null)
        {
            _focusKey = key;
            _focusView = _samples.Focus(d.FocusRun, d.FocusPhase, d.FocusTask, left, face);
        }
        return _focusView;
    }

    private void UpdateArrows()
    {
        bool visible = DemoVisible && _overNotch && _m.Demo.Mode == NotchMode.Expanded;
        _arrows.Update(visible, _m.Demo.PageIndex, TutorialDemo.Pages.Count, _arrowHover);
    }

    private static int IndexOf(NotchPage page)
    {
        for (int i = 0; i < TutorialDemo.Pages.Count; i++) if (TutorialDemo.Pages[i] == page) return i;
        return 0;
    }
}
