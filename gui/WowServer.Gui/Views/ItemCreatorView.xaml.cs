using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WowServer.Core;

namespace WowServer.Gui.Views;

/// <summary>Uma linha de atributo do item em construção.</summary>
public sealed class AtributoLinha
{
    public IReadOnlyList<string> Tipos { get; }
    private readonly IReadOnlyList<int> _codigos;

    public AtributoLinha(IReadOnlyList<string> tipos, IReadOnlyList<int> codigos)
    {
        Tipos = tipos;
        _codigos = codigos;
    }

    public int TipoIndice { get; set; }
    public string Valor { get; set; } = "0";

    public int Codigo => TipoIndice >= 0 && TipoIndice < _codigos.Count ? _codigos[TipoIndice] : 0;

    public int ValorNumero =>
        int.TryParse(Valor?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
}

/// <summary>Uma linha de efeito (magia presa ao item).</summary>
public sealed class EfeitoLinha : INotifyPropertyChanged
{
    private string _spellId = "0";

    public IReadOnlyList<string> Gatilhos { get; }
    private readonly IReadOnlyList<int> _codigos;

    public EfeitoLinha(IReadOnlyList<string> gatilhos, IReadOnlyList<int> codigos)
    {
        Gatilhos = gatilhos;
        _codigos = codigos;
    }

