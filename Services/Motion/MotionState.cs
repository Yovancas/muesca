using System.Drawing;

namespace ClaudeBarWin.Services.Motion;

/// <summary>
/// Contenedor de valores animados indexados por clave (<c>"bar:5h"</c>, <c>"bar:7d"</c>,
/// <c>"num:crit"</c>…) que el render <b>muestrea en tiempo de pintado</b>. Es la capa de estado entre
/// <c>DashboardForm</c> (que llama <see cref="SetTarget"/> al llegar un snapshot) y el dibujo (que lee
/// <see cref="Display"/>).
///
/// <para>Lenguaje de movimiento §4 #5: cada clave es un muelle <see cref="Motion.AjustePct"/> (en %)
/// que se reapunta con <see cref="SetTarget"/> conservando posición y velocidad; cada transición pone
/// su preset (§2.2). La cifra sale del MISMO muelle (<see cref="DisplayNumber"/>): redondeada a entero
/// mientras se mueve y con clamp monótono, igual que la barra (nunca cruza el objetivo en el sentido
/// del cambio). Al abrir, <see cref="Fill"/> llena desde 0 con un retardo por barra; una renovación
/// (<see cref="Reset"/>) vacía con <see cref="Motion.BlandoPct"/> y deja un rastro al 25 % de alfa que
/// se funde con Rápido (micro-interacción 2). El color de cada barra se mezcla con Rápido
/// (<see cref="SetColorTarget"/>).</para>
///
/// <para>PURO y elapsed-driven (regla de oro F3): <see cref="Advance"/> recibe el delta en ms por
/// parámetro; jamás lee el reloj ni <c>Math.Random</c>. Así los tests son deterministas sin GDI+/reloj.</para>
///
/// <para>Semántica clave:</para>
/// <list type="bullet">
/// <item><see cref="Display"/> <b>siembra</b> una clave nueva directamente en su objetivo (no hay animación
/// "desde 0" la primera vez que aparece un dato). Con <c>reduceMotion</c> devuelve el target al instante
/// — es la puerta única del gate de reduce-motion (Tarea 7).</item>
/// <item><see cref="SetTarget"/>/<see cref="Set"/> mueven el objetivo del muelle desde el estado actual
/// (sin salto). Con <c>reduceMotion</c> colapsan al instante (snap).</item>
/// <item><see cref="Advance"/> integra el tiempo en todas las claves; <see cref="IsAnimating"/> es cierto
/// mientras alguna esté en vuelo, esperando su retardo, con rastro o con color en mezcla.</item>
/// </list>
/// </summary>
public sealed class MotionState
{
    /// <summary>
    /// Tiempo máximo (ms) que integra un <see cref="Advance"/>. <see cref="SpringValue"/> recorta cada
    /// paso a <see cref="Motion.SpringMaxDt"/>; aquí un delta largo (render offline, tick tras una
    /// pausa) se trocea para alcanzar el estado que toca, hasta este tope (un muelle crítico ya se ha
    /// asentado de sobra).
    /// </summary>
    internal const double MaxCatchUpMs = 2000.0;

    /// <summary>Alfa inicial del rastro que deja una barra al vaciarse por renovación.</summary>
    internal const double TrailAlpha = 0.25;

    private sealed class Entry
    {
        public required SpringValue Spring;
        public double Dir;                       // sentido del cambio en curso (+1 sube, −1 baja, 0 quieto)
        public double PendingDelayMs = -1;       // ≥ 0: objetivo diferido (llenado de apertura)
        public double PendingTarget;
        public double? TrailPct;                 // de dónde venía la barra al renovarse
        public double TrailStartMs;
    }

    private readonly Dictionary<string, Entry> _values = new();
    private readonly Dictionary<string, ColorCrossfade> _colors = new();
    private readonly Dictionary<string, (string Text, string? Prev, double SinceMs)> _texts = new();
    private double _nowMs;                       // reloj propio, avanzado solo por Advance

    /// <summary>Devuelve (creándola si hace falta) la entrada sembrada en <paramref name="seed"/>.</summary>
    private Entry GetOrSeed(string key, double seed)
    {
        if (!_values.TryGetValue(key, out var e))
        {
            e = new Entry { Spring = new SpringValue(seed, Motion.AjustePct) };
            _values[key] = e;
        }
        return e;
    }

    /// <summary>Valor del muelle con el clamp monótono: nunca cruza el objetivo en el sentido del cambio.</summary>
    private static double Clamped(Entry e)
    {
        double v = e.Spring.Value, t = e.Spring.Target;
        if (e.Dir > 0) return Math.Min(v, t);
        if (e.Dir < 0) return Math.Max(v, t);
        return v;
    }

    /// <summary>Apunta el muelle con el preset de la transición y recuerda su sentido.</summary>
    private static void Aim(Entry e, double target, SpringPreset preset)
    {
        e.PendingDelayMs = -1;
        e.Spring.SetPreset(preset);
        e.Dir = Math.Sign(target - e.Spring.Value);
        e.Spring.SetTarget(target);
    }

