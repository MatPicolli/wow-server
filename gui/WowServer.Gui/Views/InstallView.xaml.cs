using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WowServer.Core;

namespace WowServer.Gui.Views;

public sealed class EtapaItem : INotifyPropertyChanged
{
    private string _marcador = "○";
    private Brush _corMarcador = Brushes.Gray;

    public required string Id { get; init; }
    public required string Titulo { get; init; }
    public required string Descricao { get; init; }
    public required string Duracao { get; init; }
    public required bool RequerAdmin { get; init; }

    public string Marcador { get => _marcador; set { _marcador = value; Notificar(); } }
    public Brush CorMarcador { get => _corMarcador; set { _corMarcador = value; Notificar(); } }
    public Visibility VisibilidadeAdmin => RequerAdmin ? Visibility.Visible : Visibility.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notificar([CallerMemberName] string? nome = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nome));
}

public partial class InstallView : UserControl
{
    private readonly ObservableCollection<EtapaItem> _etapas = new();
    private readonly ScriptRunner _runner = Session.Current.CreateRunner();
    private CancellationTokenSource? _cancelamento;
    private bool _ocupado;

    public InstallView()
    {
        InitializeComponent();

        Saida.Title = "Saída da instalação";

        foreach (var s in InstallPlan.Steps)
        {
            _etapas.Add(new EtapaItem
            {
                Id = s.Id,
                Titulo = s.Title,
                Descricao = s.Description,
                Duracao = s.Duration,
                RequerAdmin = s.RequiresAdmin,
            });
        }
        ListaEtapas.ItemsSource = _etapas;

        _runner.Output += AoReceberSaida;
    }

    private void AoReceberSaida(OutputLine linha) =>
        Dispatcher.Invoke(() => Saida.Append(linha.Text, linha.Kind));

    private void Limpar_Click(object sender, RoutedEventArgs e) => Saida.Clear();

    private void Parar_Click(object sender, RoutedEventArgs e)
    {
        _cancelamento?.Cancel();
        Saida.Append("[aviso] interrompido pelo usuário", OutputKind.Error);
    }

