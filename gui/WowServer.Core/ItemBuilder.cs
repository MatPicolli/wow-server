using System.Globalization;
using System.Text;

namespace WowServer.Core;

/// <summary>Um atributo do item (tipo de <see cref="ItemBrowser.Atributos"/>).</summary>
public sealed record ItemStat(int Type, int Value);

/// <summary>
/// Um feitico preso ao item. O <paramref name="Trigger"/> e o que decide se
/// vira "Usar:" ou "Equipar:" no balao.
/// </summary>
public sealed record ItemSpell(
    int SpellId, int Trigger, int Charges = 0, int Cooldown = -1,
    int Category = 0, int CategoryCooldown = -1);

/// <summary>Como um problema encontrado deve ser tratado.</summary>
public enum IssueLevel
{
    /// <summary>Impede de gravar.</summary>
    Erro,
    /// <summary>Grava, mas o resultado no jogo pode nao ser o esperado.</summary>
    Aviso,
}

public sealed record ItemIssue(IssueLevel Level, string Message);

/// <summary>O item que se quer criar, do jeito que a tela preenche.</summary>
public sealed record ItemDefinition
{
    public int Entry { get; init; }
    public string Name { get; init; } = "";
    public int Class { get; init; }
    public int Subclass { get; init; }
    public int Quality { get; init; } = 1;
    public int DisplayId { get; init; }
    public int InventoryType { get; init; }
    public int ItemLevel { get; init; }
    public int RequiredLevel { get; init; }
    public int Bonding { get; init; }
    public int Armor { get; init; }
    public double DmgMin { get; init; }
    public double DmgMax { get; init; }
    public int DmgType { get; init; }
    public int Delay { get; init; }
    public int MaxDurability { get; init; }
    public int SellPrice { get; init; }
    public int BuyPrice { get; init; }
    public int Stackable { get; init; } = 1;
    public int MaxCount { get; init; }
    public string Description { get; init; } = "";
    public int AllowableClass { get; init; } = -1;
    public int AllowableRace { get; init; } = -1;
    public int Flags { get; init; }

    public IReadOnlyList<ItemStat> Stats { get; init; } = Array.Empty<ItemStat>();
    public IReadOnlyList<ItemSpell> Spells { get; init; } = Array.Empty<ItemSpell>();

    /// <summary>
    /// Colunas extras digitadas a mao — o espaco de "codar" da tela. Chave e
    /// nome de coluna do item_template; valor vai cru para o SQL, ja validado.
    /// </summary>
    public IReadOnlyDictionary<string, string> Extra { get; init; } =
        new Dictionary<string, string>();

    /// <summary>Para o balao de previa, sem passar pelo banco.</summary>
    public ItemRow ToRow() => new(
        Entry, Name, Class, Subclass, Quality, InventoryType, ItemLevel, RequiredLevel,
        Bonding, Armor, DmgMin, DmgMax, Delay, MaxDurability, SellPrice,
        Stackable, Stats.Select(s => (s.Type, s.Value)).ToList(), Description, DisplayId);
}

/// <summary>
/// Monta o SQL que cria um item no <c>item_template</c>.
///
/// <para>
/// Duas regras mandam aqui, e as duas vieram de erro real:
/// </para>
/// <list type="number">
///   <item><description>
///     <b>So escreve coluna que existe no banco vivo.</b> O banco deste
///     servidor esta num schema mais antigo que o <c>source</c>, entao a lista
///     de colunas tem que vir de um <c>information_schema</c> de verdade, nunca
///     do SQL do core. Sem isso o erro e <c>ERROR 1054 Unknown column</c>.
///   </description></item>
///   <item><description>
///     <b>O SQL tem que poder rodar duas vezes.</b> <c>DELETE</c> antes do
///     <c>INSERT</c>, sempre — rodar de novo e o caso comum quando se esta
///     ajustando um item.
///   </description></item>
/// </list>
/// </summary>
public static class ItemBuilder
{
    /// <summary>
    /// Faixa sugerida para item criado a mao. Comeca bem acima do conteudo do
    /// jogo (o maior entry do 3.3.5a fica na casa dos 56 mil) e acima tambem da
    /// faixa 700000-700047, ja usada pelo set de heranca deste servidor.
    /// </summary>
    public const int FaixaCustomizadaInicio = 800000;

