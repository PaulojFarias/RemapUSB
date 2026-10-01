using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using RemapUSB.Infrastructure;
using static RemapUSB.Infrastructure.Native;

namespace RemapUSB.Input;

/// <param name="DeviceKey">"VID:PID" do dispositivo, ou vazio se não for USB.</param>
/// <param name="Scan">Scancode recebido, com 0xE000 quando estendido.</param>
internal sealed record KeyEvent(double At, string DeviceKey, ushort Vk, ushort Scan, bool IsUp);

/// <param name="Usage">Uso HID do botão; no "solta" é o do botão que foi solto.</param>
internal sealed record ConsumerEvent(double At, string DeviceKey, ushort Usage, bool IsUp);

/// <summary>
/// Janela invisível que recebe Raw Input de teclado e mídia de todos os dispositivos, sem foco,
/// e acompanha quais dispositivos estão conectados.
/// </summary>
internal sealed partial class RawInputSource : NativeWindow, IDisposable
{
    private static readonly IntPtr HwndMessage = new(-3);
    private static readonly uint HeaderSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
    private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();

    private readonly Dictionary<IntPtr, DeviceInfo> _devices = new();
    private readonly Dictionary<string, HashSet<IntPtr>> _present = new(StringComparer.OrdinalIgnoreCase);

    public event Action<KeyEvent>? Key;
    public event Action<ConsumerEvent>? Consumer;
    public event Action<string>? Connected;
    public event Action<string>? Disconnected;

    public static double Now => Clock.Elapsed.TotalMilliseconds;

