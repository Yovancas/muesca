using System.Globalization;
using ClaudeBarWin.Config;
using ClaudeBarWin.Models;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Motion;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Notch.Features;
using ClaudeBarWin.Services.Onboarding;
using ClaudeBarWin.UI.Notch;

namespace ClaudeBarWin.UI.Onboarding;

/// <summary>Lo que necesita el asistente de fuera: aplicar lo elegido (<see cref="SetupApply"/> en memoria) y, para el notch
/// de reposo al aterrizar, la cuota y las sesiones de ahora.</summary>
public sealed record OnboardingServices(Func<SetupAnswers, SetupOutcome> Apply, Func<RealUsage?> Quota,
    Func<IReadOnlyList<SessionState>> Sessions);

/// <summary>Dónde aterriza el asistente: el notch de verdad que va a aparecer en su monitor (modo, tamaño en px físicos de SU
/// escala y su centro en la superficie del asistente). <c>null</c> = no hay notch en este monitor: se mete arriba.</summary>
public sealed record OnboardingLanding(NotchMode Mode, SizeF Size, float Scale, float CenterX);

/// <summary>Cómo acabó: lo aplicado (con el resultado de <see cref="SetupApply"/>) o nada (cerrado sin cambios).</summary>
public sealed record OnboardingResult(SetupAnswers? Answers, SetupOutcome? Outcome)
{
    public static readonly OnboardingResult Unchanged = new(null, null);

    public bool Applied => Outcome is { Status: SetupStatus.Applied };

    /// <summary>Terminó con «Empezar» y «Ver el tutorial al terminar» marcado: la bandeja abre el tutorial al irse.</summary>
    public bool Tutorial { get; init; }
}

/// <summary>
/// El asistente de bienvenida en movimiento, sin Win32: el estado (<see cref="OnboardingWizard"/>), la coreografía
/// (<see cref="OnboardingMotion"/>), el cruce de páginas, los realces, los interruptores, el foco del teclado y la vista
/// previa en vivo (un <see cref="NotchAnimator"/> de verdad con las escenas de <see cref="ProfilePreview"/>). La ventana le
/// pasa el instante, el ratón y las teclas, y le pide cada frame (<see cref="Frame"/>) para el painter; los tests de
/// capturas lo conducen igual, sin ventana. Los textos se rehacen solo cuando algo cambia (<see cref="OnboardingText"/>).
/// </summary>
public sealed class OnboardingScene
{
    private static readonly TimeSpan PageCross = PageTransition.Duration;
    private static readonly SpringPreset IndicatorPreset = Motion.SpringAdjust with { EpsX = 0.02, EpsV = 0.2 };
    private static readonly RealUsage PreviewQuota = new()
    {
        FiveHour = new UsageWindow(42, null),
        SevenDay = new UsageWindow(18, null),
    };

    private readonly OnboardingWizard _wizard;
    private readonly OnboardingPainter _painter;
    private readonly OnboardingServices _services;
    private readonly Func<Strings, IReadOnlyDictionary<string, string>> _monitorLabels;
    private readonly Func<DateTime> _localNow;
    private readonly OnboardingMotion _motion = new();
    private readonly NotchAnimator _preview = new();
    private readonly SpringValue _indicator = new(0, IndicatorPreset);
    private readonly SpringValue _hoverIntensity = new(0, Motion.HoverIn);
    private readonly SpringValue _overlay = new(0, Motion.Rapido);
    private readonly SpringValue[] _glow = Enumerable.Range(0, SetupProfiles.All.Count).Select(_ => new SpringValue(0, Motion.Rapido)).ToArray();
    private readonly SpringValue[] _knobs = Enumerable.Range(0, OnboardingPainter.KnobCount).Select(_ => new SpringValue(0, Motion.Rapido)).ToArray();

    private Strings _strings;
    private string _stringsCode;
    private IReadOnlyDictionary<string, string> _labels;
    private OnboardingView _view;
    private OnboardingView? _previous;
    private OnboardingOverlayView? _lastOverlay;
    private TimeSpan _pageAt = TimeSpan.MinValue, _last, _now;
    private int _pageDirection = 1;
    private float _scale = 1f, _centerX;
    private OnboardingHit _hover, _pressed, _focus;
    private bool _showFocus, _started, _leaving, _handedOff;
    private OnboardingLanding? _landing;
    // Lo que se intentó aplicar la última vez: «Reintentar» tras un error vuelve a por ESO (un «Saltar» fallido reintenta
    // como «Saltar», sin instalar las sesiones en vivo de las páginas).
    private SetupAnswers? _lastAttempt;
    private bool _lastTutorial;

