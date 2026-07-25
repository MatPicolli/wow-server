namespace WowServer.Core;

/// <summary>Um valor concreto que serve para o campo, e por que serviria.</summary>
public sealed record HelpExample(string Value, string Meaning);

/// <summary>
/// A ajuda de um campo: o que ele e, o que acontece se estiver errado, e
/// exemplos de valores reais.
/// </summary>
public sealed record FieldHelpEntry(
    string Key,
    string Title,
    string Description,
    IReadOnlyList<HelpExample> Examples)
{
    /// <summary>Consequencia de errar, quando vale a pena avisar.</summary>
    public string? Warning { get; init; }
}

/// <summary>
/// Textos de ajuda dos campos da interface, com exemplos.
///
/// Ficam aqui, e nao no XAML, por tres motivos: dao para testar (nenhuma chave
/// duplicada, nenhuma entrada sem exemplo), a interface referencia so a chave, e
/// um teste consegue conferir que toda chave usada no XAML existe de verdade -
/// que e o tipo de erro que so apareceria em tempo de execucao, ja na mao do
/// usuario.
/// </summary>
public static class FieldHelp
{
    public static FieldHelpEntry? Find(string key) =>
        All.FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.Ordinal));

    public static IReadOnlyList<FieldHelpEntry> All { get; } = new[]
    {
        // ------------------------------------------------------------ pastas
        new FieldHelpEntry("pasta.client", "Client do WoW",
            "A pasta onde está o Wow.exe da sua cópia do jogo. É de onde saem os "
            + "mapas, modelos e dados de navegação — o servidor não inventa nada "
            + "disso, ele lê do seu client. Precisa ser a versão 3.3.5a, build 12340.",
            new[]
            {
                new HelpExample(@"C:\Jogos\World of Warcraft 3.3.5a",
                    "o caso comum: uma cópia solta, fora de Arquivos de Programas"),
                new HelpExample(@"D:\WoW335",
                    "em outro disco — recomendado, a extração gera uns 20 GB temporários"),
            })
        {
            Warning = "Se apontar para um client de outra versão, a extração até roda, "
                    + "mas o servidor não abre ou o mundo fica quebrado.",
        },

        new FieldHelpEntry("pasta.root", "Raiz da instalação",
            "Onde tudo o que este programa cria vai morar: código-fonte, compilação, "
            + "servidor pronto e backups. As outras pastas abaixo normalmente ficam "
            + "dentro desta.",
            new[]
            {
                new HelpExample(@"C:\AzerothCore", "o padrão"),
                new HelpExample(@"D:\AzerothCore",
                    "num disco com mais espaço; precisa de uns 60 GB no total"),
            }),

        new FieldHelpEntry("pasta.source", "Código-fonte",
            "Onde o AzerothCore é clonado do GitHub. Os módulos ficam dentro desta "
            + "pasta, em modules\\.",
            new[]
            {
                new HelpExample(@"C:\AzerothCore\source", "o padrão"),
            })
        {
            Warning = "Trocar o core apaga esta pasta inteira e clona de novo — "
                    + "e os módulos vão junto. Não guarde nada seu aqui dentro.",
        },

        new FieldHelpEntry("pasta.build", "Compilação",
            "Onde o CMake e o compilador deixam os arquivos intermediários. É a "
            + "pasta mais descartável de todas: apagar só custa uma recompilação.",
            new[]
            {
                new HelpExample(@"C:\AzerothCore\build", "o padrão"),
            }),

        new FieldHelpEntry("pasta.server", "Servidor pronto",
            "Onde ficam o worldserver.exe, o authserver.exe, os arquivos .conf — e, "
            + "dentro dela, a pasta Data com os dados extraídos do seu client.",
            new[]
            {
                new HelpExample(@"C:\AzerothCore\server", "o padrão"),
            })
        {
            Warning = "A pasta Data\\ aqui dentro guarda horas de extração. Nunca "
                    + "apague esta pasta inteira para recomeçar — use "
                    + "reset-server.ps1, que preserva a Data.",
        },

        // ------------------------------------------------------------- banco
        new FieldHelpEntry("db.host", "Servidor MySQL",
            "O endereço onde o MySQL está escutando. Rodando na mesma máquina, é "
            + "sempre 127.0.0.1.",
            new[]
            {
                new HelpExample("127.0.0.1", "o MySQL está nesta mesma máquina"),
                new HelpExample("192.168.0.50", "o MySQL está em outro PC da sua rede"),
            })
        {
            Warning = "Prefira 127.0.0.1 a 'localhost': o MySQL trata os dois como "
                    + "usuários diferentes ao conferir permissão.",
        },

        new FieldHelpEntry("db.port", "Porta do MySQL",
            "A porta em que o MySQL escuta. Só mude se você configurou outra na "
            + "instalação, o que é incomum.",
            new[]
            {
                new HelpExample("3306", "o padrão do MySQL"),
                new HelpExample("3307",
                    "usado quando há duas versões do MySQL instaladas ao mesmo tempo"),
            }),

        new FieldHelpEntry("db.user", "Usuário do banco",
            "A conta que o servidor usa no dia a dia. Não é o root: é um usuário "
            + "criado só para o AzerothCore, com acesso apenas aos três bancos dele.",
            new[]
            {
                new HelpExample("acore", "o padrão, criado pela etapa 5 da instalação"),
            })
        {
            Warning = "A senha do root do MySQL nunca fica salva aqui — ela é pedida "
                    + "na hora, em cada operação que precise dela.",
        },

        new FieldHelpEntry("db.pass", "Senha do usuário do banco",
            "A senha do usuário acima. Fica gravada em texto no settings.psd1, que é "
            + "um arquivo local e não vai para o git.",
            new[]
            {
                new HelpExample("acore", "o padrão"),
            })
        {
            Warning = "Vale trocar se o servidor for ficar acessível fora da sua rede. "
                    + "Trocar aqui exige trocar também no MySQL.",
        },

        new FieldHelpEntry("db.auth", "Banco de contas",
            "Guarda as contas de login e a lista de realms. É pequeno e insubstituível "
            + "— entra no backup.",
            new[] { new HelpExample("acore_auth", "o padrão") }),

        new FieldHelpEntry("db.world", "Banco de mundo",
            "Guarda o conteúdo do jogo: NPCs, itens, quests, loot. É recriado "
            + "sozinho pelo servidor, então não precisa de backup — a menos que você "
            + "tenha editado coisas nele, como os multiplicadores da aba Ajustes.",
            new[] { new HelpExample("acore_world", "o padrão") }),

        new FieldHelpEntry("db.char", "Banco de personagens",
            "Seus personagens, inventário, bancos, correio e progresso. É o dado mais "
            + "insubstituível de todos.",
            new[] { new HelpExample("acore_characters", "o padrão") }),

        // ------------------------------------------------------------- realm
        new FieldHelpEntry("realm.nome", "Nome do servidor",
            "O que aparece na lista de realms quando você entra no jogo. Puramente "
            + "cosmético, pode ser qualquer coisa.",
            new[]
            {
                new HelpExample("Meu Servidor", "o padrão"),
                new HelpExample("Azeroth do Mateus", "aceita espaços e acentos"),
            }),

        new FieldHelpEntry("realm.endereco", "Endereço que o client usa",
            "Depois de escolher o realm na lista, o client se conecta neste endereço "
            + "para entrar no mundo. É gravado no banco, não num arquivo.",
            new[]
            {
                new HelpExample("127.0.0.1", "só você joga, nesta mesma máquina"),
                new HelpExample("192.168.0.10",
                    "o IP local desta máquina, para jogar de outro PC da sua casa"),
            })
        {
            Warning = "Com 127.0.0.1, outro computador da rede chega a ver o realm na "
                    + "lista mas trava ao entrar no mundo — ele tenta conectar em si mesmo.",
        },

        // ------------------------------------------------------- codigo-fonte
        new FieldHelpEntry("core.repo", "Repositório do código-fonte",
            "De onde o servidor é clonado. O oficial serve para tudo, menos para "
            + "Playerbots e NPCBots, que exigem versões modificadas do servidor inteiro.",
            new[]
            {
                new HelpExample("https://github.com/azerothcore/azerothcore-wotlk.git",
                    "o oficial — a escolha certa se você não usa bots"),
                new HelpExample("https://github.com/mod-playerbots/azerothcore-wotlk.git",
                    "necessário para o módulo Playerbots"),
            })
        {
            Warning = "Mudar aqui na mão não reclona nada: só grava a intenção. Use a "
                    + "aba Módulos, ou switch-core.ps1, que fazem a troca completa.",
        },

        new FieldHelpEntry("core.branch", "Branch",
            "Qual linha de desenvolvimento do repositório usar. Cada fork tem a sua.",
            new[]
            {
                new HelpExample("master", "a branch do AzerothCore oficial"),
                new HelpExample("Playerbot", "a branch do fork de Playerbots — com P maiúsculo"),
            }),

        // ------------------------------------------------- compilacao/extracao
        new FieldHelpEntry("build.threads", "Threads de compilação",
            "Quantos arquivos compilar ao mesmo tempo. Cada thread vira um processo "
            + "do compilador consumindo de 500 a 800 MB de memória.",
            new[]
            {
                new HelpExample("0", "usa todos os núcleos — o mais rápido"),
                new HelpExample("6",
                    "metade de uma máquina de 12 núcleos: demora mais, mas dá para "
                    + "usar o computador enquanto compila"),
                new HelpExample("2", "para máquinas com pouca memória RAM"),
            })
        {
            Warning = "Com 12 núcleos a compilação chega a usar uns 7 GB de RAM. Se a "
                    + "máquina travar ou o build morrer sem explicação, reduza este número.",
        },

        new FieldHelpEntry("build.vmaps", "Extrair vmaps",
            "Vmaps são a geometria do mundo: paredes, chão e obstáculos. É o que "
            + "permite ao servidor saber que existe uma parede entre você e o inimigo.",
            new[]
            {
                new HelpExample("marcado", "o normal — leva de 20 a 40 minutos"),
                new HelpExample("desmarcado",
                    "só se você quiser um servidor de testes rápido; sem vmaps os "
                    + "inimigos enxergam e atiram através das paredes"),
            }),

        new FieldHelpEntry("build.mmaps", "Extrair mmaps",
            "Mmaps são a malha de navegação: como as criaturas descobrem um caminho "
            + "até você contornando obstáculos. É a etapa mais demorada de todas.",
            new[]
            {
                new HelpExample("marcado", "o normal — de 1 a 6 horas, uma vez só"),
                new HelpExample("desmarcado",
                    "os inimigos passam a andar em linha reta e atravessam o cenário; "
                    + "os Playerbots ficam presos"),
            })
        {
            Warning = "Cada versão do servidor gera mmaps num formato próprio. Trocar o "
                    + "core exige gerar de novo — e o servidor não avisa: ele só deixa "
                    + "de carregar a navegação.",
        },

        // ----------------------------------------------------------- ajustes
        new FieldHelpEntry("tune.multiplicador", "Multiplicador de coleta",
            "Multiplica quanto sai de cada nó de coleta. 1 é o valor original do jogo. "
            + "O cálculo parte sempre dos valores originais guardados na primeira vez, "
            + "então aplicar 3 duas vezes continua sendo 3, e não 9.",
            new[]
            {
                new HelpExample("1", "o valor original do jogo"),
                new HelpExample("3", "confortável para jogar sozinho, sem trivializar"),
                new HelpExample("10",
                    "farm muito rápido; o limite por item é 255, e valores acima disso "
                    + "são cortados com aviso"),
            }),

        new FieldHelpEntry("tune.quest", "Chance de drop de item de quest",
            "Multiplica a probabilidade de o item que a quest pede cair do inimigo. "
            + "É o ajuste que mais economiza tempo jogando sozinho.",
            new[]
            {
                new HelpExample("1", "o valor original"),
                new HelpExample("3", "acaba com a espera pelo último item da coleta"),
                new HelpExample("5", "praticamente garante o drop; a chance é limitada a 100%"),
            }),

        new FieldHelpEntry("tune.creature", "Chance de drop geral",
            "Multiplica a chance de qualquer item cair dos inimigos, não só os de quest.",
            new[]
            {
                new HelpExample("1", "o valor original"),
                new HelpExample("2", "mais loot sem descaracterizar o jogo"),
            })
        {
            Warning = "Valores altos aqui enchem sua bolsa de lixo e desvalorizam o "
                    + "leilão. O de itens de quest costuma ser o que você realmente quer.",
        },

        // ----------------------------------------------------------- modulos
        new FieldHelpEntry("mod.filtro", "Filtrar módulos",
            "Procura por nome, categoria ou descrição. Acentos não importam: digitar "
            + "'leilao' encontra 'Casa de Leilões'.",
            new[]
            {
                new HelpExample("bot", "acha Playerbots e a Casa de Leilões"),
                new HelpExample("heranca", "acha o NPC Assistente, que dá itens de herança"),
                new HelpExample("dungeon", "acha o escalonamento e o buscador solo"),
            }),

        new FieldHelpEntry("mod.url", "URL de um módulo",
            "Instala qualquer módulo que não esteja na lista. O nome da pasta sai do "
            + "final do endereço.",
            new[]
            {
                new HelpExample("https://github.com/azerothcore/mod-duel-reset",
                    "recupera vida e mana ao fim de um duelo"),
                new HelpExample("https://github.com/azerothcore/mod-npc-services",
                    "um NPC que muda nome, aparência e facção"),
            })
        {
            Warning = "Módulos fora da lista não foram testados aqui. Se um não "
                    + "compilar, apague a pasta dele e recompile — o servidor atual "
                    + "continua funcionando enquanto isso.",
        },

        // ---------------------------------------------------------- comandos
        new FieldHelpEntry("cmd.filtro", "Filtrar comandos",
            "Procura por título, categoria ou pelo próprio comando. Acentos não importam.",
            new[]
            {
                new HelpExample("conta", "criar conta, virar GM, ver quem está online"),
                new HelpExample("item", "dar item, mandar pelo correio, dar ouro"),
                new HelpExample("tele", "teleportes e deslocamento"),
            }),

        new FieldHelpEntry("cmd.personagem", "Nome do personagem",
            "Quem vai receber as cartas com os itens de herança. É o nome do "
            + "personagem dentro do jogo, não o da conta.",
            new[]
            {
                new HelpExample("Mateus", "exatamente como aparece no jogo"),
                new HelpExample("Águia", "acentos são aceitos"),
            })
        {
            Warning = "De 2 a 12 letras, sem espaços nem números. Se o personagem "
                    + "estiver logado, pode ser preciso sair e entrar para o correio aparecer.",
        },

        // ----------------------------------------------------------- console
        new FieldHelpEntry("console.comando", "Comando para o servidor",
            "Vai direto para o console do worldserver. Aqui NÃO se usa o ponto na "
            + "frente — o ponto é só no chat do jogo. Comandos que dependem de um alvo "
            + "ou da sua posição não funcionam aqui: o console não tem personagem.",
            new[]
            {
                new HelpExample("account create mateus minhasenha",
                    "cria a conta com que você entra no jogo"),
                new HelpExample("account set gmlevel mateus 3 -1",
                    "dá poderes de GM à conta, em todos os realms"),
                new HelpExample("server info", "quantos jogadores, há quanto tempo no ar"),
                new HelpExample("saveall", "grava no banco o que está em memória"),
            }),
    };
}
