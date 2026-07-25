using System.Windows;
using System.Windows.Threading;

namespace WowServer.Gui;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Uma excecao nao tratada fecharia a janela sem explicar nada. Melhor
        // mostrar o erro e seguir vivo - quase tudo aqui e recuperavel.
        DispatcherUnhandledException += OnUnhandled;
        base.OnStartup(e);
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            e.Exception.Message,
            "Algo deu errado",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        e.Handled = true;
    }
}