    /// <summary>Gatilhos possiveis de um feitico preso ao item.</summary>
    public static IReadOnlyDictionary<int, string> Gatilhos { get; } = new Dictionary<int, string>
    {
        [0] = "Ao usar (clicar no item)",
        [1] = "Ao equipar (enquanto estiver vestido)",
        [2] = "Chance ao acertar",
        [4] = "Pedra da alma",
        [5] = "Ao usar, sem atraso",
        [6] = "Ensina a magia",
    };

    public static IReadOnlyDictionary<int, string> Vinculos { get; } = new Dictionary<int, string>
    {
        [0] = "Não vincula",
        [1] = "Vincula ao pegar",
        [2] = "Vincula ao equipar",
        [3] = "Vincula ao usar",
        [4] = "Item de missão",
    };

    /// <summary>Menor entry livre a partir da faixa customizada.</summary>
    public static int NextFreeEntry(IEnumerable<int> usados, int inicio = FaixaCustomizadaInicio)
    {
        var ocupados = new HashSet<int>(usados);
        var e = inicio;
        while (ocupados.Contains(e)) e++;
        return e;
    }

    /// <summary>
    /// Confere o que a tela preencheu.
    ///
    /// <paramref name="entriesEmUso"/> e <paramref name="clientItemIds"/> podem
    /// ser <c>null</c> quando ainda nao foram lidos — o que nao se sabe nao
    /// vira acusacao.
    /// </summary>
    public static IReadOnlyList<ItemIssue> Validate(
        ItemDefinition def,
        ISet<int>? entriesEmUso = null,
        HashSet<int>? clientItemIds = null)
    {
        var achados = new List<ItemIssue>();

        void Erro(string m) => achados.Add(new ItemIssue(IssueLevel.Erro, m));
        void Aviso(string m) => achados.Add(new ItemIssue(IssueLevel.Aviso, m));

        if (def.Entry <= 0)
            Erro("O ID do item precisa ser maior que zero.");

        if (entriesEmUso is not null && entriesEmUso.Contains(def.Entry))
            Erro($"Já existe um item com o ID {def.Entry}. Escolha outro — sobrescrever "
                 + "um item do jogo quebraria tudo que usa esse ID.");

        if (string.IsNullOrWhiteSpace(def.Name))
            Erro("O item precisa de um nome.");
        else if (def.Name.Length > 255)
            Erro("O nome passa de 255 caracteres, que é o limite da coluna.");

        if (def.Description.Length > 255)
            Erro("A descrição passa de 255 caracteres, que é o limite da coluna.");

        if (def.Quality is < 0 or > 7)
            Erro("Qualidade tem que ficar entre 0 (lixo) e 7 (herança).");

        // As duas armadilhas do lado do cliente, as mesmas do navegador de itens.
        foreach (var p in ClientItemCheck.Check(def.ToRow(), clientItemIds))
            Aviso(p.Summary + " — " + p.Detail);

        if (def.DisplayId <= 0)
            Aviso("Sem displayid o item aparece sem ícone nenhum. Escolha um ícone.");

        // tinyint unsigned: passar de 255 faz o INSERT inteiro falhar sob modo
        // estrito, que e o padrao do MySQL 8.
        if (def.RequiredLevel is < 0 or > 255)
            Erro("Nível necessário tem que ficar entre 0 e 255.");
        if (def.Bonding is < 0 or > 4)
            Erro("Vínculo tem que ficar entre 0 e 4.");
        if (def.Class is < 0 or > 255)
            Erro("Categoria inválida.");
        if (def.Subclass is < 0 or > 255)
            Erro("Tipo inválido.");
        if (def.InventoryType is < 0 or > 255)
            Erro("Slot inválido.");

        if (def.Stackable < 1)
            Erro("A pilha máxima precisa ser pelo menos 1.");

        if (def.Stats.Count > 10)
            Erro("O item_template guarda no máximo 10 atributos.");
        if (def.Spells.Count > 5)
            Erro("O item_template guarda no máximo 5 efeitos.");

        foreach (var s in def.Stats)
        {
            if (s.Type is < 0 or > 255)
                Erro($"Tipo de atributo inválido: {s.Type}.");
        }

        foreach (var s in def.Spells)
        {
            if (s.SpellId <= 0)
                Erro("Efeito com ID de magia vazio — apague a linha ou preencha o ID.");
            if (!Gatilhos.ContainsKey(s.Trigger))
                Erro($"Gatilho inválido: {s.Trigger}.");
        }

        // Uma arma sem velocidade divide por zero no calculo de DPS do cliente.
        var ehArma = def.Class == 2;
        if (ehArma && def.Delay <= 0)
            Aviso("Arma sem velocidade (delay). O jogo usa 1000 = 1,0 segundo.");
        if ((def.DmgMin > 0 || def.DmgMax > 0) && def.DmgMax < def.DmgMin)
            Erro("O dano máximo está menor que o mínimo.");

        if (def.MaxDurability > 0 && def.InventoryType == 0)
            Aviso("Durabilidade só faz sentido em item que ocupa um slot de equipamento.");

        foreach (var (coluna, valor) in def.Extra)
        {
            if (!ColunaValida(coluna))
                Erro($"'{coluna}' não parece nome de coluna. Use só letras, números e _.");
            if (!ValorExtraValido(valor))
                Erro($"O valor de '{coluna}' tem caractere que não dá para gravar com segurança. "
                     + "Use um número, ou texto simples entre aspas simples.");
        }

        return achados;
    }

