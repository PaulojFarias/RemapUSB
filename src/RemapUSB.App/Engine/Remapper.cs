using System.Runtime.InteropServices;
using RemapUSB.Actions;
using RemapUSB.Infrastructure;
using RemapUSB.Input;
using RemapUSB.Model;
using static RemapUSB.Infrastructure.Native;

namespace RemapUSB.Engine;

/// <param name="Code">Teclado: VK original. Mídia: uso HID.</param>
internal sealed record RecordedButton(ButtonPart Part, ushort Code, ushort ScanCode, ushort? HookVk, string KeyName);

/// <summary>
/// Decide o que fazer com cada botão dos dispositivos salvos.
///
/// Mídia: o Raw chega antes do hook (medido no Proto2a), então o hook bloqueia a tecla que o
/// Windows gera e a ação dispara. Se o hook chegar primeiro, a tecla é segurada por até WindowMs
/// esperando o Raw; sem Raw do dispositivo, ela veio de outro teclado e é reenviada.
///
/// Teclado: bloquear no hook impede o Windows de gerar o Raw, então não dá para saber a origem.
/// O botão reage ao Raw e a tecla passa ("deixar passar"), ou foi trocada no Windows por uma tecla
/// sem uso ("neutralizar"): aí o motor a reconhece pelo scancode e devolve a original aos outros teclados.
/// </summary>
internal sealed class Remapper : IDisposable
{
    private const double WindowMs = 60;
    private const double LearnWindowMs = 30;