    // Vista previa: las escenas del perfil a la vista, desde cuándo y cuál se pidió al animador.
    private IReadOnlyList<PreviewScene> _scenes = Array.Empty<PreviewScene>();
    private TimeSpan _scenesAt;
    private int _sceneIndex = -1;
    private SetupProfile? _scenesProfile;

    /// <param name="monitorLabels">Nombre de cada monitor (por nombre GDI) en un idioma, como los de la bandeja.</param>
    /// <param name="localNow">Hora local del reloj de la vista previa (los tests de capturas la fijan).</param>
    public OnboardingScene(OnboardingWizard wizard, Strings strings, OnboardingPainter painter, OnboardingServices services,
        bool reduceMotion, Func<Strings, IReadOnlyDictionary<string, string>>? monitorLabels = null, Func<DateTime>? localNow = null)
    {
        _wizard = wizard ?? throw new ArgumentNullException(nameof(wizard));
        _painter = painter ?? throw new ArgumentNullException(nameof(painter));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _monitorLabels = monitorLabels ?? (_ => new Dictionary<string, string>());
        _localNow = localNow ?? (() => DateTime.Now);
        ReduceMotion = reduceMotion;
        _motion.ReduceMotion = reduceMotion;
        _preview.ReduceMotion = reduceMotion;
        _stringsCode = _wizard.Language;
        _strings = _wizard.Language == "system" ? strings ?? throw new ArgumentNullException(nameof(strings)) : StringsFor(_wizard.Language);
        _painter.Strings = _strings;
        _labels = _monitorLabels(_strings);
        _view = Build();
        SnapKnobs();
    }

    public OnboardingWizard Wizard => _wizard;
    public OnboardingMotion Choreography => _motion;
    public OnboardingView View => _view;
    public bool ReduceMotion { get; }

    /// <summary>Los textos del idioma elegido en el asistente (cambia en vivo al elegir otro).</summary>
    public Strings Strings => _strings;

    /// <summary>Dónde aterriza al terminar (la ventana lo calcula con el monitor y el DPI). Sin él, se mete arriba.</summary>
    public Func<OnboardingLanding?>? Landing { get; set; }

    /// <summary>Cómo acabó (<c>null</c> mientras sigue abierto).</summary>
    public OnboardingResult? Result { get; private set; }

    public float Scale => _scale;

    /// <summary>Tamaño del asistente abierto en px físicos.</summary>
    public SizeF WizardSize => new(OnboardingGeometry.Width * _scale, OnboardingGeometry.Height * _scale);

    /// <summary>La pastilla tal como está ahora (la que se pinta y donde se acierta el clic).</summary>
    public RectangleF Pill => OnboardingGeometry.PillRect(_motion.Width, _motion.Height, _centerX);

    public bool IsGone => _motion.IsGone;

    /// <summary>¿Termina en el notch de reposo de su monitor? (al salir; sin sitio, se mete arriba).</summary>
    public bool Landed => _leaving && _landing is not null;

    /// <summary>Escala y superficie (al empezar y con cada cambio de DPI o de pantalla).</summary>
    public void Place(float scale, Size surface)
    {
        bool changed = Math.Abs(scale - _scale) > 0.0001f;
        _scale = scale > 0 ? scale : 1f;
        if (!_leaving) _centerX = surface.Width / 2f;
        if (changed && _started) _motion.Resize(WizardSize, _scale);
    }

    /// <summary>Empieza la entrada (bajar y desplegarse).</summary>
    public void Start(TimeSpan now)
    {
        _started = true;
        _last = _now = now;
        _motion.Start(NotchLayout.TargetSize(NotchMode.Idle, _scale, 0f), WizardSize, _scale, now);
    }

    /// <summary>¿Hay algo en movimiento? (si no, el bucle de la ventana se para).</summary>
    public bool IsAnimating => _motion.IsAnimating || !_indicator.IsSettled || !_hoverIntensity.IsSettled || !_overlay.IsSettled
        || _glow.Any(g => !g.IsSettled) || _knobs.Any(k => !k.IsSettled) || PageCrossing(_now)
        || (PreviewVisible && (_preview.IsAnimating || _preview.SpinnerVisible(true)))
        || (_leaving && !_handedOff && _landing is null);

