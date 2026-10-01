using System.Runtime.InteropServices;
using static RemapUSB.Probe.Native;

namespace RemapUSB.Probe;

/// <summary>
/// Janela invisível (message-only) que recebe WM_INPUT de teclado, mouse,
/// consumer control e controlador de sistema, mesmo sem foco.
/// </summary>
internal sealed class RawInputWindow : NativeWindow, IDisposable
{
    private static readonly IntPtr HwndMessage = new(-3);
    private static readonly uint HeaderSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();

    private static readonly (ushort Page, ushort Usage, string Name)[] Collections =
    [
        (0x01, 0x06, "Teclado"),
        (0x01, 0x02, "Mouse"),
        (0x0C, 0x01, "Consumer control (mídia)"),
        (0x01, 0x80, "Controlador de sistema (energia)"),
    ];

    private readonly string? _filter;
    private readonly Dictionary<IntPtr, DeviceInfo> _devices = new();

    public RawInputWindow(string? filter)
    {
        _filter = filter;
        CreateHandle(new CreateParams { Parent = HwndMessage });
        ListDevices();
        Register();
    }

    private void Register()
    {
        foreach (var (page, usage, name) in Collections)
        {
            var device = new RAWINPUTDEVICE
            {
                UsagePage = page,
                Usage = usage,
                Flags = RIDEV_INPUTSINK | RIDEV_DEVNOTIFY,
                Target = Handle,
            };

            var ok = RegisterRawInputDevices([device], 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
            Log.Write(ok
                ? $"[REGISTRO] OK    {name} (0x{page:X2}/0x{usage:X2})"
                : $"[REGISTRO] FALHA {name} (0x{page:X2}/0x{usage:X2}) erro {Marshal.GetLastWin32Error()}");
        }
        Log.Write();
    }

    private void ListDevices()
    {
        uint count = 0;
        var itemSize = (uint)Marshal.SizeOf<RAWINPUTDEVICELIST>();
        GetRawInputDeviceList(null, ref count, itemSize);
        var list = new RAWINPUTDEVICELIST[count];
        GetRawInputDeviceList(list, ref count, itemSize);

        Log.Write("Dispositivos vistos pelo Raw Input" + (_filter is null ? ":" : $" (filtro {_filter}):"));
        foreach (var item in list)
        {
            var info = GetDevice(item.Device);
            if (Matches(info))
                Log.Write($"  {info.Describe()}");
        }
        Log.Write();
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
            var info = GetDevice(device);
            if (Matches(info))
                Log.Write($"{Clock.Format(Clock.Ms)} [CONECTADO]    {info.Describe()}");
        }
        else if (change == GIDC_REMOVAL && _devices.Remove(device, out var info) && Matches(info))
        {
            Log.Write($"{Clock.Format(Clock.Ms)} [DESCONECTADO] {info.Describe()}");
        }
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
            var info = GetDevice(header.Device);
            if (!Matches(info))
                return;

            var body = buffer + (int)HeaderSize;
            var line = header.Type switch
            {
                RIM_TYPEKEYBOARD => DescribeKeyboard(body),
                RIM_TYPEMOUSE => DescribeMouse(body),
                RIM_TYPEHID => DescribeHid(body),
                _ => null,
            };

            if (line is not null)
            {
                var at = Clock.Ms;
                Log.Write($"{Clock.Format(at)} [RAW]  {info.Part,-28} {line}");
                HookCorrelator.OnDeviceEvent(at);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string DescribeKeyboard(IntPtr body)
    {
        var makeCode = (ushort)Marshal.ReadInt16(body, 0);
        var flags = (ushort)Marshal.ReadInt16(body, 2);
        var vkey = (ushort)Marshal.ReadInt16(body, 6);
        var state = (flags & RI_KEY_BREAK) != 0 ? "solta  " : "aperta ";
        var e0 = (flags & RI_KEY_E0) != 0 ? " E0" : "";
        return $"{state} VK=0x{vkey:X2} {(Keys)vkey,-18} scan=0x{makeCode:X2}{e0}";
    }

    private static string? DescribeMouse(IntPtr body)
    {
        // Movimento é ignorado: só interessa botão e roda.
        var buttonFlags = (ushort)Marshal.ReadInt16(body, 4);
        var buttonData = Marshal.ReadInt16(body, 6);
        return buttonFlags == 0 ? null : $"botões=0x{buttonFlags:X4} dado={buttonData}";
    }

    private static string DescribeHid(IntPtr body)
    {
        var reportSize = Marshal.ReadInt32(body, 0);
        var count = Marshal.ReadInt32(body, 4);
        var bytes = new byte[reportSize * count];
        Marshal.Copy(body + 8, bytes, 0, bytes.Length);
        return $"relatório={Convert.ToHexString(bytes)}";
    }

    private bool Matches(DeviceInfo info) =>
        _filter is null || info.Path.Contains(_filter, StringComparison.OrdinalIgnoreCase);

    private DeviceInfo GetDevice(IntPtr device)
    {
        if (_devices.TryGetValue(device, out var cached))
            return cached;

        var info = new DeviceInfo(ReadName(device), ReadUsage(device));
        _devices[device] = info;
        return info;
    }

    private static string ReadName(IntPtr device)
    {
        uint chars = 0;
        GetRawInputDeviceInfo(device, RIDI_DEVICENAME, IntPtr.Zero, ref chars);
        if (chars == 0)
            return "(sem nome)";

        var buffer = Marshal.AllocHGlobal((int)chars * 2);
        try
        {
            GetRawInputDeviceInfo(device, RIDI_DEVICENAME, buffer, ref chars);
            return Marshal.PtrToStringUni(buffer) ?? "(sem nome)";
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
            return type switch
            {
                RIM_TYPEKEYBOARD => (type, 0x01, 0x06),
                RIM_TYPEMOUSE => (type, 0x01, 0x02),
                _ => (type, (ushort)Marshal.ReadInt16(buffer, 20), (ushort)Marshal.ReadInt16(buffer, 22)),
            };
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Dispose() => DestroyHandle();

    private sealed record DeviceInfo(string Path, (uint Type, ushort Page, ushort Usage) Usage)
    {
        public string Part
        {
            get
            {
                var name = Usage switch
                {
                    (_, 0x01, 0x06) => "teclado",
                    (_, 0x01, 0x02) => "mouse",
                    (_, 0x0C, 0x01) => "mídia",
                    (_, 0x01, 0x80) => "energia",
                    var (_, page, usage) => $"HID 0x{page:X2}/0x{usage:X2}",
                };
                return $"{name} {Interface}";
            }
        }

        private string Interface
        {
            get
            {
                var upper = Path.ToUpperInvariant();
                var mi = upper.IndexOf("&MI_", StringComparison.Ordinal);
                var col = upper.IndexOf("&COL", StringComparison.Ordinal);
                var miText = mi >= 0 ? upper.Substring(mi + 1, 5) : "";
                var colText = col >= 0 ? upper.Substring(col + 1, 5) : "";
                return string.Join(" ", new[] { miText, colText }.Where(t => t.Length > 0));
            }
        }

        public string Describe() => $"{Part,-28} {Path}";
    }
}