    private readonly RawInputSource _input;
    private readonly LowLevelKeyboardProc _callback;
    private readonly IntPtr _hook;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 10 };

    /// <summary>Acima disto o atraso do hook vai para o log: perto do limite em que o Windows o pula.</summary>
    private const uint SlowHookMs = 40;

    private Dictionary<string, DeviceConfig> _devices = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<ushort, NeutralKey> _neutralByTargetVk = new();
    private HashSet<ushort> _blockedVks = [];

    private readonly List<(double At, ButtonConfig Button, bool IsUp)> _recent = [];
    private readonly List<PendingKey> _pending = [];
    private readonly HashSet<Guid> _held = [];

    private string? _recordingKey;
    private readonly List<(double At, ushort Usage)> _recordingConsumer = [];
    private readonly List<(double At, ushort Vk)> _recordingHookVks = [];

    private volatile bool _paused;

    /// <summary>Disparado na thread de entrada.</summary>
    public event Action<RecordedButton>? Recorded;

    /// <summary>Pode ser mudado de qualquer thread.</summary>
    public bool Paused
    {
        get => _paused;
        set => _paused = value;
    }

    private sealed record PendingKey(double At, ushort Vk, ushort Scan, bool IsUp, bool Extended);

    public Remapper(RawInputSource input)
    {
        _input = input;
        _input.Key += OnKey;
        _input.Consumer += OnConsumer;

        _callback = OnHook;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _callback, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
            Log.Write("ERRO", $"hook de teclado não instalou, erro {Marshal.GetLastWin32Error()}");

        _timer.Tick += (_, _) => OnTick();
        _timer.Start();
    }

    /// <summary>Recarrega a configuração. Chamar na thread de entrada, com uma cópia da configuração.</summary>
    public void Apply(AppConfig config)
    {
        _devices = config.Devices.Where(d => d.Active).GroupBy(d => d.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var applied = ScancodeMap.ReadApplied();
        var active = applied.Count > 0 && ScancodeMap.IsActive();
        _neutralByTargetVk = active ? applied.GroupBy(n => n.TargetVk).ToDictionary(g => g.Key, g => g.First()) : new();

        _blockedVks = _devices.Values.SelectMany(d => d.Buttons)
            .Where(b => b.Part == ButtonPart.Consumer && b.Action.Type != ActionType.Keep && b.HookVk is not null)
            .Select(b => b.HookVk!.Value)
            .ToHashSet();

        _recent.Clear();
        _held.Clear();

        var neutralText = applied.Count == 0 ? "nenhuma"
            : $"{string.Join(", ", applied.Select(a => a.Describe()))}{(active ? "" : " (pendente de reinício)")}";
        Log.Write("CONFIG", $"{_devices.Count} dispositivo(s) ativo(s), {_blockedVks.Count} tecla(s) de mídia bloqueáveis, teclas neutralizadas: {neutralText}");
    }

    public void StartRecording(string deviceKey)
    {
        _recordingKey = deviceKey;
        _recordingConsumer.Clear();
        _recordingHookVks.Clear();
        Log.Write("GRAVAÇÃO", $"iniciada para {deviceKey}");
    }

    public void StopRecording()
    {
        if (_recordingKey is null)
            return;
        Log.Write("GRAVAÇÃO", $"concluída para {_recordingKey}");
        _recordingKey = null;
    }

    private bool IsRecording(string deviceKey) =>
        _recordingKey is not null && string.Equals(_recordingKey, deviceKey, StringComparison.OrdinalIgnoreCase);

    // ---------- Hook ----------

    private IntPtr OnHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
            return CallNextHookEx(_hook, nCode, wParam, lParam);

        var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        if ((IntPtr)(long)data.ExtraInfo.ToUInt64() == ActionRunner.Marker)
            return CallNextHookEx(_hook, nCode, wParam, lParam);

        var vk = (ushort)data.VkCode;
        var isUp = (int)wParam is WM_KEYUP or WM_SYSKEYUP;
        var at = RawInputSource.Now;

        if (_recordingKey is not null)
        {
            // Na gravação nada é bloqueado: só observa qual VK o Windows gera para cada botão de mídia.
            if (!isUp && (data.Flags & LLKHF_INJECTED) != 0)
                _recordingHookVks.Add((at, vk));
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        if (Paused || !_blockedVks.Contains(vk))
            return CallNextHookEx(_hook, nCode, wParam, lParam);

        // Time é o instante em que o Windows gerou a tecla; a diferença é quanto o hook demorou a ser chamado.
        var delay = unchecked((uint)Environment.TickCount - data.Time);
        if (!isUp && delay > SlowHookMs && delay < 60_000)
            Log.Write("HOOK", $"{KeyNames.VkName(vk)} chegou ao hook com {delay} ms de atraso");

        _recent.RemoveAll(r => at - r.At > WindowMs);
        var match = _recent.FindIndex(r => r.Button.HookVk == vk && r.IsUp == isUp);
        if (match >= 0)
        {
            var (_, button, _) = _recent[match];
            _recent.RemoveAt(match);
            Consume(button, isUp);
        }
        else
        {
            if (!isUp)
                Log.Write("HOOK", $"{KeyNames.VkName(vk)} segurada antes do Raw, aguardando até {WindowMs} ms");
            _pending.Add(new PendingKey(at, vk, (ushort)data.ScanCode, isUp, (data.Flags & 0x01) != 0));
        }
        return 1;
    }

    // ---------- Raw Input ----------

    private void OnConsumer(ConsumerEvent e)
    {
        if (IsRecording(e.DeviceKey))
        {
            // Espera um pouco para ver qual VK o hook recebe junto deste botão.
            if (!e.IsUp)
                _recordingConsumer.Add((e.At, e.Usage));
            return;
        }

        if (Paused || !_devices.TryGetValue(e.DeviceKey, out var device))
            return;

        var button = device.Buttons.FirstOrDefault(b => b.Part == ButtonPart.Consumer && b.Code == e.Usage && b.Action.Type != ActionType.Keep);
        if (button is null)
            return;

        Log.Write("RAW", $"{device.Name}: {(e.IsUp ? "solta" : "aperta")} {button.Name} (uso 0x{e.Usage:X4})");

        // Sem VK conhecido o Windows não gera tecla para bloquear: só dispara a ação.
        if (button.HookVk is null)
        {
            Consume(button, e.IsUp);
            return;
        }

        var pending = _pending.FindIndex(p => p.Vk == button.HookVk && p.IsUp == e.IsUp);
        if (pending >= 0)
        {
            _pending.RemoveAt(pending);
            Consume(button, e.IsUp);
            return;
        }

        _recent.Add((e.At, button, e.IsUp));
    }

    private void OnKey(KeyEvent e)
    {
        // Tecla neutralizada: chega como F13..F24 e volta a ser a original. Só o VK identifica:
        // o scancode que o Raw Input entrega nesse caso não é confiável (medido no app: "F24"
        // não era reconhecido comparando o scancode).
        var neutral = _neutralByTargetVk.TryGetValue(e.Vk, out var n) ? n : null;
        var scan = neutral?.OriginalScan ?? e.Scan;
        var vk = neutral?.OriginalVk ?? e.Vk;

        if (IsRecording(e.DeviceKey))
        {
            if (!e.IsUp)
                Recorded?.Invoke(new RecordedButton(ButtonPart.Keyboard, vk, scan, null, KeyNames.VkName(vk)));
            if (neutral is not null)
                Restore(neutral, e.IsUp);
            return;
        }

        DeviceConfig? device = null;
        if (!Paused && e.DeviceKey.Length > 0)
            _devices.TryGetValue(e.DeviceKey, out device);

        var button = device?.Buttons.FirstOrDefault(b => b.Part == ButtonPart.Keyboard && b.ScanCode == scan && b.Action.Type != ActionType.Keep);
        if (button is not null)
        {
            Log.Write("RAW", $"{device!.Name}: {(e.IsUp ? "solta" : "aperta")} {button.Name} ({KeyNames.VkName(vk)}"
                + $"{(neutral is null ? "" : $", neutralizada, chegou como {KeyNames.VkName(e.Vk)}")}, scan 0x{e.Scan:X4})");
            if (e.IsUp)
            {
                _held.Remove(button.Id);
            }
            else if (_held.Add(button.Id))
            {
                // Segurar o botão gera "aperta" repetido: a ação dispara só no primeiro.
                Consume(button, isUp: false);
            }
            return;
        }

        // Outro teclado (ou o app pausado): a tecla neutralizada volta a ser a original.
        if (neutral is not null)
            Restore(neutral, e.IsUp);
    }

    private static void Restore(NeutralKey neutral, bool isUp) =>
        ActionRunner.SendKeyEvent(neutral.OriginalVk, (ushort)(neutral.OriginalScan & 0xFF), isUp,
            extended: (neutral.OriginalScan & 0xFF00) == 0xE000);

    private static void Consume(ButtonConfig button, bool isUp)
    {
        if (isUp)
            return;

        Log.Write("REMAPEADO", $"{button.Name} → {ActionText.Describe(button.Action).Main}");
        if (button.Action.Type != ActionType.None)
            ActionRunner.Run(button.Action, button.Name);
    }

    // ---------- Timer ----------

    private void OnTick()
    {
        var now = RawInputSource.Now;

        // Raw do dispositivo sem a tecla correspondente no hook: o Windows não chamou o hook a tempo
        // (ou o pulou), e a tecla original pode ter chegado aos programas.
        foreach (var recent in _recent.Where(r => now - r.At > WindowMs).ToList())
        {
            _recent.Remove(recent);
            if (!recent.IsUp)
                Log.Write("HOOK", $"{recent.Button.Name}: o Raw chegou, mas o hook não recebeu a tecla em {WindowMs} ms");
        }

        foreach (var pending in _pending.Where(p => now - p.At > WindowMs).ToList())
        {
            _pending.Remove(pending);
            ActionRunner.SendKeyEvent(pending.Vk, pending.Scan, pending.IsUp, pending.Extended);
            if (!pending.IsUp)
                Log.Write("REPASSE", $"{KeyNames.VkName(pending.Vk)} veio de outro teclado, reenviada");
        }

        foreach (var (at, usage) in _recordingConsumer.Where(r => now - r.At >= LearnWindowMs).ToList())
        {
            _recordingConsumer.Remove((at, usage));
            var learned = _recordingHookVks.Where(h => Math.Abs(h.At - at) <= LearnWindowMs).Select(h => (ushort?)h.Vk).FirstOrDefault();
            var hookVk = learned ?? KeyNames.VkForConsumer(usage);
            Log.Write("GRAVAÇÃO", $"mídia 0x{usage:X4}, VK no hook {(hookVk is { } v ? $"0x{v:X2}" : "nenhum")}{(learned is null ? " (tabela)" : " (aprendido)")}");
            Recorded?.Invoke(new RecordedButton(ButtonPart.Consumer, usage, 0, hookVk, KeyNames.ConsumerName(usage)));
        }
        _recordingHookVks.RemoveAll(h => now - h.At > LearnWindowMs * 3);
    }

    public void Dispose()
    {
        _timer.Stop();
        if (_hook != IntPtr.Zero)
            UnhookWindowsHookEx(_hook);
    }
}
