namespace WowServer.Core;

/// <summary>Pasta de modulo com um nome que o core nao reconhece mais.</summary>
/// <param name="Current">Nome que esta em disco.</param>
/// <param name="Correct">Nome que o CMake do core procura.</param>
/// <param name="Conflict">A pasta certa ja existe ao lado da errada.</param>
public sealed record RenamedModule(string Current, string Correct, bool Conflict)
{
    public string Explanation =>
        Conflict
            ? $"As pastas '{Current}' e '{Correct}' existem as duas. O mesmo código "
              + "entraria duas vezes na compilação, então renomear não resolve — "
              + $"remova '{Current}'."
            : $"A pasta precisa se chamar '{Correct}'. O CMake do core só liga a "
              + $"biblioteca Lua ao alvo dos módulos quando encontra esse nome; com "
              + $"'{Current}' a biblioteca compila e o 'lua.h' nunca entra no include "
              + "path, e a compilação para com 25 erros iguais.";
}

/// <summary>
/// Mesma regra do <c>Get-RenamedModule</c> em scripts/lib/common.ps1, do lado
/// da interface - aqui so para desenhar o aviso e o botao. Quem bloqueia a
/// compilacao continua sendo o rebuild.ps1, e quem renomeia e o
/// fix-module-name.ps1.
/// </summary>
public static class RenamedModules
{
    /// <summary>Nome antigo -> nome que o core procura.</summary>
    public static IReadOnlyDictionary<string, string> Map { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["mod-eluna"] = "mod-ale",
        };

    public static IReadOnlyList<RenamedModule> Detect(string modulesDir)
    {
        if (string.IsNullOrWhiteSpace(modulesDir) || !Directory.Exists(modulesDir))
            return Array.Empty<RenamedModule>();

        var achados = new List<RenamedModule>();

        foreach (var (antigo, novo) in Map)
        {
            if (!Directory.Exists(Path.Combine(modulesDir, antigo))) continue;
            achados.Add(new RenamedModule(
                antigo, novo, Directory.Exists(Path.Combine(modulesDir, novo))));
        }

        return achados;
    }
}
