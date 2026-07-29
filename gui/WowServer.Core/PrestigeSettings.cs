namespace WowServer.Core;

/// <summary>Como um ajuste do prestigio deve ser mostrado e validado.</summary>
public enum PrestigeKind { Numero, Booleano, Texto }

/// <summary>Um ajuste do 01_prestige_config.lua que a interface expoe.</summary>
public sealed record PrestigeSetting(
    string Key,
    string Label,
    string Category,
    PrestigeKind Kind,
    string Description)
{
    public double? Min { get; init; }
    public double? Max { get; init; }

    /// <summary>Consequencia de errar, quando vale avisar.</summary>
    public string? Warning { get; init; }
}

/// <summary>
/// Catalogo dos ajustes do Sistema de Prestigio.
///
/// <para>
/// Toda chave daqui foi lida do <c>mods/prestige/lua_scripts/01_prestige_config.lua</c>,
/// e um teste confere que continuam existindo lá — chave que sai do arquivo e
/// vira um campo que nao grava nada.
/// </para>
///
/// <para>
/// A explicacao mostrada na tela vem do proprio arquivo, lida pelo
/// <see cref="LuaConfig"/>; o texto daqui e o rotulo e o aviso, que o arquivo
/// nao tem como dar.
/// </para>
/// </summary>
public static class PrestigeSettings
{
    public const string ElegibilidadeCategoria = "Quem pode prestigiar";
    public const string ExperienciaCategoria   = "Experiência";
    public const string BencaoCategoria        = "Bênção do Ápice";
    public const string ItensCategoria         = "Itens e correio";
    public const string ResetCategoria         = "O que é apagado";
    public const string NpcCategoria           = "NPC e visual";

    public static IReadOnlyList<string> Categories { get; } = new[]
    {
        ElegibilidadeCategoria, ExperienciaCategoria, BencaoCategoria,
        ItensCategoria, ResetCategoria, NpcCategoria,
    };

