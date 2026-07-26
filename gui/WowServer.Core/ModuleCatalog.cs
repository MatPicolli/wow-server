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

    /// <summary>Grupo mostrado na lista. Ver <see cref="ModuleCatalog.Categories"/>.</summary>
    public string Category { get; init; } = ModuleCatalog.OutrosCategoria;

    /// <summary>
    /// Passo manual que a GUI nao tem como fazer sozinha - tipicamente copiar um
    /// arquivo para dentro da pasta do WoW. Aparece destacado no card, porque
    /// sem ele o modulo instala e compila mas nao funciona.
    /// </summary>
    public string? ManualStep { get; init; }
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

    public const string SozinhoCategoria     = "Jogar sozinho";
    public const string ConvenienciaCategoria = "Conveniência";
    public const string ConteudoCategoria    = "Conteúdo e progressão";
    public const string ExtrasCategoria      = "Cosmético e extras";
    public const string OutrosCategoria      = "Outros";

    /// <summary>Ordem em que as categorias aparecem na tela.</summary>
    public static IReadOnlyList<string> Categories { get; } = new[]
    {
        SozinhoCategoria, ConvenienciaCategoria, ConteudoCategoria,
        ExtrasCategoria, OutrosCategoria,
    };

    public static IReadOnlyList<CatalogModule> All { get; } = new[]
    {
        // ---------------------------------------------------- jogar sozinho
        new CatalogModule(
            "mod-ah-bot", "Casa de Leilões (AH Bot)",
            "https://github.com/azerothcore/mod-ah-bot",
            "Enche o leilão de itens e compra o que você põe à venda.",
            "Num servidor de uma pessoa só, a casa de leilões fica deserta. Este "
            + "módulo simula vendedores e compradores, então o leilão volta a ter uso.",
            ModuleStatus.Oficial,
            PostInstallNote:
                "Precisa de uma conta e um personagem dedicados, que aparecem como "
                + "vendedores. Crie no console do worldserver:\n\n"
                + "    account create ahbot suasenha\n\n"
                + "Entre com ela, crie um personagem, saia. Depois preencha o ID da "
                + "conta e o GUID do personagem em configs\\modules\\mod_ahbot.conf.")
        { Category = SozinhoCategoria },

        new CatalogModule(
            "mod-autobalance", "Escalonamento de Dungeon",
            "https://github.com/azerothcore/mod-autobalance",
            "Reduz vida e dano dos inimigos conforme o número real de jogadores.",
            "Deixa dungeons e raides possíveis sozinho ou em grupo pequeno, "
            + "enfraquecendo os inimigos em vez de fortalecer você. Preserva melhor "
            + "a sensação de jogo do que o Solocraft.",
            ModuleStatus.Oficial)
        { Category = SozinhoCategoria },

        new CatalogModule(
            "mod-solo-lfg", "Dungeon Finder Solo",
            "https://github.com/azerothcore/mod-solo-lfg",
            "Permite entrar na fila do buscador de masmorras sozinho.",
            "O Dungeon Finder já vem no 3.3.5a e está ligado por padrão, mas exige "
            + "5 jogadores para formar grupo. Este módulo remove essa exigência. "
            + "Combina com o Escalonamento de Dungeon.",
            ModuleStatus.Oficial)
        { Category = SozinhoCategoria },

        new CatalogModule(
            "mod-solocraft", "Solocraft",
            "https://github.com/azerothcore/mod-solocraft",
            "Fortalece você para compensar o grupo que falta.",
            "Caminho oposto ao Escalonamento: em vez de enfraquecer a dungeon, "
            + "aumenta seus atributos. Usar os dois juntos costuma passar do ponto "
            + "e tornar tudo trivial — escolha um.",
            ModuleStatus.Oficial)
        { Category = SozinhoCategoria },

        new CatalogModule(
            "mod-instanced-worldbosses", "Chefes de Mundo Instanciados",
            "https://github.com/azerothcore/mod-instanced-worldbosses",
            "Põe os chefes de mundo dentro de instâncias, com trava semanal.",
            "Chefes como Azuregos e o Doomwalker ficam num mundo compartilhado, onde "
            + "quem chega primeiro leva. Instanciados, eles viram conteúdo previsível "
            + "para você — e param de sumir.",
            ModuleStatus.Oficial)
        { Category = SozinhoCategoria },

        new CatalogModule(
            "mod-playerbots", "Playerbots",
            "https://github.com/mod-playerbots/mod-playerbots",
            "Bots que jogam como personagens de verdade: quests, grupos e raides.",
            "O mais completo, e o mais invasivo. Não é um módulo comum: exige "
            + "substituir o core por um fork. Seus dados extraídos do client e seus "
            + "personagens são preservados; o que se refaz é o clone e a compilação, "
            + "cerca de 20 minutos.",
            ModuleStatus.ExigeFork,
            PostInstallNote:
                "Este módulo só funciona sobre o fork correspondente. A GUI troca o "
                + "repositório de origem e reclona antes de compilar.",
            ForkRepository: "https://github.com/mod-playerbots/azerothcore-wotlk.git",
            ForkBranch: "Playerbot")
        {
            Category = SozinhoCategoria,
            // Endereco anterior a migracao para a organizacao. Ainda resolve, e
            // quem clonou por ele tem o mesmo core - nao ha o que corrigir.
            ForkAliases = new[] { "https://github.com/liyunfan1223/azerothcore-wotlk.git" },
        },

        // ------------------------------------------------------ conveniência
        new CatalogModule(
            "mod-assistant", "NPC Assistente",
            "https://github.com/noisiver/mod-assistant",
            "Um NPC que dá itens de herança, glifos, gemas, poções e comida.",
            "Resolve de uma vez o problema dos itens de herança: eles saem de graça "
            + "com este NPC, sem emblemas e sem precisar de um personagem de nível 80 "
            + "na conta. Também sobe profissões pagando em ouro, libera rotas de voo "
            + "e reseta heroicas. Cada função liga e desliga na configuração.",
            ModuleStatus.Oficial,
            PostInstallNote:
                "O NPC tem entry 9000000. Para invocá-lo onde você estiver, use no "
                + "chat do jogo com uma conta de GM:\n\n"
                + "    .npc add 9000000\n\n"
                + "Escolha o que fica ligado em configs\\modules\\mod_assistant.conf.")
        { Category = ConvenienciaCategoria },

        new CatalogModule(
            "mod-npc-buffer", "NPC de Buffs",
            "https://github.com/azerothcore/mod-npc-buffer",
            "Um NPC que aplica os buffs de grupo que você não tem sozinho.",
            "Jogando só, você nunca recebe Marca do Selvagem, Fortitude, Intelecto "
            + "Arcano e companhia. Este NPC aplica todos, o que muda bastante a "
            + "sobrevivência em dungeon.",
            ModuleStatus.Oficial)
        { Category = ConvenienciaCategoria },

        new CatalogModule(
            "mod-learn-spells", "Aprender Magias Sozinho",
            "https://github.com/azerothcore/mod-learn-spells",
            "Ensina automaticamente as magias do seu nível, sem ir ao treinador.",
            "Elimina a viagem à capital a cada dois níveis. Cada ida ao treinador "
            + "custa alguns minutos de voo; jogando pouco por sessão, isso vira a "
            + "maior parte do tempo.",
            ModuleStatus.Oficial)
        { Category = ConvenienciaCategoria },

        new CatalogModule(
            "mod-reagent-bank", "Banco de Reagentes",
            "https://github.com/ZhengPeiRu21/mod-reagent-bank",
            "Um NPC que guarda materiais de profissão sem ocupar bolsa.",
            "Depósito separado só para minérios, ervas, panos e afins. Sem ele, "
            + "profissão de coleta enche as bolsas e o banco em poucas horas.",
            ModuleStatus.Oficial)
        { Category = ConvenienciaCategoria },

        new CatalogModule(
            "mod-instance-reset", "Resetar Instâncias",
            "https://github.com/azerothcore/mod-instance-reset",
            "Permite resetar dungeons e raides sem esperar a trava semanal.",
            "Útil para testar, para refazer uma raide que deu errado, ou só para "
            + "repetir conteúdo. Num servidor pessoal a trava original não protege "
            + "nada — ela só existe para conter economia de servidor público.",
            ModuleStatus.Oficial)
        { Category = ConvenienciaCategoria },

        new CatalogModule(
            "mod-auto-revive", "Ressuscitar Automático",
            "https://github.com/azerothcore/mod-auto-revive",
            "Levanta o personagem sozinho depois de morrer.",
            "Corta a corrida de fantasma até o corpo. Configurável por nível, então "
            + "dá para deixar ligado só enquanto você está subindo de nível.",
            ModuleStatus.Oficial)
        { Category = ConvenienciaCategoria },

        new CatalogModule(
            "mod-skip-dk-starting-area", "Pular Início do Death Knight",
            "https://github.com/azerothcore/mod-skip-dk-starting-area",
            "Cria Death Knights já fora da área inicial, no nível 55.",
            "A introdução do Death Knight leva cerca de uma hora e é sempre a mesma. "
            + "Na segunda vez, dá para pular.",
            ModuleStatus.Oficial)
        { Category = ConvenienciaCategoria },

        new CatalogModule(
            "mod-npc-enchanter", "NPC Encantador",
            "https://github.com/azerothcore/mod-npc-enchanter",
            "Um NPC que aplica encantamentos sem precisar de outro jogador.",
            "Vários encantamentos bons exigem um encantador de nível alto. Sozinho, "
            + "você simplesmente não tem acesso a eles.",
            ModuleStatus.Oficial)
        { Category = ConvenienciaCategoria },

        // ---------------------------------------------- conteúdo e progressão
        new CatalogModule(
            "mod-individual-progression", "Progressão Individual",
            "https://github.com/ZhengPeiRu21/mod-individual-progression",
            "Faz cada personagem atravessar Vanilla, TBC e WotLK na ordem.",
            "O módulo mais transformador da lista. Remove os atalhos que a Blizzard "
            + "foi acrescentando, restaura o Naxxramas original, as attunements do "
            + "TBC e a dificuldade das versões antigas. O próprio autor diz que "
            + "combina bem com Playerbots. É uma campanha longa, não um ajuste.",
            ModuleStatus.Oficial,
            PostInstallNote:
                "Duas configurações do worldserver são obrigatórias, senão o progresso "
                + "não é salvo e os itens ficam com os valores errados:\n\n"
                + "    EnablePlayerSettings = 1\n"
                + "    DBC.EnforceItemAttributes = 0\n\n"
                + "Ambas ficam em configs\\worldserver.conf.")
        { Category = ConteudoCategoria },

        new CatalogModule(
            "mod-dynamic-xp", "XP Dinâmico",
            "https://github.com/azerothcore/mod-dynamic-xp",
            "Muda o ganho de experiência conforme o nível, em vez de um valor fixo.",
            "Alternativa ao multiplicador único do servidor: dá para acelerar as "
            + "faixas que você já conhece de cor e deixar o ritmo normal onde o "
            + "conteúdo interessa.",
            ModuleStatus.Oficial)
        { Category = ConteudoCategoria },

        new CatalogModule(
            "mod-account-achievements", "Conquistas por Conta",
            "https://github.com/azerothcore/mod-account-achievements",
            "Compartilha conquistas entre todos os personagens da conta.",
            "As conquistas do WotLK são por personagem. Com vários alts num servidor "
            + "pessoal, juntá-las na conta faz mais sentido.",
            ModuleStatus.Oficial)
        { Category = ConteudoCategoria },

        new CatalogModule(
            "mod-arac", "Todas as Raças, Todas as Classes",
            "https://github.com/azerothcore/mod-arac",
            "Libera qualquer combinação de raça e classe na criação.",
            "Tauren mago, gnomo paladino, o que você quiser. É o único da lista que "
            + "não funciona só com a instalação: o client precisa de um arquivo extra, "
            + "senão a tela de criação de personagem trava.",
            ModuleStatus.Oficial)
        {
            Category = ConteudoCategoria,
            ManualStep =
                "Este módulo exige dois passos manuais que a GUI não faz:\n\n"
                + "  1. copiar o Patch-A.MPQ do repositório para a pasta Data do seu WoW\n"
                + "  2. substituir os .dbc do servidor pelos do repositório\n\n"
                + "Sem isso o client fecha ao abrir a criação de personagem. Faça "
                + "backup do banco antes.",
        },

        // ------------------------------------------------- cosmético e extras
        new CatalogModule(
            "mod-transmog", "Transmogrificação",
            "https://github.com/azerothcore/mod-transmog",
            "Muda a aparência do equipamento sem perder os atributos.",
            "Recurso que só chegou no Cataclysm, trazido para o WotLK por um NPC. "
            + "É o módulo cosmético mais usado do AzerothCore.",
            ModuleStatus.Oficial)
        { Category = ExtrasCategoria },

        new CatalogModule(
            "mod-npc-beastmaster", "Mestre das Feras",
            "https://github.com/azerothcore/mod-npc-beastmaster",
            "Deixa qualquer classe adotar e usar mascotes de caçador.",
            "Um mago com um urso de estimação não é lore, mas resolve um problema "
            + "real de quem joga sozinho: alguém para segurar o dano enquanto você "
            + "ataca.",
            ModuleStatus.Oficial)
        { Category = ExtrasCategoria },

        new CatalogModule(
            "mod-random-enchants", "Encantamentos Aleatórios",
            "https://github.com/azerothcore/mod-random-enchants",
            "Dá encantamentos aleatórios aos itens que caem.",
            "Aproxima o loot do estilo Diablo: o mesmo item pode vir melhor ou pior. "
            + "Dá motivo para continuar farmando conteúdo que você já limpou.",
            ModuleStatus.Oficial)
        { Category = ExtrasCategoria },

        new CatalogModule(
            "mod-guildhouse", "Casa de Guilda",
            "https://github.com/azerothcore/mod-guildhouse",
            "Permite comprar uma área privada para a guilda, com NPCs próprios.",
            "Vira uma base pessoal com banco, leiloeiro, treinador e portais — "
            + "conveniente mesmo numa guilda de um membro só.",
            ModuleStatus.Oficial)
        { Category = ExtrasCategoria },

        new CatalogModule(
            "mod-ale", "ALE / Eluna (scripts Lua)",
            "https://github.com/azerothcore/mod-ale",
            "Permite escrever lógica de jogo em Lua, com recarga a quente.",
            "Base de muita customização: eventos, comandos próprios, NPCs com "
            + "comportamento especial. Recarrega sem reiniciar o servidor "
            + "(reload eluna). É o antigo Eluna, renomeado para ALE "
            + "(Azeroth Lua Engine).",
            ModuleStatus.Oficial,
            PostInstallNote:
                "A pasta em modules/ precisa se chamar exatamente 'mod-ale'. O "
                + "CMake do core só liga a biblioteca Lua ao alvo dos módulos "
                + "quando encontra esse nome; clonado como 'mod-eluna' (o endereço "
                + "antigo, que o GitHub ainda redireciona) a lib Lua compila mas "
                + "'lua.h' nunca entra no include path, e a compilação para com "
                + "25 erros C1083 iguais. Vale para o core oficial e para o fork "
                + "do Playerbots — a checagem é a mesma nos dois.")
        { Category = ExtrasCategoria },
    };

    public static CatalogModule? Find(string name) =>
        All.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
}
