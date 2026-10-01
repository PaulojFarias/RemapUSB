using System.Collections.Concurrent;
using System.Text;

namespace RemapUSB.Infrastructure;

/// <summary>
/// Log em arquivo, um por execução. Rodando de dentro do repositório (desenvolvimento), o arquivo
/// vai para a raiz dele, para levar o resultado entre máquinas; instalado, vai para %LOCALAPPDATA%.
/// Teclas de outros teclados nunca entram no log.
///
/// Escrever só enfileira: a gravação em disco roda em outra thread, para nunca atrasar o hook
/// de teclado (o Windows pula um hook que demora a responder).
/// </summary>
internal static class Log
{
    private static readonly BlockingCollection<string> Queue = new();
    private static readonly Lock Gate = new();
    private static StreamWriter? _file;

    static Log()
    {
        new Thread(Drain) { IsBackground = true, Name = "RemapUSB.Log", Priority = ThreadPriority.BelowNormal }.Start();
    }

    public static string Folder { get; } = FindRepositoryRoot()
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RemapUSB", "logs");

    public static string? FilePath { get; private set; }

    public static void SetEnabled(bool enabled)
    {
        lock (Gate)
        {
            if (enabled && _file is null)
            {
                Directory.CreateDirectory(Folder);
                FilePath = Path.Combine(Folder, $"app-{Environment.MachineName}-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
                _file = new StreamWriter(FilePath, append: true, new UTF8Encoding(false)) { AutoFlush = true };
            }
            else if (!enabled && _file is not null)
            {
                _file.Dispose();
                _file = null;
            }
        }
    }

    public static void Write(string tag, string message) =>
        Queue.Add($"{DateTime.Now:HH:mm:ss.fff} [{tag}]{new string(' ', Math.Max(1, 11 - tag.Length))}{message}");

    /// <summary>Espera a fila esvaziar (ao sair do app).</summary>
    public static void Flush()
    {
        for (var i = 0; i < 50 && Queue.Count > 0; i++)
            Thread.Sleep(20);
    }

    private static void Drain()
    {
        foreach (var line in Queue.GetConsumingEnumerable())
        {
            lock (Gate)
            {
                System.Diagnostics.Debug.WriteLine(line);
                try { _file?.WriteLine(line); } catch (IOException) { }
            }
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
