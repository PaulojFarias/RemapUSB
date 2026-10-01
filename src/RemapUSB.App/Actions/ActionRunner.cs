using System.Diagnostics;
using System.Runtime.InteropServices;
using RemapUSB.Engine;
using RemapUSB.Infrastructure;
using RemapUSB.Model;
using static RemapUSB.Infrastructure.Native;

namespace RemapUSB.Actions;

internal static class ActionRunner
{
    /// <summary>Marca as teclas que o próprio app envia, para o hook deixá-las passar.</summary>
    public static readonly IntPtr Marker = new(0x52454D50);

    public static void Run(ActionConfig action, string buttonName)
    {
        switch (action.Type)
        {
            case ActionType.Key when action.Keys is { Count: > 0 }:
                SendCombo(action.Keys);
                break;
            case ActionType.Media when action.MediaVk is { } vk:
                SendKeyEvent(vk, 0, isUp: false);
                SendKeyEvent(vk, 0, isUp: true);
                break;
            case ActionType.OpenApp or ActionType.CloseApp or ActionType.ToggleApp or ActionType.RestartApp when action.App is not null:
                // Fechar pode levar segundos: fora da thread do hook.
                var app = action.App;
                var type = action.Type;
                Task.Run(() => RunApp(type, app, buttonName));
                break;
            case ActionType.Site when !string.IsNullOrWhiteSpace(action.Url):
                Shell(action.Url!, buttonName);
                break;
            case ActionType.File when !string.IsNullOrWhiteSpace(action.Path):
                Shell(action.Path!, buttonName);
                break;
            case ActionType.Command when !string.IsNullOrWhiteSpace(action.Command):
                Task.Run(() => RunCommand(action.Command!, buttonName));
                break;
        }
    }

    public static void SendKeyEvent(ushort vk, ushort scan, bool isUp, bool? extended = null)
    {
        var flags = (isUp ? KEYEVENTF_KEYUP : 0) | ((extended ?? KeyNames.IsExtended(vk)) ? KEYEVENTF_EXTENDEDKEY : 0);
        INPUT[] inputs =
        [
            new()
            {
                Type = INPUT_KEYBOARD,
                Data = new InputUnion { Keyboard = new KEYBDINPUT { Vk = vk, Scan = scan, Flags = flags, ExtraInfo = Marker } },
            },
        ];
        if (SendInput(1, inputs, Marshal.SizeOf<INPUT>()) != 1)
            Log.Write("ERRO", $"SendInput falhou, erro {Marshal.GetLastWin32Error()}");
    }

    private static void SendCombo(List<ushort> keys)
    {
        foreach (var vk in keys)
            SendKeyEvent(vk, 0, isUp: false);
        for (var i = keys.Count - 1; i >= 0; i--)
            SendKeyEvent(keys[i], 0, isUp: true);
    }

    private static readonly HashSet<string> Busy = new(StringComparer.OrdinalIgnoreCase);

    private static void RunApp(ActionType type, AppRef app, string buttonName)
    {
        // Apertar de novo enquanto o app ainda está fechando dispararia um segundo fechamento.
        var id = app.Kind == AppKind.Package ? $"{app.PackageFamily}!{app.AppId}" : app.ExePath ?? app.Name;
        lock (Busy)
        {
            if (!Busy.Add(id))
            {
                Log.Write("AÇÃO", $"{buttonName}: ação em {app.Name} ainda em andamento, ignorada");
                return;
            }
        }

        List<Process> running = [];
        try
        {
            running = FindProcesses(app);
            switch (type)
            {
                case ActionType.OpenApp:
                    if (running.Count > 0)
                        BringToFront(running);
                    else
                        Launch(app);
                    break;
                case ActionType.CloseApp:
                    if (running.Count > 0)
                        Close(app, running);
                    else
                        Log.Write("AÇÃO", $"{app.Name} não estava aberto");
                    break;
                case ActionType.ToggleApp:
                    if (running.Count > 0)
                        Close(app, running);
                    else
                        Launch(app);
                    break;
                case ActionType.RestartApp:
                    if (running.Count > 0)
                        Close(app, running);
                    Launch(app);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Write("ERRO", $"{buttonName}: {ex.Message}");
        }
        finally
        {
            foreach (var process in running)
                process.Dispose();
            lock (Busy)
                Busy.Remove(id);
        }
    }

    private static List<Process> FindProcesses(AppRef app)
    {
        var folder = app.Kind == AppKind.Package && app.PackageFamily is not null ? AppCatalog.PackageRoot(app.PackageFamily) : null;
        var result = new List<Process>();
        foreach (var process in Process.GetProcesses())
        {
            var path = GetProcessPath(process.Id);
            var match = path is not null && (app.Kind == AppKind.Exe
                ? string.Equals(path, app.ExePath, StringComparison.OrdinalIgnoreCase)
                : folder is not null && path.StartsWith(folder.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase));
            if (match)
                result.Add(process);
            else
                process.Dispose();
        }
        return result;
    }

    private static void Launch(AppRef app)
    {
        if (app.Kind == AppKind.Package)
            Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{app.PackageFamily}!{app.AppId}") { UseShellExecute = true });
        else
            Process.Start(new ProcessStartInfo(app.ExePath!) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(app.ExePath) });
        Log.Write("AÇÃO", $"{app.Name} aberto");
    }

