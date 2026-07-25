namespace WowServer.Core;

/// <summary>Onde o comando funciona.</summary>
public enum CommandTarget
{
    /// <summary>
    /// Roda no console do worldserver. A GUI consegue enviar sozinha.
    /// </summary>
    Console,

    /// <summary>
    /// Precisa do chat do jogo, com um personagem GM logado. Tipicamente porque
    /// depende de quem esta selecionado ou de onde voce esta - o console nao tem
    /// personagem nem posicao.
    /// </summary>
    Jogo,
}

/// <summary>
/// Um comando de GM pronto para usar. <see cref="Template"/> pode conter
/// marcadores &lt;assim&gt;, que o usuario troca antes de mandar.
/// </summary>
public sealed record GmCommand(
    string Category,
    string Title,
    string Description,
    string Template,
    CommandTarget Target)
{
    /// <summary>Marcadores que ainda precisam ser preenchidos, sem repetir.</summary>
    public IReadOnlyList<string> Placeholders => GmCommands.FindPlaceholders(Template);

    public bool NeedsFilling => Placeholders.Count > 0;
}

/// <summary>
/// Catalogo curado de comandos de GM do AzerothCore 3.3.5a.
///
/// Cada entrada diz onde funciona, porque essa e a parte que confunde: o
/// console do worldserver nao tem personagem nem posicao, entao comandos que
/// dependem de alvo ou de onde voce esta so funcionam no chat do jogo.
/// </summary>
public static class GmCommands
{
    public const string ContaCategoria      = "Conta e permissões";
    public const string PersonagemCategoria = "Personagem";
    public const string ItensCategoria      = "Itens e dinheiro";
    public const string MundoCategoria      = "Mundo e deslocamento";
    public const string ServidorCategoria   = "Servidor";

    public static IReadOnlyList<string> Categories { get; } = new[]
    {
        ContaCategoria, PersonagemCategoria, ItensCategoria,
        MundoCategoria, ServidorCategoria,
    };

    /// <summary>
    /// Acha os marcadores &lt;nome&gt; de um template, na ordem, sem repetir.
    /// </summary>
    public static IReadOnlyList<string> FindPlaceholders(string template)
    {
        var achados = new List<string>();
        var i = 0;

        while (i < template.Length)
        {
            var abre = template.IndexOf('<', i);
            if (abre < 0) break;

            var fecha = template.IndexOf('>', abre + 1);
            if (fecha < 0) break;

            var nome = template[(abre + 1)..fecha];

            // '<' e '>' aparecem em texto normal; so conta como marcador o que
            // tem conteudo e nenhum espaco dentro.
            if (nome.Length > 0 && !nome.Contains(' ') && !achados.Contains(nome))
                achados.Add(nome);

            i = fecha + 1;
        }

        return achados;
    }

    /// <summary>
    /// Troca os marcadores pelos valores dados. Marcador sem valor fica como
    /// esta - melhor mandar visivelmente incompleto do que mandar vazio.
    /// </summary>
    public static string Fill(string template, IReadOnlyDictionary<string, string> valores)
    {
        var texto = template;
        foreach (var (chave, valor) in valores)
        {
            if (string.IsNullOrWhiteSpace(valor)) continue;
            texto = texto.Replace($"<{chave}>", valor.Trim());
        }
        return texto;
    }

