namespace ClaudeBarWin.Services;

/// <summary>
/// Escalado DPI centralizado (T11, auditoría §2 P0 #1). PerMonitorV2 está activo y las FUENTES ya
/// escalan solas (tamaño en puntos → GDI+ lo convierte con el DPI del Graphics), pero TODA la
/// geometría del panel estaba en px fijos de 96 DPI: al 125/150% (portátiles) las filas se quedaban
/// cortas para el texto crecido (solapes, % pisando barras) y el panel de 340px era enano.
/// <see cref="Scale(int)"/> proyecta un px de diseño (rejilla a 96 DPI) al DPI vigente con UN único
/// redondeo (AwayFromZero, como el escalado de Windows); a 96 DPI (factor 1.0) es identidad EXACTA,
/// así que el render-test queda pixel-perfect.
/// <para>
/// El factor ambiente es <c>[ThreadStatic]</c> a propósito: lo fija el hilo de UI (DashboardForm en
/// ShowConfigured / OnDpiChanged) y lo leen las pasadas de medir/pintar EN ESE MISMO hilo — medir y
/// pintar ven siempre el mismo factor (invariante medir==pintar intacto). Ventajas colaterales: los
/// tests que aplican otro DPI solo afectan a SU hilo (xUnit paraleliza por clases en hilos distintos,
/// un static plano sería una carrera) y el harness de render (PrepareForRender) NUNCA lo aplica, de
/// modo que los PNG se generan a factor 1.0 determinista sea cual sea el monitor de la máquina.
/// </para>
/// </summary>
public static class Dpi
{
    /// <summary>DPI de diseño: todas las constantes de layout del panel están pensadas a 96.</summary>
    public const float BaseDpi = 96f;

    // Nullable + [ThreadStatic]: un inicializador de campo ThreadStatic solo correría en el primer
    // hilo, así que el default por hilo (1.0) se resuelve en la propiedad (null ⇒ 1.0).
    [ThreadStatic] private static float? _factor;

    /// <summary>Factor de escala vigente en ESTE hilo (1.0 = 96 DPI, 1.25 = 120, 1.5 = 144…).</summary>
    public static float Factor => _factor ?? 1f;

    /// <summary>Fija el factor ambiente del hilo actual a partir del DPI real del monitor.</summary>
    public static void Apply(int deviceDpi) => _factor = FactorFor(deviceDpi);

    /// <summary>Factor para un DPI dado (96→1.0, 120→1.25, 144→1.5). DPI inválido (≤0) ⇒ 1.0.</summary>
    public static float FactorFor(int deviceDpi) => deviceDpi <= 0 ? 1f : deviceDpi / BaseDpi;

    /// <summary>Escala un px de diseño (96 DPI) con el factor ambiente del hilo.</summary>
    public static int Scale(int px) => Scale(px, Factor);

    /// <summary>
    /// Escala un px de diseño con un factor explícito (PURO, para tests). Redondeo AwayFromZero
    /// (11·1.5 = 16.5 → 17): el medio píxel sube, como hace el escalado de Windows — Math.Round a
    /// secas (banker's) bajaría a 16. A factor 1.0 es identidad exacta en todo el rango de layout.
    /// </summary>
    public static int Scale(int px, float factor) =>
        (int)Math.Round(px * (double)factor, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Escala SIN redondear (F3 Task 5): amplitudes de movimiento (bote, entrada escalonada) que se
    /// aplican con <c>TranslateTransform(float)</c> para deslizar sin escalones.
    /// </summary>
    public static float ScaleF(float px) => ScaleF(px, Factor);

    /// <summary>Versión pura de <see cref="ScaleF(float)"/> con factor explícito.</summary>
    public static float ScaleF(float px, float factor) => px * factor;
}
