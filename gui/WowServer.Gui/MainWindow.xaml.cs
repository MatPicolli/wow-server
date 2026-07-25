using System.Windows;

namespace WowServer.Gui;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

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

        base.OnClosing(e);
    }
}
