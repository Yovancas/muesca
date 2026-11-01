using ClaudeBarWin.Services.Motion;

namespace ClaudeBarWin.Tests;

public class MotionPrefsTests
{
    // El helper envuelve SPI_GETCLIENTAREAANIMATION y NUNCA lanza: si el SO no responde, fallback
    // a false (animaciones permitidas). Se deja disponible para una futura opción "seguir Windows",
    // pero el DEFAULT del toggle NO depende de él (AppConfig.ReduceMotion = false).
    [Fact]
    public void OsReducedMotion_does_not_throw_and_returns_a_bool()
    {
        // No asumimos el valor del SO de CI; basta con que sea determinista y no reviente.
        bool a = MotionPrefs.OsReducedMotion();
        bool b = MotionPrefs.OsReducedMotion();
        Assert.Equal(a, b);
    }
}
