using System.Text;
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
    private const ushort VkBrowserBack = 0xA6;
    private const ushort VkBrowserHome = 0xAC;
    private const ushort VkMediaPlayPause = 0xB3;

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
        Log.Write("Mapeamentos: Voltar -> Esc | Home -> reiniciar TubeTV | Menu de contexto -> Play/Pause. Ctrl+C para sair.");
        Log.Write();

        Mapping[] mappings =
        [
            new("Voltar", Part.Consumer, UsageAcBack, VkBrowserBack, "Esc",
                () => Actions.SendKey(VkEscape)),
            new("Home", Part.Consumer, UsageAcHome, VkBrowserHome, "reiniciar TubeTV",
                () => Actions.RestartPackagedApp(tubeTv, TubeTvAppId, TubeTvProcess)),
            new("Menu de contexto", Part.Keyboard, VkApps, VkApps, "Play/Pause",
                () => Actions.SendKey(VkMediaPlayPause, extended: true)),
        ];

        using var input = new DeviceInput(DeviceId);
        using var remapper = new Remapper(mappings, input);
        Log.Write();
        Console.WriteLine($"Gravando em {logPath}");
        Console.WriteLine();

        Application.Run();
    }
}
