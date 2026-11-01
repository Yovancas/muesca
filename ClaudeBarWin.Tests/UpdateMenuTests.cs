using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Updates;

namespace ClaudeBarWin.Tests;

/// <summary>La entrada de actualizaciones del menú de la bandeja según la etapa de <see cref="IAppUpdates"/>.</summary>
public class UpdateMenuTests
{
    private static readonly Strings En = Localization.Get("en");
    private static readonly Strings Es = Localization.Get("es");

    [Fact]
    public void Each_stage_has_its_text()
    {
        Assert.Equal(Es.CheckUpdates, UpdateMenu.Text(UpdateStage.Idle, null, 0, Es));
        Assert.Equal("Buscando actualizaciones…", UpdateMenu.Text(UpdateStage.Checking, null, 0, Es));
        Assert.Equal("Descargando la v0.5.1… 42 %", UpdateMenu.Text(UpdateStage.Downloading, "0.5.1", 0.42, Es));
        Assert.Equal("Reiniciar para actualizar a la v0.5.1", UpdateMenu.Text(UpdateStage.ReadyToApply, "0.5.1", 1, Es));
        Assert.Equal("Restart to update to v0.5.1", UpdateMenu.Text(UpdateStage.ReadyToApply, "0.5.1", 1, En));
        // Falló sin versión (sin red): volver a buscar. Falló la descarga: la versión sigue ahí.
        Assert.Equal(Es.CheckUpdates, UpdateMenu.Text(UpdateStage.Failed, null, 0, Es));
        Assert.Equal(string.Format(Es.UpdateAvailableFmt, "0.5.1"), UpdateMenu.Text(UpdateStage.Failed, "0.5.1", 0, Es));
    }

    [Fact]
    public void Download_progress_is_clamped_and_rounded()
    {
        Assert.Equal("Downloading v0.5.1… 0 %", UpdateMenu.Text(UpdateStage.Downloading, "0.5.1", -1, En));
        Assert.Equal("Downloading v0.5.1… 100 %", UpdateMenu.Text(UpdateStage.Downloading, "0.5.1", 7, En));
        Assert.Equal("Downloading v0.5.1… 67 %", UpdateMenu.Text(UpdateStage.Downloading, "0.5.1", 0.666, En));
    }

    [Fact]
    public void Clicking_does_what_the_text_says()
    {
        Assert.Equal(UpdateMenuAction.Check, UpdateMenu.Action(UpdateStage.Idle, null));
        Assert.Equal(UpdateMenuAction.Check, UpdateMenu.Action(UpdateStage.Failed, null));
        Assert.Equal(UpdateMenuAction.None, UpdateMenu.Action(UpdateStage.Checking, null));
        Assert.Equal(UpdateMenuAction.None, UpdateMenu.Action(UpdateStage.Downloading, "0.5.1"));
        Assert.Equal(UpdateMenuAction.Apply, UpdateMenu.Action(UpdateStage.ReadyToApply, "0.5.1"));
        Assert.Equal(UpdateMenuAction.Apply, UpdateMenu.Action(UpdateStage.Failed, "0.5.1"));
    }

    [Fact]
    public void The_notice_after_checking_says_what_happened()
    {
        Assert.Equal("Estás al día (v0.5.0).", UpdateMenu.AfterCheck(UpdateStage.Idle, null, "0.5.0", "log", Es));
        Assert.Contains("0.5.1", UpdateMenu.AfterCheck(UpdateStage.Downloading, "0.5.1", "0.5.0", "log", Es));
        Assert.Contains("segundo plano", UpdateMenu.AfterCheck(UpdateStage.Downloading, "0.5.1", "0.5.0", "log", Es));
        Assert.Contains("lista", UpdateMenu.AfterCheck(UpdateStage.ReadyToApply, "0.5.1", "0.5.0", "log", Es));
        Assert.Contains(@"C:\x\claudebar.log", UpdateMenu.AfterCheck(UpdateStage.Failed, null, "0.5.0", @"C:\x\claudebar.log", Es));
    }

    [Fact]
    public void Every_new_update_text_exists_in_Spanish_and_falls_back_to_English_elsewhere()
    {
        foreach (var get in new Func<Strings, string>[]
                 {
                     s => s.UpdateDownloadingFmt, s => s.UpdateRestartFmt, s => s.UpdateFoundFmt, s => s.UpdateReadyFmt,
                     s => s.UpdateFailedLogFmt, s => s.UpdatedFmt,
                 })
        {
            Assert.NotEqual(get(En), get(Es));
            Assert.Equal(get(En), get(Localization.Get("de")));
            // Ningún aviso de la bandeja pasa de lo que admite un globo (255).
            Assert.True(string.Format(get(Es), "0.10.10", "100").Length < 255);
        }
    }
}
