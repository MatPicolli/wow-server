using System.Globalization;

namespace WowServer.Core;

/// <summary>Uma linha de item_template, so com o que a tela usa.</summary>
public sealed record ItemRow(
    int Entry,
    string Name,
    int Class,
    int Subclass,
    int Quality,
    int InventoryType,
    int ItemLevel,
    int RequiredLevel,
    int Bonding,
    int Armor,
    double DmgMin,
    double DmgMax,
    int Delay,
    int Durability,
    int SellPrice,
    int MaxStack,
    IReadOnlyList<(int Type, int Value)> Stats,
    string Description,
    int DisplayId);

/// <summary>Filtros da busca de itens.</summary>
public sealed record ItemFilter
{
    public string? Name { get; init; }
    public int? Class { get; init; }
    public int? Subclass { get; init; }
    public int? Quality { get; init; }
    public int? MinLevel { get; init; }
    public int? MaxLevel { get; init; }
    public int Limit { get; init; } = 200;
}

public static class ItemBrowser
{
    public const int LimiteMaximo = 500;

    /// <summary>Nome de cada classe de item (ItemClass em ItemTemplate.h).</summary>
    public static IReadOnlyDictionary<int, string> Classes { get; } = new Dictionary<int, string>
    {
        [0] = "Consumível",
        [1] = "Recipiente",
        [2] = "Arma",
        [3] = "Gema",
        [4] = "Armadura",
        [5] = "Reagente",
        [6] = "Projétil",
        [7] = "Material de profissão",
        [8] = "Genérico",
        [9] = "Receita",
        [10] = "Dinheiro",
        [11] = "Aljava",
        [12] = "Item de quest",
        [13] = "Chave",
        [14] = "Permanente",
        [15] = "Diversos",
        [16] = "Glifo",
    };

    public static IReadOnlyDictionary<int, string> ArmasSubclasses { get; } = new Dictionary<int, string>
    {
        [0] = "Machado (1 mão)", [1] = "Machado (2 mãos)", [2] = "Arco", [3] = "Arma de fogo",
        [4] = "Maça (1 mão)", [5] = "Maça (2 mãos)", [6] = "Lança de haste", [7] = "Espada (1 mão)",
        [8] = "Espada (2 mãos)", [10] = "Cajado", [13] = "Punho", [14] = "Diversos",
        [15] = "Adaga", [16] = "Arremesso", [18] = "Besta", [19] = "Varinha",
        [20] = "Vara de pesca",
    };

    public static IReadOnlyDictionary<int, string> ArmaduraSubclasses { get; } = new Dictionary<int, string>
    {
        [0] = "Diversos", [1] = "Tecido", [2] = "Couro", [3] = "Malha", [4] = "Placas",
        [5] = "Broquel", [6] = "Escudo", [7] = "Livro", [8] = "Ídolo", [9] = "Totem",
        [10] = "Sigilo",
    };

    public static IReadOnlyDictionary<int, string> Qualidades { get; } = new Dictionary<int, string>
    {
        [0] = "Lixo", [1] = "Comum", [2] = "Incomum", [3] = "Raro",
        [4] = "Épico", [5] = "Lendário", [6] = "Artefato", [7] = "Herança",
    };

    /// <summary>Cor de cada qualidade, igual a do jogo.</summary>
    public static string CorDaQualidade(int quality) => quality switch
    {
        0 => "#9D9D9D",   // cinza
        1 => "#FFFFFF",   // branco
        2 => "#1EFF00",   // verde
        3 => "#0070DD",   // azul
        4 => "#A335EE",   // roxo
        5 => "#FF8000",   // laranja
        6 => "#E6CC80",   // artefato
        7 => "#00CCFF",   // heranca
        _ => "#FFFFFF",
    };

    public static string NomeDaSubclasse(int classe, int subclasse) => classe switch
    {
        2 => ArmasSubclasses.TryGetValue(subclasse, out var a) ? a : "",
        4 => ArmaduraSubclasses.TryGetValue(subclasse, out var b) ? b : "",
        _ => "",
    };

