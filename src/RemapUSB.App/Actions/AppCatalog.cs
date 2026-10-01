using System.Text;
using System.Xml.Linq;
using Microsoft.Win32;
using RemapUSB.Infrastructure;
using RemapUSB.Model;

namespace RemapUSB.Actions;

/// <summary>Apps instalados para o seletor da tela, e onde cada um está instalado agora.</summary>
internal static class AppCatalog
{
    private const string PackagesKey = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";

    private static Task<List<AppRef>>? _loading;

    /// <summary>Carrega uma vez, em segundo plano. Ler centenas de manifestos leva alguns segundos.</summary>
    public static Task<List<AppRef>> LoadAsync() => _loading ??= Task.Run(() =>
    {
        var apps = new List<AppRef>();
        try { apps.AddRange(ListPackages()); } catch (Exception ex) { Log.Write("ERRO", $"listar apps da Store: {ex.Message}"); }
        try { apps.AddRange(ListStartMenu()); } catch (Exception ex) { Log.Write("ERRO", $"listar atalhos: {ex.Message}"); }

        return apps
            .GroupBy(a => a.Kind == AppKind.Package ? $"{a.PackageFamily}!{a.AppId}" : a.ExePath!.ToLowerInvariant())
            .Select(g => g.First())
            .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    });

    /// <summary>Pasta de instalação atual do pacote (muda a cada atualização).</summary>
    public static string? PackageRoot(string family)
    {
        var (name, publisher) = SplitFamily(family);
        using var key = Registry.CurrentUser.OpenSubKey(PackagesKey);
        if (key is null)
            return null;

        foreach (var fullName in key.GetSubKeyNames())
        {
            if (fullName.StartsWith(name + "_", StringComparison.OrdinalIgnoreCase)
                && fullName.EndsWith("_" + publisher, StringComparison.OrdinalIgnoreCase))
            {
                using var package = key.OpenSubKey(fullName);
                if (package?.GetValue("PackageRootFolder") is string root)
                    return root;
            }
        }
        return null;
    }

    private static IEnumerable<AppRef> ListPackages()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PackagesKey);
        if (key is null)
            yield break;

        foreach (var fullName in key.GetSubKeyNames())
        {
            using var package = key.OpenSubKey(fullName);
            if (package?.GetValue("PackageRootFolder") is not string root)
                continue;

            // Componentes do próprio Windows (menu Iniciar, barra de tarefas...) não interessam.
            if (root.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase))
                continue;

            var manifest = Path.Combine(root, "AppxManifest.xml");
            if (!File.Exists(manifest))
                continue;

            XDocument doc;
            try { doc = XDocument.Load(manifest); } catch { continue; }

            var parts = fullName.Split('_');
            if (parts.Length < 5)
                continue;
            var family = $"{parts[0]}_{parts[^1]}";

            foreach (var app in doc.Descendants().Where(e => e.Name.LocalName == "Application"))
            {
                var visual = app.Descendants().FirstOrDefault(e => e.Name.LocalName == "VisualElements");
                if (visual is null || string.Equals((string?)visual.Attribute("AppListEntry"), "none", StringComparison.OrdinalIgnoreCase))
                    continue;

                var id = (string?)app.Attribute("Id");
                if (id is null)
                    continue;

                var display = Resolve((string?)visual.Attribute("DisplayName"), fullName, parts[0])
                    ?? Resolve(package.GetValue("DisplayName") as string, fullName, parts[0])
                    ?? parts[0];

                yield return new AppRef { Kind = AppKind.Package, Name = display, PackageFamily = family, AppId = id };
            }
        }
    }

    /// <summary>
    /// Resolve nomes indiretos ("ms-resource:..." do manifesto ou "@{...}" do registro) pelo Windows.
    /// Nulo se não der: o nome cru não serve para mostrar.
    /// </summary>
    private static string? Resolve(string? value, string fullName, string packageName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        string[] candidates;
        if (value.StartsWith("@{", StringComparison.Ordinal))
        {
            candidates = [value];
        }
        else if (value.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
        {
            var resource = value["ms-resource:".Length..];
            candidates = resource.StartsWith("//")
                ? [$"@{{{fullName}?ms-resource:{resource}}}"]
                : resource.StartsWith('/')
                    ? [$"@{{{fullName}?ms-resource://{packageName}{resource}}}"]
                    : [$"@{{{fullName}?ms-resource://{packageName}/resources/{resource}}}", $"@{{{fullName}?ms-resource://{packageName}/{resource}}}"];
        }
        else
        {
            return value;
        }

        foreach (var candidate in candidates)
        {
            var buffer = new StringBuilder(512);
            if (Native.SHLoadIndirectString(candidate, buffer, (uint)buffer.Capacity, IntPtr.Zero) == 0 && buffer.Length > 0
                && !buffer.ToString().StartsWith("@{", StringComparison.Ordinal) && !buffer.ToString().StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
                return buffer.ToString();
        }
        return null;
    }

    private static IEnumerable<AppRef> ListStartMenu()
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null)
            yield break;
        dynamic shell = Activator.CreateInstance(shellType)!;

        string[] folders =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
        ];

        foreach (var folder in folders.Where(Directory.Exists))
        {
            foreach (var link in Directory.EnumerateFiles(folder, "*.lnk", SearchOption.AllDirectories))
            {
                var name = Path.GetFileNameWithoutExtension(link);
                if (name.Contains("uninstall", StringComparison.OrdinalIgnoreCase) || name.Contains("desinstalar", StringComparison.OrdinalIgnoreCase))
                    continue;

                string? target = null;
                try { target = (string)shell.CreateShortcut(link).TargetPath; } catch { }
                if (string.IsNullOrEmpty(target) || !target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(target))
                    continue;

                yield return new AppRef { Kind = AppKind.Exe, Name = name, ExePath = target };
            }
        }
    }

    private static (string Name, string Publisher) SplitFamily(string family)
    {
        var index = family.LastIndexOf('_');
        return index < 0 ? (family, "") : (family[..index], family[(index + 1)..]);
    }
}
