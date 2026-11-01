using ClaudeBarWin.Services.Motion;

namespace ClaudeBarWin.Tests;

public class SpringValueTests
{
    // Críticamente amortiguado: tras ~3 s llega al objetivo y el umbral lo encaja exacto (sin residuo).
    [Fact]
    public void Settles_on_target_and_snaps()
    {
        var s = new SpringValue(0, Motion.SpringCritical);
        s.SetTarget(100);
        for (int i = 0; i < 200; i++) s.Advance(1 / 60.0);
        Assert.True(s.IsSettled);
        Assert.Equal(100, s.Value);
        Assert.Equal(0, s.Velocity);
    }

    // El preset de atención está subamortiguado a propósito: tiene que pasarse del objetivo.
    [Fact]
    public void Underdamped_overshoots()
    {
        var s = new SpringValue(0, Motion.SpringAttention);
        s.SetTarget(100);
        double max = 0;
        for (int i = 0; i < 120; i++) { s.Advance(1 / 60.0); max = Math.Max(max, s.Value); }
        Assert.True(max > 100);
    }

    // Re-apuntar a media animación no toca la velocidad: el movimiento sigue sin tirón.
    [Fact]
    public void Retarget_keeps_velocity_continuous()
    {
        var s = new SpringValue(0, Motion.SpringSoft);
        s.SetTarget(100);
        for (int i = 0; i < 5; i++) s.Advance(1 / 60.0);
        var v = s.Velocity;
        s.SetTarget(50);
        Assert.Equal(v, s.Velocity);
    }

    // Un frame enorme (PC suspendido, depurador) se recorta a SpringMaxDt: nada explota.
    [Fact]
    public void Large_dt_is_clamped_and_stable()
    {
        var s = new SpringValue(0, Motion.SpringSoft);
        s.SetTarget(100);
        s.Advance(5.0);
        Assert.InRange(s.Value, -1000, 1000);
    }

    // Los subpasos fijos hacen que 60 Hz y 240 Hz den (casi) la misma curva.
    [Fact]
    public void Same_result_regardless_of_frame_rate()
    {
        SpringValue a = new(0, Motion.SpringSoft), b = new(0, Motion.SpringSoft);
        a.SetTarget(100);
        b.SetTarget(100);
        for (int i = 0; i < 30; i++) a.Advance(1 / 60.0);
        for (int i = 0; i < 120; i++) b.Advance(1 / 240.0);
        Assert.Equal(a.Value, b.Value, 0);
    }

    // Un impulso despierta a un muelle en reposo (el «bote» de atención del notch).
    [Fact]
    public void Impulse_moves_settled_spring()
    {
        var s = new SpringValue(10, Motion.SpringAttention);
        s.Impulse(260);
        Assert.False(s.IsSettled);
    }
}
