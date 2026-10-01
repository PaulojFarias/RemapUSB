using System.Runtime.InteropServices;
using static RemapUSB.Probe.Native;

namespace RemapUSB.Probe;

/// <summary>
/// Hook de teclado de baixo nível só para observar: mostra o que chega ao fluxo
/// de teclado do Windows (de qualquer teclado) e nunca bloqueia nada.
/// </summary>
internal sealed class KeyboardHook : IDisposable
{
    private readonly LowLevelKeyboardProc _callback;
    private readonly IntPtr _hook;

    public KeyboardHook()
    {
        _callback = OnKey;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _callback, GetModuleHandle(null), 0);
        Console.WriteLine(_hook == IntPtr.Zero
            ? $"[HOOK] FALHA ao instalar, erro {Marshal.GetLastWin32Error()}"
            : "[HOOK] OK    teclado de baixo nível (só observa)");
    }

    private IntPtr OnKey(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var message = (int)wParam;
            var state = message is WM_KEYUP or WM_SYSKEYUP ? "solta  " : "aperta ";
            var injected = (data.Flags & 0x10) != 0 ? " (injetada)" : "";
            Console.WriteLine($"{Clock.Now} [HOOK] {"qualquer teclado",-28} {state} VK=0x{data.VkCode:X2} {(Keys)data.VkCode,-18} scan=0x{data.ScanCode:X2}{injected}");
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
            UnhookWindowsHookEx(_hook);
    }
}