    /// <summary>Nomes dos atributos (ItemModType).</summary>
    public static IReadOnlyDictionary<int, string> Atributos { get; } = new Dictionary<int, string>
    {
        [0] = "Mana", [1] = "Vida", [3] = "Agilidade", [4] = "Força", [5] = "Intelecto",
        [6] = "Espírito", [7] = "Vigor", [12] = "Defesa", [13] = "Esquiva", [14] = "Aparar",
        [15] = "Bloqueio", [16] = "Acerto corpo a corpo", [17] = "Acerto à distância",
        [18] = "Acerto mágico", [19] = "Crítico corpo a corpo", [20] = "Crítico à distância",
        [21] = "Crítico mágico", [31] = "Acerto", [32] = "Crítico", [35] = "Resiliência",
        [36] = "Aceleração", [37] = "Perícia em armas", [38] = "Poder de ataque",
        [39] = "Poder de ataque à distância", [43] = "Regeneração de mana",
        [44] = "Penetração de armadura", [45] = "Poder mágico", [46] = "Regeneração de vida",
        [47] = "Penetração mágica", [48] = "Valor de bloqueio",
    };

    /// <summary>Onde o item é equipado (InventoryType).</summary>
    public static IReadOnlyDictionary<int, string> Slots { get; } = new Dictionary<int, string>
    {
        [1] = "Cabeça", [2] = "Pescoço", [3] = "Ombros", [4] = "Camisa", [5] = "Peito",
        [6] = "Cintura", [7] = "Pernas", [8] = "Pés", [9] = "Pulsos", [10] = "Mãos",
        [11] = "Anel", [12] = "Berloque", [13] = "Uma mão", [14] = "Mão secundária",
        [15] = "À distância", [16] = "Capa", [17] = "Duas mãos", [18] = "Bolsa",
        [19] = "Tabardo", [20] = "Peito", [21] = "Mão principal", [22] = "Mão secundária",
        [23] = "Item de mão", [25] = "Arremesso", [26] = "À distância", [28] = "Relíquia",
    };

    public static string TextoDoVinculo(int bonding) => bonding switch
    {
        1 => "Vincula ao ser saqueado",
        2 => "Vincula ao equipar",
        3 => "Vincula ao usar",
        4 => "Item de missão",
        _ => "",
    };

    /// <summary>
    /// Monta o SELECT da busca.
    ///
    /// Os filtros numericos entram como numero, nunca como texto colado; o nome
    /// e escapado e vai num LIKE. Sem isso, um item chamado O'Reilly - ou um
    /// nome digitado com aspas - quebraria a consulta.
    /// </summary>
    public static string BuildQuery(ItemFilter filtro, string worldDb)
    {
        var onde = new List<string>();

        if (!string.IsNullOrWhiteSpace(filtro.Name))
            onde.Add($"name LIKE '%{EscaparLike(filtro.Name.Trim())}%'");

        if (filtro.Class is int c) onde.Add($"class = {c}");
        if (filtro.Subclass is int s) onde.Add($"subclass = {s}");
        if (filtro.Quality is int q) onde.Add($"Quality = {q}");
        if (filtro.MinLevel is int min) onde.Add($"ItemLevel >= {min}");
        if (filtro.MaxLevel is int max) onde.Add($"ItemLevel <= {max}");

        var limite = Math.Clamp(filtro.Limit, 1, LimiteMaximo);
        var filtroSql = onde.Count > 0 ? "WHERE " + string.Join(" AND ", onde) : "";

        // A ordem importa para a tela: os melhores primeiro, e entry como
        // desempate para o resultado nao mudar de posicao entre buscas iguais.
        return
            "SELECT entry, name, class, subclass, Quality, InventoryType, ItemLevel, RequiredLevel, "
            + "bonding, armor, dmg_min1, dmg_max1, delay, MaxDurability, SellPrice, stackable, "
            + "stat_type1, stat_value1, stat_type2, stat_value2, stat_type3, stat_value3, "
            + "stat_type4, stat_value4, stat_type5, stat_value5, description, displayid "
            + $"FROM `{worldDb}`.item_template {filtroSql} "
            + $"ORDER BY Quality DESC, ItemLevel DESC, entry ASC LIMIT {limite};";
    }

