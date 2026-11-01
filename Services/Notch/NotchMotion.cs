using System.Drawing;

namespace ClaudeBarWin.Services.Notch;

/// <summary>
/// Parámetros de movimiento y de «forma viva» del notch en un único sitio (lenguaje de movimiento,
/// <c>docs/superpowers/specs/2026-10-28-claudebar-motion-language.md</c> §3 y §6). PURO: ni render ni
/// ventana leen literales sueltos; se afinan aquí sin tocar ni el painter ni <c>NotchForm</c>.
///
/// <para>Unidades: px a escala 1 (el llamante multiplica por <c>s</c>); tiempos sin escalar.
/// Los k/c de los muelles NO viven aquí: siguen en <c>Motion</c> (regla de la spec §2).</para>
/// </summary>
public static class NotchMotion
{
    // ---------------------------------------------------------------- forma

    /// <summary>«Orejas» cóncavas superiores que unen la pastilla con el borde: <c>(4 + 4·t)·s</c>, con
    /// el mismo <c>t</c> que el radio (<see cref="NotchLayout.CornerRadius"/>).</summary>
    public static float EarSize(float width, float scale)
    {
        if (scale <= 0) return 0;
        float t = Math.Clamp((width - 30f * scale) / (110f * scale), 0f, 1f);
        return (4f + 4f * t) * scale;
    }

    /// <summary>Δancho mínimo (px a escala 1) para que haya squash-stretch.</summary>
    public const float SquashMinDelta = 100f;

    /// <summary>Velocidad del ancho (px/s a escala 1) a la que el squash satura (|s| = 1).</summary>
    public const float SquashVelocity = 2000f;

    /// <summary>Estiramiento máximo del ancho y aplastamiento máximo del alto con |s| = 1.</summary>
    public const float SquashStretchX = 0.02f, SquashCompressY = 0.04f;

    /// <summary>
    /// Factor de squash <c>s ∈ [−1, 1]</c> a partir de la velocidad del muelle del ancho (px/s físicos).
    /// 0 si la transición cambia el ancho menos de <see cref="SquashMinDelta"/>·escala: en cambios pequeños
    /// la deformación solo se percibe como temblor.
    /// </summary>
    public static float Squash(float widthVelocity, float transitionDeltaWidth, float scale)
    {
        if (scale <= 0 || Math.Abs(transitionDeltaWidth) < SquashMinDelta * scale) return 0f;
        return Math.Clamp(widthVelocity / (SquashVelocity * scale), -1f, 1f);
    }

    /// <summary>Tamaño DIBUJADO de la forma con squash <paramref name="squash"/> (el contenido no se deforma).</summary>
    public static SizeF Squashed(float width, float height, float squash)
    {
        float a = Math.Min(1f, Math.Abs(squash));
        return new SizeF(width * (1f + SquashStretchX * a), height * (1f - SquashCompressY * a));
    }

    // ---------------------------------------------------------------- sombra y halo

    /// <summary>Margen (px a escala 1) que la sombra ocupa a los lados y por abajo de la pastilla.</summary>
    public const float ShadowMargin = 24f;

    /// <summary>Textura de sombra: lado (px a escala 1), radio del box blur, alfa máxima y caída en y.</summary>
    public const float ShadowTexture = 96f, ShadowBlurRadius = 12f, ShadowMaxAlpha = 0.35f, ShadowOffsetY = 3f;

    /// <summary>Halo rojo del aviso 95 %: radio del desenfoque y alfa (estático, no pulsa).</summary>
    public const float HaloRadius = 6f, HaloMaxAlpha = 0.35f;

    /// <summary>Opacidad objetivo de la sombra por modo (se mezcla con el muelle Rápido).</summary>
    public static float ShadowOpacity(NotchMode m) => m switch
    {
        NotchMode.Working or NotchMode.QuotaHud => 0.4f,
        NotchMode.Peek => 0.6f,
        NotchMode.Expanded or NotchMode.Permission or NotchMode.Completed or NotchMode.QuotaAlert or NotchMode.Reminder => 1f,
        _ => 0f,   // Hidden, Minimal, Idle
    };

