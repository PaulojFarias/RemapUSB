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

    /// <summary>Partes do dispositivo alvo (teclado, mídia) presentes agora.</summary>
    private readonly HashSet<IntPtr> _present = new();

    public event Action<DeviceEvent>? Received;

    /// <summary>Teclas de outros teclados. Não vão para o log, para não registrar o que é digitado.</summary>
    public event Action<DeviceEvent>? OtherKeyboard;

    public DeviceInput(string deviceId)
    {
        _deviceId = deviceId;
        CreateHandle(new CreateParams { Parent = HwndMessage });
        LogInitialPresence();

        // DEVNOTIFY avisa quando uma parte é conectada ou removida.
        RAWINPUTDEVICE[] devices =
        [
            new() { UsagePage = 0x01, Usage = 0x06, Flags = RIDEV_INPUTSINK | RIDEV_DEVNOTIFY, Target = Handle },
            new() { UsagePage = 0x0C, Usage = 0x01, Flags = RIDEV_INPUTSINK | RIDEV_DEVNOTIFY, Target = Handle },
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
        else if (m.Msg == WM_INPUT_DEVICE_CHANGE)
            HandleDeviceChange((int)m.WParam, m.LParam);

        base.WndProc(ref m);
    }

    private void LogInitialPresence()
    {
        uint count = 0;
        var itemSize = (uint)Marshal.SizeOf<RAWINPUTDEVICELIST>();
        GetRawInputDeviceList(null, ref count, itemSize);
        var list = new RAWINPUTDEVICELIST[count];
        GetRawInputDeviceList(list, ref count, itemSize);

        foreach (var item in list)
        {
            if (item.Type is RIM_TYPEKEYBOARD or RIM_TYPEHID && IsTarget(item.Device))
                _present.Add(item.Device);
        }

        Log.Write(_present.Count > 0
            ? $"{Stamp()} [DONGLE]    conectado ao iniciar"
            : $"{Stamp()} [DONGLE]    desconectado ao iniciar");
    }

    private void HandleDeviceChange(int change, IntPtr device)
    {
        // Ao registrar, o Windows anuncia as partes que já estavam plugadas: o Add devolve
        // false para elas e nada é registrado. Só a primeira parte que volta e a última que
        // sai geram linha, para o controle inteiro aparecer uma vez só.
        if (change == GIDC_ARRIVAL && IsTarget(device) && _present.Add(device) && _present.Count == 1)
            Log.Write($"{Stamp()} [DONGLE]    conectado");
        else if (change == GIDC_REMOVAL && _present.Remove(device) && _present.Count == 0)
            Log.Write($"{Stamp()} [DONGLE]    desconectado");
    }

    private static string Stamp() => $"{Clock.Format(Clock.Ms)} {DateTime.Now:HH:mm:ss}";

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
            var at = Clock.Ms;
            var body = buffer + (int)HeaderSize;

            if (!IsTarget(header.Device))
            {
                if (header.Type == RIM_TYPEKEYBOARD)
                    OtherKeyboard?.Invoke(ReadKeyboard(at, body));
                return;
            }

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
