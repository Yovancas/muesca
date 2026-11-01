namespace ClaudeBarWin.Services.Motion;

/// <summary>
/// Constantes de la Fase 3 (microinteracciones): duraciones, desfases y amplitudes en un
/// único sitio. Nada de literales sueltos de tiempo/offset repartidos por el render — todo
/// se referencia desde aquí (regla de oro F3: "sin literales de offset nuevos").
/// </summary>
public static class Motion
{
    /// <summary>Duración nominal de la API de tweens de números/barras (&gt; 0 ⇒ animar; hoy van con muelle).</summary>
    public const double TweenMs = 220.0;

    /// <summary>Retardo entre barras al llenarse en la apertura del panel (§4 #5).</summary>
    public const double BarFillStaggerMs = 25.0;

    /// <summary>Fade de opacidad al abrir el panel y fade-in del hover. ~120 ms.</summary>
    public const double FadeMs = 120.0;

    /// <summary>Desfase por índice en la entrada escalonada de secciones (§4 #3).</summary>
    public const double StaggerMs = 30.0;

    /// <summary>Pasos máximos de la entrada escalonada: a partir de la 6.ª sección entran con la 5.ª.</summary>
    public const int StaggerMaxSteps = 5;

    /// <summary>ω (1/s) de la curva crítica cerrada de la entrada escalonada: √300 ≈ 17,3.</summary>
    public static readonly double StaggerOmega = Math.Sqrt(300);

    /// <summary>Desplazamiento vertical inicial (px de diseño a 96 DPI; se escala en el punto de uso) de la entrada escalonada.</summary>
    public const int StaggerMaxOffsetPx = 6;

    /// <summary>Desplazamiento (px de diseño) del contenido hacia la bandeja al cerrar el panel (§4 #2).</summary>
    public const int CloseShiftPx = 4;

    /// <summary>Alfa por debajo del cual el panel que se cierra hace <c>Hide()</c> (~180 ms con Rápido).</summary>
    public const double CloseHideAlpha = 0.03;

    /// <summary>Pico (px de diseño a 96 DPI) del primer bote del Salto del panel (v₀ <see cref="HopPanelV0"/>).</summary>
    public const int BounceAmplitudePx = 6;

    // ── Salto del gato (§5): gravedad y suelo. Velocidades en px/s a escala 1,0 (escalan con el DPI). ──

    /// <summary>Gravedad del Salto (px/s²).</summary>
    public const double HopGravity = 272.0;

    /// <summary>Restitución: la velocidad se multiplica por esto en cada toque de suelo.</summary>
    public const double HopRestitution = 0.75;

    /// <summary>El Salto se para cuando el siguiente pico bajaría de esto (px).</summary>
    public const double HopMinPeakPx = 1.2;

    /// <summary>Velocidad de despegue del Salto del panel: 6 / 3,4 / 1,9 px y 419 / 314 / 236 ms (≈ 970 ms).</summary>
    public const double HopPanelV0 = 57.0;

    /// <summary>Velocidad de despegue del Salto del notch: 3 / 1,7 px y 294 / 221 ms (≈ 515 ms).</summary>
    public const double HopNotchV0 = 40.0;

    /// <summary>Salto del notch durante el rebote de la forma (Permiso): un solo bote de 1,65 px y 221 ms.</summary>
    public const double HopNotchShapeV0 = 30.0;

    /// <summary>Duración (ms) de la cara alternativa en cada toque de suelo (micro-interacción 1).</summary>
    public const double HopLandingFaceMs = 90.0;

    /// <summary>
    /// Calendario de saltos de atención (ms desde que empieza): entrada y repeticiones a los 3,2 / 6,4 /
    /// 9,6 / 12,8 s y una última a los 22,8 s (6 en total). Después, quieto. Igual que el Permiso del notch.
    /// </summary>
    public static readonly double[] AttentionHopScheduleMs = { 0, 3200, 6400, 9600, 12800, 22800 };

    /// <summary>Duración (ms) del destello in-panel de celebración de reset ("✓ cuota renovada").</summary>
    public const double CelebrationMs = 2600.0;

    /// <summary>Plazo (ms) tras el que los ojos del gato vuelven al centro cuando el cursor sale (§5).</summary>
    public const double EyesReturnMs = 1500.0;

    /// <summary>Cruce simultáneo (ms, lineal en alfa) de un texto que cambia dentro del mismo diseño.</summary>
    public const double TextCrossfadeMs = 120.0;

    /// <summary>Retardo (ms) desde la celebración hasta que el fondo del chip empieza a florecer (§4 #6).</summary>
    public const double ChipBgDelayMs = 80.0;

    /// <summary>Retardo (ms) del texto del chip respecto a su fondo.</summary>
    public const double ChipTextDelayMs = 60.0;

    /// <summary>Fundido (ms) del texto del chip al entrar.</summary>
    public const double ChipTextFadeMs = 120.0;

    /// <summary>Subida (px de diseño) del chip al salir.</summary>
    public const int ChipRisePx = 3;

    /// <summary>Margen (px de diseño) hacia arriba del rect que se invalida con el chip en vuelo.</summary>
    public const int ChipDirtyUpPx = 6;

    /// <summary>Radio (px de diseño) del fondo del realce de hover (§4 #4).</summary>
    public const int HoverRadiusPx = 6;

    /// <summary>Intensidad por encima de la cual el realce se desliza de A a B; por debajo se encaja y se funde.</summary>
    public const double HoverSlideMinIntensity = 0.3;

    /// <summary>Intervalo del tick rápido cuando hay algo animando (~30 fps).</summary>
    public const int FastTickMs = 33;

    /// <summary>Intervalo del tick lento (solo countdown, panel visible sin animación).</summary>
    public const int SlowTickMs = 1000;