    private static void SnapEntry(Entry e, double target)
    {
        e.PendingDelayMs = -1;
        e.Dir = 0;
        e.TrailPct = null;
        e.Spring.Snap(target);
    }

    /// <summary>
    /// Valor a pintar para <paramref name="key"/> (barra). Si la clave es nueva, se siembra en
    /// <paramref name="target"/> (sin animación de primera aparición). Con <paramref name="reduceMotion"/>
    /// devuelve el target al instante; si no, la posición del muelle con el clamp monótono.
    /// </summary>
    public double Display(string key, double target, bool reduceMotion)
    {
        var e = GetOrSeed(key, target);
        return reduceMotion ? target : Clamped(e);
    }

    /// <summary>
    /// Cifra a pintar para <paramref name="key"/>: <c>Round(barra)</c> del mismo muelle mientras se mueve
    /// (el ancho del texto no tiembla) y el objetivo exacto una vez asentado.
    /// </summary>
    public double DisplayNumber(string key, double target, bool reduceMotion)
    {
        var e = GetOrSeed(key, target);
        if (reduceMotion) return target;
        if (e.Spring.IsSettled && e.PendingDelayMs < 0) return e.Spring.Target;
        return Math.Round(Clamped(e), MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Apunta la clave a <paramref name="target"/>. <paramref name="durMs"/> ≤ 0 o
    /// <paramref name="reduceMotion"/> ⇒ salto al objetivo; si no, el muelle tira hacia él conservando
    /// su velocidad. <paramref name="ease"/> se ignora (compatibilidad con la API de tweens).
    /// </summary>
    public void Set(string key, double target, double durMs, Func<double, double>? ease = null, bool reduceMotion = false)
    {
        var e = GetOrSeed(key, target);
        if (reduceMotion || durMs <= 0)
        {
            // Gate único de reduce-motion: el objetivo se asienta sin animación.
            SnapEntry(e, target);
            return;
        }
        if (e.Spring.Target == target && e.PendingDelayMs < 0) return; // mismo objetivo: no reinicia
        Aim(e, target, Motion.AjustePct);
    }

    /// <summary>
    /// Actualización normal de números/barras (AjustePct, todas a la vez). Con
    /// <paramref name="reduceMotion"/> colapsa al objetivo al instante.
    /// </summary>
    public void SetTarget(string key, double target, bool reduceMotion)
        => Set(key, target, Motion.TweenMs, null, reduceMotion);

    /// <summary>
    /// Llenado de apertura (§4 #5): la barra salta a 0 y, pasados <paramref name="delayMs"/>, se llena
    /// hasta <paramref name="target"/> con <see cref="Motion.BlandoPct"/> (primer dibujo de las barras, §2).
    /// </summary>
    public void Fill(string key, double target, double delayMs, bool reduceMotion)
    {
        var e = GetOrSeed(key, target);
        if (reduceMotion) { SnapEntry(e, target); return; }
        SnapEntry(e, 0);
        e.Spring.SetPreset(Motion.BlandoPct);
        e.PendingTarget = target;
        e.PendingDelayMs = Math.Max(0, delayMs);
        if (e.PendingDelayMs == 0) Aim(e, target, Motion.BlandoPct);
    }

    /// <summary>
    /// Renovación de la cuota (§4 #5 + micro-interacción 2): vaciado con <see cref="Motion.BlandoPct"/>
    /// (sin pasada; la barra no baja del objetivo ni la cifra pasa por 0) y rastro al 25 % de alfa
    /// desde donde estaba la barra, que se funde con Rápido.
    /// </summary>
    public void Reset(string key, double target, bool reduceMotion)
    {
        var e = GetOrSeed(key, target);
        if (reduceMotion) { SnapEntry(e, target); return; }
        double from = Clamped(e);
        Aim(e, target, Motion.BlandoPct);
        if (from > target)
        {
            e.TrailPct = from;
            e.TrailStartMs = _nowMs;
        }
    }

    /// <summary>Rastro de la renovación en curso: % de donde venía la barra y su alfa (0..0,25); null si no hay.</summary>
    public (double Pct, double Alpha)? TrailOf(string key)
    {
        if (!_values.TryGetValue(key, out var e) || e.TrailPct is not { } pct) return null;
        double a = TrailAlpha * (1 - SpringCurve.Progress(_nowMs - e.TrailStartMs, Motion.Rapido));
        return a < TrailAlpha * Motion.Rapido.EpsX ? null : (pct, a);
    }

    /// <summary>
    /// Color de la barra hacia el que mezclar con Rápido. La primera vez (o con <paramref name="snap"/>:
    /// reduce-motion, cambio de tema) se fija sin animar.
    /// </summary>
    public void SetColorTarget(string key, Color c, bool snap)
    {
        if (!_colors.TryGetValue(key, out var cf)) { _colors[key] = new ColorCrossfade(c); return; }
        cf.SetTarget(c, snap);
    }

    /// <summary>Color mezclado de la barra; null si nunca se fijó (el llamante usa el suyo).</summary>
    public Color? DisplayColor(string key) => _colors.TryGetValue(key, out var c) ? c.Current : null;

    /// <summary>
    /// Cruce de un texto que cambia dentro del mismo diseño (micro-interacción 4): devuelve el texto
    /// previo y la mezcla (0 → 1 lineal en <see cref="Motion.TextCrossfadeMs"/>) desde que cambió; sin
    /// cambio en curso, (null, 1). Se llama al pintar; la primera lectura siembra sin cruce.
    /// </summary>
    public (string? Prev, double Blend) TextBlend(string key, string text)
    {
        if (!_texts.TryGetValue(key, out var t)) { _texts[key] = (text, null, _nowMs); return (null, 1.0); }
        if (t.Text != text) { _texts[key] = t = (text, t.Text, _nowMs); }
        if (t.Prev is null) return (null, 1.0);
        double blend = (_nowMs - t.SinceMs) / Motion.TextCrossfadeMs;
        if (blend >= 1) { _texts[key] = (t.Text, null, t.SinceMs); return (null, 1.0); }
        return (t.Prev, Math.Max(0, blend));
    }

    /// <summary>Integra <paramref name="elapsedMs"/> en todas las claves (en trozos de ≤ 50 ms).</summary>
    public void Advance(double elapsedMs)
    {
        if (!(elapsedMs > 0)) return;
        _nowMs += elapsedMs;
        foreach (var e in _values.Values)
        {
            double remaining = elapsedMs;
            if (e.PendingDelayMs >= 0)
            {
                if (e.PendingDelayMs > remaining) { e.PendingDelayMs -= remaining; remaining = 0; }
                else
                {
                    remaining -= e.PendingDelayMs;
                    Aim(e, e.PendingTarget, Motion.BlandoPct);
                }
            }
            if (remaining > 0) AdvanceSpring(e.Spring, remaining);
            if (e.Spring.IsSettled && e.PendingDelayMs < 0) e.Dir = 0;
            if (e.TrailPct is not null && TrailOfEntryDone(e)) e.TrailPct = null;
        }
        foreach (var c in _colors.Values)
            if (c.IsAnimating) c.Advance(elapsedMs / 1000.0);
    }

    private bool TrailOfEntryDone(Entry e)
        => TrailAlpha * (1 - SpringCurve.Progress(_nowMs - e.TrailStartMs, Motion.Rapido)) < TrailAlpha * Motion.Rapido.EpsX;

    /// <summary>Lleva todas las claves a su objetivo al instante (gate global de reduce-motion).</summary>
    public void SnapAll()
    {
        foreach (var e in _values.Values)
        {
            double target = e.PendingDelayMs >= 0 ? e.PendingTarget : e.Spring.Target;
            SnapEntry(e, target);
        }
        foreach (var c in _colors.Values) c.SetTarget(c.Target, snap: true);
    }

    /// <summary><c>true</c> mientras alguna clave esté animando (alimenta al scheduler).</summary>
    public bool IsAnimating
    {
        get
        {
            foreach (var e in _values.Values)
                if (!e.Spring.IsSettled || e.PendingDelayMs >= 0 || e.TrailPct is not null)
                    return true;
            foreach (var c in _colors.Values)
                if (c.IsAnimating) return true;
            foreach (var t in _texts.Values)
                if (t.Prev is not null && _nowMs - t.SinceMs < Motion.TextCrossfadeMs) return true;
            return false;
        }
    }

    /// <summary>Velocidad actual de la clave (unidades/s; 0 si no existe). Diagnóstico y tests.</summary>
    internal double VelocityOf(string key) => _values.TryGetValue(key, out var e) ? e.Spring.Velocity : 0.0;

    /// <summary>Preset vigente de la clave (diagnóstico y tests).</summary>
    internal SpringPreset? PresetOf(string key) => _values.TryGetValue(key, out var e) ? e.Spring.Preset : null;
    /// <summary>
    /// Progreso (0→1) del fundido de apertura (Rápido, §4 #1) que arranca en reposo en 0 hacia 1 tras
    /// <paramref name="elapsedMs"/>. Lo usan el render offline y los GIF para reproducir la misma curva
    /// que el panel vivo sin reloj.
    /// </summary>
    public static double SpringProgressAt(double elapsedMs)
    {
        var s = new SpringValue(0, Motion.Rapido);
        s.SetTarget(1);
        AdvanceSpring(s, elapsedMs);
        return Math.Clamp(s.Value, 0.0, 1.0);
    }

    /// <summary>Integra un delta largo troceándolo en pasos que <see cref="SpringValue"/> no recorta.</summary>
    internal static void AdvanceSpring(SpringValue v, double elapsedMs)
    {
        double remaining = Math.Min(elapsedMs, MaxCatchUpMs) / 1000.0;
        while (remaining > 1e-9 && !v.IsSettled)
        {
            double step = Math.Min(remaining, Motion.SpringMaxDt);
            v.Advance(step);
            remaining -= step;
        }
    }
}
