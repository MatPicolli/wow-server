using System.Windows;
using System.Windows.Controls;
using WowServer.Core;

namespace WowServer.Gui;

public partial class MainWindow : Window
{
    private readonly UiState _estado = UiState.Load();
    private bool _pronta;

    public MainWindow()
    {
        InitializeComponent();
        RestaurarJanela();

        Loaded += (_, _) =>
        {
            TelaAjustes.LoadFrom(_estado);
            TelaServidor.LoadFrom(_estado);

            if (_estado.SelectedTab >= 0 && _estado.SelectedTab < Abas.Items.Count)
                Abas.SelectedIndex = _estado.SelectedTab;

            _pronta = true;
        };

        if (!Session.Current.RepoValido)
        {
            MessageBox.Show(
                "Não encontrei os scripts do servidor a partir de:\n\n"
                + Session.Current.RepoRoot
                + "\n\nO executável precisa estar dentro da pasta do repositório "
                + "(a que tem as pastas 'scripts' e 'config').",
                "Onde estão os scripts?",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void RestaurarJanela()
    {
        if (_estado.WindowWidth > 400) Width = _estado.WindowWidth;
        if (_estado.WindowHeight > 300) Height = _estado.WindowHeight;

        // Uma posicao gravada pode ter vindo de um monitor que nao existe
        // mais; nesse caso deixamos o padrao (centralizado) valer.
        var usavel = _estado.HasUsablePosition(
            SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);

        if (usavel)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = _estado.WindowLeft;
            Top = _estado.WindowTop;
        }
    }

    private void Abas_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_pronta || !ReferenceEquals(e.OriginalSource, Abas)) return;
        _estado.SelectedTab = Abas.SelectedIndex;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        var servidor = Session.Current.Server;
        if (servidor is not null && (servidor.AuthRunning || servidor.WorldRunning))
        {
            var r = MessageBox.Show(
                "O servidor ainda está rodando.\n\n"
                + "Fechar agora encerra os processos e pode perder progresso não salvo "
                + "dos personagens.\n\nFechar mesmo assim?",
                "Servidor em execução",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (r != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            servidor.KillAll();
        }

        SalvarEstado();
        base.OnClosing(e);
    }

    private void SalvarEstado()
    {
        // WindowState maximizado guarda tamanho errado; RestoreBounds tem o
        // tamanho de antes de maximizar, que e o que interessa restaurar.
        var limites = WindowState == WindowState.Normal
            ? new Rect(Left, Top, Width, Height)
            : RestoreBounds;

        _estado.WindowWidth = limites.Width;
        _estado.WindowHeight = limites.Height;
        _estado.WindowLeft = limites.Left;
        _estado.WindowTop = limites.Top;
        _estado.SelectedTab = Abas.SelectedIndex;

        TelaAjustes.SaveTo(_estado);
        TelaServidor.SaveTo(_estado);

        _estado.Save();
    }
}