    /// <summary>Hasta el siguiente cambio de escena de la vista previa (un temporizador de un disparo); null si no se ve.</summary>
    public TimeSpan? NextWakeIn(TimeSpan now) =>
        PreviewVisible && !_leaving ? ProfilePreview.NextChangeIn(_scenes, now - _scenesAt) : null;

    /// <summary>Avanza todo hasta <paramref name="now"/>.</summary>
    public void Step(TimeSpan now)
    {
        _now = now;
        double dt = Math.Clamp((now - _last).TotalSeconds, 0.0, 1.0 / 20.0);
        _last = now;
        _motion.Step(now);
        if (dt > 0)
        {
            _indicator.Advance(dt);
            _hoverIntensity.Advance(dt);
            _overlay.Advance(dt);
            foreach (var g in _glow) g.Advance(dt);
            foreach (var k in _knobs) k.Advance(dt);
        }
        if (ReduceMotion)
        {
            _indicator.Snap(_indicator.Target);
            _overlay.Snap(_overlay.Target);
            foreach (var k in _knobs) k.Snap(k.Target);
        }
        if (_overlay.Value <= 0.001 && _wizard.Overlay == OnboardingOverlay.None) _lastOverlay = null;
        StepPreview(now);
    }

    /// <summary>¿Toca hacer el relevo? (el notch de reposo ya está entero, o se va sin notch). True UNA vez.</summary>
    public bool TakeHandoff()
    {
        if (!_leaving || _handedOff) return false;
        if (_landing is null || _motion.TakeHandoff())
        {
            _handedOff = true;
            return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- frame

    /// <summary>Lo que se pinta en este instante (llamar después de <see cref="Step"/>).</summary>
    public OnboardingFrame Frame()
    {
        var view = _view;
        if (view.Overlay is null && _lastOverlay is not null && _overlay.Value > 0.001) view = view with { Overlay = _lastOverlay };
        float blend = PageBlend(_now);
        float shift = MathF.Round(_pageDirection * PageTransition.Shift * (1f - blend));
        float prevShift = MathF.Round(-_pageDirection * PageTransition.Shift * blend);
        NotchFrame? landed = null;
        if (_landing is { } l && _motion.Stage is OnboardingStage.Landed or OnboardingStage.Gone)
            landed = LandedFrame(l);
        return new OnboardingFrame(_motion.Width, _motion.Height, _scale, _centerX, view, Shape,
            _motion.Squash, _motion.ContentAlpha, _motion.ContentOffsetY, _motion.LogoAlpha,
            blend < 1f ? _previous : null, blend, shift, prevShift, (float)_indicator.Value,
            _motion.Interactive ? _hover : OnboardingHit.None, (float)Math.Clamp(_hoverIntensity.Value, 0, 1),
            _pressed, _focus, _showFocus,
            _glow.Select(g => (float)g.Value).ToArray(), _knobs.Select(k => (float)k.Value).ToArray(),
            (float)Math.Clamp(_overlay.Value, 0, 1), PreviewFrame(), landed, _motion.ShadowAlpha);
    }

    /// <summary>Alfa de toda la ventana (va como alfa constante de <c>UpdateLayeredWindow</c>).</summary>
    public float WindowAlpha => _motion.PillAlpha;

    private OnboardingShape Shape => new(_wizard.Pages.Count, _wizard.AskMonitor, _wizard.Overlay);

    private bool PageCrossing(TimeSpan now) => _previous is not null && PageBlend(now) < 1f;

    private float PageBlend(TimeSpan now) => ReduceMotion || _pageAt == TimeSpan.MinValue ? 1f
        : (float)Math.Clamp((now - _pageAt).TotalMilliseconds / PageCross.TotalMilliseconds, 0, 1);

    // ---------------------------------------------------------------- ratón y teclado

    /// <summary>El ratón se movió a <paramref name="p"/> (superficie). True si cambió lo que se ve.</summary>
    public bool PointerMove(Point p)
    {
        var hit = _motion.Interactive ? OnboardingGeometry.HitTest(p, _wizard.Page, Pill, _scale, Shape) : OnboardingHit.None;
        if (!Usable(hit)) hit = OnboardingHit.None;
        return SetHover(hit);
    }

    public bool PointerLeave()
    {
        _pressed = OnboardingHit.None;
        return SetHover(OnboardingHit.None);
    }

    /// <summary>¿Hay un control usable bajo el ratón? (la ventana pone la mano).</summary>
    public bool OverControl => _hover.Control != OnboardingControl.None;

    public void PointerDown(Point p)
    {
        PointerMove(p);
        _pressed = _hover;
    }

    /// <summary>Soltar: la acción va si se suelta sobre el MISMO control que se pulsó (como un botón de verdad).</summary>
    public void PointerUp(Point p, TimeSpan now)
    {
        PointerMove(p);
        var pressed = _pressed;
        _pressed = OnboardingHit.None;
        if (pressed.Control == OnboardingControl.None || pressed != _hover) return;
        _showFocus = false;
        Activate(pressed, now);
    }

    /// <summary>
    /// Una tecla. Tab / Mayús+Tab mueven el foco; Espacio usa el control con el foco; Enter pulsa el botón con el foco o,
    /// en lo demás, «Siguiente»; Esc es «Saltar» (o cierra el aviso); ← → cambian el selector o el segmento con el foco y
    /// ↑ ↓ eligen otra tarjeta (o mueven el foco). False si no es una tecla del asistente. <paramref name="repeat"/>: la
    /// autorrepetición de una tecla mantenida; la de Enter, Espacio y Esc se traga (si no, medio segundo de Enter recorre
    /// todas las páginas y aplica sin verlas, y Esc mantenido cierra el aviso de error y abre sin querer el de «Saltar»).
    /// </summary>
    public bool Key(Keys key, bool shift, TimeSpan now, bool repeat = false)
    {
        if (repeat && key is Keys.Enter or Keys.Space or Keys.Escape) return true;
        if (!_motion.Interactive || _leaving) return key is Keys.Escape or Keys.Enter or Keys.Tab or Keys.Space;
        switch (key)
        {
            case Keys.Tab:
                _showFocus = true;
                _focus = OnboardingGeometry.MoveFocus(FocusOrder(), _focus, shift ? -1 : 1);
                return true;
            case Keys.Escape:
                if (_wizard.Overlay == OnboardingOverlay.Error) Activate(new(OnboardingControl.OverlaySecondary), now);
                else
                {
                    _wizard.RequestSkip();
                    FocusOverlay();
                    Rebuild(now);
                }
                return true;
            case Keys.Enter:
                if (_wizard.Overlay != OnboardingOverlay.None)
                    Activate(_focus.Control is OnboardingControl.OverlayPrimary or OnboardingControl.OverlaySecondary
                        ? _focus : new OnboardingHit(OnboardingControl.OverlayPrimary), now);
                else Activate(_focus.IsButton && Usable(_focus) ? _focus : new OnboardingHit(OnboardingControl.Next), now);
                return true;
            case Keys.Space:
                if (_focus.Control != OnboardingControl.None && Usable(_focus)) Activate(_focus, now);
                return true;
            case Keys.Left:
            case Keys.Right:
                return Arrow(key == Keys.Right ? 1 : -1, now);
            case Keys.Up:
            case Keys.Down:
            {
                int d = key == Keys.Down ? 1 : -1;
                if (_focus.Control == OnboardingControl.Card || (_wizard.Page == OnboardingPage.Profile && !_showFocus))
                {
                    _wizard.StepProfile(d);
                    _focus = new OnboardingHit(OnboardingControl.Card, (int)_wizard.Profile);
                    _showFocus = true;
                    Rebuild(now);
                }
                else
                {
                    _showFocus = true;
                    _focus = OnboardingGeometry.MoveFocus(FocusOrder(), _focus, d);
                }
                return true;
            }
        }
        return false;
    }

    /// <summary>Alt+F4 o cerrar la ventana: como Esc (pide confirmar; nada se cierra sin querer).</summary>
    public void RequestClose(TimeSpan now)
    {
        if (_leaving || !_motion.Interactive) return;
        if (_wizard.Overlay == OnboardingOverlay.None) _wizard.RequestSkip();
        FocusOverlay();
        Rebuild(now);
    }

    private bool Arrow(int d, TimeSpan now)
    {
        switch (_focus.Control)
        {
            case OnboardingControl.Language: _wizard.StepLanguage(d); break;
            case OnboardingControl.Monitor: _wizard.StepMonitor(d); break;
            case OnboardingControl.Size:
                _wizard.StepSize(d);
                _focus = new(OnboardingControl.Size, SizeIndex());
                break;
            case OnboardingControl.Open:
                _wizard.SetOpenOnClick(d > 0);
                _focus = new(OnboardingControl.Open, _wizard.OpenOnClick ? 1 : 0);
                break;
            case OnboardingControl.Card:
                _wizard.StepProfile(d);
                _focus = new(OnboardingControl.Card, (int)_wizard.Profile);
                break;
            default:
                return false;
        }
        Rebuild(now);
        return true;
    }

    /// <summary>Lo que hace cada control (clic, Espacio o Enter).</summary>
    public void Activate(OnboardingHit hit, TimeSpan now)
    {
        if (_leaving || !Usable(hit)) return;
        var w = _wizard;
        switch (hit.Control)
        {
            case OnboardingControl.Skip:
                w.RequestSkip();
                FocusOverlay();
                break;
            case OnboardingControl.Back: w.Back(); break;
            case OnboardingControl.Next:
                if (w.Next())
                {
                    Leave(w.Answers(), now, w.ShowTutorial);
                    return;
                }
                break;
            case OnboardingControl.Dot: w.GoTo(hit.Index); break;
            case OnboardingControl.Card:
                if (hit.Index >= 0 && hit.Index < SetupProfiles.All.Count) w.SelectProfile(SetupProfiles.All[hit.Index]);
                break;
            case OnboardingControl.Feature: w.ToggleFeature((SetupFeature)hit.Index); break;
            case OnboardingControl.ResetFeatures: w.ResetFeatures(); break;
            case OnboardingControl.Language: w.StepLanguage(1); break;
            case OnboardingControl.LanguagePrev: w.StepLanguage(-1); break;
            case OnboardingControl.Monitor: w.StepMonitor(1); break;
            case OnboardingControl.MonitorPrev: w.StepMonitor(-1); break;
            case OnboardingControl.Size:
                if (hit.Index >= 0 && hit.Index < OnboardingWizard.SizeOptions.Count) w.SetSize(OnboardingWizard.SizeOptions[hit.Index]);
                break;
            case OnboardingControl.Open: w.SetOpenOnClick(hit.Index == 1); break;
            case OnboardingControl.Startup: w.ToggleStartup(); break;
            case OnboardingControl.ResetSettings: w.ResetSettings(); break;
            case OnboardingControl.LiveSessions: w.ToggleLiveSessions(); break;
            case OnboardingControl.Permissions: w.TogglePermissions(); break;
            case OnboardingControl.UsageStats: w.ToggleUsageStats(); break;
            case OnboardingControl.Tutorial: w.ToggleTutorial(); break;
            case OnboardingControl.OverlaySecondary:
                if (w.Overlay == OnboardingOverlay.Error)
                {
                    // Cerrar tras un error: sin cambios (lo que no se aplicó, no se aplicó).
                    w.CancelOverlay();
                    Leave(null, now);
                    return;
                }
                w.CancelOverlay();
                _focus = OnboardingHit.None;
                break;
            case OnboardingControl.OverlayPrimary:
                if (w.Overlay == OnboardingOverlay.Error)
                {
                    w.CancelOverlay();
                    Leave(_lastAttempt ?? w.Answers(), now, _lastTutorial);   // reintentar lo mismo que falló
                    return;
                }
                w.CancelOverlay();
                Leave(w.SkipApplies ? w.SkipAnswers() : null, now);
                return;
        }
        Rebuild(now);
    }

    /// <summary>
    /// Terminar: aplica lo elegido (si hay algo que aplicar) y, si fue bien, se encoge hasta donde aparecerá el notch. Si no
    /// se pudo aplicar, el aviso de error tapa la página y no se cierra.
    /// </summary>
    private void Leave(SetupAnswers? answers, TimeSpan now, bool tutorial = false)
    {
        if (answers is not null)
        {
            _lastAttempt = answers;
            _lastTutorial = tutorial;
            SetupOutcome outcome;
            try { outcome = _services.Apply(answers); }
            catch (Exception ex)
            {
                AppLog.Error("asistente del notch: no se pudo aplicar", ex);
                outcome = SetupOutcome.Fail(SetupStatus.Failed, ex.Message);
            }
            if (outcome.Status != SetupStatus.Applied)
            {
                _wizard.ShowError(outcome.Message);
                FocusOverlay();
                Rebuild(now);
                return;
            }
            Result = new OnboardingResult(answers, outcome) { Tutorial = tutorial };
        }
        else Result = OnboardingResult.Unchanged;

        _leaving = true;
        _hover = _pressed = _focus = OnboardingHit.None;
        _showFocus = false;
        _hoverIntensity.SetTarget(0);
        try { _landing = Landing?.Invoke(); }
        catch (Exception ex)
        {
            AppLog.Warn($"asistente del notch: sin sitio para aterrizar: {ex.Message}");
            _landing = null;
        }
        if (_landing is { } l) _centerX = l.CenterX;
        _motion.Finish(_landing?.Size ?? SizeF.Empty, now);
    }

    private bool SetHover(OnboardingHit hit)
    {
        if (hit == _hover) return false;
        _hover = hit;
        _hoverIntensity.SetPreset(hit.Control == OnboardingControl.None ? Motion.Rapido : Motion.HoverIn);
        _hoverIntensity.SetTarget(hit.Control == OnboardingControl.None ? 0 : 1);
        if (ReduceMotion) _hoverIntensity.Snap(_hoverIntensity.Target);
        for (int i = 0; i < _glow.Length; i++)
        {
            _glow[i].SetTarget(hit.Control == OnboardingControl.Card && hit.Index == i ? 1 : 0);
            if (ReduceMotion) _glow[i].Snap(_glow[i].Target);
        }
        return true;
    }

    // ---------------------------------------------------------------- foco

    /// <summary>¿Se puede usar ahora? (lo deshabilitado se pinta atenuado y no responde).</summary>
    public bool Usable(OnboardingHit hit)
    {
        var w = _wizard;
        if (hit.Control == OnboardingControl.None) return false;
        if (w.Overlay != OnboardingOverlay.None)
            return hit.Control is OnboardingControl.OverlayPrimary or OnboardingControl.OverlaySecondary;
        return hit.Control switch
        {
            OnboardingControl.Back => w.CanGoBack,
            OnboardingControl.Dot => hit.Index <= w.Furthest && hit.Index != w.PageIndex,
            OnboardingControl.Feature => w.FeatureEnabled((SetupFeature)hit.Index),
            OnboardingControl.Monitor or OnboardingControl.MonitorPrev => w.NotchSettingsEnabled && w.AskMonitor,
            OnboardingControl.Size or OnboardingControl.Open => w.NotchSettingsEnabled,
            OnboardingControl.LiveSessions => w.LiveSessionsEnabled,
            OnboardingControl.Permissions => w.PermissionsAvailable,
            OnboardingControl.OverlayPrimary or OnboardingControl.OverlaySecondary => false,
            _ => true,
        };
    }

    private IReadOnlyList<OnboardingHit> FocusOrder() => OnboardingGeometry.FocusOrder(_wizard.Page, Shape, Usable,
        c => c == OnboardingControl.Size ? SizeIndex() : _wizard.OpenOnClick ? 1 : 0);

    private int SizeIndex()
    {
        var sizes = OnboardingWizard.SizeOptions;
        for (int i = 0; i < sizes.Count; i++) if (sizes[i] == _wizard.SizePercent) return i;
        return 0;
    }

    /// <summary>Con un aviso encima, el foco va a su botón seguro (seguir / cerrar), pero solo se ve si se usa el teclado.</summary>
    private void FocusOverlay()
    {
        if (_wizard.Overlay != OnboardingOverlay.None) _focus = new OnboardingHit(OnboardingControl.OverlaySecondary);
    }

    // ---------------------------------------------------------------- estado → vista

    /// <summary>Tras cada cambio: el idioma (si cambió), los textos, el cruce de página, los interruptores y la vista previa.</summary>
    private void Rebuild(TimeSpan now)
    {
        if (_wizard.Language != _stringsCode)
        {
            _stringsCode = _wizard.Language;
            _strings = StringsFor(_stringsCode);
            _painter.Strings = _strings;
            _labels = _monitorLabels(_strings);
        }
        var old = _view;
        _view = Build();
        if (_view.Overlay is { } overlay) _lastOverlay = overlay;
        _overlay.SetTarget(_view.Overlay is null ? 0 : 1);
        if (old.Page != _view.Page)
        {
            _previous = old with { Overlay = null };
            _pageAt = now;
            _pageDirection = _view.PageIndex >= old.PageIndex ? 1 : -1;
            _indicator.SetTarget(_view.PageIndex);
            if (ReduceMotion) _indicator.Snap(_view.PageIndex);
            _hover = OnboardingHit.None;
            _focus = _showFocus ? OnboardingGeometry.MoveFocus(FocusOrder(), OnboardingHit.None, 1) : OnboardingHit.None;
            if (_view.Page == OnboardingPage.Profile) _scenesProfile = null;   // al volver a los perfiles, la vista previa empieza
        }
        else if (_view.PageIndex != (int)Math.Round(_indicator.Target)) _indicator.SetTarget(_view.PageIndex);
        if (_focus.Control != OnboardingControl.None && !Usable(_focus) && _wizard.Overlay == OnboardingOverlay.None)
            _focus = OnboardingHit.None;
        SetKnobTargets();
    }

    private OnboardingView Build() => OnboardingText.Build(_wizard, _strings, _labels);

    /// <summary>Los textos de un idioma del asistente: «system» = el de Windows con el formato regional del usuario.</summary>
    public static Strings StringsFor(string code) => string.IsNullOrEmpty(code) || code == "system"
        ? Localization.Get(Localization.ResolveSystemCode()) with { Culture = CultureInfo.CurrentCulture }
        : Localization.Get(code);

    private void SetKnobTargets()
    {
        for (int i = 0; i < _knobs.Length; i++)
        {
            _knobs[i].SetTarget(KnobOn(i) ? 1 : 0);
            if (ReduceMotion) _knobs[i].Snap(_knobs[i].Target);
        }
    }

    private void SnapKnobs()
    {
        for (int i = 0; i < _knobs.Length; i++) _knobs[i].Snap(KnobOn(i) ? 1 : 0);
    }

    private bool KnobOn(int i)
    {
        int features = SetupFeatures.All.Count;
        if (i < features) return _wizard.FeatureOn((SetupFeature)i);
        if (i == OnboardingPainter.KnobIndex(OnboardingControl.Startup)) return _wizard.StartWithWindows;
        if (i == OnboardingPainter.KnobIndex(OnboardingControl.LiveSessions)) return _wizard.LiveSessionsOn;
        if (i == OnboardingPainter.KnobIndex(OnboardingControl.UsageStats)) return _wizard.UsageStats;
        if (i == OnboardingPainter.KnobIndex(OnboardingControl.Tutorial)) return _wizard.ShowTutorial;
        return _wizard.PermissionsValue;
    }

    // ---------------------------------------------------------------- vista previa

    private bool PreviewVisible => _motion.Interactive
        && (_wizard.Page == OnboardingPage.Profile || (_previous?.Page == OnboardingPage.Profile && PageCrossing(_now)));

    private float PreviewScale => _scale * OnboardingGeometry.PreviewScale;

    private void StepPreview(TimeSpan now)
    {
        if (_wizard.Page != OnboardingPage.Profile) return;
        var profile = _wizard.Profile;
        if (_scenesProfile != profile)
        {
            var scenes = ProfilePreview.Scenes(ProfilePreview.FeaturesFor(_wizard, profile));
            bool same = scenes.SequenceEqual(_scenes);
            _scenesProfile = profile;
            if (!same || _sceneIndex < 0)
            {
                _scenes = scenes;
                _scenesAt = now;
                _sceneIndex = -1;
            }
        }
        int i = ProfilePreview.SceneAt(_scenes, now - _scenesAt);
        if (i == _sceneIndex || _scenes.Count == 0) { _preview.Step(now); return; }
        _sceneIndex = i;
        var scene = _scenes[i];
        float ps = PreviewScale;
        _preview.SetTarget(scene.Mode, PreviewAlert(scene), PreviewSize(scene, ps), ps, now, PreviewLayout(scene));
        _preview.Step(now);
    }

    private static int PreviewLayout(PreviewScene scene) => (int)scene.Face + (scene.Mascot ? 100 : 0);

    private static NotchAlert? PreviewAlert(PreviewScene scene) => scene.Face == PreviewFace.Done
        ? new NotchAlert(NotchAlertKind.Completed, "preview", "claudebar", null, DateTime.UnixEpoch, null)
        : null;

    private SizeF PreviewSize(PreviewScene scene, float ps)
    {
        var p = _painter.Notch;
        int cat = scene.Mascot ? IdleScene.LayoutMascot : 0;
        switch (scene.Face)
        {
            case PreviewFace.None: return SizeF.Empty;
            case PreviewFace.Line: return IdleScene.SizeFor(NotchMode.Minimal, cat, 0f, ps);
            case PreviewFace.Clock:
            {
                var (time, date) = ClockNow();
                return IdleScene.SizeFor(NotchMode.Idle, IdleScene.LayoutClock | cat, p.MeasureClock(ps, time, date) / ps, ps);
            }
            case PreviewFace.Song: return NotchLayout.IdleMusicSize(ps);
            case PreviewFace.Lyrics:
            {
                var slots = new CompactLyricsSlots(Art: false, Quota: true);
                return CompactLyrics.Size(CompactLyrics.Extra(p.MeasureCompactLine(LyricLine, ps), slots), ps);
            }
            case PreviewFace.Working:
                return NotchLayout.TargetSize(NotchMode.Working, ps, p.MeasureWorkingExtra(PreviewSessions, ps));
            case PreviewFace.Done: return NotchLayout.TargetSize(NotchMode.Completed, ps, 0f);
            default: return IdleScene.SizeFor(NotchMode.Idle, cat, 0f, ps);
        }
    }

    private const string LyricLine = "Here comes the sun, and I say";

    private static readonly IReadOnlyList<SessionState> PreviewSessions = new[]
    {
        new SessionState
        {
            SessionId = "preview", Cwd = @"C:\code\claudebar", ProjectName = "claudebar", Phase = SessionPhase.Processing,
            LastTool = "Edit", LastActivityUtc = DateTime.UnixEpoch,
        },
    };

    private static readonly NowPlaying PreviewTrack = new("Here Comes the Sun", "The Beatles", "Abbey Road", true,
        TimeSpan.FromSeconds(42), TimeSpan.FromSeconds(185), DateTime.UnixEpoch, null, Color.FromArgb(0xE8, 0xB0, 0x4C), "preview");

    private (string Time, string Date) ClockNow() =>
        ClockText.Format(_localNow(), _strings.Culture, ClockText.Prefers24h(CultureInfo.CurrentCulture));

    private NotchFrame? PreviewFrame()
    {
        if (!PreviewVisible || _scenes.Count == 0) return null;
        if (_preview.IsGone && _scenes[Math.Max(0, _sceneIndex)].Face == PreviewFace.None) return null;
        var shown = _preview.ShownMode;
        int layout = _preview.ShownLayout;
        var face = (PreviewFace)(layout % 100);
        bool cat = layout >= 100;
        MusicView? music = face switch
        {
            PreviewFace.Song => new MusicView(PreviewTrack, PreviewTrack.Position, IdleFace: true),
            PreviewFace.Lyrics => new MusicView(PreviewTrack, PreviewTrack.Position, IdleFace: true, LyricsState: LyricsState.Found,
                Compact: new CompactLyricsView(LyricLine, Art: false, Quota: true)),
            _ => null,
        };
        IdleView? idle = null;
        if (face == PreviewFace.Clock || cat)
        {
            var (time, date) = face == PreviewFace.Clock ? ClockNow() : ("", "");
            idle = new IdleView(face == PreviewFace.Clock, time, date, cat ? new MascotLook(MascotState.Awake, Point.Empty, false) : null,
                _preview.ContentAlpha);
        }
        return new NotchFrame(_preview.Width, _preview.Height, PreviewScale, _preview.IsGone ? NotchMode.Hidden : shown,
            _preview.ShownAlert, face == PreviewFace.Working ? PreviewSessions : Array.Empty<SessionState>(), PreviewQuota,
            SpinnerPhase(), _preview.ContentAlpha, 1f, _preview.ShadowAlpha, 0f, _preview.Squash, _preview.ContentOffsetY,
            ReduceMotion, Music: music, Idle: idle);
    }

    private float SpinnerPhase() => (float)(_now.TotalMilliseconds / NotchMotion.SpinnerPeriod.TotalMilliseconds % 1.0);

    /// <summary>El notch de reposo al aterrizar, como lo pintará el de verdad (su escala, su cuota y sus sesiones).</summary>
    private NotchFrame LandedFrame(OnboardingLanding l)
    {
        var quota = SafeGet(_services.Quota, null);
        var sessions = SafeGet(_services.Sessions, Array.Empty<SessionState>()) ?? Array.Empty<SessionState>();
        return new NotchFrame(_motion.Width, _motion.Height, l.Scale, l.Mode, null, sessions, quota, SpinnerPhase(),
            _motion.LandedAlpha, ReduceMotion: ReduceMotion);
    }

    private static T? SafeGet<T>(Func<T> get, T? fallback)
    {
        try { return get(); }
        catch { return fallback; }
    }
}
