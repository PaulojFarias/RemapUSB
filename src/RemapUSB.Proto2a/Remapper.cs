using System.Runtime.InteropServices;
using RemapUSB.Probe;
using static RemapUSB.Probe.Native;

namespace RemapUSB.Proto2a;

/// <param name="Code">VK (teclado) ou uso HID (mídia) que o Raw Input entrega para o botão.</param>
/// <param name="HookVk">
/// VK que o mesmo botão gera no hook, para bloqueá-lo. Nulo quando a tecla já foi neutralizada
/// no Windows (ex.: Menu virou F24 pelo Scancode Map): ela passa sem efeito e só o Raw dispara a ação.
/// </param>
internal sealed record Mapping(string Name, Part Part, ushort Code, ushort? HookVk, string ActionName, Action Fire);

/// <summary>
/// Bloqueia no hook as teclas mapeadas e decide, cruzando com o Raw Input, se vieram do
/// dispositivo alvo:
/// - Raw antes do hook (o normal na mídia): o hook já sabe a origem e decide na hora.
/// - Hook antes do Raw: bloqueia, espera o Raw por até WindowMs e, se ele não vier, a tecla
///   é de outro teclado e é reenviada ao Windows.
/// Na parte de teclado o bloqueio não serve: com a tecla bloqueada no hook, o Windows nem gera
/// o Raw (medido no Proto2a). Por isso tecla de teclado é neutralizada no Windows e não bloqueada.
/// </summary>
internal sealed class Remapper : IDisposable
{
    private const double WindowMs = 60;

    /// <summary>Marca as teclas que o próprio app envia, para o hook deixá-las passar.</summary>
    public static readonly IntPtr Marker = new(0x52454D50);

    private readonly IReadOnlyList<Mapping> _mappings;
    private readonly LowLevelKeyboardProc _callback;
    private readonly IntPtr _hook;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 10 };
    private readonly List<DeviceEvent> _recent = new();
    private readonly List<PendingKey> _pending = new();
    private readonly HashSet<Mapping> _held = new();

    private sealed record PendingKey(double At, Mapping Mapping, bool IsUp, KBDLLHOOKSTRUCT Data);

    public Remapper(IReadOnlyList<Mapping> mappings, DeviceInput input)
    {
        _mappings = mappings;
        input.Received += OnDeviceEvent;

        _callback = OnHook;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _callback, GetModuleHandle(null), 0);
        Log.Write(_hook == IntPtr.Zero
            ? $"[HOOK] FALHA ao instalar, erro {Marshal.GetLastWin32Error()}"
            : "[HOOK] OK    teclado de baixo nível (bloqueia só as teclas mapeadas)");

        _timer.Tick += (_, _) => ReplayExpired(Clock.Ms);
        _timer.Start();
    }

    private IntPtr OnHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
            return CallNextHookEx(_hook, nCode, wParam, lParam);

        var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        if ((IntPtr)(long)data.ExtraInfo.ToUInt64() == Marker)
            return CallNextHookEx(_hook, nCode, wParam, lParam);

        var mapping = _mappings.FirstOrDefault(m => m.HookVk is { } vk && vk == data.VkCode);
        if (mapping is null)
            return CallNextHookEx(_hook, nCode, wParam, lParam);

        var at = Clock.Ms;
        var isUp = (int)wParam is WM_KEYUP or WM_SYSKEYUP;
        _recent.RemoveAll(e => at - e.At > WindowMs);

        var match = _recent.FirstOrDefault(e => Matches(mapping, e, isUp));
        if (match is not null)
        {
            _recent.Remove(match);
            Consume(at, mapping, isUp, "Raw antes do hook");
        }
        else
        {
            Log.Write($"{Clock.Format(at)} [HOOK]      {(isUp ? "solta " : "aperta")} {(Keys)data.VkCode} bloqueada, aguardando Raw");
            _pending.Add(new PendingKey(at, mapping, isUp, data));
        }

        return 1;
    }

    private void OnDeviceEvent(DeviceEvent e)
    {
        var neutral = _mappings.FirstOrDefault(m => m.HookVk is null && m.Part == e.Part && m.Code == e.Code);
        if (neutral is not null)
        {
            // Segurar o botão gera "aperta" repetido; a ação dispara só no primeiro.
            if (e.IsUp)
                _held.Remove(neutral);
            else if (_held.Add(neutral))
                Consume(e.At, neutral, isUp: false, "tecla neutralizada, sem bloqueio");
            return;
        }

        var pending = _pending.FirstOrDefault(p => Matches(p.Mapping, e, p.IsUp));
        if (pending is not null)
        {
            _pending.Remove(pending);
            Consume(e.At, pending.Mapping, pending.IsUp, "hook antes do Raw");
            return;
        }

        _recent.RemoveAll(r => e.At - r.At > WindowMs);
        _recent.Add(e);
    }

    private static bool Matches(Mapping mapping, DeviceEvent e, bool isUp) =>
        e.Part == mapping.Part
        && e.IsUp == isUp
        // O "solta" da mídia não diz qual botão foi solto.
        && ((e.Part == Part.Consumer && isUp) || e.Code == mapping.Code);

    private static void Consume(double at, Mapping mapping, bool isUp, string order)
    {
        if (isUp)
        {
            Log.Write($"{Clock.Format(at)} [BLOQUEADO] solta  {mapping.Name} ({order})");
            return;
        }

        Log.Write($"{Clock.Format(at)} [REMAPEADO] {mapping.Name} -> {mapping.ActionName} ({order})");
        try
        {
            mapping.Fire();
        }
        catch (Exception ex)
        {
            Log.Write($"{Clock.Format(Clock.Ms)} [ERRO]      {mapping.ActionName}: {ex.Message}");
        }
    }

    private void ReplayExpired(double now)
    {
        var expired = _pending.Where(p => now - p.At > WindowMs).ToList();
        foreach (var pending in expired)
        {
            _pending.Remove(pending);
            Log.Write($"{Clock.Format(now)} [REPASSE]   {(pending.IsUp ? "solta " : "aperta")} {(Keys)pending.Data.VkCode} sem Raw do controle: veio de outro teclado, reenviada");
            Actions.SendKeyEvent(
                (ushort)pending.Data.VkCode,
                (ushort)pending.Data.ScanCode,
                pending.IsUp,
                (pending.Data.Flags & LLKHF_EXTENDED) != 0);
        }
    }

    public void Dispose()
    {
        _timer.Dispose();
        if (_hook != IntPtr.Zero)
            UnhookWindowsHookEx(_hook);
    }
}