    /// <summary>Pede às janelas para fechar (como o X). Se em 3s não fechar, encerra à força.</summary>
    private static void Close(AppRef app, List<Process> processes)
    {
        var ids = processes.Select(p => (uint)p.Id).ToHashSet();
        var windows = TopWindows(ids);
        foreach (var hwnd in windows)
            PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

        if (windows.Count == 0)
        {
            // Sem janela para pedir, esperar só atrasa: encerra já, e registra o que havia na tela.
            Log.Write("AÇÃO", $"{app.Name}: nenhuma janela encontrada para pedir o fechamento. {DescribeWindows(ids)}");
            foreach (var process in processes)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(3000);
            }
            Log.Write("AÇÃO", $"{app.Name} encerrado à força");
            return;
        }

        var deadline = DateTime.UtcNow.AddSeconds(3);
        foreach (var process in processes)
        {
            var left = deadline - DateTime.UtcNow;
            if (!process.WaitForExit(left > TimeSpan.Zero ? left : TimeSpan.Zero))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(3000);
                Log.Write("AÇÃO", $"{app.Name} (PID {process.Id}) não fechou pelas janelas, encerrado à força");
            }
        }
        Log.Write("AÇÃO", $"{app.Name} fechado ({windows.Count} janela(s) receberam o pedido de fechar)");
    }

    private static void BringToFront(List<Process> processes)
    {
        var hwnd = TopWindows(processes.Select(p => (uint)p.Id).ToHashSet()).FirstOrDefault();
        if (hwnd == IntPtr.Zero)
            return;
        if (IsIconic(hwnd))
            ShowWindow(hwnd, SW_RESTORE);
        SetForegroundWindow(hwnd);
        Log.Write("AÇÃO", "app já estava aberto, trazido para a frente");
    }

    /// <summary>
    /// Janelas principais dos processos. Em app UWP a janela de cima é do ApplicationFrameHost
    /// e só a filha (CoreWindow) é do processo do app, então a moldura entra pela filha.
    /// </summary>
    private static List<IntPtr> TopWindows(HashSet<uint> processIds)
    {
        var result = new List<IntPtr>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) || GetWindow(hwnd, GW_OWNER) != IntPtr.Zero)
                return true;

            GetWindowThreadProcessId(hwnd, out var pid);
            if (processIds.Contains(pid))
            {
                result.Add(hwnd);
            }
            else if (ClassName(hwnd) == "ApplicationFrameWindow")
            {
                EnumChildWindows(hwnd, (child, _) =>
                {
                    GetWindowThreadProcessId(child, out var childPid);
                    if (!processIds.Contains(childPid))
                        return true;
                    result.Add(hwnd);
                    return false;
                }, IntPtr.Zero);
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }

    /// <summary>Diagnóstico: janelas dos processos do app e molduras UWP, para entender por que nenhuma serviu.</summary>
    private static string DescribeWindows(HashSet<uint> processIds)
    {
        var own = new List<string>();
        var frames = new List<string>();
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            var cls = ClassName(hwnd);
            var flags = $"{(IsWindowVisible(hwnd) ? "visível" : "oculta")}{(GetWindow(hwnd, GW_OWNER) != IntPtr.Zero ? ", com dono" : "")}";
            if (processIds.Contains(pid))
                own.Add($"{cls} ({flags})");
            else if (cls == "ApplicationFrameWindow")
            {
                var children = new List<string>();
                EnumChildWindows(hwnd, (child, _) =>
                {
                    GetWindowThreadProcessId(child, out var childPid);
                    children.Add($"{ClassName(child)}{(processIds.Contains(childPid) ? "*" : "")}");
                    return true;
                }, IntPtr.Zero);
                frames.Add($"[{flags}: {string.Join(", ", children)}]");
            }
            return true;
        }, IntPtr.Zero);

        return $"Janelas do app: {(own.Count == 0 ? "nenhuma" : string.Join("; ", own.Take(10)))}. "
            + $"Molduras UWP (* = do app): {(frames.Count == 0 ? "nenhuma" : string.Join(" ", frames.Take(10)))}";
    }

    private static void Shell(string target, string buttonName)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            Log.Write("AÇÃO", $"aberto: {target}");
        }
        catch (Exception ex)
        {
            Log.Write("ERRO", $"{buttonName}: não abriu {target}: {ex.Message}");
        }
    }

    private static void RunCommand(string command, string buttonName)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c {command}") { CreateNoWindow = true, UseShellExecute = false });
            Log.Write("AÇÃO", $"comando executado: {command}");
        }
        catch (Exception ex)
        {
            Log.Write("ERRO", $"{buttonName}: comando falhou: {ex.Message}");
        }
    }
}