    // ── Presets del lenguaje de movimiento (§2; masa 1, ζ = c / 2√k). Ningún k/c fuera de aquí. ──
    // Umbrales por unidad (§2.1): geometría 0,5 px y 5 px/s; opacidad 0,002 y 0,02/s; ángulo 0,1° y
    // 1°/s; porcentaje 0,05 y 0,5/s. Las variantes por unidad comparten el par (k, c).

    /// <summary>Abrir (280/25, ζ 0,75): toda forma que crece. Pasada 2,9 %, 95 % a 186 ms.</summary>
    public static readonly SpringPreset Abrir = new(280, 25, 0.5, 5);

    /// <summary>Cerrar (1200/69, ζ 1,00): toda forma que encoge o se va. 95 % a 137 ms (74 % de Abrir).</summary>
    public static readonly SpringPreset Cerrar = new(1200, 69, 0.5, 5);

    /// <summary>Ajuste (300/35, ζ 1,01): cambios dentro de un mismo modo, alto del panel. 95 % a 279 ms.</summary>
    public static readonly SpringPreset Ajuste = new(300, 35, 0.5, 5);

    /// <summary>Blando (150/24, ζ 0,98): llenados y vaciados lentos de datos. 95 % a 375 ms, sin pasada.</summary>
    public static readonly SpringPreset Blando = new(150, 24, 0.5, 5);

    /// <summary>Atención (300/16, ζ 0,46): solo con <see cref="SpringValue.Impulse"/>. Pico 0,0326·v₀ a los 71 ms.</summary>
    public static readonly SpringPreset Atencion = new(300, 16, 0.5, 5);

    /// <summary>Rápido (900/60, ζ 1,00) en 0..1: opacidades, mezclas de color y salida del hover. 95 % a 159 ms.</summary>
    public static readonly SpringPreset Rapido = new(900, 60, 0.002, 0.02);

    /// <summary>HoverIn (2500/100, ζ 1,00) en 0..1: entrada del hover y vuelta al soltar. 95 % a 95 ms.</summary>
    public static readonly SpringPreset HoverIn = new(2500, 100, 0.002, 0.02);

    /// <summary>HoverIn en geometría (px): el rect del realce que se desliza entre elementos.</summary>
    public static readonly SpringPreset HoverInPx = new(2500, 100, 0.5, 5);

    /// <summary>Alias explícito de <see cref="Rapido"/> en la unidad 0..1 (§2.1).</summary>
    public static readonly SpringPreset Rapido01 = Rapido;

    /// <summary>Ajuste en porcentaje (barras y cifras de cuota).</summary>
    public static readonly SpringPreset AjustePct = new(300, 35, 0.05, 0.5);

    /// <summary>Blando en porcentaje (primer llenado y vaciado por renovación de la cuota).</summary>
    public static readonly SpringPreset BlandoPct = new(150, 24, 0.05, 0.5);

    /// <summary>Ajuste en grados (anillo).</summary>
    public static readonly SpringPreset AjusteAng = new(300, 35, 0.1, 1);

    /// <summary>Blando en grados (primer dibujo y vaciado del anillo).</summary>
    public static readonly SpringPreset BlandoAng = new(150, 24, 0.1, 1);

    // ── Muelles heredados del notch (sus umbrales son los globales). El panel ya no los usa. ──

    /// <summary>Muelle suave por defecto (expandir/contraer el notch), un pelín subamortiguado.</summary>
    public static readonly SpringPreset SpringSoft = new(230, 19);

    /// <summary>Muelle críticamente amortiguado: llega sin pasarse (cambios de tamaño de contenido).</summary>
    public static readonly SpringPreset SpringCritical = new(200, 29);

    /// <summary>Muelle lento y blando (entradas/salidas discretas).</summary>
    public static readonly SpringPreset SpringGentle = new(110, 15.5);

    /// <summary>Muelle subamortiguado para el bote de atención: rebota a propósito.</summary>
    public static readonly SpringPreset SpringAttention = new(230, 9);

    // ── Presets del lenguaje de movimiento (docs/superpowers/specs/2026-10-28-claudebar-motion-language.md §2).
    //    Los usa la coreografía del notch (NotchAnimator); los de arriba se mantienen para quien ya los usa.

    /// <summary>Abrir (ζ 0,75): toda forma que crece. Se pasa un 2,9 % y vuelve; 95 % a 186 ms.</summary>
    public static readonly SpringPreset SpringOpen = new(280, 25);

    /// <summary>Cerrar (ζ 1,00): toda forma que encoge o se va. Sin pasada; 95 % a 137 ms (74 % de Abrir).</summary>
    public static readonly SpringPreset SpringClose = new(1200, 69);

    /// <summary>Ajuste (ζ 1,01): cambios dentro de un mismo modo (texto más largo, barras, números).</summary>
    public static readonly SpringPreset SpringAdjust = new(300, 35);

    /// <summary>Rápido (ζ 1,00): opacidades, sombra y mezclas de color; 95 % a 159 ms.</summary>
    public static readonly SpringPreset SpringFast = new(900, 60);

    /// <summary>Delta máximo (s) que integra un <see cref="SpringValue.Advance"/>; el resto se descarta.</summary>
    public const double SpringMaxDt = 0.05;

    /// <summary>Tamaño máximo (s) de cada subpaso de integración del muelle.</summary>
    public const double SpringSubstep = 0.004;

    /// <summary>Distancia al objetivo por debajo de la cual el muelle se considera en reposo (umbral por defecto de un preset).</summary>
    public const double SpringEpsX = 0.05;

    /// <summary>Velocidad (unidades/s) por debajo de la cual el muelle se considera en reposo (umbral por defecto de un preset).</summary>
    public const double SpringEpsV = 0.5;
}