    public string SpellId
    {
        get => _spellId;
        set
        {
            if (_spellId == value) return;
            _spellId = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SpellId)));
        }
    }

    public int GatilhoIndice { get; set; }
    public string Cooldown { get; set; } = "-1";

    public int SpellNumero =>
        int.TryParse(SpellId?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    public int CooldownNumero =>
        int.TryParse(Cooldown?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : -1;

    public int GatilhoCodigo =>
        GatilhoIndice >= 0 && GatilhoIndice < _codigos.Count ? _codigos[GatilhoIndice] : 0;

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>Um ícone disponível para escolher.</summary>
public sealed class IconeLinha
{
    public required int DisplayId { get; init; }
    public required string Nome { get; init; }
    public required ImageSource Imagem { get; init; }
    public Brush CorBorda { get; set; } = Brushes.Transparent;
    public string Dica => $"{Nome}  (displayid {DisplayId})";
}

/// <summary>Uma linha da conferência.</summary>
public sealed class ProblemaLinha
{
    public required string Texto { get; init; }
    public required Brush Cor { get; init; }
}

public partial class ItemCreatorView : UserControl
{
    private readonly ScriptRunner _runner = Session.Current.CreateRunner();

    private readonly List<AtributoLinha> _atributos = new();
    private readonly List<EfeitoLinha> _efeitos = new();

    private readonly List<int> _codigosAtributo = new();
    private readonly List<int> _codigosGatilho = new();
    private readonly List<int> _codigosSlot = new();
    private readonly List<int> _codigosQualidade = new();
    private readonly List<int> _codigosVinculo = new();

    private List<IconeLinha> _icones = new();
    private int _displayIdEscolhido;

    /// <summary>Colunas do item_template no banco vivo. Vazio = ainda não li.</summary>
    private List<string> _colunas = new();

    /// <summary>Entries já ocupados. null = ainda não li o banco.</summary>
    private HashSet<int>? _entriesEmUso;

    private HashSet<int>? _idsDoCliente;
    private IReadOnlyList<SpellEntry>? _magias;
    private bool _ocupado;

    public ItemCreatorView()
    {
        InitializeComponent();
        Saida.Title = "Saída";

        _runner.Output += linha =>
            Dispatcher.Invoke(() => Saida.Append(linha.Text, linha.Kind));

        MontarCombos();
        MontarLinhas();

        // Valores iniciais no construtor, e nao no XAML: definir Text no XAML
        // dispara TextChanged durante o InitializeComponent, quando os campos
        // declarados depois ainda nao existem.
        TxtEntry.Text = ItemBuilder.FaixaCustomizadaInicio.ToString(CultureInfo.InvariantCulture);
        TxtNome.Text = "Item Novo";
        TxtItemLevel.Text = "1";
        TxtReqLevel.Text = "1";
        TxtPilha.Text = "1";
        TxtArmadura.Text = "0";
        TxtDanoMin.Text = "0";
        TxtDanoMax.Text = "0";
        TxtDelay.Text = "0";
        TxtDurabilidade.Text = "0";

        Loaded += async (_, _) => await CarregarDoBancoAsync();
        IsVisibleChanged += async (_, e) => { if (e.NewValue is true) await CarregarDoBancoAsync(); };

        Atualizar();
    }

    // ------------------------------------------------------------- montagem --

    private void MontarCombos()
    {
        // "Para que serve" é um atalho: cada opção preenche categoria,
        // subcategoria e vínculo de uma vez.
        ComboTipo.ItemsSource = new[]
        {
            "Equipamento (armadura)", "Equipamento (arma)",
            "Usável (dispara uma magia)", "Consumível (gasta ao usar)",
            "Material de profissão", "Item de missão", "Diversos",
        };
        ComboTipo.SelectedIndex = 0;

        // O dicionario de slots nao tem o 0 - ele nao e um slot, e a ausencia
        // de slot. Entra a mao, na frente, porque e o valor certo para tudo que
        // nao e equipamento.
        ComboSlot.Items.Add("Não equipável");
        _codigosSlot.Add(0);
        foreach (var (codigo, nome) in ItemBrowser.Slots.OrderBy(s => s.Key))
        {
            ComboSlot.Items.Add($"{nome}  ({codigo})");
            _codigosSlot.Add(codigo);
        }
        ComboSlot.SelectedIndex = Math.Max(0, _codigosSlot.IndexOf(1));

        foreach (var (codigo, nome) in ItemBrowser.Qualidades.OrderBy(q => q.Key))
        {
            ComboQualidade.Items.Add(nome);
            _codigosQualidade.Add(codigo);
        }
        ComboQualidade.SelectedIndex = _codigosQualidade.IndexOf(1) >= 0 ? _codigosQualidade.IndexOf(1) : 0;

        foreach (var (codigo, nome) in ItemBuilder.Vinculos.OrderBy(b => b.Key))
        {
            ComboVinculo.Items.Add(nome);
            _codigosVinculo.Add(codigo);
        }
        ComboVinculo.SelectedIndex = 0;
    }

    private void MontarLinhas()
    {
        var nomesAtributo = new List<string> { "(nenhum)" };
        _codigosAtributo.Add(0);
        foreach (var (codigo, nome) in ItemBrowser.Atributos.OrderBy(a => a.Key))
        {
            nomesAtributo.Add(nome);
            _codigosAtributo.Add(codigo);
        }

        var nomesGatilho = new List<string>();
        foreach (var (codigo, nome) in ItemBuilder.Gatilhos.OrderBy(g => g.Key))
        {
            nomesGatilho.Add(nome);
            _codigosGatilho.Add(codigo);
        }

        for (var i = 0; i < 10; i++) _atributos.Add(new AtributoLinha(nomesAtributo, _codigosAtributo));
        for (var i = 0; i < 5; i++) _efeitos.Add(new EfeitoLinha(nomesGatilho, _codigosGatilho));

        ListaAtributos.ItemsSource = _atributos;
        ListaEfeitos.ItemsSource = _efeitos;
    }

    // --------------------------------------------------------- ler do banco --

    /// <summary>
    /// Lê do banco vivo o que não dá para adivinhar: quais colunas o
    /// item_template tem aqui, e quais entries já estão ocupados.
    ///
    /// A lista de colunas é o ponto mais importante da tela. O banco deste
    /// servidor está num schema mais antigo que o código-fonte, então montar o
    /// INSERT com a lista do AzerothCore daria 'Unknown column' — e o item não
    /// seria criado.
    /// </summary>
    private async Task CarregarDoBancoAsync()
    {
        if (_ocupado) return;
        if (_colunas.Count > 0 && _entriesEmUso is not null) return;   // já li

        _idsDoCliente ??= LerItemDbc();

        if (!_runner.ScriptExists("gm-items.ps1"))
        {
            Saida.Append("[aviso] scripts\\gm-items.ps1 não encontrado — sem ele não dá para "
                         + "conferir colunas nem IDs em uso", OutputKind.Error);
            return;
        }

        var db = Session.Current.Loaded?.MySql.WorldDb;
        if (string.IsNullOrWhiteSpace(db)) return;

        _ocupado = true;
        try
        {
            var colunas = await ConsultarAsync(
                $"SELECT COLUMN_NAME FROM information_schema.columns "
                + $"WHERE table_schema = '{db}' AND table_name = 'item_template' "
                + "ORDER BY ORDINAL_POSITION");

            if (colunas.Count > 0)
            {
                _colunas = colunas.Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
                Saida.Append($"[ok] li {_colunas.Count} colunas de {db}.item_template");
            }

            var entries = await ConsultarAsync(
                $"SELECT entry FROM {db}.item_template WHERE entry >= {ItemBuilder.FaixaCustomizadaInicio - 200000}");

            _entriesEmUso = new HashSet<int>(
                entries.Select(l => int.TryParse(l.Trim(), out var v) ? v : -1).Where(v => v > 0));

            Saida.Append($"[ok] {_entriesEmUso.Count} IDs já usados na faixa alta");
        }
        catch (Exception ex)
        {
            Saida.Append($"[aviso] não consegui ler o banco: {ex.Message}", OutputKind.Error);
        }
        finally
        {
            _ocupado = false;
        }

        Atualizar();
    }

    /// <summary>
    /// Roda um SELECT pelo gm-items.ps1 e devolve as linhas, sem o cabeçalho.
    /// Esse script só executa SELECT, e é por isso que ele serve aqui.
    /// </summary>
    private async Task<List<string>> ConsultarAsync(string sql)
    {
        var linhas = new List<string>();
        void Coletar(OutputLine l)
        {
            if (l.Kind == OutputKind.Normal && l.Text.Length > 0) linhas.Add(l.Text);
        }

        _runner.Output += Coletar;
        try { await _runner.RunAsync("gm-items.ps1", new[] { "-Query", sql }); }
        finally { _runner.Output -= Coletar; }

        // A primeira linha é o cabeçalho do cliente do MySQL.
        return linhas.Count > 1 ? linhas.Skip(1).ToList() : new List<string>();
    }

    private HashSet<int>? LerItemDbc()
    {
        try
        {
            return ClientItemCheck.ReadClientItemIds(
                Path.Combine(Session.Current.Loaded?.ServerDir ?? "", "Data"));
        }
        catch { return null; }
    }

    // ---------------------------------------------------------- montar item --

    private static int Numero(TextBox caixa, int padrao = 0) =>
        int.TryParse(caixa.Text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? v : padrao;

    private static double Decimal(TextBox caixa)
    {
        var t = (caixa.Text ?? "").Trim().Replace(',', '.');
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    private static int DoCombo(ComboBox combo, IReadOnlyList<int> codigos) =>
        combo.SelectedIndex >= 0 && combo.SelectedIndex < codigos.Count ? codigos[combo.SelectedIndex] : 0;

    /// <summary>
    /// Lê 'coluna = valor' do campo livre. Linha vazia e comentário são
    /// ignorados; o resto vira par, e a validação decide se serve.
    /// </summary>
    private Dictionary<string, string> LerExtras()
    {
        var extras = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var bruta in (TxtExtra.Text ?? "").Split('\n'))
        {
            var linha = bruta.Trim();
            if (linha.Length == 0 || linha.StartsWith("--", StringComparison.Ordinal)) continue;

            var igual = linha.IndexOf('=');
            if (igual <= 0) continue;

            extras[linha[..igual].Trim()] = linha[(igual + 1)..].Trim();
        }

        return extras;
    }

    private ItemDefinition Montar()
    {
        var (classe, subclasse) = TipoEscolhido();

        return new ItemDefinition
        {
            Entry = Numero(TxtEntry),
            Name = TxtNome.Text ?? "",
            Class = classe,
            Subclass = subclasse,
            Quality = DoCombo(ComboQualidade, _codigosQualidade),
            DisplayId = _displayIdEscolhido,
            InventoryType = DoCombo(ComboSlot, _codigosSlot),
            ItemLevel = Numero(TxtItemLevel),
            RequiredLevel = Numero(TxtReqLevel),
            Bonding = DoCombo(ComboVinculo, _codigosVinculo),
            Armor = Numero(TxtArmadura),
            DmgMin = Decimal(TxtDanoMin),
            DmgMax = Decimal(TxtDanoMax),
            Delay = Numero(TxtDelay),
            MaxDurability = Numero(TxtDurabilidade),
            Stackable = Math.Max(1, Numero(TxtPilha, 1)),
            Description = TxtDescricao.Text ?? "",
            Stats = _atributos
                .Where(a => a.Codigo > 0 && a.ValorNumero != 0)
                .Select(a => new ItemStat(a.Codigo, a.ValorNumero))
                .ToList(),
            Spells = _efeitos
                .Where(e => e.SpellNumero > 0)
                .Select(e => new ItemSpell(e.SpellNumero, e.GatilhoCodigo, 0, e.CooldownNumero))
                .ToList(),
            Extra = LerExtras(),
        };
    }

    /// <summary>Categoria e subcategoria do atalho "Para que serve".</summary>
    private (int Classe, int Subclasse) TipoEscolhido() => ComboTipo.SelectedIndex switch
    {
        0 => (4, 1),    // armadura / tecido
        1 => (2, 7),    // arma / espada de uma mão
        2 => (0, 8),    // consumível / outro — usável, não gasta
        3 => (0, 0),    // consumível / poção
        4 => (7, 0),    // material de profissão
        5 => (12, 0),   // item de missão
        _ => (15, 0),   // diversos
    };

    // ------------------------------------------------------------ atualizar --

    // Tres nomes diferentes de proposito. TextChangedEventArgs e
    // SelectionChangedEventArgs herdam de RoutedEventArgs, entao tres
    // sobrecargas com o mesmo nome deixariam a escolha para a resolucao de
    // sobrecarga no codigo gerado pelo XAML - risco desnecessario.
    private void Campo_Changed(object sender, RoutedEventArgs e) => Atualizar();
    private void Texto_Changed(object sender, TextChangedEventArgs e) => Atualizar();
    private void Selecao_Changed(object sender, SelectionChangedEventArgs e) => Atualizar();

    private void Tipo_Changed(object sender, SelectionChangedEventArgs e)
    {
        // Item que não é equipamento não tem slot; deixar um slot escolhido ali
        // faria o jogo tentar vesti-lo.
        if (ComboSlot is not null && ComboTipo.SelectedIndex >= 2)
            ComboSlot.SelectedIndex = Math.Max(0, _codigosSlot.IndexOf(0));

        Atualizar();
    }

    private void Atualizar()
    {
        // TextChanged dispara durante o InitializeComponent, antes de os
        // controles declarados depois existirem.
        if (PainelBalao is null || ListaProblemas is null || TxtExtra is null) return;

        var def = Montar();

        PainelBalao.Children.Clear();
        foreach (var linha in ItemTooltip.Build(def.ToRow(), _idsDoCliente))
        {
            PainelBalao.Children.Add(new TextBlock
            {
                Text = linha.Text,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 2),
                Foreground = Pincel(linha.Color),
            });
        }

        var problemas = ItemBuilder.Validate(def, _entriesEmUso, _idsDoCliente)
            .Select(p => new ProblemaLinha
            {
                Texto = (p.Level == IssueLevel.Erro ? "✕ " : "! ") + p.Message,
                Cor = (Brush)Application.Current.Resources[p.Level == IssueLevel.Erro ? "Err" : "Warn"],
            })
            .ToList();

        if (_colunas.Count == 0)
        {
            problemas.Add(new ProblemaLinha
            {
                Texto = "! Ainda não li as colunas do item_template. Sem isso não dá para "
                        + "montar o comando com segurança — confira que o MySQL está no ar.",
                Cor = (Brush)Application.Current.Resources["Warn"],
            });
        }

        if (problemas.Count == 0)
        {
            problemas.Add(new ProblemaLinha
            {
                Texto = "✓ Nada a corrigir.",
                Cor = (Brush)Application.Current.Resources["Ok"],
            });
        }

        ListaProblemas.ItemsSource = problemas;
    }

    private static Brush Pincel(string hex)
    {
        try { return (Brush)new BrushConverter().ConvertFromString(hex)!; }
        catch { return Brushes.White; }
    }

    // ---------------------------------------------------------------- ícones --

    private void CarregarIcones()
    {
        if (_icones.Count > 0) return;

        var pastaIcones = Path.Combine(Session.Current.Loaded?.ServerDir ?? "", "Data", "icons");
        var dbc = Path.Combine(Session.Current.Loaded?.ServerDir ?? "", "Data", "dbc", "ItemDisplayInfo.dbc");

        if (!Directory.Exists(pastaIcones) || !File.Exists(dbc))
        {
            Saida.Append("[aviso] não achei os ícones extraídos. Rode 'Extrair ícones' na aba Itens.");
            return;
        }

        try
        {
            // displayid -> nome do ícone. Fica só o primeiro displayid de cada
            // nome: são muitos apontando para o mesmo desenho, e a lista tem
            // que caber na tela.
            var nomes = IconExtractor.ReadIconNames(dbc);
            var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var lista = new List<IconeLinha>();

            foreach (var (displayId, nome) in nomes.OrderBy(p => p.Key))
            {
                if (displayId >= ClientItemCheck.DisplayIdMaximo) continue;   // o client não resolve
                if (!vistos.Add(nome)) continue;

                var png = Path.Combine(pastaIcones, nome + ".png");
                if (!File.Exists(png)) continue;

                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(png);
                    bmp.EndInit();
                    bmp.Freeze();

                    lista.Add(new IconeLinha { DisplayId = (int)displayId, Nome = nome, Imagem = bmp });
                }
                catch { /* PNG corrompido: pula, não derruba a lista */ }
            }

            _icones = lista;
            Saida.Append($"[ok] {_icones.Count} ícones disponíveis");
        }
        catch (Exception ex)
        {
            Saida.Append($"[aviso] não consegui ler os ícones: {ex.Message}", OutputKind.Error);
        }
    }

    private void FiltroIcone_Changed(object sender, TextChangedEventArgs e)
    {
        if (ListaIcones is null) return;

        CarregarIcones();

        var termo = (CaixaIcone.Text ?? "").Trim();
        IEnumerable<IconeLinha> visiveis = _icones;

        if (termo.Length > 0)
            visiveis = visiveis.Where(i => i.Nome.Contains(termo, StringComparison.OrdinalIgnoreCase));

        // Teto de 300: a grade inteira são milhares de imagens, e desenhar
        // todas trava a tela sem ajudar ninguém.
        ListaIcones.ItemsSource = visiveis.Take(300).ToList();
    }

    private void Icone_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement el || el.Tag is not int displayId) return;

        _displayIdEscolhido = displayId;

        var escolhido = _icones.FirstOrDefault(i => i.DisplayId == displayId);
        TxtIconeEscolhido.Text = escolhido is null ? $"displayid {displayId}" : escolhido.Nome;

        foreach (var i in _icones)
            i.CorBorda = i.DisplayId == displayId
                ? (Brush)Application.Current.Resources["Accent"]
                : Brushes.Transparent;

        FiltroIcone_Changed(this, null!);
        Atualizar();
    }

    // --------------------------------------------------------------- magias --

    private void Magia_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) ProcurarMagia_Click(sender, new RoutedEventArgs());
    }

    private void ProcurarMagia_Click(object sender, RoutedEventArgs e)
    {
        if (_magias is null)
        {
            _magias = SpellDbc.Read(Path.Combine(Session.Current.Loaded?.ServerDir ?? "", "Data"));

            if (_magias is null)
            {
                Saida.Append("[aviso] não consegui ler o Spell.dbc do client.");
                Saida.Append("    ele sai na etapa 5 da instalação, junto com os outros .dbc.");
                Saida.Append("    dá para digitar o ID da magia à mão nas linhas de efeito.");
                return;
            }

            Saida.Append($"[ok] {_magias.Count} magias no Spell.dbc");
        }

        var achadas = SpellDbc.Search(_magias, CaixaMagia.Text ?? "");
        ListaMagias.ItemsSource = achadas
            .Select(s => new { s.Id, Nome = s.Name })
            .ToList();

        if (achadas.Count == 0) Saida.Append("nenhuma magia com esse nome ou ID");
    }

    private void Magia_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement el || el.Tag is not int id) return;

        // Preenche a primeira linha de efeito vazia; se todas estiverem cheias,
        // sobrescrever calado seria pior que avisar.
        var livre = _efeitos.FirstOrDefault(x => x.SpellNumero <= 0);
        if (livre is null)
        {
            MessageBox.Show(
                "As cinco linhas de efeito já estão preenchidas. Apague o ID de uma delas "
                + "para liberar espaço — o item_template guarda no máximo cinco.",
                "Sem espaço");
            return;
        }

        livre.SpellId = id.ToString(CultureInfo.InvariantCulture);
        Atualizar();
    }

    // ------------------------------------------------------------------ SQL --

    /// <summary>
    /// Monta o SQL, ou devolve null quando há erro — a lista de conferência já
    /// mostra o motivo.
    /// </summary>
    private string? MontarSql()
    {
        var def = Montar();

        var erros = ItemBuilder.Validate(def, _entriesEmUso, _idsDoCliente)
            .Where(p => p.Level == IssueLevel.Erro)
            .ToList();

        if (erros.Count > 0)
        {
            MessageBox.Show(
                "Corrija antes de continuar:\n\n" + string.Join("\n", erros.Select(x => "• " + x.Message)),
                "Faltou alguma coisa");
            return null;
        }

        if (_colunas.Count == 0)
        {
            MessageBox.Show(
                "Ainda não consegui ler as colunas do item_template do seu banco.\n\n"
                + "Sem essa lista eu montaria o comando às cegas, e uma coluna que não "
                + "existe aqui derrubaria o INSERT inteiro. Confira se o MySQL está no ar "
                + "e volte para esta aba.",
                "Preciso ler o banco primeiro");
            return null;
        }

        var sql = ItemBuilder.BuildSql(def, _colunas, out var ignoradas);

        if (ignoradas.Count > 0)
        {
            Saida.Append("[aviso] estas colunas não existem no seu banco e ficaram de fora: "
                         + string.Join(", ", ignoradas));
        }

        return sql;
    }

    private void VerSql_Click(object sender, RoutedEventArgs e)
    {
        var sql = MontarSql();
        if (sql is null) return;

        Saida.Append("==> SQL gerado");
        foreach (var linha in sql.Split('\n')) Saida.Append(linha.TrimEnd());
    }

    private void SalvarSql_Click(object sender, RoutedEventArgs e)
    {
        var sql = MontarSql();
        if (sql is null) return;

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Arquivo SQL (*.sql)|*.sql",
            FileName = $"item-{Numero(TxtEntry)}.sql",
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            ConfFile.Save(dlg.FileName, sql);   // sem BOM, que o cliente do MySQL não engole
            Saida.Append($"[ok] salvo em {dlg.FileName}");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Não consegui salvar: " + ex.Message, "Erro");
        }
    }

    private async void Aplicar_Click(object sender, RoutedEventArgs e)
    {
        var sql = MontarSql();
        if (sql is null) return;

        if (!_runner.ScriptExists("apply-sql.ps1"))
        {
            Saida.Append("[erro] scripts\\apply-sql.ps1 não encontrado — atualize o repositório",
                         OutputKind.Error);
            return;
        }

        // Arquivo temporário: o SQL tem quebras de linha e aspas, e passar isso
        // por linha de comando é justamente onde as coisas quebram.
        var arquivo = Path.Combine(Path.GetTempPath(), $"item-{Numero(TxtEntry)}.sql");

        try
        {
            ConfFile.Save(arquivo, sql);

            Saida.Append("==> o que seria gravado");
            var previa = await _runner.RunAsync("apply-sql.ps1", new[] { "-File", arquivo });
            if (previa != 0)
            {
                MessageBox.Show("O script recusou — o motivo está no console.", "Não dá para aplicar");
                return;
            }

            var r = MessageBox.Show(
                $"Gravar o item {Numero(TxtEntry)} — \"{TxtNome.Text}\" — no banco?\n\n"
                + "O banco do mundo é copiado antes, e o comando roda numa transação: "
                + "se falhar no meio, nada fica pela metade.\n\n"
                + "O SQL apaga o item com esse ID antes de inserir, então dá para "
                + "aplicar de novo depois de ajustar.",
                "Criar item", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;

            var codigo = await _runner.RunAsync("apply-sql.ps1", new[] { "-File", arquivo, "-Apply" });

            if (codigo == 0)
            {
                Saida.Append("[ok] item criado");
                Saida.Append("    para o servidor enxergar sem reiniciar, mande no console do");
                Saida.Append("    worldserver:  reload item_template");
                _entriesEmUso?.Add(Numero(TxtEntry));
                Atualizar();
            }
        }
        catch (Exception ex)
        {
            Saida.Append($"[erro] {ex.Message}", OutputKind.Error);
        }
        finally
        {
            try { File.Delete(arquivo); } catch { /* some sozinho no temp */ }
        }
    }

    private void SugerirId_Click(object sender, RoutedEventArgs e)
    {
        if (_entriesEmUso is null)
        {
            MessageBox.Show(
                "Ainda não li os IDs em uso do banco. Confira se o MySQL está no ar "
                + "e volte para esta aba.",
                "Preciso ler o banco primeiro");
            return;
        }

        TxtEntry.Text = ItemBuilder.NextFreeEntry(_entriesEmUso).ToString(CultureInfo.InvariantCulture);
        Atualizar();
    }
}
