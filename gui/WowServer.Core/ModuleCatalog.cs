namespace WowServer.Core;

public enum ModuleStatus
{
    /// <summary>Instalado e verificado nesta configuracao.</summary>
    Testado,
    /// <summary>Da org oficial do AzerothCore, mas nao exercitado aqui.</summary>
    Oficial,
    /// <summary>Exige trocar o core inteiro por um fork.</summary>
    ExigeFork,
}

public sealed record CatalogModule(
    string Name,
    string DisplayName,
    string Repository,
    string Summary,
    string Details,
    ModuleStatus Status,
    string? PostInstallNote = null,
    string? ForkRepository = null,
    string? ForkBranch = null)
{
    /// <summary>
    /// Enderecos antigos que ainda apontam para o mesmo fork (o GitHub
    /// redireciona repositorio que mudou de dono). Servem so para reconhecer um
    /// core ja instalado; clone novo usa sempre <see cref="ForkRepository"/>.
    /// </summary>
    public IReadOnlyList<string> ForkAliases { get; init; } = Array.Empty<string>();
}

public static class ModuleCatalog
{
    /// <summary>
    /// Diz se duas URLs apontam para o mesmo projeto, comparando so o final
    /// "dono/repo".
    /// <para>
    /// A mesma origem aparece escrita de varias formas: https, ssh
    /// (git@github.com:dono/repo.git), com credencial embutida, atras de um
    /// proxy. Comparar a URL inteira acusaria "core incompativel" em todos
    /// esses casos, com o modulo perfeitamente instalavel.
    /// </para>
    /// </summary>
    public static bool SameRepository(string a, string b) =>
        string.Equals(RepoTail(a), RepoTail(b), StringComparison.OrdinalIgnoreCase);

    private static string RepoTail(string url)
    {
        // A barra final vem depois do .git ("...repo.git/"), entao ela sai
        // antes - na ordem inversa o sufixo nao casa.
        var limpo = url.Trim().TrimEnd('/');
        if (limpo.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            limpo = limpo[..^4];
        limpo = limpo.TrimEnd('/');

        // O array explicito e obrigatorio: Split('/', ':', opcoes) compila, mas
        // liga em Split(char, int, StringSplitOptions) - o ':' vira 'count' por
        // conversao implicita de char para int, e a URL ssh nunca e separada.
        var partes = limpo.Split(new[] { '/', ':' }, StringSplitOptions.RemoveEmptyEntries);
        return partes.Length >= 2
            ? partes[^2] + "/" + partes[^1]
            : limpo;
    }

    /// <summary>
    /// Diz se o core que esta instalado serve para este modulo de fork.
    /// Aceita tambem os enderecos antigos do projeto: o Playerbots migrou de
    /// conta pessoal para organizacao, e mandar reclonar o core por causa de um
    /// redirecionamento do GitHub apagaria o codigo-fonte sem necessidade.
    /// </summary>
    public static bool SatisfiesFork(CatalogModule module, string coreRepository)
    {
        if (module.ForkRepository is null) return true;
        if (SameRepository(module.ForkRepository, coreRepository)) return true;
        return module.ForkAliases.Any(a => SameRepository(a, coreRepository));
    }

    public static IReadOnlyList<CatalogModule> All { get; } = new[]
    {
        new CatalogModule(
            "mod-ah-bot", "Casa de Leiloes (AH Bot)",
            "https://github.com/azerothcore/mod-ah-bot",
            "Enche o leilao de itens e compra o que voce poe a venda.",
            "Num servidor de uma pessoa so, a casa de leiloes fica deserta. Este "
            + "modulo simula vendedores e compradores, entao o leilao volta a ter uso.",
            ModuleStatus.Oficial,
            PostInstallNote:
                "Precisa de uma conta e um personagem dedicados, que aparecem como "
                + "vendedores. Crie no console do worldserver:\n\n"
                + "    account create ahbot suasenha\n\n"
                + "Entre com ela, crie um personagem, saia. Depois preencha o ID da "
                + "conta e o GUID do personagem em configs\\modules\\mod_ahbot.conf."),

        new CatalogModule(
            "mod-autobalance", "Escalonamento de Dungeon",
            "https://github.com/azerothcore/mod-autobalance",
            "Reduz vida e dano dos inimigos conforme o numero real de jogadores.",
            "Deixa dungeons e raides possiveis sozinho ou em grupo pequeno, "
            + "enfraquecendo os inimigos em vez de fortalecer voce. Preserva melhor "
            + "a sensacao de jogo do que o Solocraft.",
            ModuleStatus.Oficial),

        new CatalogModule(
            "mod-solo-lfg", "Dungeon Finder Solo",
            "https://github.com/azerothcore/mod-solo-lfg",
            "Permite entrar na fila do buscador de masmorras sozinho.",
            "O Dungeon Finder ja vem no 3.3.5a e esta ligado por padrao, mas exige "
            + "5 jogadores para formar grupo. Este modulo remove essa exigencia. "
            + "Combina com o Escalonamento de Dungeon.",
            ModuleStatus.Oficial),

        new CatalogModule(
            "mod-solocraft", "Solocraft",
            "https://github.com/azerothcore/mod-solocraft",
            "Fortalece voce para compensar o grupo que falta.",
            "Caminho oposto ao Escalonamento: em vez de enfraquecer a dungeon, "
            + "aumenta seus atributos. Usar os dois juntos costuma passar do ponto "
            + "e tornar tudo trivial - escolha um.",
            ModuleStatus.Oficial),

        new CatalogModule(
            "mod-eluna", "Eluna (scripts Lua)",
            "https://github.com/azerothcore/mod-eluna",
            "Permite escrever logica de jogo em Lua, com recarga a quente.",
            "Base de muita customizacao: eventos, comandos proprios, NPCs com "
            + "comportamento especial. Recarrega sem reiniciar o servidor "
            + "(reload eluna).",
            ModuleStatus.Oficial),

        new CatalogModule(
            "mod-playerbots", "Playerbots",
            "https://github.com/mod-playerbots/mod-playerbots",
            "Bots que jogam como personagens de verdade: quests, grupos e raides.",
            "O mais completo, e o mais invasivo. Nao e um modulo comum: exige "
            + "substituir o core por um fork. Seus dados extraidos do client e seus "
            + "personagens sao preservados; o que se refaz e o clone e a compilacao, "
            + "cerca de 20 minutos.",
            ModuleStatus.ExigeFork,
            PostInstallNote:
                "Este modulo so funciona sobre o fork correspondente. A GUI troca o "
                + "repositorio de origem e reclona antes de compilar.",
            ForkRepository: "https://github.com/mod-playerbots/azerothcore-wotlk.git",
            ForkBranch: "Playerbot")
        {
            // Endereco anterior a migracao para a organizacao. Ainda resolve, e
            // quem clonou por ele tem o mesmo core - nao ha o que corrigir.
            ForkAliases = new[] { "https://github.com/liyunfan1223/azerothcore-wotlk.git" },
        },
    };

    public static CatalogModule? Find(string name) =>
        All.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
}
