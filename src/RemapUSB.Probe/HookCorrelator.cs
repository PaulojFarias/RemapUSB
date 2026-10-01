namespace RemapUSB.Probe;

/// <summary>
/// O hook não sabe de qual teclado veio a tecla. Com filtro ativo, só grava as linhas
/// do hook próximas (no tempo) de um evento Raw Input do dispositivo filtrado, para não
/// registrar o que é digitado em outros teclados.
/// </summary>
internal static class HookCorrelator
{
    private const double WindowMs = 150;

    private static readonly List<(double At, string Line)> Pending = new();
    private static double _lastDeviceEventAt = double.NegativeInfinity;

    public static bool Filtering { get; set; }

    public static void OnDeviceEvent(double at)
    {
        _lastDeviceEventAt = at;
        foreach (var (hookAt, line) in Pending)
        {
            if (at - hookAt <= WindowMs)
                Log.Write(line);
        }
        Pending.Clear();
    }

    public static void OnHookEvent(double at, string line)
    {
        if (!Filtering || at - _lastDeviceEventAt <= WindowMs)
        {
            Log.Write(line);
            return;
        }

        Pending.RemoveAll(p => at - p.At > WindowMs);
        Pending.Add((at, line));
    }
}
