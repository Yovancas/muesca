using System.Text;
using ClaudeBarWin.Services.Notch;

namespace ClaudeBarWin.Tests;

/// <summary>
/// <see cref="NotchSound"/>: el WAV de dos tonos se sintetiza en memoria (sin recursos ni archivos) y la
/// decisión de sonar es pura: solo Terminado y Aviso 95, nunca en pantalla completa ni con el sonido apagado.
/// </summary>
public class NotchSoundTests
{
    [Fact]
    public void Chime_is_valid_wav()
    {
        var b = NotchSound.BuildChime();
        Assert.Equal("RIFF", Encoding.ASCII.GetString(b, 0, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(b, 8, 4));
        Assert.Equal("fmt ", Encoding.ASCII.GetString(b, 12, 4));
        Assert.Equal("data", Encoding.ASCII.GetString(b, 36, 4));
        Assert.Equal(b.Length - 8, BitConverter.ToInt32(b, 4));         // tamaño del RIFF
        Assert.Equal(1, BitConverter.ToInt16(b, 20));                   // PCM
        Assert.Equal(1, BitConverter.ToInt16(b, 22));                   // mono
        Assert.Equal(22050, BitConverter.ToInt32(b, 24));
        Assert.Equal(16, BitConverter.ToInt16(b, 34));                  // 16 bits
        Assert.Equal(b.Length - 44, BitConverter.ToInt32(b, 40));       // tamaño de los datos
    }

    [Fact]
    public void Chime_duration_is_about_036s()
    {
        var b = NotchSound.BuildChime();
        double seconds = (b.Length - 44) / 2.0 / 22050;
        Assert.InRange(seconds, 0.3, 0.45);
    }

    [Fact]
    public void Chime_is_audible_without_clipping_and_fades_out()
    {
        var b = NotchSound.BuildChime();
        int n = (b.Length - 44) / 2;
        short Sample(int i) => BitConverter.ToInt16(b, 44 + i * 2);
        int peak = Enumerable.Range(0, n).Max(i => Math.Abs((int)Sample(i)));
        Assert.InRange(peak, 4000, short.MaxValue - 1);                 // se oye y no satura
        Assert.True(Math.Abs((int)Sample(0)) < 2000);                   // ataque: empieza casi en silencio
        Assert.True(Math.Abs((int)Sample(n - 1)) < 2000);               // y acaba apagado (sin chasquido)
    }

    [Fact]
    public void Chime_respects_sample_rate()
    {
        var b = NotchSound.BuildChime(44100);
        Assert.Equal(44100, BitConverter.ToInt32(b, 24));
        Assert.InRange((b.Length - 44) / 2.0 / 44100, 0.3, 0.45);
    }

    [Theory]
    [InlineData(NotchAlertKind.Completed, true)]
    [InlineData(NotchAlertKind.QuotaAlert, true)]
    [InlineData(NotchAlertKind.QuotaHud, false)]
    [InlineData(NotchAlertKind.Permission, false)]
    public void Only_Completed_and_QuotaAlert_chime(NotchAlertKind kind, bool expected)
    {
        Assert.Equal(expected, NotchSound.ShouldPlay(kind, soundEnabled: true, fullscreen: false));
    }

    [Fact]
    public void Silent_when_disabled_or_fullscreen()
    {
        Assert.False(NotchSound.ShouldPlay(NotchAlertKind.Completed, soundEnabled: false, fullscreen: false));
        Assert.False(NotchSound.ShouldPlay(NotchAlertKind.Completed, soundEnabled: true, fullscreen: true));
    }

    [Theory]
    [InlineData(80, NotchAlertKind.QuotaHud)]
    [InlineData(94, NotchAlertKind.QuotaHud)]
    [InlineData(95, NotchAlertKind.QuotaAlert)]
    [InlineData(100, NotchAlertKind.QuotaAlert)]
    public void Quota_crossing_kind_matches_the_state_machine(int threshold, NotchAlertKind expected)
    {
        Assert.Equal(expected, NotchStateMachine.QuotaAlertKind(threshold));
    }

    [Fact]
    public void Answerable_permission_chimes_but_read_only_does_not()
    {
        // Sonido de atención (F2): solo el Permiso con botones (petición del helper) pide al usuario que actúe en el notch.
        Assert.True(NotchSound.ShouldPlay(NotchAlertKind.Permission, soundEnabled: true, fullscreen: false, answerable: true));
        Assert.False(NotchSound.ShouldPlay(NotchAlertKind.Permission, soundEnabled: true, fullscreen: false, answerable: false));
        Assert.False(NotchSound.ShouldPlay(NotchAlertKind.Permission, soundEnabled: false, fullscreen: false, answerable: true));
        Assert.False(NotchSound.ShouldPlay(NotchAlertKind.Permission, soundEnabled: true, fullscreen: true, answerable: true));
        Assert.False(NotchSound.ShouldPlay(NotchAlertKind.QuotaHud, soundEnabled: true, fullscreen: false, answerable: true));
    }

    [Fact]
    public void ChimeKey_dedupes_permission_by_request_and_other_alerts_by_instance()
    {
        var t = DateTime.UtcNow;
        var a = new NotchAlert(NotchAlertKind.Permission, "s", "proj", "Bash: ls", t, null, "r1");
        var reArmed = a with { Detail = "Bash: ls -la" };          // el texto cambia y se vuelve a armar: misma petición
        var other = a with { RequestId = "r2" };
        Assert.Equal(NotchSound.ChimeKey(a), NotchSound.ChimeKey(reArmed));
        Assert.NotEqual(NotchSound.ChimeKey(a), NotchSound.ChimeKey(other));
        var readOnly = a with { RequestId = null };
        Assert.Null(NotchSound.ChimeKey(readOnly));
        var done = new NotchAlert(NotchAlertKind.Completed, "s", "proj", null, t, NotchTiming.Completed);
        Assert.Null(NotchSound.ChimeKey(done));
    }
}
