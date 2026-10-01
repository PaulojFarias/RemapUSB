using System.Diagnostics;
using System.Runtime.InteropServices;
using RemapUSB.Probe;
using static RemapUSB.Probe.Native;

namespace RemapUSB.Proto2a;

internal static class Actions
{
    public static void SendKey(ushort vk, bool extended = false)
    {
        SendKeyEvent(vk, 0, isUp: false, extended);
        SendKeyEvent(vk, 0, isUp: true, extended);
    }

    public static void SendKeyEvent(ushort vk, ushort scan, bool isUp, bool extended)
    {
        var flags = (isUp ? KEYEVENTF_KEYUP : 0) | (extended ? KEYEVENTF_EXTENDEDKEY : 0);
        INPUT[] inputs =
        [
            new()
            {
                Type = INPUT_KEYBOARD,
                Data = new InputUnion
                {
                    Keyboard = new KEYBDINPUT { Vk = vk, Scan = scan, Flags = flags, ExtraInfo = Remapper.Marker },
                },
            },
        ];

        if (SendInput(1, inputs, Marshal.SizeOf<INPUT>()) != 1)
            Log.Write($"{Clock.Format(Clock.Ms)} [ERRO]      SendInput falhou, erro {Marshal.GetLastWin32Error()}");
    }

    /// <summary>Fecha o app (se estiver aberto) e abre de novo pelo identificador do pacote.</summary>
    public static void RestartPackagedApp(string? packageFamily, string appId, string processName)
    {
        if (packageFamily is null)
        {
            Log.Write($"{Clock.Format(Clock.Ms)} [ERRO]      pacote do app não encontrado nesta máquina");
            return;
        }

        // Fora da thread do hook: fechar pode levar alguns segundos.
        Task.Run(() =>
        {
            try
            {
                foreach (var process in Process.GetProcessesByName(processName))
                {
                    using (process)
                    {
                        Log.Write($"{Clock.Format(Clock.Ms)} [AÇÃO]      fechando {processName} (PID {process.Id})");
                        if (!process.CloseMainWindow() || !process.WaitForExit(3000))
                        {
                            process.Kill();
                            process.WaitForExit(3000);
                            Log.Write($"{Clock.Format(Clock.Ms)} [AÇÃO]      não fechou sozinho, encerrado à força");
                        }
                    }
                }

                Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{packageFamily}!{appId}") { UseShellExecute = true });
                Log.Write($"{Clock.Format(Clock.Ms)} [AÇÃO]      {packageFamily}!{appId} aberto");
            }
            catch (Exception ex)
            {
                Log.Write($"{Clock.Format(Clock.Ms)} [ERRO]      reiniciar app: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Cada pacote instalado para o usuário tem uma pasta com o nome da família em
    /// %LOCALAPPDATA%\Packages. Assim o nome da família sai da máquina, não fica fixo no código.
    /// </summary>
    public static string? FindPackageFamily(string packageName)
    {
        var packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
        if (!Directory.Exists(packages))
            return null;

        return Directory.GetDirectories(packages, packageName + "_*").Select(Path.GetFileName).FirstOrDefault();
    }
}
