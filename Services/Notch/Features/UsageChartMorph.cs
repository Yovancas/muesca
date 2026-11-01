using ClaudeBarWin.Services.Motion;

namespace ClaudeBarWin.Services.Notch.Features;

/// <summary>Cómo se pasa de una gráfica a otra: transformando la curva (mismo modo) o fundiendo una en la otra.</summary>
public enum UsageMorphKind { None, Morph, Fade }

/// <summary>
/// Transición de la gráfica de la página Uso al cambiar de rango, ocultar una serie o cambiar de modo (lenguaje de
/// movimiento: los datos cambian con un muelle, sin saltos).
/// <list type="bullet">
/// <item><b>Morph</b> (mismo modo): cada borde de la forma vieja va al de la nueva con el muelle Ajuste (sin pasarse). En el
/// gasto apilado, las series se alinean por clave sobre el orden de la nueva (las que se van, después) con grosor 0 donde
/// faltan: los bordes son sumas de grosores, así la pila sigue cerrada en CADA fotograma (ninguna franja pisa a otra). Las
/// líneas de la cuota que aparecen o se van, en su sitio y fundiéndose.</item>
/// <item><b>Fade</b> (otro modo, o con «reducir movimiento»): cruce lineal de <see cref="FadeDuration"/>; la vieja se
/// pinta con <c>1 − Progress</c> y la nueva con <c>Progress</c>.</item>
/// </list>
/// PURO: el instante llega por parámetro; quieto (<see cref="IsAnimating"/> = false) el bucle de la ventana se para. La
/// forma intermedia se escribe en <see cref="Current"/>, reservada al empezar (nada por fotograma).
/// </summary>
public sealed class UsageChartMorph
{
    /// <summary>Cruce con «reducir movimiento» o entre modos (el del texto que cambia dentro del mismo diseño).</summary>
    public static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(Motion.Motion.TextCrossfadeMs);

    /// <summary>Muelle del morphing: Ajuste (cambios dentro del mismo modo, ζ ≈ 1) con los umbrales de la unidad 0..1.</summary>
    public static readonly SpringPreset Preset = Motion.Motion.Ajuste with { EpsX = 0.002, EpsV = 0.02 };

    private readonly SpringValue _t = new(1, Preset);
    private TimeSpan _start, _last;
    private bool _fading;
    private float[][] _fromB = Array.Empty<float[]>(), _toB = Array.Empty<float[]>();   // bordes (apilado) por la unión
    private int[] _fromK = Array.Empty<int>(), _toK = Array.Empty<int>();               // líneas: índice en cada extremo
    private float _fromPast = 1f, _toPast = 1f, _fromFirst, _toFirst;

    /// <summary>Gráfica que entra (o la única, quieta).</summary>
    public UsageChartModel? To { get; private set; }

    /// <summary>Gráfica que sale; null quieta.</summary>
    public UsageChartModel? From { get; private set; }

    public UsageMorphKind Kind { get; private set; }

    /// <summary>Avance de la transición (0 → 1). Quieta, 1.</summary>
    public float Progress { get; private set; } = 1f;

    /// <summary>Forma intermedia del morph (null si no hay morph en curso: se pinta la de <see cref="To"/>).</summary>
    public UsageShape? Current { get; private set; }

    public bool IsAnimating => Kind != UsageMorphKind.None;

    /// <summary>Pone <paramref name="model"/> sin transición (primera vez, al volver a abrir la página, otro idioma).</summary>
    public void Jump(UsageChartModel model)
    {
        To = model ?? throw new ArgumentNullException(nameof(model));
        Settle();
    }

