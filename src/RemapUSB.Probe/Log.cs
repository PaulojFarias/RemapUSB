using System.Diagnostics;
using System.Text;

namespace RemapUSB.Probe;

/// <summary>
/// Escreve no console e num .txt na raiz do repositório, para levar o resultado entre máquinas.
/// </summary>
internal static class Log
{
    private static StreamWriter? _file;

    public static string Open(string prefix = "probe")
    {
        var root = FindRepositoryRoot() ?? Directory.GetCurrentDirectory();
        var path = Path.Combine(root, $"{prefix}-{Environment.MachineName}-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        _file = new StreamWriter(path, append: false, new UTF8Encoding(false)) { AutoFlush = true };
        return path;
    }

    private static readonly Lock Gate = new();

    public static void Write(string line = "")
    {
        lock (Gate)
        {
            Console.WriteLine(line);
            _file?.WriteLine(line);
        }
    }

    private static string? FindRepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "RemapUSB.slnx")))
                return dir.FullName;
        }
        return null;
    }
}

internal static class Clock
{
    private static readonly Stopwatch Watch = Stopwatch.StartNew();

    public static double Ms => Watch.Elapsed.TotalMilliseconds;

    public static string Format(double ms) => $"{ms,10:F1}ms";
}
