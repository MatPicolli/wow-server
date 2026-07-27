namespace WowServer.Core;

/// <summary>Como o valor de um ajuste e interpretado, para validar e explicar.</summary>
public enum ConfigKind
{
    /// <summary>Multiplicador; 1 = original do jogo.</summary>
    Multiplicador,
    /// <summary>Porcentagem de 0 a 100.</summary>
    Porcentagem,
    /// <summary>Numero inteiro com faixa propria.</summary>
    Inteiro,
}

/// <summary>
/// Um ajuste do worldserver.conf que a interface expoe.
/// </summary>
public sealed record ConfigSetting(
    string Key,
    string Label,
    string Category,
    string Default,
    ConfigKind Kind,
    string Description,
    IReadOnlyList<HelpExample> Examples)
{
    public double? Min { get; init; }
    public double? Max { get; init; }
    public string? Warning { get; init; }

    /// <summary>A mesma ajuda dos outros campos, montada a partir daqui.</summary>
    public FieldHelpEntry Help => new(
        Key,
        $"{Label}  ({Key})",
        Description,
        Examples)
    { Warning = Warning };
}

/// <summary>
/// Catalogo de ajustes do worldserver.conf.
///
/// Toda chave e todo valor padrao daqui foi lido do worldserver.conf.dist do
/// proprio AzerothCore, nao de memoria. Chave inventada nao daria erro: o
/// servidor ignora o que nao conhece, e o ajuste simplesmente nao teria efeito.
/// </summary>
public static class ConfigTuning
{
    public const string ProgressaoCategoria = "Progressão";
    public const string ProfissaoCategoria  = "Profissões";
    public const string CombateCategoria    = "Combate e dificuldade";
    public const string EconomiaCategoria   = "Economia";
    public const string ConvenienciaCategoria = "Conveniência";

    public static IReadOnlyList<string> Categories { get; } = new[]
    {
        ProgressaoCategoria, ProfissaoCategoria, CombateCategoria,
        EconomiaCategoria, ConvenienciaCategoria,
    };

    public static ConfigSetting? Find(string key) =>
        All.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Valida o texto digitado para um ajuste.
    /// </summary>
    public static bool TryParse(ConfigSetting setting, string? texto, out string normalizado, out string? erro)
    {
        normalizado = string.Empty;
        erro = null;

        var t = (texto ?? string.Empty).Trim();
        if (t.Length == 0) { erro = "Escreva um valor."; return false; }

        // Virgula decimal e o que sai do teclado brasileiro, mas o arquivo de
        // configuracao e lido com ponto. Aceitar as duas e converter.
        t = t.Replace(',', '.');

        if (!double.TryParse(t, System.Globalization.NumberStyles.Float,
                             System.Globalization.CultureInfo.InvariantCulture, out var valor))
        {
            erro = $"'{texto}' não é um número.";
            return false;
        }

        if (setting.Kind == ConfigKind.Inteiro && valor != Math.Floor(valor))
        {
            erro = "Este ajuste só aceita número inteiro.";
            return false;
        }

        var min = setting.Min ?? (setting.Kind == ConfigKind.Multiplicador ? 0 : 0);
        var max = setting.Max ?? (setting.Kind == ConfigKind.Porcentagem ? 100 : double.MaxValue);

        if (valor < min) { erro = $"O mínimo é {Formatar(min)}."; return false; }
        if (valor > max) { erro = $"O máximo é {Formatar(max)}."; return false; }

        normalizado = Formatar(valor);
        return true;
    }

    private static string Formatar(double v) =>
        v == Math.Floor(v) && Math.Abs(v) < 1e15
            ? ((long)v).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : v.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