    private async void Etapa_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        var passo = InstallPlan.Steps.FirstOrDefault(s => s.Id == id);
        if (passo is null) return;
        await ExecutarAsync(new[] { passo });
    }

    private async void Tudo_Click(object sender, RoutedEventArgs e) =>
        await ExecutarAsync(InstallPlan.Steps);

    private async Task ExecutarAsync(IEnumerable<InstallStep> passos)
    {
        if (_ocupado)
        {
            MessageBox.Show("Já existe uma etapa em andamento.", "Aguarde");
            return;
        }

        if (!Session.Current.RepoValido)
        {
            MessageBox.Show(
                $"Não encontrei os scripts a partir de:\n{Session.Current.RepoRoot}\n\n"
                + "Coloque o executável dentro da pasta do repositório.",
                "Repositório não encontrado");
            return;
        }

        _ocupado = true;
        BtnTudo.IsEnabled = false;
        BtnParar.IsEnabled = true;
        _cancelamento = new CancellationTokenSource();

        var inicioGeral = DateTime.Now;
        var houveFalha = false;

        try
        {
            foreach (var passo in passos)
            {
                var item = _etapas.First(x => x.Id == passo.Id);
                Marcar(item, "◐", "Accent");
                Saida.Append($"==> {passo.Title}");

                var inicioEtapa = DateTime.Now;
                var progresso = new BuildProgressTracker();

                // Compilar e a etapa em que o MSBuild permite medir andamento;
                // nas outras a barra fica indeterminada, so indicando atividade.
                if (passo.Id == "build")
                {
                    var cfg = Session.Current.Loaded;
                    if (cfg is not null)
                        progresso.EstimatedTotal = await Task.Run(
                            () => BuildProgressTracker.EstimateSourceCount(cfg.SourceDir));
                }

                void AoSair(OutputLine linha)
                {
                    progresso.Feed(linha.Text);
                    Dispatcher.Invoke(() => Saida.ShowProgress(
                        passo.Title, progresso.Percent,
                        progresso.Describe(DateTime.Now - inicioEtapa)));
                }

                _runner.Output += AoSair;
                Saida.ShowProgress(passo.Title, null, passo.Duration);

                int codigo;
                try
                {
                    if (passo.RequiresAdmin)
                    {
                        codigo = await RodarElevadoAsync(passo);
                    }
                    else
                    {
                        codigo = await _runner.RunAsync(
                            passo.Script, passo.Arguments, _cancelamento.Token);
                    }
                }
                finally
                {
                    _runner.Output -= AoSair;
                    Saida.HideProgress();
                }

                if (codigo == 0 && progresso.Errors == 0)
                {
                    Marcar(item, "●", "Ok");
                    Saida.Append($"    concluído em {DateTime.Now - inicioEtapa:hh\\:mm\\:ss}");
                }
                else
                {
                    Marcar(item, "✕", "Err");
                    houveFalha = true;
                    Saida.AppendBanner(
                        $"{passo.Title.ToUpperInvariant()} FALHOU"
                        + (progresso.Errors > 0 ? $" — {progresso.Errors} erro(s)" : $" — código {codigo}"),
                        sucesso: false);

                    if (progresso.FirstFailure is not null)
                        Saida.Append("Motivo: " + progresso.FirstFailure, OutputKind.Error);

                    // Com erro de compilador vale caçar a primeira ocorrencia;
                    // sem nenhum, a causa e outra e o conselho atrapalha.
                    if (progresso.Errors > 1)
                        Saida.Append("Os erros seguintes costumam ser consequência do primeiro.");
                    break;   // nao adianta seguir: as etapas dependem umas das outras
                }

                if (_cancelamento.IsCancellationRequested) break;
            }

            if (!houveFalha && !_cancelamento.IsCancellationRequested)
            {
                Saida.AppendBanner(
                    $"TUDO PRONTO em {DateTime.Now - inicioGeral:hh\\:mm\\:ss}", sucesso: true);
                Saida.Append("Próximo passo: aba Servidor, botão Iniciar.");
            }
        }
        catch (Exception ex)
        {
            Saida.HideProgress();
            Saida.AppendBanner($"INTERROMPIDO — {ex.Message}", sucesso: false);
        }
        finally
        {
            _ocupado = false;
            BtnTudo.IsEnabled = true;
            BtnParar.IsEnabled = false;
            Saida.HideProgress();
            _cancelamento?.Dispose();
            _cancelamento = null;
        }
    }

    /// <summary>
    /// A instalacao de dependencias precisa de administrador. Em vez de rodar
    /// a GUI inteira elevada, sobe so este processo com 'runas' - o Windows
    /// pede a confirmacao ao usuario.
    ///
    /// Efeito colateral: processo elevado nao aceita saida redirecionada, entao
    /// esta etapa abre a propria janela e aqui so sabemos o codigo de saida.
    /// </summary>
    private async Task<int> RodarElevadoAsync(InstallStep passo)
    {
        Saida.Append("    esta etapa abre uma janela separada, com privilégio de administrador");
        Saida.Append("    acompanhe o progresso por lá; ao fechar, o resultado aparece aqui");

        var script = System.IO.Path.Combine(_runner.ScriptsDir, passo.Script);
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -NoExit -File \"{script}\"",
            WorkingDirectory = Session.Current.RepoRoot,
            UseShellExecute = true,
            Verb = "runas",
        };

        try
        {
            using var p = Process.Start(psi);
            if (p is null) return -1;
            await p.WaitForExitAsync().ConfigureAwait(true);
            return p.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Saida.Append("[erro] elevação recusada", OutputKind.Error);
            return -1;
        }
    }

    private void Marcar(EtapaItem item, string marcador, string chaveCor)
    {
        item.Marcador = marcador;
        item.CorMarcador = (Brush)Application.Current.Resources[chaveCor];
    }
}
