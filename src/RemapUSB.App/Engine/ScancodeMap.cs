using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32;
using RemapUSB.Infrastructure;
using RemapUSB.Model;

namespace RemapUSB.Engine;

/// <param name="OriginalScan">Scancode da tecla neutralizada (0xE0xx quando estendida).</param>
/// <param name="TargetScan">Scancode da tecla sem uso que entra no lugar (F13 a F24).</param>
internal sealed record NeutralKey(ushort OriginalScan, ushort TargetScan)
{
    public ushort TargetVk => ScancodeMap.PoolVkFor(TargetScan);

    public ushort OriginalVk => (ushort)Native.MapVirtualKey(OriginalScan, Native.MAPVK_VSC_TO_VK_EX);

    public string Describe() => $"{KeyNames.VkName(OriginalVk)} → {KeyNames.VkName(TargetVk)}";
}

/// <summary>
/// Scancode Map do Windows: troca uma tecla por outra em todos os teclados, no driver.
/// O app usa F13 a F24 como teclas "sem uso" e não mexe em entradas que não sejam dele.
/// Gravar exige administrador e só vale depois de reiniciar.
/// </summary>
internal static class ScancodeMap
{
    private const string KeyPath = @"SYSTEM\CurrentControlSet\Control\Keyboard Layout";
    private const string ValueName = "Scancode Map";

    // F24 primeiro (a que o Proto2a já usava), depois F23 até F13.
    private static readonly (ushort Scan, ushort Vk)[] Pool =
    [
        (0x76, 0x87), (0x6E, 0x86), (0x6D, 0x85), (0x6C, 0x84), (0x6B, 0x83), (0x6A, 0x82),
        (0x69, 0x81), (0x68, 0x80), (0x67, 0x7F), (0x66, 0x7E), (0x65, 0x7D), (0x64, 0x7C),
    ];

    public static ushort PoolVkFor(ushort scan) => Pool.FirstOrDefault(p => p.Scan == scan).Vk;

    private static bool IsOurs(ushort targetScan) => Pool.Any(p => p.Scan == targetScan);

    /// <summary>Todas as entradas gravadas: (novo, original).</summary>
    private static List<(ushort Target, ushort Original)> ReadAll()
    {
        using var key = Registry.LocalMachine.OpenSubKey(KeyPath);
        var result = new List<(ushort, ushort)>();
        if (key?.GetValue(ValueName) is not byte[] map || map.Length < 16)
            return result;

        var count = BitConverter.ToInt32(map, 8);
        for (var i = 0; i < count - 1 && 12 + i * 4 + 4 <= map.Length; i++)
        {
            var offset = 12 + i * 4;
            result.Add((BitConverter.ToUInt16(map, offset), BitConverter.ToUInt16(map, offset + 2)));
        }
        return result;
    }

    /// <summary>Teclas que o app neutralizou e estão gravadas no registro.</summary>
    public static List<NeutralKey> ReadApplied() =>
        ReadAll().Where(e => IsOurs(e.Target)).Select(e => new NeutralKey(e.Original, e.Target)).ToList();

    /// <summary>
    /// O registro só vale depois do boot. Se a chave foi gravada depois do último boot,
    /// ainda está pendente de reinício.
    /// </summary>
    public static bool IsActive()
    {
        using var key = Registry.LocalMachine.OpenSubKey(KeyPath);
        if (key is null || Native.RegQueryInfoKey(key.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, out var lastWrite) != 0)
            return false;

        var written = DateTime.FromFileTimeUtc(lastWrite);
        var boot = DateTime.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);
        return written < boot;
    }

    /// <summary>Teclas que a configuração atual pede para neutralizar, reaproveitando os alvos já gravados.</summary>
    public static List<NeutralKey> Desired(AppConfig config)
    {
        var scans = config.Devices
            .SelectMany(d => d.Buttons)
            .Where(b => b.Part == ButtonPart.Keyboard && b.Original == OriginalMode.Neutralize && b.Action.Type != ActionType.Keep)
            .Select(b => b.ScanCode)
            .Distinct()
            .ToList();

        var applied = ReadApplied();
        var result = applied.Where(a => scans.Contains(a.OriginalScan)).ToList();
        foreach (var scan in scans.Where(s => result.All(r => r.OriginalScan != s)))
        {
            var free = Pool.FirstOrDefault(p => result.All(r => r.TargetScan != p.Scan));
            if (free.Scan == 0)
            {
                Log.Write("ERRO", "mais de 12 teclas neutralizadas; o restante fica sem neutralizar");
                break;
            }
            result.Add(new NeutralKey(scan, free.Scan));
        }
        return result;
    }

    /// <summary>Grava as teclas do app (mantendo as de terceiros). Pede administrador. Devolve false se cancelado.</summary>
    public static async Task<bool> WriteAsync(IReadOnlyList<NeutralKey> ours)
    {
        var entries = ReadAll().Where(e => !IsOurs(e.Target)).ToList();
        entries.AddRange(ours.Select(o => (o.TargetScan, o.OriginalScan)));

        string args;
        if (entries.Count == 0)
        {
            args = $"delete \"HKLM\\{KeyPath}\" /v \"{ValueName}\" /f";
        }
        else
        {
            var bytes = new List<byte>(new byte[8]);
            bytes.AddRange(BitConverter.GetBytes(entries.Count + 1));
            foreach (var (target, original) in entries)
            {
                bytes.AddRange(BitConverter.GetBytes(target));
                bytes.AddRange(BitConverter.GetBytes(original));
            }
            bytes.AddRange(new byte[4]);
            args = $"add \"HKLM\\{KeyPath}\" /v \"{ValueName}\" /t REG_BINARY /d {Convert.ToHexString(bytes.ToArray())} /f";
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo("reg.exe", args)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            })!;
            await process.WaitForExitAsync();
            Log.Write("WINDOWS", process.ExitCode == 0
                ? $"Scancode Map gravado: {(ours.Count == 0 ? "nenhuma tecla do app" : string.Join(", ", ours.Select(o => o.Describe())))}"
                : $"reg.exe terminou com código {process.ExitCode}");
            return process.ExitCode == 0;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            Log.Write("WINDOWS", "gravação do Scancode Map cancelada no pedido de administrador");
            return false;
        }
    }
}