    public static IReadOnlyList<ConfigSetting> All { get; } = new[]
    {
        // -------------------------------------------------------- progressão
        new ConfigSetting("Rate.XP.Kill", "XP por matar", ProgressaoCategoria, "1",
            ConfigKind.Multiplicador,
            "Multiplica a experiência ganha ao matar inimigos.",
            new[]
            {
                new HelpExample("1", "o ritmo original — 1 a 80 leva semanas"),
                new HelpExample("3", "confortável para jogar sozinho sem pular conteúdo"),
                new HelpExample("10", "chega ao nível 80 em poucos dias"),
                new HelpExample("0", "trava o nível; útil para ficar num patamar de conteúdo"),
            }),

        new ConfigSetting("Rate.XP.Quest", "XP por quest", ProgressaoCategoria, "1",
            ConfigKind.Multiplicador,
            "Multiplica a experiência das missões concluídas.",
            new[]
            {
                new HelpExample("1", "o original"),
                new HelpExample("3", "acompanha bem um XP.Kill também em 3"),
            }),

        new ConfigSetting("Rate.XP.Explore", "XP por explorar", ProgressaoCategoria, "1",
            ConfigKind.Multiplicador,
            "Multiplica a experiência de descobrir áreas novas do mapa.",
            new[]
            {
                new HelpExample("1", "o original"),
                new HelpExample("5", "recompensa quem gosta de sair explorando"),
            }),

        new ConfigSetting("Rate.Talent", "Pontos de talento", ProgressaoCategoria, "1",
            ConfigKind.Multiplicador,
            "Multiplica quantos pontos de talento você recebe por nível.",
            new[]
            {
                new HelpExample("1", "o original"),
                new HelpExample("2", "duas árvores completas até o 80"),
            })
        {
            Warning = "Valores altos deixam o personagem forte demais cedo demais e "
                    + "tiram a escolha de qual talento pegar.",
        },

        new ConfigSetting("Rate.Reputation.Gain", "Reputação", ProgressaoCategoria, "1",
            ConfigKind.Multiplicador,
            "Multiplica o ganho de reputação com as facções.",
            new[]
            {
                new HelpExample("1", "o original"),
                new HelpExample("5", "as recompensas de Exaltado ficam alcançáveis sozinho"),
            }),

        new ConfigSetting("StartPlayerLevel", "Nível inicial", ProgressaoCategoria, "1",
            ConfigKind.Inteiro,
            "Em que nível os personagens novos começam.",
            new[]
            {
                new HelpExample("1", "o começo normal"),
                new HelpExample("55", "pula direto para o conteúdo de Outland"),
                new HelpExample("80", "nível máximo, para testar equipamento e raides"),
            })
        {
            Min = 1, Max = 80,
        },

        // ------------------------------------------------------- profissões
        new ConfigSetting("SkillGain.Gathering", "Perícia de coleta", ProfissaoCategoria, "1",
            ConfigKind.Multiplicador,
            "Multiplica os pontos de perícia ganhos ao coletar — mineração, herbalismo, "
            + "esfolamento. É o 'XP de profissão' de coleta.",
            new[]
            {
                new HelpExample("1", "o original"),
                new HelpExample("5", "sobe a profissão junto com o personagem, sem farm dedicado"),
            }),

        new ConfigSetting("SkillGain.Crafting", "Perícia de criação", ProfissaoCategoria, "1",
            ConfigKind.Multiplicador,
            "Multiplica os pontos ganhos ao fabricar itens — alfaiataria, ferraria, alquimia.",
            new[]
            {
                new HelpExample("1", "o original"),
                new HelpExample("5", "gasta bem menos material para subir a profissão"),
            }),

        new ConfigSetting("SkillGain.Weapon", "Perícia de arma", ProfissaoCategoria, "1",
            ConfigKind.Multiplicador,
            "Multiplica os pontos de perícia com armas ao acertar golpes.",
            new[] { new HelpExample("1", "o original"), new HelpExample("10", "sobe quase de imediato ao trocar de arma") }),

        new ConfigSetting("SkillGain.Defense", "Perícia de defesa", ProfissaoCategoria, "1",
            ConfigKind.Multiplicador,
            "Multiplica os pontos de defesa ganhos ao levar dano.",
            new[] { new HelpExample("1", "o original"), new HelpExample("5", "útil para quem tanka sozinho") }),

        new ConfigSetting("SkillChance.Orange", "Chance de subir em receita laranja", ProfissaoCategoria, "100",
            ConfigKind.Porcentagem,
            "Chance de ganhar ponto ao usar uma receita laranja (a mais difícil). "
            + "As outras cores têm ajustes próprios: amarela 75, verde 25, cinza 0.",
            new[]
            {
                new HelpExample("100", "o original — laranja sempre sobe"),
                new HelpExample("100", "não vale a pena reduzir; é o que já garante progresso"),
            }),

        new ConfigSetting("SkillChance.Green", "Chance de subir em receita verde", ProfissaoCategoria, "25",
            ConfigKind.Porcentagem,
            "Chance de ganhar ponto numa receita verde. Aumentar aqui é o que permite "
            + "subir a profissão sem correr atrás de receitas novas o tempo todo.",
            new[]
            {
                new HelpExample("25", "o original"),
                new HelpExample("75", "receitas verdes continuam valendo bem mais tempo"),
            }),

        new ConfigSetting("SkillChance.Prospecting", "Prospectar dá perícia", ProfissaoCategoria, "0",
            ConfigKind.Porcentagem,
            "Chance de prospectar minério dar ponto de joalheria. Vem desligado no "
            + "original, o que torna a joalheria bem cara de subir.",
            new[]
            {
                new HelpExample("0", "o original — prospectar não dá perícia"),
                new HelpExample("100", "cada prospecção conta, sozinho isso muda muito"),
            }),

        new ConfigSetting("SkillChance.Milling", "Moer ervas dá perícia", ProfissaoCategoria, "0",
            ConfigKind.Porcentagem,
            "O mesmo para a inscrição: chance de moer ervas dar ponto de perícia.",
            new[]
            {
                new HelpExample("0", "o original"),
                new HelpExample("100", "torna a inscrição viável sem comprar material"),
            }),

        new ConfigSetting("MaxPrimaryTradeSkill", "Profissões principais", ProfissaoCategoria, "2",
            ConfigKind.Inteiro,
            "Quantas profissões principais um personagem pode aprender.",
            new[]
            {
                new HelpExample("2", "o original"),
                new HelpExample("4", "dá para ser autossuficiente sem alts"),
                new HelpExample("11", "todas de uma vez"),
            })
        {
            Min = 0, Max = 11,
        },

        // --------------------------------------------- combate e dificuldade
        new ConfigSetting("Rate.MoveSpeed.Player", "Velocidade do personagem", CombateCategoria, "1",
            ConfigKind.Multiplicador,
            "Multiplica a velocidade de movimento do seu personagem, a pé e montado.",
            new[]
            {
                new HelpExample("1", "o original"),
                new HelpExample("1.5", "corta bem o tempo de viagem sem estranhar"),
                new HelpExample("2", "rápido; acima disso a movimentação fica difícil de controlar"),
            })
        {
            Min = 0.1, Max = 10,
            Warning = "Valores altos podem disparar a proteção contra velocidade impossível "
                    + "e desconectar você. Suba aos poucos e teste.",
        },

        new ConfigSetting("Rate.MoveSpeed.NPC", "Velocidade dos NPCs", CombateCategoria, "1",
            ConfigKind.Multiplicador,
            "O mesmo para as criaturas. Reduzir facilita fugir de uma luta perdida.",
            new[]
            {
                new HelpExample("1", "o original"),
                new HelpExample("0.8", "dá para escapar correndo"),
            })
        { Min = 0.1, Max = 10 },

        new ConfigSetting("Rate.Creature.Normal.HP", "Vida das criaturas comuns", CombateCategoria, "1",
            ConfigKind.Multiplicador,
            "Multiplica a vida dos inimigos comuns. Reduzir acelera muito o jogo solo.",
            new[]
            {
                new HelpExample("1", "o original"),
                new HelpExample("0.5", "lutas duram metade do tempo"),
                new HelpExample("2", "mais desafio, se você usa Solocraft ou heranças"),
            })
        { Min = 0.1, Max = 100 },

        new ConfigSetting("Rate.Creature.Normal.Damage", "Dano das criaturas comuns", CombateCategoria, "1",
            ConfigKind.Multiplicador,
            "Multiplica o dano que os inimigos comuns causam.",
            new[]
            {
                new HelpExample("1", "o original"),
                new HelpExample("0.7", "erra menos e morre menos jogando sozinho"),
            })
        { Min = 0.1, Max = 100 },

        new ConfigSetting("Rate.Health", "Regeneração de vida", CombateCategoria, "1",
            ConfigKind.Multiplicador,
            "Multiplica a velocidade com que a vida volta fora de combate.",
            new[]
            {
                new HelpExample("1", "o original"),
                new HelpExample("5", "quase elimina a pausa entre uma luta e outra"),
            }),

        new ConfigSetting("Rate.Mana", "Regeneração de mana", CombateCategoria, "1",
            ConfigKind.Multiplicador,
            "O mesmo para a mana. Faz diferença enorme para classes conjuradoras solo.",
            new[]
            {
                new HelpExample("1", "o original"),
                new HelpExample("5", "dá para continuar lutando sem sentar a cada inimigo"),
            }),

        new ConfigSetting("Rate.Damage.Fall", "Dano de queda", CombateCategoria, "1",
            ConfigKind.Multiplicador,
            "Multiplica o dano ao cair de uma altura.",
            new[]
            {
                new HelpExample("1", "o original"),
                new HelpExample("0", "desliga o dano de queda"),
            }),

        new ConfigSetting("Death.SicknessLevel", "Nível da doença de ressurreição", CombateCategoria, "11",
            ConfigKind.Inteiro,
            "A partir de que nível morrer aplica a penalidade de atributos. Abaixo "
            + "desse nível não há penalidade.",
            new[]
            {
                new HelpExample("11", "o original"),
                new HelpExample("81", "desliga a penalidade por completo"),
            })
        { Min = 0, Max = 81 },

        // -------------------------------------------------------- economia
        new ConfigSetting("Rate.Drop.Money", "Ouro que cai", EconomiaCategoria, "1",
            ConfigKind.Multiplicador,
            "Multiplica o dinheiro que os inimigos deixam cair.",
            new[]
            {
                new HelpExample("1", "o original"),
                new HelpExample("3", "paga montaria e reparos sem farm dedicado"),
            }),

        new ConfigSetting("Rate.RepairCost", "Custo de reparo", EconomiaCategoria, "1",
            ConfigKind.Multiplicador,
            "Multiplica o preço de consertar o equipamento.",
            new[]
            {
                new HelpExample("1", "o original"),
                new HelpExample("0", "reparo de graça"),
            }),

        new ConfigSetting("StartPlayerMoney", "Dinheiro inicial", EconomiaCategoria, "0",
            ConfigKind.Inteiro,
            "Quanto dinheiro um personagem novo recebe, em cobre. 10000 = 1 ouro.",
            new[]
            {
                new HelpExample("0", "o original — começa sem nada"),
                new HelpExample("100000", "10 ouro, o bastante para as primeiras bolsas"),
                new HelpExample("10000000", "1000 ouro, montaria epica de cara"),
            })
        { Min = 0 },

        new ConfigSetting("Rate.Auction.Time", "Duração dos leilões", EconomiaCategoria, "1",
            ConfigKind.Multiplicador,
            "Multiplica quanto tempo um item fica no leilão antes de voltar.",
            new[]
            {
                new HelpExample("1", "o original"),
                new HelpExample("3", "menos idas ao correio para relistar"),
            }),

        new ConfigSetting("DurabilityLoss.OnDeath", "Perda de durabilidade ao morrer", EconomiaCategoria, "10",
            ConfigKind.Porcentagem,
            "Quanto por cento de durabilidade o equipamento perde a cada morte.",
            new[]
            {
                new HelpExample("10", "o original"),
                new HelpExample("0", "morrer não estraga mais o equipamento"),
            }),

        // ----------------------------------------------------- conveniência
        new ConfigSetting("Rate.InstanceResetTime", "Tempo de trava das instâncias", ConvenienciaCategoria, "1",
            ConfigKind.Multiplicador,
            "Multiplica o intervalo até uma raide poder ser refeita.",
            new[]
            {
                new HelpExample("1", "o original — trava semanal"),
                new HelpExample("0.14", "aproximadamente diária, em vez de semanal"),
            }),

        new ConfigSetting("Rate.Rest.InGame", "Descanso acumulado", ConvenienciaCategoria, "1",
            ConfigKind.Multiplicador,
            "Multiplica a velocidade com que a barra de descanso (XP em dobro) enche "
            + "enquanto você está logado numa estalagem.",
            new[]
            {
                new HelpExample("1", "o original"),
                new HelpExample("5", "quase sempre com bônus de descanso disponível"),
            }),

        new ConfigSetting("NoResetTalentsCost", "Reset de talentos de graça", ConvenienciaCategoria, "0",
            ConfigKind.Inteiro,
            "Com 1, redistribuir talentos não custa nada e não encarece a cada vez "
            + "(o original vai de 1 ouro para 5, 10, e assim por diante). Continua "
            + "sendo preciso falar com um treinador de classe — isto zera só o preço.",
            new[]
            {
                new HelpExample("0", "o original — a taxa sobe a cada reset"),
                new HelpExample("1", "trocar de build à vontade, sem pensar no custo"),
            })
        { Min = 0, Max = 1 },

        new ConfigSetting("Rate.Corpse.Decay.Looted", "Sumiço do corpo saqueado", ConvenienciaCategoria, "0.5",
            ConfigKind.Multiplicador,
            "Multiplica quanto tempo o corpo já saqueado continua no chão.",
            new[]
            {
                new HelpExample("0.5", "o original"),
                new HelpExample("0.1", "os corpos somem quase na hora, o cenário fica limpo"),
            }),
    };
}