    /// <summary>
    /// Va a <paramref name="model"/>. Mismo modo, con datos a los dos lados y sin <paramref name="reduceMotion"/>: morph; si no,
    /// fundido. A media transición parte de lo que se ve (la forma intermedia), sin saltos.
    /// </summary>
    public void Go(UsageChartModel model, bool reduceMotion, TimeSpan now)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (To is null) { Jump(model); return; }
        var from = To;
        var fromShape = Current ?? from.Shape;
        bool morph = !reduceMotion && from.Mode == model.Mode && !from.Empty && !from.Loading && !model.Empty && !model.Loading
            && Kind != UsageMorphKind.Fade && fromShape.Stacked == model.Shape.Stacked;
        From = from;
        To = model;
        _start = _last = now;
        if (morph)
        {
            Prepare(fromShape, model.Shape);
            Kind = UsageMorphKind.Morph;
            _t.Snap(0);
            _t.SetTarget(1);
            _fading = false;
        }
        else
        {
            Current = null;
            Kind = UsageMorphKind.Fade;
            _fading = true;
        }
        Progress = 0f;
        Write(0f);
    }

    /// <summary>Avanza hasta <paramref name="now"/> y escribe la forma intermedia.</summary>
    public void Step(TimeSpan now)
    {
        if (Kind == UsageMorphKind.None) return;
        if (_fading)
        {
            double k = (now - _start).TotalMilliseconds / FadeDuration.TotalMilliseconds;
            if (k >= 1) { Settle(); return; }
            Progress = (float)Math.Clamp(k, 0, 1);
            return;
        }
        double dt = (now - _last).TotalSeconds;
        _last = now;
        if (dt > 0) _t.Advance(dt);
        if (_t.IsSettled && Math.Abs(_t.Value - 1) < Preset.EpsX) { Settle(); return; }
        Progress = (float)Math.Clamp(_t.Value, 0, 1.05);
        Write(Progress);
    }

    /// <summary>Acaba ya (página que se cierra, reduce-motion a medias).</summary>
    public void Settle()
    {
        Kind = UsageMorphKind.None;
        From = null;
        Current = null;
        Progress = 1f;
        _fading = false;
        _t.Snap(1);
    }

    // ---------------------------------------------------------------- forma intermedia

    private void Prepare(UsageShape from, UsageShape to)
    {
        // Unión de claves: el orden de la nueva y, después, las que se van (en su orden).
        var keys = to.Keys.ToList();
        var colors = to.Colors.ToList();
        for (int k = 0; k < from.Keys.Length; k++)
            if (!keys.Contains(from.Keys[k])) { keys.Add(from.Keys[k]); colors.Add(from.Colors[k]); }
        Current = new UsageShape(to.Stacked, to.PastFraction, keys, colors, to.FirstX);
        _fromPast = from.PastFraction;
        _toPast = to.PastFraction;
        _fromFirst = from.FirstX;
        _toFirst = to.FirstX;
        if (to.Stacked)
        {
            _fromB = Bounds(from, keys);
            _toB = Bounds(to, keys);
            return;
        }
        _fromK = keys.Select(from.IndexOf).ToArray();
        _toK = keys.Select(to.IndexOf).ToArray();
        _fromB = from.Upper;
        _toB = to.Upper;
    }

    /// <summary>Bordes acumulados de <paramref name="s"/> sobre el orden <paramref name="keys"/> (grosor 0 donde no está).</summary>
    private static float[][] Bounds(UsageShape s, IReadOnlyList<string> keys)
    {
        var b = new float[keys.Count + 1][];
        b[0] = new float[UsageShape.Samples];
        for (int j = 0; j < keys.Count; j++)
        {
            b[j + 1] = (float[])b[j].Clone();
            int k = s.IndexOf(keys[j]);
            if (k < 0) continue;
            for (int m = 0; m < UsageShape.Samples; m++) b[j + 1][m] += Math.Max(0f, s.Upper[k][m] - s.Lower[k][m]);
        }
        return b;
    }

    private void Write(float t)
    {
        if (Current is not { } c) return;
        c.PastFraction = Lerp(_fromPast, _toPast, Math.Clamp(t, 0f, 1f));
        c.FirstX = Lerp(_fromFirst, _toFirst, Math.Clamp(t, 0f, 1f));
        if (c.Stacked)
        {
            for (int k = 0; k < c.Keys.Length; k++)
                for (int m = 0; m < UsageShape.Samples; m++)
                {
                    c.Lower[k][m] = Lerp(_fromB[k][m], _toB[k][m], t);
                    c.Upper[k][m] = Lerp(_fromB[k + 1][m], _toB[k + 1][m], t);
                }
            return;
        }
        float a = Math.Clamp(t, 0f, 1f);
        for (int k = 0; k < c.Keys.Length; k++)
        {
            int fi = _fromK[k], ti = _toK[k];
            c.Alpha[k] = fi >= 0 && ti >= 0 ? 1f : ti >= 0 ? a : 1f - a;
            for (int m = 0; m < UsageShape.Samples; m++)
            {
                float f = fi >= 0 ? _fromB[fi][m] : float.NaN, g = ti >= 0 ? _toB[ti][m] : float.NaN;
                if (float.IsNaN(f)) f = g;
                if (float.IsNaN(g)) g = f;
                c.Upper[k][m] = Lerp(f, g, t);
                c.Lower[k][m] = 0f;
            }
        }
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
