using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WowServer.Core;

namespace WowServer.Gui.Views;

/// <summary>Uma linha da lista de itens.</summary>
public sealed class ItemLinha : INotifyPropertyChanged
{
    private bool _marcado;

    public ItemLinha(ItemRow dados) => Dados = dados;

    public ItemRow Dados { get; }

    public bool Marcado
    {
        get => _marcado;
        set { _marcado = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Marcado))); }
    }

    public int Entry => Dados.Entry;
    public string Nome => Dados.Name;
    public Brush CorQualidade => Pincel(ItemBrowser.CorDaQualidade(Dados.Quality));

    /// <summary>Primeira letra da categoria, no lugar do ícone.</summary>
    public string Inicial =>
        ItemBrowser.Classes.TryGetValue(Dados.Class, out var c) && c.Length > 0
            ? c[..1].ToUpperInvariant()
            : "?";

    public string Detalhe
    {
        get
        {
            var partes = new List<string> { $"ID {Dados.Entry}" };

            if (ItemBrowser.Classes.TryGetValue(Dados.Class, out var classe)) partes.Add(classe);

            var sub = ItemBrowser.NomeDaSubclasse(Dados.Class, Dados.Subclass);
            if (sub.Length > 0) partes.Add(sub);

            if (ItemBrowser.Qualidades.TryGetValue(Dados.Quality, out var q)) partes.Add(q);

            return string.Join("  ·  ", partes);
        }
    }

    public string NivelTexto =>
        Dados.ItemLevel > 0 ? $"nv {Dados.ItemLevel}" : "";

    public event PropertyChangedEventHandler? PropertyChanged;

    private static Brush Pincel(string hex)
    {
        try { return (Brush)new BrushConverter().ConvertFromString(hex)!; }
        catch { return Brushes.White; }
    }
}

public partial class ItemsView : UserControl
{
    private readonly ScriptRunner _runner = Session.Current.CreateRunner();
    private readonly List<ItemLinha> _itens = new();
    private bool _ocupado;

    public ItemsView()
    {
        InitializeComponent();
        Saida.Title = "Saída";

        _runner.Output += linha =>
            Dispatcher.Invoke(() => Saida.Append(linha.Text, linha.Kind));

        MontarFiltros();
        MostrarBalaoVazio();
    }

    private void MontarFiltros()
    {
        ComboClasse.Items.Add(new ComboBoxItem { Content = "todas", Tag = null });
        foreach (var (id, nome) in ItemBrowser.Classes.OrderBy(x => x.Value))
            ComboClasse.Items.Add(new ComboBoxItem { Content = nome, Tag = id });
        ComboClasse.SelectedIndex = 0;

        ComboQualidade.Items.Add(new ComboBoxItem { Content = "todas", Tag = null });
        foreach (var (id, nome) in ItemBrowser.Qualidades)
            ComboQualidade.Items.Add(new ComboBoxItem { Content = nome, Tag = id });
        ComboQualidade.SelectedIndex = 0;

        AtualizarSubclasses();
    }

    /// <summary>
    /// As subclasses só fazem sentido dentro de uma classe: subclasse 7 é
    /// "Espada" em arma e "Livro" em armadura. Por isso a lista é refeita
    /// quando a categoria muda.
    /// </summary>
    private void AtualizarSubclasses()
    {
        if (ComboSubclasse is null) return;

        ComboSubclasse.Items.Clear();
        ComboSubclasse.Items.Add(new ComboBoxItem { Content = "todos", Tag = null });

        var classe = ValorSelecionado(ComboClasse);
        var mapa = classe switch
        {
            2 => ItemBrowser.ArmasSubclasses,
            4 => ItemBrowser.ArmaduraSubclasses,
            _ => null,
        };

        if (mapa is not null)
        {
            foreach (var (id, nome) in mapa.OrderBy(x => x.Value))
                ComboSubclasse.Items.Add(new ComboBoxItem { Content = nome, Tag = id });
        }

        ComboSubclasse.SelectedIndex = 0;
        ComboSubclasse.IsEnabled = mapa is not null;
    }

    private void Classe_Changed(object sender, SelectionChangedEventArgs e) => AtualizarSubclasses();

    private static int? ValorSelecionado(ComboBox combo) =>
        combo?.SelectedItem is ComboBoxItem { Tag: int v } ? v : null;

    private static int? LerNivel(TextBox caixa) =>
        int.TryParse(caixa.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? v : null;

    private void Nome_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Procurar_Click(sender, e);
    }

