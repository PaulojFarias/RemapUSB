using RemapUSB.Infrastructure;

namespace RemapUSB.Engine;

/// <summary>
/// Modo sem janela (RemapUSB.exe --desfazer-teclas), chamado pelo desinstalador: remove do
/// Scancode Map só as teclas que o app neutralizou, como o "Desfazer tudo" de Configurações.
/// O código de saída diz ao desinstalador o que aconteceu.
/// </summary>
internal static class NeutralCleanup
{
    public const string Argument = "--desfazer-teclas";

    public const int NothingToUndo = 0;
    public const int Failed = 1;
    public const int UndoneRestartNeeded = 10;

    public static int Run()
    {
        Log.SetEnabled(true);
        Log.Write("DESFAZER", $"RemapUSB {BuildInfo.Describe()} | desfazendo teclas neutralizadas (desinstalação)");

        int result;
        try
        {
            var applied = ScancodeMap.ReadApplied();
            if (applied.Count == 0)
            {
                Log.Write("DESFAZER", "nenhuma tecla neutralizada pelo app");
                result = NothingToUndo;
            }
            else
            {
                Log.Write("DESFAZER", $"teclas a desfazer: {string.Join(", ", applied.Select(a => a.Describe()))}");

                // Fora da thread da interface: o await dentro de WriteAsync voltaria para ela e travaria.
                var ok = Task.Run(() => ScancodeMap.WriteAsync([])).GetAwaiter().GetResult();
                result = ok ? UndoneRestartNeeded : Failed;
                Log.Write("DESFAZER", ok ? "desfeito; vale depois de reiniciar" : "não desfeito (administrador recusado ou erro)");
            }
        }
        catch (Exception ex)
        {
            Log.Write("ERRO", $"desfazer teclas: {ex}");
            result = Failed;
        }

        Log.Flush();
        return result;
    }
}
