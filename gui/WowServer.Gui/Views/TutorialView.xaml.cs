using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WowServer.Gui.Views;

public partial class TutorialView : UserControl
{
    private sealed record Passo(string Numero, string Titulo, string Texto);

    private static readonly Passo[] Roteiro =
    {
        new("1", "Aponte para o seu WoW",
            "Abra a engrenagem (Configurações) e indique a pasta do client — a que contém o Wow.exe. "
            + "É a única informação obrigatória; o resto tem valores que funcionam. "
            + "Aproveite e confira a senha do MySQL."),

        new("2", "Instale as dependências",
            "Na aba Instalação, execute a primeira etapa. Ela abre uma janela separada pedindo "
            + "permissão de administrador e baixa o compilador, o banco de dados e as bibliotecas. "
            + "É a etapa mais longa depois da extração, e só precisa ser feita uma vez."),

        new("3", "Confira antes de gastar tempo",
            "A etapa de Conferência leva segundos e diz exatamente o que ficou faltando. "
            + "Só siga adiante quando ela passar limpa — descobrir uma dependência faltando "
            + "no meio da compilação custa meia hora."),

        new("4", "Compile o servidor",
            "As etapas de Código-fonte e Compilação baixam e constroem o servidor. "
            + "A compilação usa todos os núcleos do processador; o computador vai ficar pesado."),

        new("5", "Prepare o banco de dados",
            "Cria as três bases que o servidor usa. Elas ficam vazias de propósito — "
            + "o próprio servidor as preenche no primeiro start, e isso leva uns 10 minutos."),

        new("6", "Extraia os dados do seu client",
            "Aqui o programa lê os arquivos do seu WoW e gera os mapas que o servidor usa. "
            + "É a etapa mais demorada: pode passar de 6 horas, quase tudo na parte de navegação "
            + "dos monstros. Deixe rodando e vá fazer outra coisa. Se interromper, retoma depois."),

        new("7", "Monte e configure",
            "Duas etapas rápidas: juntar os arquivos na pasta do servidor e gerar as configurações. "
            + "Também é aqui que o seu client passa a apontar para o servidor local."),

        new("8", "Suba o servidor",
            "Na aba Servidor, clique em Iniciar. Aparecem dois painéis: o de login e o do mundo. "
            + "No primeiro start o painel do mundo despeja milhares de linhas aplicando dados — "
            + "é esperado. Espere aparecer o prompt 'AC>'."),

        new("9", "Crie sua conta",
            "Ainda na aba Servidor, use a caixa de comando embaixo do painel do mundo:\n\n"
            + "    account create seunome suasenha\n"
            + "    account set gmlevel seunome 3 -1\n\n"
            + "O segundo comando te dá poderes de administrador dentro do jogo. "
            + "A senha do WoW 3.3.5a ignora maiúsculas e tem limite de 16 caracteres."),

        new("10", "Entre no jogo",
            "Abra o Wow.exe do seu client — não o Launcher, que sobrescreve a configuração de conexão. "
            + "Faça login com a conta que você acabou de criar."),

        new("11", "Depois de estar dentro",
            "Faça um backup pela engrenagem. Em Ajustes você regula quanto vem de cada coleta e a "
            + "chance de itens de quest. Em Módulos dá pra adicionar leilão povoado, escalonamento "
            + "de dungeon e bots que jogam com você."),
    };

    public TutorialView()
    {
        InitializeComponent();

        foreach (var p in Roteiro) Passos.Children.Add(Montar(p));
    }

    private static UIElement Montar(Passo p)
    {
        var numero = new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(15),
            Background = (Brush)Application.Current.Resources["BgPanel"],
            BorderBrush = (Brush)Application.Current.Resources["Accent"],
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 14, 0),
            Child = new TextBlock
            {
                Text = p.Numero,
                Foreground = (Brush)Application.Current.Resources["Accent"],
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

        var corpo = new StackPanel();
        corpo.Children.Add(new TextBlock
        {
            Text = p.Titulo,
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            Margin = new Thickness(0, 4, 0, 6),
        });
        corpo.Children.Add(new TextBlock
        {
            Text = p.Texto,
            Foreground = (Brush)Application.Current.Resources["TextDim"],
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 20,
        });

        var linha = new DockPanel { Margin = new Thickness(0, 0, 0, 22) };
        DockPanel.SetDock(numero, Dock.Left);
        linha.Children.Add(numero);
        linha.Children.Add(corpo);
        return linha;
    }
}