    private async void Procurar_Click(object sender, RoutedEventArgs e)
    {
        if (_ocupado) return;

        var cfg = Session.Current.Loaded;
        if (cfg is null)
        {
            try { cfg = Session.Current.Loaded = await Session.Current.Settings.LoadAsync(); }
            catch (Exception ex)
            {
                MessageBox.Show("Não consegui ler as configurações: " + ex.Message, "Erro");
                return;
            }
        }

        var filtro = new ItemFilter
        {
            Name = CaixaNome.Text,
            Class = ValorSelecionado(ComboClasse),
            Subclass = ValorSelecionado(ComboSubclasse),
            Quality = ValorSelecionado(ComboQualidade),
            MinLevel = LerNivel(CaixaNivelMin),
            MaxLevel = LerNivel(CaixaNivelMax),
        };

        var sql = ItemBrowser.BuildQuery(filtro, cfg.MySql.WorldDb);

        _itens.Clear();
        Lista.ItemsSource = null;
        TxtResumo.Text = "procurando...";

        // O script devolve uma linha por item; o parse mora no Core, onde os
        // testes conseguem alcançá-lo.
        var lidos = new List<ItemLinha>();
        void Coletar(OutputLine linha)
        {
            var row = ItemBrowser.ParseRow(linha.Text);
            if (row is not null) lidos.Add(new ItemLinha(row));
        }

        _runner.Output += Coletar;
        _ocupado = true;
        int codigo;
        try
        {
            codigo = await _runner.RunAsync("gm-items.ps1", new[] { "-Query", sql });
        }
        catch (Exception ex)
        {
            Saida.Append($"[erro] {ex.Message}", OutputKind.Error);
            TxtResumo.Text = "";
            return;
        }
        finally
        {
            _runner.Output -= Coletar;
            _ocupado = false;
        }

        if (codigo != 0)
        {
            TxtResumo.Text = "a busca falhou — veja a saída";
            return;
        }

        _itens.AddRange(lidos);
        Lista.ItemsSource = _itens;

        TxtResumo.Text = _itens.Count switch
        {
            0 => "nenhum item encontrado",
            ItemBrowser.LimiteMaximo => $"{_itens.Count} itens (limite atingido — refine a busca)",
            _ => $"{_itens.Count} itens",
        };
    }

    private void MarcarTodos_Click(object sender, RoutedEventArgs e)
    {
        foreach (var i in _itens) i.Marcado = true;
    }

    private void Desmarcar_Click(object sender, RoutedEventArgs e)
    {
        foreach (var i in _itens) i.Marcado = false;
    }

    private IReadOnlyList<ItemLinha> Marcados() => _itens.Where(i => i.Marcado).ToList();

    // ------------------------------------------------------------- balão ---

    private void Item_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: int entry }) return;

        var linha = _itens.FirstOrDefault(i => i.Entry == entry);
        if (linha is null) return;

        MostrarBalao(linha.Dados);
    }

    private void MostrarBalaoVazio()
    {
        PainelBalao.Children.Clear();
        PainelBalao.Children.Add(new TextBlock
        {
            Text = "Passe o mouse sobre um item para ver os detalhes aqui.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextDim"],
        });
    }

    /// <summary>
    /// Desenha o balão de informações, no formato do jogo.
    ///
    /// Não há ícone: os ícones são arquivos BLP dentro dos MPQ do client, e a
    /// instalação extrai só dbc, maps, vmaps e mmaps — Interface\Icons não
    /// entra. Mostrar o ícone exigiria ler MPQ e decodificar BLP, então no
    /// lugar dele vai um quadrado na cor da qualidade.
    ///
    /// Efeitos de magia ("Usar: ...") também ficam de fora: o texto mora no
    /// Spell.dbc, não no banco.
    /// </summary>
    private void MostrarBalao(ItemRow item)
    {
        PainelBalao.Children.Clear();

        foreach (var linha in ItemTooltip.Build(item))
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
    }

    private static Brush Pincel(string hex)
    {
        try { return (Brush)new BrushConverter().ConvertFromString(hex)!; }
        catch { return Brushes.White; }
    }

    // ------------------------------------------------------------- envio ---

    private void Correio_Click(object sender, RoutedEventArgs e)
    {
        var marcados = Marcados();
        if (marcados.Count == 0)
        {
            MessageBox.Show("Marque pelo menos um item.", "Nada marcado");
            return;
        }

        var personagem = CaixaPersonagem.Text.Trim();
        if (personagem.Length == 0)
        {
            MessageBox.Show("Escreva o nome do personagem que vai receber.", "Falta o nome");
            return;
        }

        var servidor = Session.Current.Server;
        if (servidor is null || !servidor.WorldRunning)
        {
            MessageBox.Show(
                "O worldserver precisa estar rodando para enviar o correio.\n\n"
                + "Ligue na aba Servidor e tente de novo.",
                "Servidor desligado");
            return;
        }

        var comandos = MailPlan.Build(marcados.Select(i => i.Entry).ToList(), personagem);

        var r = MessageBox.Show(
            $"Enviar {marcados.Count} item(ns) para '{personagem}', em {comandos.Count} carta(s)?",
            "Confirmar envio", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;

        foreach (var c in comandos)
        {
            servidor.SendWorldCommand(c);
            Saida.Append($"> {c}");
        }

        Saida.Append($"[ok] {comandos.Count} carta(s) enviada(s)");
    }

    private void CopiarAddItem_Click(object sender, RoutedEventArgs e)
    {
        var marcados = Marcados();
        if (marcados.Count == 0)
        {
            MessageBox.Show("Marque pelo menos um item.", "Nada marcado");
            return;
        }

        var comandos = MailPlan.BuildAddItem(marcados.Select(i => i.Entry).ToList());
        var texto = string.Join(Environment.NewLine, comandos);

        try
        {
            Clipboard.SetDataObject(texto, copy: true);
            Saida.Append($"==> {comandos.Count} comando(s) .additem copiado(s)");
            Saida.Append("    cole no chat do jogo, um por vez");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Não consegui copiar: " + ex.Message, "Área de transferência ocupada");
        }
    }
}
