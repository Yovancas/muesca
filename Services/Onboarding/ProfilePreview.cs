using ClaudeBarWin.Config;
using ClaudeBarWin.Services.Notch;

namespace ClaudeBarWin.Services.Onboarding;

/// <summary>Qué enseña el notch en una escena de la vista previa.</summary>
public enum PreviewFace
{
    /// <summary>Sin notch (Solo cuota): la cuota vive en la bandeja.</summary>
    None,
    /// <summary>Reposo con los dos anillos de la cuota.</summary>
    Rings,
    /// <summary>Reposo con el reloj.</summary>
    Clock,
    /// <summary>La raya de Mínimo.</summary>
    Line,
    /// <summary>Reposo con la canción (carátula mini + título).</summary>
    Song,
    /// <summary>Reposo con la letra compacta (la línea que suena).</summary>
    Lyrics,
    /// <summary>Claude trabajando: spinner + «proyecto · herramienta».</summary>
    Working,
    /// <summary>El aviso «Terminado».</summary>
    Done,
}

/// <summary>Una escena: el modo del notch, su cara, si va el gato y cuánto se queda.</summary>
public readonly record struct PreviewScene(NotchMode Mode, PreviewFace Face, bool Mascot, TimeSpan Duration);

/// <summary>
/// La vista previa en vivo de la página de perfiles: cómo quedará el notch con lo marcado, como una secuencia corta que se
/// repite (reposo → lo que añade el perfil → Claude trabajando → «Terminado»). Cada escena la pinta el painter de verdad y
/// los cambios los anima el <c>NotchAnimator</c> de siempre. PURO: la ventana pregunta la escena del instante y cuándo
/// cambia (un temporizador de un disparo; entre escenas, nada se mueve).
/// </summary>
public static class ProfilePreview
{
    /// <summary>Lo que dura cada escena.</summary>
    public static readonly TimeSpan SceneFor = TimeSpan.FromMilliseconds(2400);

    /// <summary>
    /// Escenas para esas casillas (<paramref name="on"/>). Sin notch, una sola (<see cref="PreviewFace.None"/>). Con Mínimo,
    /// la raya; si no, el reloj (con él) o los anillos, y el gato si está. Luego la letra compacta (con música, letras y letra
    /// pequeña) o la canción (solo con música), Trabajando y el aviso de terminar.
    /// </summary>
    public static IReadOnlyList<PreviewScene> Scenes(Func<SetupFeature, bool> on)
    {
        ArgumentNullException.ThrowIfNull(on);
        if (!on(SetupFeature.Notch)) return new[] { new PreviewScene(NotchMode.Hidden, PreviewFace.None, false, SceneFor) };
        bool minimal = on(SetupFeature.Minimal), cat = on(SetupFeature.Mascot);
        var scenes = new List<PreviewScene>(4)
        {
            minimal ? new(NotchMode.Minimal, PreviewFace.Line, cat, SceneFor)
                : new(NotchMode.Idle, on(SetupFeature.Clock) ? PreviewFace.Clock : PreviewFace.Rings, cat, SceneFor),
        };
        if (!minimal && on(SetupFeature.Music))
        {
            bool lyrics = on(SetupFeature.Lyrics) && on(SetupFeature.CompactLyrics);
            scenes.Add(new(NotchMode.Idle, lyrics ? PreviewFace.Lyrics : PreviewFace.Song, false, SceneFor));
        }
        scenes.Add(new(NotchMode.Working, PreviewFace.Working, false, SceneFor));
        scenes.Add(new(NotchMode.Completed, PreviewFace.Done, false, SceneFor));
        return scenes;
    }

    /// <summary>Escena que toca a los <paramref name="elapsed"/> de empezar (la secuencia se repite).</summary>
    public static int SceneAt(IReadOnlyList<PreviewScene> scenes, TimeSpan elapsed)
    {
        if (scenes.Count <= 1 || elapsed <= TimeSpan.Zero) return 0;
        long total = scenes.Sum(s => s.Duration.Ticks);
        if (total <= 0) return 0;
        long t = elapsed.Ticks % total;
        for (int i = 0; i < scenes.Count; i++)
        {
            if (t < scenes[i].Duration.Ticks) return i;
            t -= scenes[i].Duration.Ticks;
        }
        return scenes.Count - 1;
    }

    /// <summary>Hasta el siguiente cambio de escena; null con una sola (nada que despertar).</summary>
    public static TimeSpan? NextChangeIn(IReadOnlyList<PreviewScene> scenes, TimeSpan elapsed)
    {
        if (scenes.Count <= 1) return null;
        long total = scenes.Sum(s => s.Duration.Ticks);
        if (total <= 0) return null;
        long t = Math.Max(0, elapsed.Ticks) % total;
        for (int i = 0; i < scenes.Count; i++)
        {
            if (t < scenes[i].Duration.Ticks) return TimeSpan.FromTicks(scenes[i].Duration.Ticks - t);
            t -= scenes[i].Duration.Ticks;
        }
        return TimeSpan.FromTicks(1);
    }

    /// <summary>Las casillas con las que se ve un perfil en la vista previa: las del asistente si es el perfil con el que
    /// están rellenas (lo tocado a mano cuenta), y si no las que pondría ese perfil.</summary>
    public static Func<SetupFeature, bool> FeaturesFor(OnboardingWizard wizard, SetupProfile profile)
    {
        ArgumentNullException.ThrowIfNull(wizard);
        if (profile == wizard.FeaturesProfile) return wizard.FeatureOn;
        var c = wizard.Context;
        var values = SetupFeatures.Prefill(profile, c.Current, c.Fresh, wizard.Recommended);
        return f => values.TryGetValue(f, out var v) && v;
    }
}
