using System.Reflection;

namespace RemapUSB.Infrastructure;

/// <summary>Qual build está rodando, gravado na compilação (ver o target RemapUsbBuildInfo no .csproj).</summary>
internal static class BuildInfo
{
    private static readonly Dictionary<string, string?> Metadata = typeof(BuildInfo).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .GroupBy(a => a.Key)
        .ToDictionary(g => g.Key, g => g.First().Value);

    public static string Commit => Metadata.GetValueOrDefault("GitCommit") is { Length: > 0 } commit ? commit : "desconhecido";

    public static bool Dirty => Metadata.GetValueOrDefault("GitDirty") == "true";

    public static string Time => Metadata.GetValueOrDefault("BuildTime") ?? "?";

    /// <summary>Ex.: "26d554b (com alterações não commitadas), compilado em 2026-10-01 18:40:12".</summary>
    public static string Describe() => $"{Commit}{(Dirty ? " (com alterações não commitadas)" : "")}, compilado em {Time}";
}
