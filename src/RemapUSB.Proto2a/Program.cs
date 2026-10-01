using System.Text;
using Microsoft.Win32;
using RemapUSB.Probe;

namespace RemapUSB.Proto2a;

/// <summary>
/// Protótipo 2a: remapeia Voltar, Home e Menu de contexto do controle sem trocar driver.
/// Os mapeamentos estão fixos aqui; no app eles virão da tela de gravação.
/// </summary>
internal static class Program
{
    private const string DeviceId = "VID_0627&PID_697D";
    private const string TubeTvPackage = "9932MartCliment.YoutubeTVClient";
    private const string TubeTvAppId = "App";
    private const string TubeTvProcess = "TubeTV - Client for YT TV";

    private const ushort VkEscape = 0x1B;
    private const ushort VkApps = 0x5D;
    private const ushort VkF24 = 0x87;
    private const ushort VkBrowserBack = 0xA6;
    private const ushort VkBrowserHome = 0xAC;
    private const ushort VkMediaPlayPause = 0xB3;

    private const ushort ScanApps = 0x5D;

    private const ushort UsageAcBack = 0x0224;
    private const ushort UsageAcHome = 0x0223;

    [STAThread]
    private static void Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        var logPath = Log.Open("proto2a");
        var tubeTv = Actions.FindPackageFamily(TubeTvPackage);

        Log.Write($"RemapUSB Proto2a | máquina {Environment.MachineName} | {DateTime.Now:yyyy-MM-dd HH:mm:ss} | dispositivo {DeviceId}");
        Log.Write($"TubeTV: {tubeTv ?? "NÃO ENCONTRADO (Home vai registrar erro)"}");
        Log.Write(IsAppsMappedToF24()
            ? "Scancode Map: Menu -> F24 configurado (só vale depois de reiniciar o Windows)"
            : "Scancode Map: Menu -> F24 AUSENTE. Rode tools/menu-para-f24.reg e reinicie, senão o Menu do controle não é remapeado");
        Log.Write("Mapeamentos: Voltar -> Esc | Home -> reiniciar TubeTV | Menu de contexto (F24) -> Play/Pause. Ctrl+C para sair.");
        Log.Write();

        Mapping[] mappings =
        [
            new("Voltar", Part.Consumer, UsageAcBack, VkBrowserBack, "Esc",
                () => Actions.SendKey(VkEscape)),
            new("Home", Part.Consumer, UsageAcHome, VkBrowserHome, "reiniciar TubeTV",
                () => Actions.RestartPackagedApp(tubeTv, TubeTvAppId, TubeTvProcess)),
            // Neutralizada pelo Scancode Map: chega como F24 e não é bloqueada.
            new("Menu de contexto", Part.Keyboard, VkF24, HookVk: null, "Play/Pause",
                () => Actions.SendKey(VkMediaPlayPause, extended: true)),
        ];

        using var input = new DeviceInput(DeviceId);
        input.OtherKeyboard += RestoreAppsKey;
        using var remapper = new Remapper(mappings, input);
        Log.Write();
        Console.WriteLine($"Gravando em {logPath}");
        Console.WriteLine();

        Application.Run();
    }

    /// <summary>Em outros teclados o Menu também virou F24; devolve a tecla original.</summary>
    private static void RestoreAppsKey(DeviceEvent e)
    {
        if (e.Code != VkF24)
            return;

        Log.Write($"{Clock.Format(e.At)} [REPASSE]   {(e.IsUp ? "solta " : "aperta")} F24 de outro teclado -> Menu");
        Actions.SendKeyEvent(VkApps, ScanApps, e.IsUp, extended: true);
    }

    private static bool IsAppsMappedToF24()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Keyboard Layout");
        if (key?.GetValue("Scancode Map") is not byte[] map)
            return false;

        // Cada entrada tem 4 bytes: scancode novo (76 00 = F24) e scancode original (5D E0 = Menu).
        byte[] entry = [0x76, 0x00, 0x5D, 0xE0];
        for (var i = 12; i + 4 <= map.Length; i += 4)
        {
            if (map.AsSpan(i, 4).SequenceEqual(entry))
                return true;
        }
        return false;
    }
}
