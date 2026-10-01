using RemapUSB.Model;

namespace RemapUSB.Engine;

internal static class ActionText
{
    public static readonly (ActionType Type, string Label)[] All =
    [
        (ActionType.Keep, "Manter original"),
        (ActionType.None, "Não fazer nada"),
        (ActionType.Key, "Enviar tecla ou atalho"),
        (ActionType.Media, "Tecla de mídia"),
        (ActionType.OpenApp, "Abrir app"),
        (ActionType.CloseApp, "Fechar app"),
        (ActionType.ToggleApp, "Abrir/fechar app (alternar)"),
        (ActionType.RestartApp, "Reiniciar app"),
        (ActionType.Site, "Abrir site"),
        (ActionType.File, "Abrir arquivo ou pasta"),
        (ActionType.Command, "Executar comando"),
    ];

    public static string Label(ActionType type) => All.First(a => a.Type == type).Label;

    public static string Keys(IEnumerable<ushort>? keys) =>
        keys is null || !keys.Any() ? "(sem tecla)" : string.Join(" + ", keys.Select(KeyNames.VkName));

    public static (string Main, string Sub) Describe(ActionConfig action) => action.Type switch
    {
        ActionType.Keep => ("Manter original", ""),
        ActionType.None => ("Não fazer nada", ""),
        ActionType.Key => ($"Enviar {Keys(action.Keys)}", ""),
        ActionType.Media => ($"Tecla de mídia: {KeyNames.MediaKeys.FirstOrDefault(m => m.Vk == action.MediaVk).Name ?? "(escolha)"}", ""),
        ActionType.Site => ("Abrir site", action.Url ?? ""),
        ActionType.File => ("Abrir arquivo", action.Path ?? ""),
        ActionType.Command => ("Executar comando", action.Command ?? ""),
        _ => (Label(action.Type), action.App?.Name ?? "(escolha o app)"),
    };
}
