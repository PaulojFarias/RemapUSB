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
        HookCorrelator.Filtering = filter is not null;

        var logPath = Log.Open();
        Log.Write($"RemapUSB Probe | máquina {Environment.MachineName} | {DateTime.Now:yyyy-MM-dd HH:mm:ss} | filtro {filter ?? "(nenhum)"}");
        Log.Write("Aperte os botões do controle com qualquer janela em foco. Ctrl+C para sair.");
        Log.Write("[RAW] = Raw Input (sabe o dispositivo) | [HOOK] = fluxo de teclado do Windows (não sabe o dispositivo)");
        Log.Write(filter is null
            ? "Sem filtro: o hook grava TODAS as teclas de todos os teclados."
            : "Com filtro: o hook só grava teclas a até 150ms de um evento do dispositivo filtrado.");
        Log.Write();

        using var window = new RawInputWindow(filter);
        using var hook = new KeyboardHook();
        Log.Write();
        Console.WriteLine($"Gravando em {logPath}");
        Console.WriteLine();

        Application.Run();
    }
}