    public static IReadOnlyList<GmCommand> All { get; } = new[]
    {
        // ------------------------------------------------- conta e permissoes
        new GmCommand(ContaCategoria, "Criar uma conta",
            "Cria a conta com que você entra no jogo.",
            "account create <usuario> <senha>", CommandTarget.Console),

        new GmCommand(ContaCategoria, "Virar GM",
            "Nível 3 libera todos os comandos. O '-1' vale para todos os realms. "
            + "É preciso sair e entrar de novo para valer.",
            "account set gmlevel <usuario> 3 -1", CommandTarget.Console),

        new GmCommand(ContaCategoria, "Ligar o modo GM",
            "Com o modo ligado você fica invisível e invulnerável. Desligue com "
            + "'off' antes de jogar de verdade.",
            "gm on", CommandTarget.Jogo),

        new GmCommand(ContaCategoria, "Listar contas online",
            "Quem está conectado agora.",
            "account onlinelist", CommandTarget.Console),

        // ------------------------------------------------------- personagem
        new GmCommand(PersonagemCategoria, "Subir de nível",
            "Sobe o personagem selecionado (ou você) até o nível indicado.",
            "character level <nivel>", CommandTarget.Jogo),

        new GmCommand(PersonagemCategoria, "Aprender todas as magias da classe",
            "Ensina de uma vez o que o treinador ensinaria.",
            "learn all my class", CommandTarget.Jogo),

        new GmCommand(PersonagemCategoria, "Liberar todas as montarias e voo",
            "Inclui as habilidades de equitação.",
            "learn all my mounts", CommandTarget.Jogo),

        new GmCommand(PersonagemCategoria, "Recuperar vida e mana",
            "Cura por completo quem estiver selecionado.",
            "revive", CommandTarget.Jogo),

        new GmCommand(PersonagemCategoria, "Velocidade de corrida",
            "1 é o normal. Acima de uns 5 o servidor começa a reclamar de "
            + "movimento impossível.",
            "modify speed <multiplicador>", CommandTarget.Jogo),

        // -------------------------------------------------- itens e dinheiro
        new GmCommand(ItensCategoria, "Dar um item",
            "Vai direto para a bolsa de quem estiver selecionado.",
            "additem <id> <quantidade>", CommandTarget.Jogo),

        new GmCommand(ItensCategoria, "Dar um item pelo nome",
            "Quando você não sabe o ID. As aspas fazem parte do comando.",
            "additem \"<parte do nome>\"", CommandTarget.Jogo),

        new GmCommand(ItensCategoria, "Enviar itens pelo correio",
            "Funciona no console, sem precisar estar logado. No máximo 12 itens "
            + "por carta. Use id:quantidade para mandar mais de um.",
            "send items <personagem> \"<assunto>\" \"<mensagem>\" <ids separados por espaco>",
            CommandTarget.Console),

        new GmCommand(ItensCategoria, "Dar ouro",
            "O valor é em cobre: 10000 = 1 ouro.",
            "modify money <cobre>", CommandTarget.Jogo),

        // ------------------------------------------- mundo e deslocamento
        new GmCommand(MundoCategoria, "Ir para uma cidade",
            "Aceita nomes como stormwind, orgrimmar, dalaran, shattrath.",
            "tele <lugar>", CommandTarget.Jogo),

        new GmCommand(MundoCategoria, "Trazer alguém até você",
            "O personagem é teleportado para onde você está.",
            "appear <personagem>", CommandTarget.Jogo),

        new GmCommand(MundoCategoria, "Ir até alguém",
            "O contrário do anterior.",
            "summon <personagem>", CommandTarget.Jogo),

        new GmCommand(MundoCategoria, "Criar um NPC aqui",
            "O NPC fica gravado no banco e sobrevive ao restart.",
            "npc add <id>", CommandTarget.Jogo),

        new GmCommand(MundoCategoria, "Apagar o NPC selecionado",
            "Remove do mundo e do banco.",
            "npc delete", CommandTarget.Jogo),

        // --------------------------------------------------------- servidor
        new GmCommand(ServidorCategoria, "Ver o estado do servidor",
            "Quantos jogadores, há quanto tempo no ar, qual revisão.",
            "server info", CommandTarget.Console),

        new GmCommand(ServidorCategoria, "Salvar todo mundo agora",
            "Grava no banco o que está em memória. Vale antes de um backup.",
            "saveall", CommandTarget.Console),

        new GmCommand(ServidorCategoria, "Avisar todos os jogadores",
            "Aparece como mensagem do sistema.",
            "announce <mensagem>", CommandTarget.Console),

        new GmCommand(ServidorCategoria, "Desligar com aviso",
            "O tempo é em segundos, e o mínimo é 1 — o servidor recusa 0.",
            "server shutdown <segundos>", CommandTarget.Console),

        new GmCommand(ServidorCategoria, "Recarregar uma tabela",
            "Aplica no servidor ligado o que você mudou no banco. Exemplo de "
            + "tabela: creature_template, item_template, gameobject_template.",
            "reload <tabela>", CommandTarget.Console),
    };
}
