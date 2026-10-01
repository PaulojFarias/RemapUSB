using System.Windows;
using System.Windows.Media;
using RemapUSB.Engine;
using RemapUSB.Infrastructure;
using RemapUSB.Model;

namespace RemapUSB.Ui;

internal static class UiBrushes
{
    public static Brush Resource(string key, Color fallback) =>
        Application.Current.TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);

    public static Brush Success => Resource("SystemFillColorSuccessBrush", Color.FromRgb(0x0F, 0x7B, 0x0F));
    public static Brush Critical => Resource("SystemFillColorCriticalBrush", Color.FromRgb(0xC4, 0x2B, 0x1C));
    public static Brush Secondary => Resource("TextFillColorSecondaryBrush", Colors.Gray);
    public static Brush Tertiary => Resource("TextFillColorTertiaryBrush", Colors.Gray);
    public static Brush Primary => Resource("TextFillColorPrimaryBrush", Colors.Black);

    public static readonly Brush MediaBadge = Frozen(Color.FromArgb(0x33, 0x00, 0x78, 0xD4));
    public static readonly Brush KeyboardBadge = Frozen(Color.FromArgb(0x40, 0xF7, 0xA4, 0x00));

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

internal sealed class DeviceVm(DeviceConfig config, Func<string, bool> isConnected) : ObservableObject
{
    public DeviceConfig Config { get; } = config;

    public string Name => Config.Name;

    public string Subtitle =>
        $"VID_{Config.Vid} · PID_{Config.Pid} · {Config.Buttons.Count} botões gravados · {Config.Buttons.Count(b => b.Action.Type != ActionType.Keep)} remapeados";

    public bool Connected => isConnected(Config.Key);
    public string StatusText => Connected ? "Conectado" : "Desconectado";
    public Brush StatusBrush => Connected ? UiBrushes.Success : UiBrushes.Critical;

    public bool Active
    {
        get => Config.Active;
        set
        {
            Config.Active = value;
            App.Services.SaveAndApply();
            Raise();
        }
    }

    public void Refresh() => RaiseAll();
}

internal sealed class ButtonVm(ButtonConfig config) : ObservableObject
{
    private bool _flash;

    public ButtonConfig Config { get; } = config;

    public string Name
    {
        get => Config.Name;
        set
        {
            Config.Name = value;
            Raise();
        }
    }

    public string PartLabel => Config.Part == ButtonPart.Consumer ? "Mídia" : "Teclado";
    public Brush PartBackground => Config.Part == ButtonPart.Consumer ? UiBrushes.MediaBadge : UiBrushes.KeyboardBadge;

    public string CodeText => Config.Part == ButtonPart.Consumer
        ? $"{Config.KeyName} · uso 0x{Config.Code:X4}"
        : $"{Config.KeyName} · VK 0x{Config.Code:X2}";

    public string ActionMain => ActionText.Describe(Config.Action).Main;
    public Brush ActionBrush => Config.Action.Type == ActionType.Keep ? UiBrushes.Tertiary : UiBrushes.Primary;

    public string ActionSub
    {
        get
        {
            var sub = ActionText.Describe(Config.Action).Sub;
            var note = Config.Action.Type == ActionType.Keep ? ""
                : Config.Part == ButtonPart.Consumer
                    ? Config.HookVk is null ? "tecla original não pode ser bloqueada" : "tecla original bloqueada"
                    : Config.Original == OriginalMode.Neutralize ? "tecla original neutralizada" : $"{Config.KeyName} também chega ao programa";
            return string.Join(" · ", new[] { sub, note }.Where(s => s.Length > 0));
        }
    }

    /// <summary>Destaque rápido quando o botão é apertado de novo na gravação.</summary>
    public bool Flash
    {
        get => _flash;
        set
        {
            _flash = value;
            Raise();
        }
    }

    public void Refresh() => RaiseAll();
}

internal sealed class AppItemVm(AppRef app)
{
    public AppRef App { get; } = app;
    public string Name => App.Name;
    public string Kind => App.Kind == AppKind.Package ? "Store / MSIX" : ".exe";
    public string Detail => App.Kind == AppKind.Package ? App.PackageFamily ?? "" : App.ExePath ?? "";
}