    // ---------------------------------------------------------------- contenido

    /// <summary>Spinner de Trabajando: barrido, diámetro y trazo (px a escala 1) y periodo de una vuelta.</summary>
    public const float SpinnerSweepDeg = 280f, SpinnerDiameter = 14f, SpinnerStroke = 2f;
    public static readonly TimeSpan SpinnerPeriod = TimeSpan.FromMilliseconds(900);

    /// <summary>Ritmo del spinner cuando es lo único que se mueve (y en ahorro de energía). Spec de
    /// movimiento §3.1 / §6.7 tras la revisión: 30 fps (20 en ahorro; 15 se veía a saltos).</summary>
    public const int SpinnerFps = 30, SpinnerFpsSaver = 20;

    /// <summary>Anillo de cuota: diámetro (20 en Vista previa), trazo y alfa de la pista.</summary>
    public const float RingDiameter = 18f, RingDiameterPeek = 20f, RingStroke = 2.5f, RingTrackAlpha = 0.15f;

    /// <summary>Pasos de alfa del texto y los pinceles (el alfa se cuantiza a 1/32).</summary>
    public const int AlphaSteps = 32;

    /// <summary>Subida del contenido nuevo al entrar (px a escala 1, en enteros 4 → 0).</summary>
    public const float ContentRise = 4f;

    /// <summary>Crossfade: por debajo de este Δtamaño (px a escala 1) los dos contenidos se cruzan a la vez.</summary>
    public const float CrossfadeSplitDelta = 40f;

    /// <summary>Tiempos del crossfade (spec de movimiento §3.0).</summary>
    public static readonly TimeSpan CrossfadeSimultaneous = TimeSpan.FromMilliseconds(120),
        CrossfadeOut = TimeSpan.FromMilliseconds(70),
        CrossfadeIn = TimeSpan.FromMilliseconds(140);

    /// <summary>Progreso de la transición a partir del cual entra el contenido nuevo.</summary>
    public const float CrossfadeInAtProgress = 0.6f;

    /// <summary>Desfase entre ancho y alto (crece: ancho primero; encoge: alto primero). Sin desfase si Δalto ≤ 6 px.</summary>
    public static readonly TimeSpan AxisStagger = TimeSpan.FromMilliseconds(40);
    public const float AxisStaggerMinDeltaHeight = 6f;

    /// <summary>Al encoger, el contenido entra cuando el ancho lleva este progreso (spec de movimiento §3.3).</summary>
    public const float CollapseInAtProgress = 0.7f;

    /// <summary>Bote del fin de una fase del pomodoro: el anillo (el del aviso y el de la página Foco) entra desde este tamaño y
    /// llega a 1 con el muelle de atención (<c>Motion.SpringAttention</c>: rebota a propósito). Con «reducir movimiento», quieto.</summary>
    public const float FocusPopFrom = 0.8f;

    /// <summary>Alfa de toda la pastilla mientras se arrastra una ventana (Snap Layouts).</summary>
    public const float SnapDimAlpha = 0.35f;

    /// <summary>Alfa global por debajo de la cual una pastilla que se oculta cuenta como ida (§3.9).</summary>
    public const float GoneAlpha = 0.03f;

    /// <summary>Forma «asentada» para armar los botones del Permiso: a menos de este hueco del objetivo (px lógicos)
    /// y por debajo de esta velocidad (px lógicos/s). Spec de movimiento §3.4: max(400 ms, a &lt; 1 px y |v| &lt; 40 px/s).</summary>
    public const float ArmSettleDistance = 1f, ArmSettleVelocity = 40f;

    // ---------------------------------------------------------------- bucle

    /// <summary>Respaldo del bucle cuando DWM no compone (DwmFlush falla o vuelve en &lt; 1 ms): 16 ms (§6.5).</summary>
    public const int FallbackFrameMs = 16;

    /// <summary>Retornos «instantáneos» seguidos de DwmFlush que activan el respaldo.</summary>
    public const int DwmFlushFastReturns = 2;
}
