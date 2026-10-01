using System.Diagnostics;
using System.Text;

namespace RemapUSB.Probe;

/// <summary>
/// Protótipo 1: mostra cada botão apertado e por qual parte do dispositivo ele chegou.
/// Uso: RemapUSB.Probe [filtro]   ex.: RemapUSB.Probe VID_0627
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var filter = args.Length > 0 ? args[0] : null;

        Console.WriteLine("RemapUSB Probe: aperte os botões do controle. Ctrl+C para sair.");
        Console.WriteLine("[RAW] = Raw Input (sabe o dispositivo) | [HOOK] = fluxo de teclado do Windows (não sabe o dispositivo)");
        Console.WriteLine();

        using var window = new RawInputWindow(filter);
        using var hook = new KeyboardHook();
        Console.WriteLine();

        Application.Run();
    }
}

internal static class Clock
{
    private static readonly Stopwatch Watch = Stopwatch.StartNew();

    public static string Now => $"{Watch.Elapsed.TotalMilliseconds,10:F1}ms";
}
