using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ClaudeBarWin.Services.Packaging;

/// <summary>Lo que lleva dentro un alias de ejecución: paquete, aplicación (<c>familia!Id</c>) y el exe al que lanza.</summary>
public sealed record AppExecLinkInfo(string PackageFamilyName, string AppUserModelId, string TargetPath)
{
    /// <summary>¿Lanza el exe <paramref name="exeName"/> de una aplicación del paquete <paramref name="familyName"/>?</summary>
    public bool Launches(string familyName, string exeName) =>
        string.Equals(PackageFamilyName, familyName, StringComparison.OrdinalIgnoreCase)
        && AppUserModelId.StartsWith(familyName + "!", StringComparison.OrdinalIgnoreCase)
        && string.Equals(Path.GetFileName(TargetPath), exeName, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Lee el destino de un alias de ejecución de MSIX (<c>%LOCALAPPDATA%\Microsoft\WindowsApps\x.exe</c>): un archivo de 0
/// bytes con un punto de reanálisis <c>IO_REPARSE_TAG_APPEXECLINK</c>. Sirve para no escribir en settings.json un alias que
/// otro paquete haya declarado con el mismo nombre («If multiple apps register for the same alias, the system will invoke
/// the last one that was registered»). El formato de los datos NO está documentado: se sigue el que usa Cygwin para tratar
/// estos alias como enlaces (versión y cuatro cadenas UTF-16 terminadas en NUL: paquete, aplicación, exe y tipo). Si no se
/// puede leer o no cuadra, null: quien llama decide sin él.
/// </summary>
public static class AppExecLink
{
    internal const uint ReparseTag = 0x8000001B;   // IO_REPARSE_TAG_APPEXECLINK

    private const uint FsctlGetReparsePoint = 0x000900A8;
    private const uint FileReadAttributes = 0x80;
    private const uint FileShareAll = 0x7;
    private const uint OpenExisting = 3;
    private const uint FileFlagOpenReparsePoint = 0x00200000, FileFlagBackupSemantics = 0x02000000;
    private const int MaxReparseBuffer = 16 * 1024;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition,
        uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, IntPtr inBuffer, uint inSize, byte[] outBuffer,
        uint outSize, out uint returned, IntPtr overlapped);

    /// <summary>El destino del alias de <paramref name="path"/>; null si no es un alias, no existe o no se puede leer.</summary>
    public static AppExecLinkInfo? TryRead(string path)
    {
        try
        {
            using var handle = CreateFile(path, FileReadAttributes, FileShareAll, IntPtr.Zero, OpenExisting,
                FileFlagOpenReparsePoint | FileFlagBackupSemantics, IntPtr.Zero);
            if (handle.IsInvalid) return null;
            var buffer = new byte[MaxReparseBuffer];
            if (!DeviceIoControl(handle, FsctlGetReparsePoint, IntPtr.Zero, 0, buffer, (uint)buffer.Length, out uint returned, IntPtr.Zero))
                return null;
            return Parse(buffer.AsSpan(0, (int)Math.Min(returned, (uint)buffer.Length)));
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// <c>REPARSE_DATA_BUFFER</c> de un alias → su destino. Etiqueta (4 bytes), longitud de los datos (2), reservado (2) y los
    /// datos: versión (4) y las cadenas. Hacen falta al menos las tres primeras, no vacías, y que la aplicación sea del
    /// paquete (<c>familia!Id</c>). PURO.
    /// </summary>
    internal static AppExecLinkInfo? Parse(ReadOnlySpan<byte> reparse)
    {
        if (reparse.Length < 12 || BinaryPrimitives.ReadUInt32LittleEndian(reparse) != ReparseTag) return null;
        int dataLength = BinaryPrimitives.ReadUInt16LittleEndian(reparse[4..]);
        if (dataLength < 4 || 8 + dataLength > reparse.Length) return null;
        var strings = reparse.Slice(12, dataLength - 4);
        if (strings.Length % 2 != 0) strings = strings[..^1];

        var text = Encoding.Unicode.GetString(strings);
        var parts = text.Split('\0');
        if (parts.Length < 4) return null;   // tres cadenas, cada una con su NUL
        string family = parts[0], app = parts[1], target = parts[2];
        if (family.Length == 0 || target.Length == 0 || !app.StartsWith(family + "!", StringComparison.OrdinalIgnoreCase)) return null;
        return new AppExecLinkInfo(family, app, target);
    }
}
