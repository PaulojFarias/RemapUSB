namespace RemapUSB.Engine;

/// <summary>Nomes de teclas e tabelas do Windows usadas pelo motor e pela tela.</summary>
internal static class KeyNames
{
    public const ushort VkApps = 0x5D;

    public static readonly (string Name, ushort Vk)[] MediaKeys =
    [
        ("Play/Pause", 0xB3),
        ("Próxima faixa", 0xB0),
        ("Faixa anterior", 0xB1),
        ("Parar", 0xB2),
        ("Volume +", 0xAF),
        ("Volume −", 0xAE),
        ("Mudo", 0xAD),
    ];

    /// <summary>Uso HID de mídia → VK que o Windows gera. Reserva para quando a gravação não aprende.</summary>
    private static readonly Dictionary<ushort, ushort> ConsumerToVk = new()
    {
        [0x00B5] = 0xB0, [0x00B6] = 0xB1, [0x00B7] = 0xB2, [0x00CD] = 0xB3,
        [0x00E2] = 0xAD, [0x00E9] = 0xAF, [0x00EA] = 0xAE,
        [0x018A] = 0xB4, [0x0183] = 0xB5, [0x0192] = 0xB7, [0x0194] = 0xB6,
        [0x0221] = 0xAA, [0x0223] = 0xAC, [0x0224] = 0xA6, [0x0225] = 0xA7,
        [0x0226] = 0xA9, [0x0227] = 0xA8, [0x022A] = 0xAB,
    };

    private static readonly Dictionary<ushort, string> ConsumerNames = new()
    {
        [0x00B5] = "Próxima faixa", [0x00B6] = "Faixa anterior", [0x00B7] = "Parar", [0x00CD] = "Play/Pause",
        [0x00E2] = "Mudo", [0x00E9] = "Volume +", [0x00EA] = "Volume −",
        [0x0221] = "Pesquisar", [0x0223] = "Início (navegador)", [0x0224] = "Voltar (navegador)",
        [0x0225] = "Avançar (navegador)", [0x0226] = "Parar (navegador)", [0x0227] = "Atualizar (navegador)",
        [0x022A] = "Favoritos", [0x018A] = "E-mail", [0x0192] = "Calculadora", [0x0194] = "Explorador",
    };

    private static readonly Dictionary<ushort, string> VkNames = new()
    {
        [0x08] = "Backspace", [0x09] = "Tab", [0x0D] = "Enter", [0x1B] = "Esc", [0x20] = "Espaço",
        [0x21] = "Page Up", [0x22] = "Page Down", [0x23] = "End", [0x24] = "Home",
        [0x25] = "Seta para a esquerda", [0x26] = "Seta para cima", [0x27] = "Seta para a direita", [0x28] = "Seta para baixo",
        [0x2D] = "Insert", [0x2E] = "Delete", [0x5B] = "Win", [0x5C] = "Win", [0x5D] = "Menu",
        [0x10] = "Shift", [0x11] = "Ctrl", [0x12] = "Alt", [0xA0] = "Shift", [0xA1] = "Shift",
        [0xA2] = "Ctrl", [0xA3] = "Ctrl", [0xA4] = "Alt", [0xA5] = "Alt",
        [0xA6] = "Voltar (navegador)", [0xA7] = "Avançar (navegador)", [0xA8] = "Atualizar (navegador)",
        [0xA9] = "Parar (navegador)", [0xAA] = "Pesquisar", [0xAB] = "Favoritos", [0xAC] = "Início (navegador)",
        [0xAD] = "Mudo", [0xAE] = "Volume −", [0xAF] = "Volume +",
        [0xB0] = "Próxima faixa", [0xB1] = "Faixa anterior", [0xB2] = "Parar", [0xB3] = "Play/Pause",
    };

    /// <summary>Teclas que precisam da flag "estendida" no SendInput.</summary>
    private static readonly HashSet<ushort> ExtendedVks =
    [
        0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x2D, 0x2E, 0x5B, 0x5C, 0x5D, 0x6F, 0x90,
        0xA3, 0xA5, 0xA6, 0xA7, 0xA8, 0xA9, 0xAA, 0xAB, 0xAC, 0xAD, 0xAE, 0xAF, 0xB0, 0xB1, 0xB2, 0xB3,
        0xB4, 0xB5, 0xB6, 0xB7,
    ];

    /// <summary>Teclas que dá para neutralizar sem custo grande: quase ninguém usa.</summary>
    private static readonly HashSet<ushort> LowRiskVks = [0x5D, 0x5B, 0x5C, 0xAA, 0xAB, 0xB4, 0xB5, 0xB6, 0xB7];

    public static ushort? VkForConsumer(ushort usage) => ConsumerToVk.TryGetValue(usage, out var vk) ? vk : null;

    public static string ConsumerName(ushort usage) =>
        ConsumerNames.TryGetValue(usage, out var name) ? name : $"Mídia 0x{usage:X4}";

    public static string VkName(ushort vk)
    {
        if (VkNames.TryGetValue(vk, out var name))
            return name;
        if (vk is >= 0x70 and <= 0x87)
            return $"F{vk - 0x6F}";
        if (vk is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A)
            return ((char)vk).ToString();
        return ((System.Windows.Forms.Keys)vk).ToString();
    }

    public static bool IsExtended(ushort vk) => ExtendedVks.Contains(vk);

    public static bool IsLowRisk(ushort vk) => LowRiskVks.Contains(vk) || vk is >= 0x7C and <= 0x87;

    public static bool IsModifier(ushort vk) => vk is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or >= 0xA0 and <= 0xA5;
}
