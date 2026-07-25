using System.Windows;
using System.Windows.Controls;
using WowServer.Core;

namespace WowServer.Gui.Views;

public partial class ServerView : UserControl
{
    private ServerController? _servidor;
    private LogTailer? _authLog;
    private bool _ladoALado = true;

    public ServerView()
    {
        InitializeComponent();

        PainelAuth.Title = "authserver — login";
        PainelWorld.Title = "worldserver — mundo";
        PainelWorld.AcceptsCommands = true;
        PainelWorld.CommandSubmitted += EnviarComando;

        LadoALado_Click(this, new RoutedEventArgs());
    }

    public void LoadFrom(UiState estado)
    {
        PainelAuth.WrapEnabled = estado.ConsoleWrap;
        PainelWorld.WrapEnabled = estado.ConsoleWrap;

        if (estado.ServerSideBySide) LadoALado_Click(this, new RoutedEventArgs());
        else Empilhado_Click(this, new RoutedEventArgs());
    }

    public void SaveTo(UiState estado)
    {
        estado.ConsoleWrap = PainelWorld.WrapEnabled;
        estado.ServerSideBySide = _ladoALado;
    }

    private void EnviarComando(string comando)
    {
        try
        {
            _servidor?.SendWorldCommand(comando);
        }
        catch (Exception ex)
        {
            PainelWorld.Append($"[erro] {ex.Message}", OutputKind.Error);
        }
    }

    private async void Iniciar_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var cfg = Session.Current.Loaded ?? await Session.Current.Settings.LoadAsync();
            Session.Current.Loaded = cfg;

            _servidor = new ServerController(cfg.ServerDir);
            Session.Current.Server = _servidor;

            _servidor.Output += AoReceberSaida;
            _servidor.Exited += AoEncerrar;

            PainelAuth.SetState("iniciando", true);
            _servidor.StartAuth();
            AcompanharLogDoAuth(cfg);

            // O worldserver so consegue registrar o realm depois que o
            // authserver esta ouvindo; um respiro evita erro de conexao no log.
            await Task.Delay(2000);

            PainelWorld.SetState("iniciando", true);
            _servidor.StartWorld();

            BtnIniciar.IsEnabled = false;
            BtnParar.IsEnabled = true;
            BtnMatar.IsEnabled = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Não consegui iniciar");
            PainelWorld.Append($"[erro] {ex.Message}", OutputKind.Error);
        }
    }

    /// <summary>
    /// O authserver escreve pouco e a saida fica presa no buffer do pipe (ver
    /// LogTailer). O painel dele e alimentado pelo Auth.log, que nao sofre
    /// disso e ainda traz o historico completo da sessao.
    /// </summary>
    private void AcompanharLogDoAuth(ServerSettings cfg)
    {
        _authLog?.Dispose();

        var caminho = System.IO.Path.Combine(cfg.ServerDir, "logs", "Auth.log");

        _authLog = new LogTailer(caminho);
        _authLog.Line += linha => Dispatcher.Invoke(() =>
        {
            PainelAuth.Append(linha);
            PainelAuth.SetState("rodando", true);
        });
        _authLog.Start(fromStart: true);

        PainelAuth.Append($"[lendo {caminho}]");
    }

    private void AoReceberSaida(ServerOutput saida) => Dispatcher.Invoke(() =>
    {
        var painel = saida.Role == ServerRole.Auth ? PainelAuth : PainelWorld;
        painel.Append(saida.Text, saida.Kind);

        // 'AC>' e o prompt do worldserver: so entao ele terminou de carregar.
        if (saida.Role == ServerRole.World && saida.Text.Contains("AC>", StringComparison.Ordinal))
            PainelWorld.SetState("pronto", true);
    });

    private void AoEncerrar(ServerRole papel) => Dispatcher.Invoke(() =>
    {
        var painel = papel == ServerRole.Auth ? PainelAuth : PainelWorld;
        painel.SetState("encerrado", false);

        if (_servidor is { AuthRunning: false, WorldRunning: false })
        {
            BtnIniciar.IsEnabled = true;
            BtnParar.IsEnabled = false;
            BtnMatar.IsEnabled = false;
        }
    });

    private void Parar_Click(object sender, RoutedEventArgs e)
    {
        PainelWorld.Append("> desligando (salvando personagens)");
        PainelAuth.Append("> encerrando o authserver");

        _servidor?.StopAll();

        // Evita clique repetido enquanto o desligamento acontece; os botoes
        // voltam ao normal quando os dois processos avisarem que sairam.
        BtnParar.IsEnabled = false;
    }

    private void Matar_Click(object sender, RoutedEventArgs e)
    {
        var r = MessageBox.Show(
            "Forçar encerramento mata o processo na hora.\n\n"
            + "O que ainda não foi gravado no banco se perde — inclusive progresso "
            + "recente dos personagens.\n\nContinuar?",
            "Forçar encerramento", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (r == MessageBoxResult.Yes) _servidor?.KillAll();
    }

    private void LadoALado_Click(object sender, RoutedEventArgs e)
    {
        _ladoALado = true;
        ColSep.Width = new GridLength(10);
        Col1.Width = new GridLength(1, GridUnitType.Star);
        LinSep.Height = new GridLength(0);
        Lin1.Height = new GridLength(0);

        Grid.SetRow(PainelWorld, 0);
        Grid.SetColumn(PainelWorld, 2);
    }

    private void Empilhado_Click(object sender, RoutedEventArgs e)
    {
        _ladoALado = false;
        ColSep.Width = new GridLength(0);
        Col1.Width = new GridLength(0);
        LinSep.Height = new GridLength(10);
        Lin1.Height = new GridLength(1, GridUnitType.Star);

        Grid.SetRow(PainelWorld, 2);
        Grid.SetColumn(PainelWorld, 0);
    }
}