    public RawInputSource()
    {
        CreateHandle(new CreateParams { Parent = HwndMessage });

        // As partes já plugadas entram antes do registro; o Windows as anuncia de novo ao
        // registrar e o HashSet ignora a repetição.
        foreach (var device in EnumerateDevices())
            Track(device);

        RAWINPUTDEVICE[] devices =
        [
            new() { UsagePage = 0x01, Usage = 0x06, Flags = RIDEV_INPUTSINK | RIDEV_DEVNOTIFY, Target = Handle },
            new() { UsagePage = 0x0C, Usage = 0x01, Flags = RIDEV_INPUTSINK | RIDEV_DEVNOTIFY, Target = Handle },
        ];
        if (!RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
            Log.Write("ERRO", $"Raw Input não registrou, erro {Marshal.GetLastWin32Error()}");
    }

    public bool IsConnected(string deviceKey) => _present.TryGetValue(deviceKey, out var set) && set.Count > 0;

    /// <summary>Partes do dispositivo que o Windows expõe ao Raw Input, para a tela de escuta.</summary>
    public List<string> DescribeParts(string deviceKey)
    {
        var parts = new List<string>();
        foreach (var item in ListAll())
        {
            var info = GetInfo(item.Device);
            if (!string.Equals(info.Key, deviceKey, StringComparison.OrdinalIgnoreCase))
                continue;

            var part = info.Kind switch
            {
                DeviceKind.Keyboard => "Teclado",
                DeviceKind.Consumer => "Mídia",
                DeviceKind.Mouse => "Mouse",
                _ => null,
            };
            if (part is not null && !parts.Contains(part))
                parts.Add(part);
        }
        return parts;
    }

    public string? ProductName(string deviceKey)
    {
        var path = _devices.Values.FirstOrDefault(d => string.Equals(d.Key, deviceKey, StringComparison.OrdinalIgnoreCase))?.Path;
        if (path is null)
            return null;

        // Acesso 0 basta para ler o nome, mesmo em coleções que o Windows abre com exclusividade.
        var handle = CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (handle == new IntPtr(-1))
            return null;
        try
        {
            var buffer = new StringBuilder(128);
            return HidD_GetProductString(handle, buffer, 256) && buffer.Length > 0 ? buffer.ToString().Trim() : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_INPUT)
            HandleInput(m.LParam);
        else if (m.Msg == WM_INPUT_DEVICE_CHANGE)
            HandleDeviceChange((int)m.WParam, m.LParam);

        base.WndProc(ref m);
    }

    private void HandleDeviceChange(int change, IntPtr device)
    {
        if (change == GIDC_ARRIVAL)
        {
            _devices.Remove(device);
            Track(device);
        }
        else if (change == GIDC_REMOVAL && _devices.Remove(device, out var info) && info.Key.Length > 0
                 && _present.TryGetValue(info.Key, out var set) && set.Remove(device) && set.Count == 0)
        {
            Disconnected?.Invoke(info.Key);
        }
    }

    private void Track(IntPtr device)
    {
        var info = GetInfo(device);
        if (info.Key.Length == 0 || info.Kind is not (DeviceKind.Keyboard or DeviceKind.Consumer))
            return;

        if (!_present.TryGetValue(info.Key, out var set))
            _present[info.Key] = set = [];
        if (set.Add(device) && set.Count == 1)
            Connected?.Invoke(info.Key);
    }

    private void HandleInput(IntPtr rawInput)
    {
        uint size = 0;
        GetRawInputData(rawInput, RID_INPUT, IntPtr.Zero, ref size, HeaderSize);
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(rawInput, RID_INPUT, buffer, ref size, HeaderSize) != size)
                return;

            var header = Marshal.PtrToStructure<RAWINPUTHEADER>(buffer);
            var body = buffer + (int)HeaderSize;
            var info = GetInfo(header.Device);
            var at = Now;

            if (header.Type == RIM_TYPEKEYBOARD)
                HandleKeyboard(at, info, body);
            else if (header.Type == RIM_TYPEHID && info.Kind == DeviceKind.Consumer)
                HandleConsumer(at, info, body);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void HandleKeyboard(double at, DeviceInfo info, IntPtr body)
    {
        var makeCode = (ushort)Marshal.ReadInt16(body, 0);
        var flags = (ushort)Marshal.ReadInt16(body, 2);
        var vk = (ushort)Marshal.ReadInt16(body, 6);

        // 0xFF é o VK "falso" que o Windows gera em sequências de teclas especiais (E1, Pause).
        if (vk == 0xFF || (flags & RI_KEY_E1) != 0)
            return;

        var scan = (flags & RI_KEY_E0) != 0 ? (ushort)(0xE000 | makeCode) : makeCode;
        Key?.Invoke(new KeyEvent(at, info.Key, vk, scan, (flags & RI_KEY_BREAK) != 0));
    }

    private void HandleConsumer(double at, DeviceInfo info, IntPtr body)
    {
        var reportSize = Marshal.ReadInt32(body, 0);
        var count = Marshal.ReadInt32(body, 4);
        for (var i = 0; i < count; i++)
        {
            var report = new byte[reportSize];
            Marshal.Copy(body + 8 + i * reportSize, report, 0, reportSize);
            var current = ParseConsumer(info, report);

            // Compara com o que estava apertado: o que entrou é "aperta", o que saiu é "solta".
            foreach (var usage in current.Except(info.Pressed).ToList())
            {
                info.Pressed.Add(usage);
                Consumer?.Invoke(new ConsumerEvent(at, info.Key, usage, IsUp: false));
            }
            foreach (var usage in info.Pressed.Except(current).ToList())
            {
                info.Pressed.Remove(usage);
                Consumer?.Invoke(new ConsumerEvent(at, info.Key, usage, IsUp: true));
            }
        }
    }

    /// <summary>
    /// Usa o parser HID do Windows (descritor do próprio dispositivo). Se ele não entender o
    /// relatório, cai no formato mais comum: [id][uso low][uso high].
    /// </summary>
    private static HashSet<ushort> ParseConsumer(DeviceInfo info, byte[] report)
    {
        if (info.Preparsed != IntPtr.Zero)
        {
            var usages = new ushort[16];
            uint length = (uint)usages.Length;
            var status = HidP_GetUsages(HidP_Input, 0x0C, 0, usages, ref length, info.Preparsed, report, (uint)report.Length);
            if (status == HIDP_STATUS_SUCCESS && (length > 0 || report.Skip(1).All(b => b == 0)))
                return [.. usages.Take((int)length).Where(u => u != 0)];
        }

        if (report.Length < 3)
            return [];
        var fallback = (ushort)(report[1] | (report[2] << 8));
        return fallback == 0 ? [] : [fallback];
    }

    private DeviceInfo GetInfo(IntPtr device)
    {
        if (_devices.TryGetValue(device, out var cached))
            return cached;

        var path = ReadName(device);
        var (type, page, usage) = ReadUsage(device);
        var kind = type switch
        {
            RIM_TYPEKEYBOARD => DeviceKind.Keyboard,
            RIM_TYPEMOUSE => DeviceKind.Mouse,
            _ when page == 0x0C && usage == 0x01 => DeviceKind.Consumer,
            _ => DeviceKind.Other,
        };
        var match = VidPid().Match(path);
        var key = match.Success ? $"{match.Groups[1].Value.ToUpperInvariant()}:{match.Groups[2].Value.ToUpperInvariant()}" : "";
        var info = new DeviceInfo(path, key, kind, kind == DeviceKind.Consumer ? ReadPreparsed(device) : IntPtr.Zero);
        _devices[device] = info;
        return info;
    }

    private static IEnumerable<IntPtr> EnumerateDevices() => ListAll().Select(d => d.Device);

    private static RAWINPUTDEVICELIST[] ListAll()
    {
        uint count = 0;
        var itemSize = (uint)Marshal.SizeOf<RAWINPUTDEVICELIST>();
        GetRawInputDeviceList(null, ref count, itemSize);
        var list = new RAWINPUTDEVICELIST[count];
        GetRawInputDeviceList(list, ref count, itemSize);
        return list;
    }

    private static string ReadName(IntPtr device)
    {
        uint chars = 0;
        GetRawInputDeviceInfo(device, RIDI_DEVICENAME, IntPtr.Zero, ref chars);
        if (chars == 0)
            return "";

        var buffer = Marshal.AllocHGlobal((int)chars * 2);
        try
        {
            GetRawInputDeviceInfo(device, RIDI_DEVICENAME, buffer, ref chars);
            return Marshal.PtrToStringUni(buffer) ?? "";
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static (uint Type, ushort Page, ushort Usage) ReadUsage(IntPtr device)
    {
        // RID_DEVICE_INFO: cbSize, dwType e, para HID, vendor/product/version/usagePage/usage.
        const int infoSize = 32;
        var buffer = Marshal.AllocHGlobal(infoSize);
        try
        {
            Marshal.WriteInt32(buffer, 0, infoSize);
            uint size = infoSize;
            GetRawInputDeviceInfo(device, RIDI_DEVICEINFO, buffer, ref size);
            var type = (uint)Marshal.ReadInt32(buffer, 4);
            return type == RIM_TYPEHID
                ? (type, (ushort)Marshal.ReadInt16(buffer, 20), (ushort)Marshal.ReadInt16(buffer, 22))
                : (type, (ushort)0, (ushort)0);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Descritor HID já interpretado pelo Windows; fica vivo enquanto o app roda.</summary>
    private static IntPtr ReadPreparsed(IntPtr device)
    {
        uint size = 0;
        GetRawInputDeviceInfo(device, RIDI_PREPARSEDDATA, IntPtr.Zero, ref size);
        if (size == 0)
            return IntPtr.Zero;

        var buffer = Marshal.AllocHGlobal((int)size);
        if (GetRawInputDeviceInfo(device, RIDI_PREPARSEDDATA, buffer, ref size) == uint.MaxValue)
        {
            Marshal.FreeHGlobal(buffer);
            return IntPtr.Zero;
        }
        return buffer;
    }

    [GeneratedRegex(@"VID_([0-9A-F]{4})&PID_([0-9A-F]{4})", RegexOptions.IgnoreCase)]
    private static partial Regex VidPid();

    public void Dispose() => DestroyHandle();

    private enum DeviceKind { Keyboard, Consumer, Mouse, Other }

    private sealed record DeviceInfo(string Path, string Key, DeviceKind Kind, IntPtr Preparsed)
    {
        public HashSet<ushort> Pressed { get; } = [];
    }
}
