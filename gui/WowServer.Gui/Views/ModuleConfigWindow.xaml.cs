using System.ComponentModel;
using System.IO;
using System.Windows;
using WowServer.Core;

namespace WowServer.Gui.Views;

/// <summary>Uma chave editavel do .conf.</summary>
public sealed class ConfLinha : INotifyPropertyChanged
{
    private string _valor;

    public ConfLinha(ConfEntry entrada)
    {
        Chave = entrada.Key;
        Original = entrada.Unquoted;
        _valor = entrada.Unquoted;
        Comentario = entrada.Comment;
    }

    public string Chave { get; }
    public string Original { get; }
    public string Comentario { get; }

    public string Valor
    {
        get => _valor;
        set
        {
            if (_valor == value) return;
            _valor = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Valor)));
        }
    }

    public bool Mudou => Valor != Original;

    /// <summary>
    /// O comentario que o proprio arquivo traz acima da chave. Os .conf do
    /// AzerothCore sao quase todos explicacao — e a melhor ajuda possivel aqui,
    /// porque veio junto com a versao instalada do modulo.
    /// </summary>
    public string Explicacao =>
        Comentario.Length > 0 ? Comentario : "(sem explicação no arquivo)";

    public string TextoBusca => $"{Chave} {Comentario}";

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Editor do .conf de um modulo.
///
/// <para>
/// Edita o arquivo no lugar, trocando so o valor de cada chave: os .conf sao
/// quase todos comentario explicando cada opcao, e regerar o arquivo jogaria
/// isso fora. Mesma razao pela qual o Psd1Editor existe do lado das
/// configuracoes.
/// </para>
///
/// <para>
/// Edita o <c>.conf</c>, nunca o <c>.conf.dist</c> ao lado: o <c>.dist</c> e o
/// modelo que o deploy copia, e mexer nele nao muda nada no servidor.
/// </para>
/// </summary>
public partial class ModuleConfigWindow : Window
{
    private readonly List<ModuleConfFile> _arquivos;
    private List<ConfLinha> _linhas = new();
    private string _texto = string.Empty;
    private ModuleConfFile? _atual;

    public ModuleConfigWindow(string moduloDisplayName, IReadOnlyList<ModuleConfFile> arquivos)
    {
        InitializeComponent();

        _arquivos = arquivos.ToList();
        Title = $"Configurações — {moduloDisplayName}";
        TxtTitulo.Text = moduloDisplayName;

        // Um modulo pode gerar mais de um .conf (o Playerbots gera dois).
        // Com um so, o seletor nao aparece.
        if (_arquivos.Count > 1)
        {
            LinhaArquivo.Visibility = Visibility.Visible;
            ComboArquivo.ItemsSource = _arquivos.Select(a => a.FileName).ToList();
            ComboArquivo.SelectedIndex = 0;   // dispara Arquivo_Changed, que carrega
        }
        else
        {
            Carregar(_arquivos.FirstOrDefault());
        }
    }

    private void Arquivo_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var i = ComboArquivo.SelectedIndex;
        if (i >= 0 && i < _arquivos.Count) Carregar(_arquivos[i]);
    }

    private void Carregar(ModuleConfFile? arquivo)
    {
        _atual = arquivo;
        _linhas = new List<ConfLinha>();

        if (arquivo is null || !File.Exists(arquivo.FullPath))
        {
            TxtCaminho.Text = "Não encontrei o arquivo de configuração deste módulo.";
            Aplicar();
            return;
        }

        try
        {
            _texto = File.ReadAllText(arquivo.FullPath);
            foreach (var entrada in ConfFile.Parse(_texto))
                _linhas.Add(new ConfLinha(entrada));

            TxtCaminho.Text = $"{arquivo.FullPath}  ·  {_linhas.Count} opções";
        }
        catch (Exception ex)
        {
            TxtCaminho.Text = "Não consegui ler o arquivo: " + ex.Message;
        }

        Aplicar();
    }

    private void Filtro_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e) => Aplicar();

    private void Aplicar()
    {
        // TextChanged dispara durante o InitializeComponent, antes de a lista existir.
        if (Lista is null || CaixaFiltro is null) return;

        var termo = CaixaFiltro.Text.Trim();
        IEnumerable<ConfLinha> visiveis = _linhas;

        if (termo.Length > 0)
        {
            visiveis = visiveis.Where(
                l => l.TextoBusca.Contains(termo, StringComparison.OrdinalIgnoreCase));
        }

        Lista.ItemsSource = visiveis.ToList();

        TxtRodape.Text = _atual is null
            ? ""
            : "As mudanças valem quando o servidor for reiniciado. O arquivo original "
              + "é copiado antes de gravar.";
    }

    private void Salvar_Click(object sender, RoutedEventArgs e)
    {
        if (_atual is null) { Close(); return; }

        var mudadas = _linhas.Where(l => l.Mudou).ToList();
        if (mudadas.Count == 0)
        {
            MessageBox.Show("Nenhum valor foi alterado.", "Nada a salvar");
            return;
        }

        var resumo = string.Join("\n", mudadas.Take(12)
            .Select(l => $"  {l.Chave}:  {l.Original}  ->  {l.Valor}"));
        if (mudadas.Count > 12) resumo += $"\n  ... e mais {mudadas.Count - 12}";

        var r = MessageBox.Show(
            $"Gravar {mudadas.Count} alteração(ões) em {_atual.FileName}?\n\n{resumo}\n\n"
            + "O arquivo original é copiado antes. As mudanças só valem quando o "
            + "servidor for reiniciado.",
            "Salvar configuração", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;

        try
        {
            var copia = ConfFile.Backup(_atual.FullPath);

            var novo = ConfFile.SetValues(
                _texto,
                mudadas.Select(l => new KeyValuePair<string, string>(l.Chave, l.Valor)),
                out var naoAchadas);

            ConfFile.Save(_atual.FullPath, novo);

            var msg = $"Gravado em {_atual.FileName}.\n\nCópia do original: {Path.GetFileName(copia)}";

            // Uma chave que o parser leu e o regex nao achou na hora de gravar
            // seria um bug do editor, nao do usuario - por isso ela e mostrada
            // em vez de sumir em silencio.
            if (naoAchadas.Count > 0)
                msg += "\n\nNão consegui gravar: " + string.Join(", ", naoAchadas);

            MessageBox.Show(msg, "Pronto");
            Carregar(_atual);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Não consegui gravar: " + ex.Message, "Erro");
        }
    }

    private void Fechar_Click(object sender, RoutedEventArgs e) => Close();
}