    public static PrestigeSetting? Find(string key) =>
        All.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.Ordinal));

    public static IReadOnlyList<PrestigeSetting> All { get; } = new[]
    {
        new PrestigeSetting("MIN_LEVEL", "Nível mínimo", ElegibilidadeCategoria,
            PrestigeKind.Numero,
            "Abaixo deste nível o NPC recusa. É o preço de entrada do sistema.")
        { Min = 1, Max = 80 },

        new PrestigeSetting("MAX_LEVEL", "Nível máximo da faixa normal", ElegibilidadeCategoria,
            PrestigeKind.Numero,
            "Topo da faixa em que se prestigia sem ganhar a Bênção. Quem está no "
            + "nível máximo do servidor passa pela outra porta.")
        { Min = 1, Max = 80 },

        new PrestigeSetting("ALLOW_MAX_LEVEL", "Permitir prestigiar no nível máximo",
            ElegibilidadeCategoria, PrestigeKind.Booleano,
            "Deixa quem está no nível máximo prestigiar — e é por aí que se ganha a "
            + "Bênção do Ápice.")
        {
            Warning = "Desligando isto, a Bênção do Ápice fica inalcançável: não há "
                    + "outro caminho para ela.",
        },

        new PrestigeSetting("SERVER_MAX_LEVEL", "Nível máximo do servidor",
            ElegibilidadeCategoria, PrestigeKind.Numero,
            "Precisa bater com o MaxPlayerLevel do worldserver.conf. É este número "
            + "que decide quem ganha a Bênção.")
        { Min = 1, Max = 255 },

        new PrestigeSetting("MAX_PRESTIGE", "Quantos prestígios no máximo",
            ElegibilidadeCategoria, PrestigeKind.Numero,
            "Teto de vezes que um personagem pode prestigiar. 0 remove o limite.")
        {
            Min = 0, Max = 255,
            Warning = "Este valor é calibrado junto com o incremento de XP: "
                    + "1 + (MAX_PRESTIGE × XP_STEP) deve dar exatamente o teto. "
                    + "Mexer só aqui faz os últimos prestígios virarem custo sem "
                    + "benefício — o jogador perde tudo e o multiplicador não sobe.",
        },

        new PrestigeSetting("XP_STEP", "Quanto de XP cada prestígio dá",
            ExperienciaCategoria, PrestigeKind.Numero,
            "Somado ao multiplicador por prestígio. Com 0,4 o primeiro prestígio "
            + "leva a 1,4× e o décimo a 5,0×.")
        {
            Min = 0, Max = 10,
            Warning = "Ver o aviso de MAX_PRESTIGE: os dois andam juntos.",
        },

        new PrestigeSetting("XP_CAP", "Teto do multiplicador de XP",
            ExperienciaCategoria, PrestigeKind.Numero,
            "O multiplicador nunca passa disto, por mais prestígios que o "
            + "personagem tenha.")
        { Min = 1, Max = 100 },

        new PrestigeSetting("MAXLEVEL_BONUS_ENABLED", "Ligar a Bênção do Ápice",
            BencaoCategoria, PrestigeKind.Booleano,
            "Desligado, o módulo inteiro da Bênção para de agir — os quatro "
            + "multiplicadores abaixo deixam de valer."),

        new PrestigeSetting("MAXLEVEL_REP_MULT", "Reputação", BencaoCategoria,
            PrestigeKind.Numero,
            "Multiplica o ganho de reputação de quem tem a Bênção.")
        {
            Min = 1, Max = 100,
            Warning = "Abaixo de 1 o mod força o mínimo de 1: um resultado zero ou "
                    + "negativo faria o core bloquear o ganho inteiro, e o "
                    + "personagem nunca mais ganharia reputação.",
        },

        new PrestigeSetting("MAXLEVEL_SKILL_MULT", "Pontos de profissão", BencaoCategoria,
            PrestigeKind.Numero,
            "Multiplica o passo de subida de profissão — não o valor da skill.")
        { Min = 1, Max = 100 },

        new PrestigeSetting("MAXLEVEL_LOOT_MULT", "Recursos coletados", BencaoCategoria,
            PrestigeKind.Numero,
            "Multiplica o que cai de material de profissão: minério, erva, couro, "
            + "pano, pó e peixe.")
        {
            Min = 1, Max = 100,
            Warning = "Vale para toda a categoria Trade Goods. Se algum material "
                    + "funciona como moeda no seu servidor, ponha na "
                    + "RESOURCE_BLACKLIST do arquivo antes de subir isto.",
        },

        new PrestigeSetting("MAXLEVEL_HONOR_MULT", "Honra de PvP", BencaoCategoria,
            PrestigeKind.Numero,
            "Multiplica a honra ganha. Chega com alguns segundos de atraso, porque "
            + "não existe hook de honra no ALE e o mod compara o total de tempo em "
            + "tempo.")
        { Min = 1, Max = 100 },

        new PrestigeSetting("MAXLEVEL_HONOR_POLL_MS", "Intervalo da checagem de honra",
            BencaoCategoria, PrestigeKind.Numero,
            "Em milissegundos. Menor deixa o bônus mais imediato e consulta o banco "
            + "mais vezes; maior economiza e o atraso fica visível.")
        { Min = 500, Max = 600000 },

        new PrestigeSetting("MAIL_SUBJECT", "Assunto da carta", ItensCategoria,
            PrestigeKind.Texto,
            "Título das cartas que devolvem o equipamento depois da ascensão."),

        new PrestigeSetting("MAIL_BODY", "Corpo da carta", ItensCategoria,
            PrestigeKind.Texto,
            "Texto dentro da carta."),

        new PrestigeSetting("MAIL_DELAY", "Atraso da entrega", ItensCategoria,
            PrestigeKind.Numero,
            "Segundos até a carta chegar. 0 entrega na hora.")
        { Min = 0, Max = 259200 },

        new PrestigeSetting("MAX_ITEM_STACKS", "Teto de pilhas de item", ItensCategoria,
            PrestigeKind.Numero,
            "Cada pilha vira uma carta separada — limitação da API de correio. "
            + "Acima deste número o mod recusa e pede para limpar a mochila.")
        { Min = 1, Max = 255 },

        new PrestigeSetting("INCLUDE_BANK", "Esvaziar o banco também", ItensCategoria,
            PrestigeKind.Booleano,
            "O padrão é não mexer no banco: ele já é armazenamento seguro e "
            + "permanente."),

        new PrestigeSetting("RESET_TALENTS", "Zerar talentos", ResetCategoria,
            PrestigeKind.Booleano,
            "Devolve os pontos de talento na ascensão."),

        new PrestigeSetting("RESET_ACHIEVEMENTS", "Zerar conquistas", ResetCategoria,
            PrestigeKind.Booleano,
            "Apaga as conquistas do personagem.")
        { Warning = "Não tem desfazer. O único jeito de voltar é o backup do banco." },

        new PrestigeSetting("RESET_LEVEL_SKILLS", "Zerar skills de arma e magia",
            ResetCategoria, PrestigeKind.Booleano,
            "Devolve para 1 as perícias que sobem com o nível: armas, escolas de "
            + "magia (Fogo, Gelo, Arcano, Sagrado, Natureza, Sombras) e Defesa.")
        {
            Warning = "Deixando desligado, o personagem fica nível 1 com Fogo em 400 e "
                    + "máximo 5. Não é o mod: o core só baixa o máximo quando o nível "
                    + "cai, nunca o valor — ele não foi feito para nível caindo. "
                    + "Profissões não entram aqui: continuam preservadas pelo snapshot.",
        },

        new PrestigeSetting("RESET_SPELLS", "Zerar magias", ResetCategoria,
            PrestigeKind.Booleano,
            "Apaga as magias aprendidas e re-ensina as iniciais da classe. Sem "
            + "isto, um personagem nível 1 sai por aí com magias de nível 80.")
        {
            Warning = "Isto também apaga as magias de profissão. O mod guarda o "
                    + "nível de cada profissão antes e restaura no login seguinte — "
                    + "por isso a tabela character_prestige_skills precisa existir.",
        },

        new PrestigeSetting("NPC_ENTRY", "Entry do NPC", NpcCategoria,
            PrestigeKind.Numero,
            "O entry em creature_template que o mod escuta. Mudar aqui exige criar "
            + "o NPC de novo com o novo número.")
        {
            Min = 1,
            Warning = "Se este número não bater com o NPC que existe no banco, "
                    + "clicar nele não abre menu — e o sintoma é igual ao de script "
                    + "quebrado. O botão Instalar cria o NPC com o valor que está aqui.",
        },

        new PrestigeSetting("GOSSIP_TEXT_ID", "Texto de gossip", NpcCategoria,
            PrestigeKind.Numero,
            "O npc_text.ID usado como cabeçalho do menu.")
        { Min = 0 },

        new PrestigeSetting("AURA_SPELL_ID", "Aura cosmética", NpcCategoria,
            PrestigeKind.Numero,
            "Magia existente usada só como ícone de buff. 0 desliga.")
        {
            Min = 0,
            Warning = "Só serve magia que o cliente já conhece do Spell.dbc dele. "
                    + "Uma magia inventada no servidor não desenha ícone nenhum. E o "
                    + "nome no tooltip será o da magia original, não 'Prestígio' — "
                    + "é por isso que o mod usa um addon em vez de aura.",
        },

        new PrestigeSetting("DEBUG", "Log detalhado", NpcCategoria, PrestigeKind.Booleano,
            "Escreve cada passo no log do worldserver. Vale deixar ligado enquanto "
            + "estiver testando: é assim que as falhas dos métodos não confirmados "
            + "da API aparecem."),
    };

    /// <summary>
    /// Valida o que o usuario digitou, no tipo que o arquivo ja usa.
    /// </summary>
    public static bool TryFormat(
        PrestigeSetting setting, LuaSetting atual, string? digitado,
        out string formatado, out string? erro)
    {
        formatado = string.Empty;
        erro = null;

        var t = (digitado ?? string.Empty).Trim();

        if (setting.Kind != PrestigeKind.Texto && t.Length == 0)
        {
            erro = "Escreva um valor.";
            return false;
        }

        var valor = LuaConfig.FormatLike(atual, t);
        if (valor is null)
        {
            erro = setting.Kind switch
            {
                PrestigeKind.Booleano => "Use sim ou não.",
                PrestigeKind.Numero => "Use um número.",
                _ => "Texto de uma linha só.",
            };
            return false;
        }

        if (setting.Kind == PrestigeKind.Numero)
        {
            var n = double.Parse(valor, System.Globalization.CultureInfo.InvariantCulture);
            if (setting.Min is not null && n < setting.Min)
            {
                erro = $"O mínimo é {setting.Min}.";
                return false;
            }
            if (setting.Max is not null && n > setting.Max)
            {
                erro = $"O máximo é {setting.Max}.";
                return false;
            }
        }

        formatado = valor;
        return true;
    }

    /// <summary>
    /// A curva de XP que os valores atuais produzem — o jeito de ver na hora se
    /// os ultimos prestigios viraram custo sem beneficio.
    /// </summary>
    public static IReadOnlyList<(int Prestigio, double Multiplicador)> XpCurve(
        int maxPrestige, double step, double cap)
    {
        var curva = new List<(int, double)>();
        if (maxPrestige <= 0 || maxPrestige > 50) return curva;

        for (var i = 1; i <= maxPrestige; i++)
            curva.Add((i, Math.Min(1 + i * step, cap)));

        return curva;
    }

    /// <summary>
    /// Diz se a calibragem esta certa: o ultimo prestigio tem que chegar
    /// exatamente ao teto. Sobrando teto, o jogador nunca alcanca o que foi
    /// prometido; batendo antes, os ultimos prestigios nao dao nada.
    /// </summary>
    public static string? CurveWarning(int maxPrestige, double step, double cap)
    {
        var curva = XpCurve(maxPrestige, step, cap);
        if (curva.Count == 0) return null;

        var primeiroNoTeto = curva.FirstOrDefault(p => p.Multiplicador >= cap - 1e-9);
        if (primeiroNoTeto.Prestigio > 0 && primeiroNoTeto.Prestigio < maxPrestige)
        {
            var perdidos = maxPrestige - primeiroNoTeto.Prestigio;
            return $"O teto de {cap:0.##}× é alcançado já no prestígio "
                 + $"{primeiroNoTeto.Prestigio}. Os últimos {perdidos} viram custo sem "
                 + "benefício: o jogador perde tudo e o multiplicador não sobe.";
        }

        var ultimo = curva[^1].Multiplicador;
        if (ultimo < cap - 1e-9)
        {
            return $"Nem o último prestígio chega ao teto de {cap:0.##}× "
                 + $"(para em {ultimo:0.##}×). O teto configurado é inalcançável.";
        }

        return null;
    }
}
