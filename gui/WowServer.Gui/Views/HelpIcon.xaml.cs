using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WowServer.Core;

namespace WowServer.Gui.Views;

/// <summary>
/// O "?" que fica ao lado de um campo. Ao apontar, mostra o que o campo faz e
/// exemplos de valores que servem, cada um explicado.
///
/// O texto nao mora aqui: vem do catalogo <see cref="FieldHelp"/>, no projeto
/// Core, que os testes conseguem conferir. Aqui so se monta o balao.
/// </summary>
public partial class HelpIcon : UserControl
{
    /// <summary>
    /// Chave da entrada em <see cref="FieldHelp"/>.
    ///
    /// DependencyProperty porque e definida no XAML de cada tela, e o balao
    /// precisa ser remontado quando ela chega - o valor do XAML e aplicado
    /// depois do construtor.
    /// </summary>
    public static readonly DependencyProperty ChaveProperty =
        DependencyProperty.Register(
            nameof(Chave), typeof(string), typeof(HelpIcon),
            new PropertyMetadata(string.Empty, AoTrocarChave));

    public string Chave
    {
        get => (string)GetValue(ChaveProperty);
        set => SetValue(ChaveProperty, value);
    }

    /// <summary>
    /// Ajuda passada direto, em vez de buscada por chave.
    ///
    /// Serve para listas montadas em tempo de execucao - os ajustes do
    /// worldserver.conf, que sao dezenas e trazem a propria explicacao. Repetir
    /// esses textos no catalogo de chaves fixas seria duplicar conteudo que ja
    /// existe, e daria duas versoes para desencontrar.
    /// </summary>
    public static readonly DependencyProperty EntradaProperty =
        DependencyProperty.Register(
            nameof(Entrada), typeof(FieldHelpEntry), typeof(HelpIcon),
            new PropertyMetadata(null, AoTrocarChave));

    public FieldHelpEntry? Entrada
    {
        get => (FieldHelpEntry?)GetValue(EntradaProperty);
        set => SetValue(EntradaProperty, value);
    }

    public HelpIcon() => InitializeComponent();

    private static void AoTrocarChave(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HelpIcon icone) icone.Montar();
    }

    private void Montar()
    {
        // O setter pode rodar antes do InitializeComponent quando alguem cria o
        // controle em codigo; sair calado e melhor do que derrubar a janela.
        if (Bolha is null) return;

        // Entrada direta manda; a chave e o caminho das telas fixas.
        var entrada = Entrada ?? FieldHelp.Find(Chave);
        if (entrada is null)
        {
            // Chave errada e erro de programacao, nao do usuario. Fica visivel
            // aqui em vez de virar um balao vazio e silencioso.
            Bolha.ToolTip = $"(sem ajuda cadastrada para '{Chave}')";
            return;
        }

        Bolha.ToolTip = MontarBalao(entrada);
    }

    private static FrameworkElement MontarBalao(FieldHelpEntry entrada)
    {
        // Application.Current e null no designer do Visual Studio. Sem as cores
        // do tema o balao fica feio, mas continua legivel - melhor do que a
        // tela de design quebrar com NullReferenceException.
        var res = Application.Current?.Resources;
        Brush Cor(string chave, Brush padrao) =>
            res is not null && res[chave] is Brush b ? b : padrao;

        var pilha = new StackPanel { MaxWidth = 420 };

        pilha.Children.Add(new TextBlock
        {
            Text = entrada.Title,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6),
        });

        pilha.Children.Add(new TextBlock
        {
            Text = entrada.Description,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Cor("Text", Brushes.White),
        });

        if (entrada.Examples.Count > 0)
        {
            pilha.Children.Add(new TextBlock
            {
                Text = "Exemplos",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = Cor("TextDim", Brushes.Gray),
                Margin = new Thickness(0, 10, 0, 4),
            });

            foreach (var ex in entrada.Examples)
            {
                var linha = new StackPanel { Margin = new Thickness(0, 0, 0, 5) };

                linha.Children.Add(new TextBlock
                {
                    Text = ex.Value,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Cor("Accent", Brushes.SkyBlue),
                });

                linha.Children.Add(new TextBlock
                {
                    Text = ex.Meaning,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Cor("TextDim", Brushes.Gray),
                    Margin = new Thickness(10, 1, 0, 0),
                });

                pilha.Children.Add(linha);
            }
        }

        if (entrada.Warning is not null)
        {
            pilha.Children.Add(new TextBlock
            {
                Text = entrada.Warning,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Cor("Warn", Brushes.Orange),
                Margin = new Thickness(0, 8, 0, 0),
            });
        }

        return new Border
        {
            Background = Cor("BgPanel", Brushes.Black),
            BorderBrush = Cor("Border", Brushes.Gray),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 10, 12, 10),
            Child = pilha,
        };
    }
}
