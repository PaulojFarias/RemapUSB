using System.Windows;
using Microsoft.Win32;
using RemapUSB.Engine;
using RemapUSB.Infrastructure;
using RemapUSB.Model;
using RemapUSB.Ui;

namespace RemapUSB;

/// <summary>
/// Peças que vivem enquanto o app roda, compartilhadas entre a bandeja, a janela e o motor.
/// O motor roda na thread de entrada (InputHost); os eventos daqui já chegam na thread da interface.
/// </summary>
internal sealed class Services : IDisposable
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "RemapUSB";

    private readonly InputHost _host;

    public AppConfig Config { get; }
    public TrayIcon Tray { get; }

    public event Action? StateChanged;
    public event Action<string>? DeviceConnected;
    public event Action<RecordedButton>? Recorded;

    public Services()
    {
        Config = ConfigStore.Load();
        Log.SetEnabled(Config.LogToFile);
        Log.Write("INÍCIO", $"RemapUSB | build {BuildInfo.Describe()} | máquina {Environment.MachineName} | config {ConfigStore.FilePath}");
        if (!Native.DisablePowerThrottling())
            Log.Write("ERRO", "não deu para desligar o modo de economia do Windows para o app");

        MigrateNeutralizedButtons();

        _host = new InputHost();
        foreach (var device in Config.Devices)
            Log.Write("DONGLE", $"{device.Name} {(IsConnected(device.Key) ? "conectado" : "desconectado")} ao iniciar");

        var ui = Application.Current.Dispatcher;
        _host.Input.Connected += key => ui.BeginInvoke(() => OnConnection(key, connected: true));
        _host.Input.Disconnected += key => ui.BeginInvoke(() => OnConnection(key, connected: false));
        _host.Remapper.Recorded += recorded => ui.BeginInvoke(() => Recorded?.Invoke(recorded));
        ApplyToEngine();

        Tray = new TrayIcon();
        UpdateTray();
    }

    public bool Paused
    {
        get => _host.Remapper.Paused;
        set
        {
            _host.Remapper.Paused = value;
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

    public bool IsConnected(string deviceKey) => _host.Input.IsConnected(deviceKey);

    public List<string> DescribeParts(string deviceKey) => _host.Call(() => _host.Input.DescribeParts(deviceKey));

    public string? ProductName(string deviceKey) => _host.Call(() => _host.Input.ProductName(deviceKey));

    public void StartRecording(string deviceKey) => _host.Post(() => _host.Remapper.StartRecording(deviceKey));

    public void StopRecording() => _host.Post(() => _host.Remapper.StopRecording());

    public void SaveAndApply()
    {
        ConfigStore.Save(Config);
        ApplyToEngine();
        Changed();
    }

    public DeviceConfig? FindDevice(string key) =>
        Config.Devices.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase));

    private void ApplyToEngine()
    {
        var snapshot = ConfigStore.Clone(Config);
        _host.Post(() => _host.Remapper.Apply(snapshot));
    }

    /// <summary>
    /// Botão de teclado gravado com a tecla já neutralizada (F13..F24) vira a tecla original.
    /// Corrige configurações gravadas antes de o app reconhecer a tecla neutralizada só pelo VK.
    /// </summary>
    private void MigrateNeutralizedButtons()
    {
        var applied = ScancodeMap.ReadApplied();
        var changed = false;
        foreach (var button in Config.Devices.SelectMany(d => d.Buttons).Where(b => b.Part == ButtonPart.Keyboard))
        {
            var neutral = applied.FirstOrDefault(a => a.TargetVk == button.Code);
            if (neutral is null)
                continue;

            var original = KeyNames.VkName(neutral.OriginalVk);
            Log.Write("MIGRAÇÃO", $"botão {button.Name}: {button.KeyName} → {original} (tecla neutralizada)");
            if (button.Name == button.KeyName)
                button.Name = original;
            button.Code = neutral.OriginalVk;
            button.ScanCode = neutral.OriginalScan;
            button.KeyName = original;
            button.Original = OriginalMode.Neutralize;
            changed = true;
        }
        if (changed)
            ConfigStore.Save(Config);
    }

    private void OnConnection(string key, bool connected)
    {
        if (connected)
            DeviceConnected?.Invoke(key);

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
            1 => $"{Config.Devices[0].Name} · {(IsConnected(Config.Devices[0].Key) ? "conectado" : "desconectado")}",
            var n => $"{n} dispositivos · {Config.Devices.Count(d => IsConnected(d.Key))} conectado(s)",
        };
        Tray.Update(status, Paused, StartWithWindows);
    }

    public void Dispose()
    {
        _host.Dispose();
        Tray.Dispose();
        Log.Write("FIM", "RemapUSB encerrado");
        Log.Flush();
    }
}
