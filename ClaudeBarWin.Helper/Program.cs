using System.Reflection;
using System.Text;

namespace ClaudeBarWin.Helper;

internal static class Program
{
    private static int Main(string[] args)
    {
        var mode = args.Length > 0 ? args[0] : string.Empty;
        if (mode == "--version")
        {
            try
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version;
                Console.Out.WriteLine(version is null ? "?" : version.ToString(3));
            }
            catch { }
            return 0;
        }

        if (mode == "statusline")
        {
            RunStatusline(args);
            return 0;
        }

        if (mode == "hook")
        {
            RunHook();
            return 0;
        }

        if (mode == "permission")
        {
            RunPermission();
            return 0;
        }

        try { Console.Out.WriteLine("usage: claudebar-helper statusline [--tee] | hook | permission | --version"); }
        catch { }
        return 2;
    }

    private static void RunStatusline(string[] args)
    {
        try
        {
            var input = ReadAll(Console.OpenStandardInput());
            StatuslineTap.Run(
                input,
                Console.OpenStandardOutput(),
                args.Contains("--tee", StringComparer.Ordinal),
                HelperPaths.RateLimitsDir(),
                DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        }
        catch
        {
            // Status-line integration is strictly best effort and always exits zero.
        }
    }

    private static void RunHook()
    {
        try
        {
            HookForwarder.Run(ReadAll(Console.OpenStandardInput()), HookForwarder.DefaultPipeName, 100);
        }
        catch
        {
            // Hook integration is strictly best effort and always exits zero.
        }
    }

    private static void RunPermission()
    {
        try
        {
            var verdict = PermissionClient.Ask(
                ReadAll(Console.OpenStandardInput()),
                PermissionProtocol.PipeName,
                PermissionProtocol.ConnectTimeoutMs,
                PermissionProtocol.DecisionTimeoutMs,
                PermissionClient.IsTrustedServer);

            // stdout solo lleva la decisión exacta; sin decisión explícita queda vacío y Claude Code muestra su diálogo.
            var decision = PermissionProtocol.RenderDecision(verdict);
            if (decision.Length == 0)
                return;

            using var stdout = Console.OpenStandardOutput();
            stdout.Write(Encoding.ASCII.GetBytes(decision));
            stdout.Flush();
        }
        catch
        {
            // Fallo seguro: sin decisión, exit 0 y nada en stdout ni stderr.
        }
    }

    private static byte[] ReadAll(Stream input)
    {
        using var buffer = new MemoryStream();
        input.CopyTo(buffer);
        return buffer.ToArray();
    }
}
