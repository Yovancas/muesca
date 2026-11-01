using ClaudeBarWin.Services.Packaging;

/// <summary>
/// Edición de la Store (docs/msix.md, «Reapertura al iniciar sesión»): Windows solo vuelve a abrir la app después de una
/// actualización de la Store si está registrada con <c>RegisterApplicationRestart</c>, pero una app registrada también es
/// «reiniciable» y, con «Guardar automáticamente mis apps reiniciables y reiniciarlas al iniciar sesión», Windows la reabriría
/// al iniciar sesión aunque «Iniciar con Windows» esté apagado. Por eso solo se registra al recibir el aviso de cierre por
/// actualización (<c>ENDSESSION_CLOSEAPP</c>) y se desregistra en cualquier otro caso.
/// </summary>
public class StoreRestartTests
{
    private const long CloseApp = StoreRestart.EndSessionCloseApp;
    private const long Critical = StoreRestart.EndSessionCritical;
    private const long Logoff = StoreRestart.EndSessionLogoff;

    [Theory]
    [InlineData(CloseApp)]
    [InlineData(CloseApp | Critical)]   // la Store o Restart Manager con prisa: sigue siendo para reemplazar archivos
    public void The_close_for_an_update_registers_the_restart(long lParam) =>
        Assert.Equal(StoreRestart.Step.Register, StoreRestart.OnQueryEndSession(lParam));

    [Theory]
    [InlineData(0L)]                    // apagar o reiniciar el equipo
    [InlineData(Critical)]
    [InlineData(Logoff)]                // cerrar sesión
    [InlineData(Logoff | Critical)]
    [InlineData(Logoff | CloseApp)]     // si dice que se cierra la sesión, es la sesión: nada de reabrir al volver
    public void Shutting_down_or_signing_out_never_leaves_the_app_restartable(long lParam) =>
        Assert.Equal(StoreRestart.Step.Unregister, StoreRestart.OnQueryEndSession(lParam));

    [Fact]
    public void The_registration_is_kept_only_while_the_update_actually_closes_the_app()
    {
        Assert.Equal(StoreRestart.Step.Keep, StoreRestart.OnEndSession(ending: true, CloseApp));
        Assert.Equal(StoreRestart.Step.Keep, StoreRestart.OnEndSession(ending: true, CloseApp | Critical));
        // Otra aplicación canceló el cierre: la app sigue abierta y no debe quedarse registrada.
        Assert.Equal(StoreRestart.Step.Unregister, StoreRestart.OnEndSession(ending: false, CloseApp));
        Assert.Equal(StoreRestart.Step.Unregister, StoreRestart.OnEndSession(ending: false, 0));
        Assert.Equal(StoreRestart.Step.Unregister, StoreRestart.OnEndSession(ending: true, 0));
        Assert.Equal(StoreRestart.Step.Unregister, StoreRestart.OnEndSession(ending: true, Logoff | CloseApp));
    }

    [Fact]
    public void Flags_are_read_the_same_from_a_32_or_64_bit_lparam()
    {
        // En 32 bits, ENDSESSION_LOGOFF (0x80000000) llega como un IntPtr negativo.
        Assert.Equal(Logoff, StoreRestart.Flags(new IntPtr(unchecked((int)0x80000000))) & Logoff);
        Assert.Equal(Logoff | CloseApp, StoreRestart.Flags(new IntPtr(unchecked((int)0x80000001))));
        Assert.Equal(CloseApp, StoreRestart.Flags(new IntPtr(1)));
        Assert.Equal(0L, StoreRestart.Flags(IntPtr.Zero));
    }

    [Fact]
    public void The_restart_is_never_for_a_crash_a_hang_or_a_reboot()
    {
        // RESTART_NO_CRASH | RESTART_NO_HANG | RESTART_NO_REBOOT: solo tras la actualización.
        Assert.Equal(1 | 2 | 8, StoreRestart.RestartFlags);
    }
}
