using Microsoft.Win32;
using RemapUSB.Engine;
using RemapUSB.Infrastructure;
using RemapUSB.Input;
using RemapUSB.Model;
using RemapUSB.Ui;

namespace RemapUSB;

/// <summary>Peças que vivem enquanto o app roda, compartilhadas entre a bandeja, a janela e o motor.</summary>
internal sealed class Services : IDisposable
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "RemapUSB";

    public AppConfig Config { get; }
    public RawInputSource Input { get; }
    public Remapper Remapper { get; }
    public TrayIcon Tray { get; }

    public event Action? StateChanged;

    public Services()
    {
        Config = ConfigStore.Load();
        Log.SetEnabled(Config.LogToFile);
        Log.Write("INÍCIO", $"RemapUSB | máquina {Environment.MachineName} | config {ConfigStore.FilePath}");

        Input = new RawInputSource();
        foreach (var device in Config.Devices)
            Log.Write("DONGLE", $"{device.Name} {(Input.IsConnected(device.Key) ? "conectado" : "desconectado")} ao iniciar");

        Remapper = new Remapper(Input);
        Remapper.Apply(Config);

        Tray = new TrayIcon();
        Input.Connected += key => OnConnection(key, connected: true);
        Input.Disconnected += key => OnConnection(key, connected: false);
        UpdateTray();
    }

    public bool Paused
    {
        get => Remapper.Paused;
        set
        {
            Remapper.Paused = value;
            Log.Write("ESTADO", value ? "remapeamento pausado" : "remapeamento retomado");
            Changed();
        }
    }

    public bool StartWithWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunValue) is string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value)
                key.SetValue(RunValue, $"\"{Environment.ProcessPath}\" --tray");
            else
                key.DeleteValue(RunValue, throwOnMissingValue: false);
            Changed();
        }
    }

    public void SaveAndApply()
    {
        ConfigStore.Save(Config);
        Remapper.Apply(Config);
        Changed();
    }

    public DeviceConfig? FindDevice(string key) =>
        Config.Devices.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase));

    private void OnConnection(string key, bool connected)
    {
        var device = FindDevice(key);
        if (device is null)
            return;

        Log.Write("DONGLE", $"{device.Name} {(connected ? "conectado" : "desconectado")}");
        Tray.Notify($"{device.Name} {(connected ? "conectado" : "desconectado")}",
            connected ? "Os remapeamentos voltaram a funcionar." : "Os remapeamentos voltam sozinhos quando ele for reconectado.");
        Changed();
    }

    private void Changed()
    {
        UpdateTray();
        StateChanged?.Invoke();
    }

    private void UpdateTray()
    {
        var status = Config.Devices.Count switch
        {
            0 => "Nenhum dispositivo salvo",
            1 => $"{Config.Devices[0].Name} · {(Input.IsConnected(Config.Devices[0].Key) ? "conectado" : "desconectado")}",
            var n => $"{n} dispositivos · {Config.Devices.Count(d => Input.IsConnected(d.Key))} conectado(s)",
        };
        Tray.Update(status, Paused, StartWithWindows);
    }

    public void Dispose()
    {
        Remapper.Dispose();
        Input.Dispose();
        Tray.Dispose();
        Log.Write("FIM", "RemapUSB encerrado");
    }
}
