using System.Reflection;

namespace ClaudeBarWin.Services;

/// <summary>Acceso al contenido del hook PowerShell embebido como recurso.</summary>
public static class HookScript
{
    /// <summary>Devuelve el contenido de hooks/claudebar-hook.ps1 embebido en el assembly.</summary>
    public static string Contents()
    {
        var asm = Assembly.GetExecutingAssembly();
        // El nombre del recurso es "<RootNamespace>.hooks.claudebar-hook.ps1" => "ClaudeBarWin.hooks.claudebar-hook.ps1"
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("claudebar-hook.ps1", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Recurso claudebar-hook.ps1 no embebido.");
        using var stream = asm.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
