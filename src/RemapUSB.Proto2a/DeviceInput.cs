using System.Runtime.InteropServices;
using RemapUSB.Probe;
using static RemapUSB.Probe.Native;

namespace RemapUSB.Proto2a;

internal enum Part { Keyboard, Consumer }

/// <param name="Code">VK para teclado; uso HID (ex.: 0x0224) para mídia, 0 quando solta.</param>
internal sealed record DeviceEvent(double At, Part Part, ushort Code, bool IsUp);

/// <summary>
/// Recebe Raw Input de teclado e consumer control e repassa só o que vem do dispositivo alvo.
/// </summary>
internal sealed class DeviceInput : NativeWindow, IDisposable
{
    private static readonly IntPtr HwndMessage = new(-3);
    private static readonly uint HeaderSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();

    private readonly string _deviceId;
    private readonly Dictionary<IntPtr, bool> _isTarget = new();

    public event Action<DeviceEvent>? Received;

    public DeviceInput(string deviceId)
    {
        _deviceId = deviceId;
        CreateHandle(new CreateParams { Parent = HwndMessage });

        RAWINPUTDEVICE[] devices =
        [
            new() { UsagePage = 0x01, Usage = 0x06, Flags = RIDEV_INPUTSINK, Target = Handle },
            new() { UsagePage = 0x0C, Usage = 0x01, Flags = RIDEV_INPUTSINK, Target = Handle },
        ];
        var ok = RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
        Log.Write(ok
            ? "[REGISTRO] OK    Raw Input de teclado e mídia"
            : $"[REGISTRO] FALHA Raw Input, erro {Marshal.GetLastWin32Error()}");
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_INPUT)
            HandleInput(m.LParam);

        base.WndProc(ref m);
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
            if (!IsTarget(header.Device))
                return;

            var at = Clock.Ms;
            var body = buffer + (int)HeaderSize;
            var deviceEvent = header.Type switch
            {
                RIM_TYPEKEYBOARD => ReadKeyboard(at, body),
                RIM_TYPEHID => ReadConsumer(at, body),
                _ => null,
            };

            if (deviceEvent is null)
                return;

            Log.Write($"{Clock.Format(at)} [RAW]       {Describe(deviceEvent)}");
            Received?.Invoke(deviceEvent);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static DeviceEvent ReadKeyboard(double at, IntPtr body)
    {
        var flags = (ushort)Marshal.ReadInt16(body, 2);
        var vk = (ushort)Marshal.ReadInt16(body, 6);
        return new DeviceEvent(at, Part.Keyboard, vk, (flags & RI_KEY_BREAK) != 0);
    }

    private static DeviceEvent? ReadConsumer(double at, IntPtr body)
    {
        // Relatório do controle: [id][uso low][uso high]. Uso 0 = botão solto.
        var reportSize = Marshal.ReadInt32(body, 0);
        if (reportSize < 3)
            return null;

        var usage = (ushort)(Marshal.ReadByte(body, 9) | (Marshal.ReadByte(body, 10) << 8));
        return new DeviceEvent(at, Part.Consumer, usage, usage == 0);
    }

    private static string Describe(DeviceEvent e) => e.Part == Part.Keyboard
        ? $"teclado {(e.IsUp ? "solta " : "aperta")} VK=0x{e.Code:X2} {(Keys)e.Code}"
        : e.IsUp ? "mídia   solta" : $"mídia   aperta uso=0x{e.Code:X4}";

    private bool IsTarget(IntPtr device)
    {
        if (!_isTarget.TryGetValue(device, out var target))
        {
            target = ReadName(device).Contains(_deviceId, StringComparison.OrdinalIgnoreCase);
            _isTarget[device] = target;
        }
        return target;
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

    public void Dispose() => DestroyHandle();
}
