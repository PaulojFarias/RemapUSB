using System.Text.Json;
using System.Text.Json.Serialization;
using RemapUSB.Infrastructure;

namespace RemapUSB.Model;

internal enum ButtonPart { Keyboard, Consumer }

internal enum OriginalMode { Pass, Neutralize }

internal enum ActionType { Keep, None, Key, Media, OpenApp, CloseApp, ToggleApp, RestartApp, Site, File, Command }

internal enum AppKind { Package, Exe }

/// <summary>App alvo de uma ação. Pacote é identificado pela família (sobrevive a atualização).</summary>
internal sealed class AppRef
{
    public AppKind Kind { get; set; }
    public string Name { get; set; } = "";
    public string? PackageFamily { get; set; }
    public string? AppId { get; set; }
    public string? ExePath { get; set; }

    public bool SameAs(AppRef other)
    {
        if (Kind != other.Kind)
            return false;
        return Kind == AppKind.Package
            ? string.Equals(PackageFamily, other.PackageFamily, StringComparison.OrdinalIgnoreCase) && AppId == other.AppId
            : string.Equals(ExePath, other.ExePath, StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class ActionConfig
{
    public ActionType Type { get; set; } = ActionType.Keep;
    public AppRef? App { get; set; }
    public List<ushort>? Keys { get; set; }
    public ushort? MediaVk { get; set; }
    public string? Url { get; set; }
    public string? Path { get; set; }
    public string? Command { get; set; }

    public ActionConfig Clone() => new()
    {
        Type = Type,
        App = App is null ? null : new AppRef { Kind = App.Kind, Name = App.Name, PackageFamily = App.PackageFamily, AppId = App.AppId, ExePath = App.ExePath },
        Keys = Keys is null ? null : [.. Keys],
        MediaVk = MediaVk,
        Url = Url,
        Path = Path,
        Command = Command,
    };
}

internal sealed class ButtonConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public ButtonPart Part { get; set; }

    /// <summary>Teclado: VK original. Mídia: uso HID (ex.: 0x0223).</summary>
    public ushort Code { get; set; }

    /// <summary>Teclado: scancode original, com 0xE000 quando estendido. Identifica o botão.</summary>
    public ushort ScanCode { get; set; }

    /// <summary>Mídia: VK que o Windows gera no hook para este uso (aprendido na gravação).</summary>
    public ushort? HookVk { get; set; }

    public string KeyName { get; set; } = "";
    public ActionConfig Action { get; set; } = new();
    public OriginalMode Original { get; set; } = OriginalMode.Pass;
}

internal sealed class DeviceConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Vid { get; set; } = "";
    public string Pid { get; set; } = "";
    public bool Active { get; set; } = true;
    public List<ButtonConfig> Buttons { get; set; } = [];

    [JsonIgnore]
    public string Key => $"{Vid}:{Pid}";
}

internal sealed class AppConfig
{
    public List<DeviceConfig> Devices { get; set; } = [];
    public bool LogToFile { get; set; } = true;
}

internal static class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RemapUSB", "config.json");

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath), Options) ?? new AppConfig();
        }
        catch (Exception ex)
        {
            // Config corrompida: guarda uma cópia e começa do zero, em vez de não abrir.
            Log.Write("ERRO", $"config inválida, começando do zero: {ex.Message}");
            File.Copy(FilePath, FilePath + ".invalida", overwrite: true);
        }
        return new AppConfig();
    }

    /// <summary>Cópia independente, para o motor ler sem disputar com a tela que edita.</summary>
    public static AppConfig Clone(AppConfig config) =>
        JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(config, Options), Options)!;

    public static void Save(AppConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(config, Options));
        File.Move(temp, FilePath, overwrite: true);
    }
}