    /// <summary>
    /// Escapa o que o MySQL trata de forma especial dentro de um LIKE.
    /// A contrabarra vem primeiro: escapar depois dobraria as que acabaram de
    /// ser inseridas.
    /// </summary>
    public static string EscaparLike(string texto) =>
        texto.Replace("\\", "\\\\")
             .Replace("'", "''")
             .Replace("%", "\\%")
             .Replace("_", "\\_");

    /// <summary>
    /// Le uma linha separada por tabulacao vinda do cliente mysql.
    /// Devolve null quando a linha nao e um registro (cabecalho, aviso, vazio).
    /// </summary>
    public static ItemRow? ParseRow(string linha)
    {
        if (string.IsNullOrWhiteSpace(linha)) return null;

        var campos = linha.Split('\t');
        if (campos.Length < 28) return null;
        if (!int.TryParse(campos[0], out var entry)) return null;

        int I(int i) => int.TryParse(campos[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
        double D(int i) => double.TryParse(campos[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;

        var stats = new List<(int, int)>();
        for (var i = 16; i <= 24; i += 2)
        {
            var tipo = I(i);
            var valor = I(i + 1);
            // stat_type 0 e 'Mana', entao nao serve como "sem atributo" - quem
            // decide e o valor. Item sem atributo tem tipo 0 e valor 0.
            if (valor != 0) stats.Add((tipo, valor));
        }

        return new ItemRow(
            entry, campos[1], I(2), I(3), I(4), I(5), I(6), I(7), I(8), I(9),
            D(10), D(11), I(12), I(13), I(14), I(15), stats,
            campos.Length > 26 ? campos[26] : "",
            I(27));
    }

    /// <summary>Ouro/prata/cobre a partir do valor em cobre.</summary>
    public static string FormatarDinheiro(int cobre)
    {
        if (cobre <= 0) return "";
        var o = cobre / 10000;
        var p = cobre % 10000 / 100;
        var c = cobre % 100;

        var partes = new List<string>();
        if (o > 0) partes.Add($"{o}o");
        if (p > 0) partes.Add($"{p}p");
        if (c > 0) partes.Add($"{c}c");
        return string.Join(" ", partes);
    }
}

/// <summary>Uma linha do balao, com a cor que ela deve ter.</summary>
public sealed record TooltipLine(string Text, string Color);

/// <summary>
/// Monta o balao de informacoes de um item, no formato do jogo.
///
/// Tudo sai do item_template. Efeitos de magia ("Usar: ...") ficam de fora de
/// proposito: o texto deles mora no Spell.dbc do client, nao no banco, e
/// inventar uma descricao seria pior do que omitir.
/// </summary>
public static class ItemTooltip
{
    public const string Branco = "#FFFFFF";
    public const string Cinza  = "#9D9D9D";
    public const string Amarelo = "#FFD100";
    public const string Verde  = "#1EFF00";
    public const string Dourado = "#FFD100";

    /// <summary>Vermelho do aviso, o mesmo tom de "requisito nao atendido" do jogo.</summary>
    public const string Vermelho = "#FF2020";

    /// <param name="clientItemIds">
    /// Entries do Item.dbc do cliente, de <see cref="ClientItemCheck.ReadClientItemIds"/>.
    /// Passando <c>null</c> o balao sai como sempre saiu — e o que fazer quando
    /// o arquivo nao foi extraido, porque acusar todo item de ausente seria pior
    /// que nao checar.
    /// </param>
    public static IReadOnlyList<TooltipLine> Build(ItemRow item, HashSet<int>? clientItemIds = null)
    {
        var linhas = new List<TooltipLine>
        {
            new(item.Name, ItemBrowser.CorDaQualidade(item.Quality)),
        };

        var vinculo = ItemBrowser.TextoDoVinculo(item.Bonding);
        if (vinculo.Length > 0) linhas.Add(new(vinculo, Branco));

        // Slot a esquerda e tipo a direita e como o jogo mostra; aqui vai na
        // mesma linha, separado, porque o balao nao tem colunas.
        var slot = ItemBrowser.Slots.TryGetValue(item.InventoryType, out var sl) ? sl : "";
        var tipo = ItemBrowser.NomeDaSubclasse(item.Class, item.Subclass);
        if (slot.Length > 0 || tipo.Length > 0)
            linhas.Add(new(string.Join("   ", new[] { slot, tipo }.Where(x => x.Length > 0)), Branco));

        if (item.DmgMin > 0 || item.DmgMax > 0)
        {
            linhas.Add(new($"{item.DmgMin:0} - {item.DmgMax:0} de dano", Branco));

            if (item.Delay > 0)
            {
                var segundos = item.Delay / 1000.0;
                var dps = (item.DmgMin + item.DmgMax) / 2.0 / segundos;
                linhas.Add(new($"Velocidade {segundos:0.00}", Branco));
                linhas.Add(new($"({dps:0.0} de dano por segundo)", Branco));
            }
        }

        if (item.Armor > 0) linhas.Add(new($"{item.Armor} de armadura", Branco));

        foreach (var (tipoStat, valor) in item.Stats)
        {
            var nome = ItemBrowser.Atributos.TryGetValue(tipoStat, out var n) ? n : $"atributo {tipoStat}";
            var sinal = valor > 0 ? "+" : "";
            linhas.Add(new($"{sinal}{valor} de {nome}", Branco));
        }

        if (item.Durability > 0) linhas.Add(new($"Durabilidade {item.Durability}", Branco));

        if (item.RequiredLevel > 1)
            linhas.Add(new($"Nível necessário: {item.RequiredLevel}", Branco));

        if (item.ItemLevel > 0)
            linhas.Add(new($"Nível do item: {item.ItemLevel}", Dourado));

        if (item.Description.Length > 0)
            linhas.Add(new($"\"{item.Description}\"", Dourado));

        var preco = ItemBrowser.FormatarDinheiro(item.SellPrice);
        if (preco.Length > 0) linhas.Add(new($"Preço de venda: {preco}", Branco));

        linhas.Add(new($"ID {item.Entry}", Cinza));

        // Item feito a mao passa pela validacao do servidor e mesmo assim sai
        // quebrado no cliente - sem icone, sem equipar no botao direito, sem
        // som. Como nao ha erro em log nenhum, o aviso tem que aparecer aqui.
        foreach (var problema in ClientItemCheck.Check(item, clientItemIds))
            linhas.Add(new("[cliente] " + problema.Summary, Vermelho));

        return linhas;
    }
}

/// <summary>
/// Divide os itens escolhidos em cartas de correio.
/// </summary>
public static class MailPlan
{
    /// <summary>Teto do servidor, em MAX_MAIL_ITEMS.</summary>
    public const int ItensPorCarta = 12;

    /// <summary>
    /// Comandos 'send items' prontos para o console do worldserver.
    /// </summary>
    public static IReadOnlyList<string> Build(
        IReadOnlyList<int> entries, string personagem, string assunto = "Itens")
    {
        var comandos = new List<string>();
        if (entries.Count == 0) return comandos;

        var total = (entries.Count + ItensPorCarta - 1) / ItensPorCarta;

        for (var i = 0; i < entries.Count; i += ItensPorCarta)
        {
            var fatia = entries.Skip(i).Take(ItensPorCarta);
            var numero = i / ItensPorCarta + 1;
            var titulo = total > 1 ? $"{assunto} {numero}/{total}" : assunto;

            comandos.Add($"send items {personagem} \"{titulo}\" \"Aproveite.\" {string.Join(" ", fatia)}");
        }

        return comandos;
    }

    /// <summary>
    /// Comandos '.additem' para colar no chat do jogo. Um por item: o comando
    /// nao aceita lista, e depende de quem esta selecionado - por isso nao
    /// funciona pelo console.
    /// </summary>
    public static IReadOnlyList<string> BuildAddItem(IReadOnlyList<int> entries) =>
        entries.Select(e => $".additem {e}").ToList();
}
