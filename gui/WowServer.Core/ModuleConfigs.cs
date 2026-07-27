namespace WowServer.Core;

/// <summary>Um .conf de modulo encontrado em <c>configs\modules</c>.</summary>
public sealed record ModuleConfFile(string ModuleName, string FileName, string FullPath);

/// <summary>
/// Liga a pasta de um modulo ao .conf que ele gerou.
///
/// <para>
/// Os dois nomes raramente batem: <c>mod-ah-bot</c> gera <c>mod_ahbot.conf</c>,
/// <c>mod-playerbots</c> gera <c>playerbots.conf</c>, <c>mod-ale</c> gera
/// <c>mod_ale.conf</c>. Comparar o nome cru nao acha quase nada, entao a
/// comparacao e feita sobre uma forma normalizada: minusculas, sem
/// <c>-</c>/<c>_</c>/espaco e sem o prefixo <c>mod</c>.
/// </para>
/// </summary>
public static class ModuleConfigs
{
    /// <summary>
    /// Reduz um nome a letras e digitos, sem o prefixo "mod". "mod-ah-bot",
    /// "mod_ahbot" e "AhBot" viram todos "ahbot".
    /// </summary>
    public static string Normalize(string name)
    {
        var limpo = new string(name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return limpo.StartsWith("mod", StringComparison.Ordinal) && limpo.Length > 3
            ? limpo[3..]
            : limpo;
    }

    /// <summary>Diz se um arquivo .conf pertence a um modulo.</summary>
    public static bool Matches(string moduleName, string confFileName)
    {
        var m = Normalize(moduleName);
        var c = Normalize(Path.GetFileNameWithoutExtension(confFileName));
        if (m.Length == 0 || c.Length == 0) return false;

        // Nao basta a igualdade: ha modulo que gera mais de um .conf, com um
        // sufixo ("playerbots.conf" e "playerbots_rpg.conf"), e ha .conf com
        // nome mais curto que a pasta.
        return m == c
            || c.StartsWith(m, StringComparison.Ordinal)
            || m.StartsWith(c, StringComparison.Ordinal);
    }

    /// <summary>
    /// Todos os .conf de <c>&lt;ServerDir&gt;\configs\modules</c>, ja ligados ao
    /// modulo de cada um. Arquivo que nao casa com modulo nenhum sai com
    /// <see cref="ModuleConfFile.ModuleName"/> vazio.
    /// </summary>
    public static IReadOnlyList<ModuleConfFile> Discover(
        string serverDir, IEnumerable<string> installedModules)
    {
        var pasta = Path.Combine(serverDir, "configs", "modules");
        if (!Directory.Exists(pasta)) return Array.Empty<ModuleConfFile>();

        var modulos = installedModules.ToList();
        var achados = new List<ModuleConfFile>();

        // Só .conf: o .conf.dist ao lado é o modelo, e editar o modelo não muda
        // nada no servidor - é o erro óbvio a evitar aqui.
        foreach (var arquivo in Directory.GetFiles(pasta, "*.conf").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var nome = Path.GetFileName(arquivo);
            var dono = modulos.FirstOrDefault(m => Matches(m, nome)) ?? "";
            achados.Add(new ModuleConfFile(dono, nome, arquivo));
        }

        return achados;
    }

    /// <summary>Os .conf de um modulo só.</summary>
    public static IReadOnlyList<ModuleConfFile> ForModule(
        string serverDir, string moduleName) =>
        Discover(serverDir, new[] { moduleName })
            .Where(c => c.ModuleName.Length > 0)
            .ToList();
}