    /// <summary>Nome de coluna aceitavel: nada de espaco, aspas ou ponto-e-virgula.</summary>
    public static bool ColunaValida(string nome) =>
        nome.Length is > 0 and <= 64 && nome.All(c => char.IsLetterOrDigit(c) || c == '_');

    /// <summary>
    /// Valor livre do campo de "codar". Aceita numero, ou texto entre aspas
    /// simples sem aspas dentro — o suficiente para as colunas que sobram, e
    /// estreito o bastante para nao dar para emendar um segundo comando.
    /// </summary>
    public static bool ValorExtraValido(string valor)
    {
        var v = valor.Trim();
        if (v.Length == 0 || v.Length > 255) return false;

        if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) return true;

        if (v.Length >= 2 && v[0] == '\'' && v[^1] == '\'')
            return !v[1..^1].Contains('\'') && !v.Contains('\\');

        return false;
    }

    /// <summary>
    /// Escapa texto para dentro de aspas simples do MySQL.
    ///
    /// A contrabarra tambem tem que ser dobrada: por padrao o MySQL a trata
    /// como escape dentro de string, entao um nome terminando em '\' engoliria
    /// a aspa de fechamento e o comando seguinte viraria parte do texto.
    /// </summary>
    public static string Escape(string texto)
    {
        var limpo = new string(texto.Where(c => !char.IsControl(c)).ToArray());
        return limpo.Replace("\\", "\\\\").Replace("'", "''");
    }

    /// <summary>
    /// Coluna -> valor ja pronto para o SQL, na ordem em que vao aparecer.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string>> ColumnValues(ItemDefinition def)
    {
        var v = new List<KeyValuePair<string, string>>();
        void Add(string c, string valor) => v.Add(new KeyValuePair<string, string>(c, valor));
        void N(string c, long valor) => Add(c, valor.ToString(CultureInfo.InvariantCulture));
        void F(string c, double valor) => Add(c, valor.ToString("0.###", CultureInfo.InvariantCulture));

        N("entry", def.Entry);
        Add("name", "'" + Escape(def.Name) + "'");
        N("class", def.Class);
        N("subclass", def.Subclass);
        N("Quality", def.Quality);
        N("displayid", def.DisplayId);
        N("InventoryType", def.InventoryType);
        N("ItemLevel", def.ItemLevel);
        N("RequiredLevel", def.RequiredLevel);
        N("bonding", def.Bonding);
        N("armor", def.Armor);
        F("dmg_min1", def.DmgMin);
        F("dmg_max1", def.DmgMax);
        N("dmg_type1", def.DmgType);
        N("delay", def.Delay);
        N("MaxDurability", def.MaxDurability);
        N("SellPrice", def.SellPrice);
        N("BuyPrice", def.BuyPrice);
        N("stackable", def.Stackable);
        N("maxcount", def.MaxCount);
        Add("description", "'" + Escape(def.Description) + "'");
        N("AllowableClass", def.AllowableClass);
        N("AllowableRace", def.AllowableRace);
        N("Flags", def.Flags);

        // Os 10 pares de atributo sao escritos SEMPRE, inclusive os zerados: um
        // DELETE + INSERT parcial deixaria a coluna no default, e o default de
        // stat_type e 0, que e "nenhum" - mas escrever explicitamente e o que
        // torna o resultado igual toda vez que o SQL roda.
        for (var i = 0; i < 10; i++)
        {
            var s = i < def.Stats.Count ? def.Stats[i] : new ItemStat(0, 0);
            N($"stat_type{i + 1}", s.Type);
            N($"stat_value{i + 1}", s.Value);
        }

        for (var i = 0; i < 5; i++)
        {
            var s = i < def.Spells.Count ? def.Spells[i] : new ItemSpell(0, 0);
            N($"spellid_{i + 1}", s.SpellId);
            N($"spelltrigger_{i + 1}", s.Trigger);
            N($"spellcharges_{i + 1}", s.Charges);
            N($"spellcooldown_{i + 1}", s.Cooldown);
            N($"spellcategory_{i + 1}", s.Category);
            N($"spellcategorycooldown_{i + 1}", s.CategoryCooldown);
        }

        foreach (var (coluna, valor) in def.Extra)
            Add(coluna, valor.Trim());

        return v;
    }

    /// <summary>
    /// O SQL final.
    ///
    /// <paramref name="colunasDoBanco"/> vem do <c>information_schema</c> do
    /// banco vivo. Coluna pedida que nao existe la e devolvida em
    /// <paramref name="ignoradas"/> em vez de entrar no comando e derrubar o
    /// INSERT inteiro com <c>Unknown column</c>.
    /// </summary>
    public static string BuildSql(
        ItemDefinition def,
        IEnumerable<string> colunasDoBanco,
        out List<string> ignoradas)
    {
        var existentes = new HashSet<string>(colunasDoBanco, StringComparer.OrdinalIgnoreCase);
        ignoradas = new List<string>();

        var usadas = new List<KeyValuePair<string, string>>();
        foreach (var par in ColumnValues(def))
        {
            if (existentes.Contains(par.Key)) usadas.Add(par);
            else ignoradas.Add(par.Key);
        }

        var sql = new StringBuilder();
        sql.AppendLine($"-- {def.Name}  (entry {def.Entry})");
        sql.AppendLine("-- Gerado pela aba \"Criar itens\". Pode rodar quantas vezes quiser:");
        sql.AppendLine("-- o DELETE antes do INSERT deixa o resultado igual toda vez.");
        sql.AppendLine();
        sql.AppendLine($"DELETE FROM `item_template` WHERE `entry` = {def.Entry};");
        sql.AppendLine();
        sql.AppendLine("INSERT INTO `item_template`");
        sql.AppendLine("    (" + string.Join(", ", usadas.Select(p => "`" + p.Key + "`")) + ")");
        sql.AppendLine("VALUES");
        sql.AppendLine("    (" + string.Join(", ", usadas.Select(p => p.Value)) + ");");

        return sql.ToString();
    }
}
