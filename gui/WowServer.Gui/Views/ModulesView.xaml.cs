using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WowServer.Core;

namespace WowServer.Gui.Views;

public enum AcaoModulo { Instalar, CorrigirCore, Nenhuma }

public sealed class ModuloItem
{
    public required string Nome { get; init; }
    public required string Resumo { get; init; }
    public required string Detalhes { get; init; }
    public required string Repositorio { get; init; }
    public required string Selo { get; init; }
    public required Brush CorSelo { get; init; }
    public required string TextoBotao { get; init; }
    public required bool PodeInstalar { get; init; }
    public required AcaoModulo Acao { get; init; }
    public string? Alerta { get; init; }
    public Visibility VisibilidadeAlerta => Alerta is null ? Visibility.Collapsed : Visibility.Visible;
}

public partial class ModulesView : UserControl
{
    private readonly ScriptRunner _runner = Session.Current.CreateRunner();

    public ModulesView()
    {
        InitializeComponent();
        Saida.Title = "Saída";

        _runner.Output += linha =>
            Dispatcher.Invoke(() => Saida.Append(linha.Text, linha.Kind));

        Recarregar();

        // A deteccao de core incompativel depende das configuracoes; se esta
        // tela abrir antes da de Configuracoes, elas ainda nao foram lidas.
        Loaded += async (_, _) =>
        {
            if (Session.Current.Loaded is null)
            {
                try
                {
                    Session.Current.Loaded = await Session.Current.Settings.LoadAsync();
                    Recarregar();
                }
                catch (Exception ex)
                {
                    Saida.Append($"[aviso] não consegui ler as configurações: {ex.Message}");
                }
            }
        };
    }

    private string ModulesDir
    {
        get
        {
            var cfg = Session.Current.Loaded;
            var source = cfg?.SourceDir ?? @"C:\AzerothCore\source";
            return Path.Combine(source, "modules");
        }
    }

    private void Recarregar()
    {
        var res = Application.Current.Resources;
        var itens = new List<ModuloItem>();
        var cfg = Session.Current.Loaded;

        foreach (var m in ModuleCatalog.All)
        {
            var instalado = Directory.Exists(Path.Combine(ModulesDir, m.Name));

            // Um modulo de fork instalado sobre o core errado compila contra
            // assinaturas que nao existem e falha com dezenas de erros
            // C2660. Mostrar isso aqui evita uma compilacao inteira perdida.
            var coreErrado = instalado
                && m.Status == ModuleStatus.ExigeFork
                && m.ForkRepository is not null
                && cfg is not null
                && !ModuleCatalog.SatisfiesFork(m, cfg.SourceRepository);

            var (selo, cor) = m.Status switch
            {
                ModuleStatus.Testado => ("testado", (Brush)res["Ok"]),
                ModuleStatus.ExigeFork => ("troca o core", (Brush)res["Warn"]),
                _ => ("oficial", (Brush)res["Accent"]),
            };

            if (instalado) (selo, cor) = ("instalado", (Brush)res["Ok"]);
            if (coreErrado) (selo, cor) = ("core incompatível", (Brush)res["Err"]);

            var acao = coreErrado ? AcaoModulo.CorrigirCore
                     : instalado ? AcaoModulo.Nenhuma
                     : AcaoModulo.Instalar;

            itens.Add(new ModuloItem
            {
                Nome = m.DisplayName,
                Resumo = m.Summary,
                Detalhes = m.Details,
                Repositorio = m.Repository,
                Selo = selo,
                CorSelo = cor,
                Acao = acao,
                PodeInstalar = acao != AcaoModulo.Nenhuma,
                TextoBotao = acao switch
                {
                    AcaoModulo.CorrigirCore => "Corrigir o core",
                    AcaoModulo.Instalar => "Instalar",
                    _ => "Já instalado",
                },
                Alerta = coreErrado
                    ? $"Este módulo está instalado, mas o código do servidor em uso é "
                      + $"'{cfg!.SourceBranch}' de {cfg.SourceRepository}. Ele precisa de "
                      + $"'{m.ForkBranch}' de {m.ForkRepository} — sem isso a compilação falha."
                    : null,
            });
        }

        Lista.ItemsSource = itens;
    }

    private void Abrir_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string url }) return;
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private async void Instalar_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string display }) return;

        var modulo = ModuleCatalog.All.FirstOrDefault(m => m.DisplayName == display);
        if (modulo is null) return;

        // "Corrigir o core": o modulo ja esta na pasta, o que falta e o
        // codigo-fonte certo por baixo dele.
        var jaClonado = Directory.Exists(Path.Combine(ModulesDir, modulo.Name));

        if (modulo.Status == ModuleStatus.ExigeFork)
        {
            var aviso = MessageBox.Show(
                $"{modulo.DisplayName} não é um módulo comum: ele exige substituir o "
                + "código do servidor por uma versão modificada.\n\n"
                + "O que acontece:\n"
                + "  • o código-fonte é apagado e clonado de novo\n"
                + "  • a compilação é refeita (cerca de 20 minutos no total)\n\n"
                + "  • os outros módulos instalados são reinstalados automaticamente,\n"
                + "    porque a pasta de módulos vive dentro do código-fonte\n\n"
                + "O que é preservado:\n"
                + "  • os dados extraídos do seu WoW (as horas de extração NÃO se repetem)\n"
                + "  • seus personagens e contas\n\n"
                + "Faça um backup antes. Continuar?",
                "Isso troca o servidor inteiro", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (aviso != MessageBoxResult.Yes) return;

            // Se a troca do core falhar, parar aqui e essencial: clonar o
            // modulo sobre o core errado compila contra assinaturas que nao
            // existem e enche a tela de erros C2660.
            if (!await TrocarParaForkAsync(modulo)) return;
        }
        else if (jaClonado)
        {
            Saida.Append($"[aviso] {modulo.Name} já está instalado");
            return;
        }

        await ClonarAsync(modulo);
        Recarregar();

        if (modulo.PostInstallNote is not null)
        {
            Saida.Append("");
            Saida.Append("==> depois de instalar:");
            foreach (var linha in modulo.PostInstallNote.Split('\n'))
                Saida.Append("    " + linha.TrimEnd());
        }

        Saida.Append("");
        Saida.Append("==> agora use 'Recompilar' para o módulo entrar no servidor");
    }

    /// <returns>true se o core ficou pronto para receber o modulo.</returns>
    private async Task<bool> TrocarParaForkAsync(CatalogModule modulo)
    {
        if (modulo.ForkRepository is null || modulo.ForkBranch is null) return true;

        // Trocar o core apaga a pasta de fontes inteira, e modules/ mora
        // dentro dela. Anotamos a origem de cada modulo instalado agora para
        // reinstalar depois - senao eles somem sem aviso.
        var instalados = await ListarInstaladosAsync();
        if (instalados.Count > 0)
            Saida.Append($"==> {instalados.Count} módulo(s) serão reinstalados após a troca");

        var cfg = Session.Current.Loaded ?? await Session.Current.Settings.LoadAsync();
        cfg.SourceRepository = modulo.ForkRepository;
        cfg.SourceBranch = modulo.ForkBranch;

        try
        {
            Session.Current.Settings.Save(cfg);
        }
        catch (Exception ex)
        {
            Saida.Append($"[erro] não consegui gravar as configurações: {ex.Message}", OutputKind.Error);
            return false;
        }
        Session.Current.Loaded = cfg;

        Saida.Append($"==> origem trocada para {modulo.ForkBranch} @ {modulo.ForkRepository}");

        var codigo = await _runner.RunAsync("02-clone-source.ps1", new[] { "-Force" });
        if (codigo != 0)
        {
            Saida.Append($"[erro] a troca do core falhou (código {codigo})", OutputKind.Error);
            Saida.Append("       o módulo NÃO foi instalado - sobre o core errado ele não compila", OutputKind.Error);
            return false;
        }

        foreach (var (nome, url) in instalados)
        {
            if (string.Equals(nome, modulo.Name, StringComparison.OrdinalIgnoreCase)) continue;
            Saida.Append($"==> reinstalando {nome}");
            await RodarGitAsync(new[] { "clone", "--recurse-submodules", url, Path.Combine(ModulesDir, nome) });
        }

        return true;
    }

    /// <summary>Nome e URL de origem de cada modulo instalado agora.</summary>
    private async Task<List<(string Nome, string Url)>> ListarInstaladosAsync()
    {
        var lista = new List<(string, string)>();
        if (!Directory.Exists(ModulesDir)) return lista;

        foreach (var dir in Directory.GetDirectories(ModulesDir))
        {
            if (!Directory.Exists(Path.Combine(dir, ".git"))) continue;

            var url = await CapturarGitAsync(new[] { "-C", dir, "remote", "get-url", "origin" });
            if (!string.IsNullOrWhiteSpace(url))
                lista.Add((Path.GetFileName(dir), url.Trim()));
        }
        return lista;
    }

    private static async Task<string> CapturarGitAsync(string[] argumentos)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in argumentos) psi.ArgumentList.Add(a);

        try
        {
            using var p = Process.Start(psi);
            if (p is null) return string.Empty;
            var saida = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            return p.ExitCode == 0 ? saida : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private async Task ClonarAsync(CatalogModule modulo)
    {
        Directory.CreateDirectory(ModulesDir);
        var destino = Path.Combine(ModulesDir, modulo.Name);

        if (Directory.Exists(destino))
        {
            Saida.Append($"[aviso] {modulo.Name} já existe em {destino}");
            return;
        }

        Saida.Append($"==> clonando {modulo.Name}");
        // --recurse-submodules: modulos como o Eluna trazem a engine Lua
        // como submodulo, e sem ele a compilacao falha com "lua.h: No such
        // file or directory".
        await RodarGitAsync(new[] { "clone", "--recurse-submodules", modulo.Repository, destino });
    }

    private async void Atualizar_Click(object sender, RoutedEventArgs e)
    {
        if (!Directory.Exists(ModulesDir))
        {
            Saida.Append("[aviso] nenhum módulo instalado ainda");
            return;
        }

        foreach (var dir in Directory.GetDirectories(ModulesDir))
        {
            if (!Directory.Exists(Path.Combine(dir, ".git"))) continue;
            Saida.Append($"==> atualizando {Path.GetFileName(dir)}");
            await RodarGitAsync(new[] { "-C", dir, "pull", "--ff-only" });
            await RodarGitAsync(new[] { "-C", dir, "submodule", "update", "--init", "--recursive" });
        }
    }

    private async void Rebuild_Click(object sender, RoutedEventArgs e)
    {
        Saida.Append("==> recompilando com os módulos instalados");
        await RecompilarAsync();
    }

    /// <summary>
    /// Roda o rebuild acompanhando o progresso. O MSBuild nao reporta
    /// andamento, entao contamos os arquivos que ele ecoa contra uma
    /// estimativa do total de fontes.
    /// </summary>
    private async Task RecompilarAsync()
    {
        var progresso = new BuildProgressTracker();
        var inicio = DateTime.Now;

        var cfg = Session.Current.Loaded;
        if (cfg is not null)
        {
            progresso.EstimatedTotal = await Task.Run(
                () => BuildProgressTracker.EstimateSourceCount(cfg.SourceDir));

            if (progresso.EstimatedTotal > 0)
                Saida.Append($"    ~{progresso.EstimatedTotal} arquivos a compilar");
        }

        void AoSair(OutputLine linha)
        {
            progresso.Feed(linha.Text);
            Dispatcher.Invoke(() =>
                Saida.ShowProgress("Compilando", progresso.Percent,
                                   progresso.Describe(DateTime.Now - inicio)));
        }

        _runner.Output += AoSair;
        try
        {
            var codigo = await _runner.RunAsync("rebuild.ps1");
            var duracao = DateTime.Now - inicio;

            Saida.HideProgress();

            if (codigo == 0 && progresso.Errors == 0)
            {
                Saida.AppendBanner(
                    $"COMPILAÇÃO CONCLUÍDA em {duracao:hh\\:mm\\:ss} — "
                    + $"{progresso.CompiledFiles} arquivos, {progresso.FinishedProjects} projetos",
                    sucesso: true);
                Saida.Append("Use a aba Servidor para iniciar.");
            }
            else
            {
                Saida.AppendBanner(
                    $"COMPILAÇÃO FALHOU após {duracao:hh\\:mm\\:ss} — {progresso.Errors} erro(s)",
                    sucesso: false);
                Saida.Append("Procure a PRIMEIRA linha com 'error' — as seguintes costumam ser consequência.");
                Saida.Append("Use 'copiar' ou 'salvar...' aqui em cima para levar o log inteiro.");
            }
        }
        catch (Exception ex)
        {
            Saida.HideProgress();
            Saida.AppendBanner($"COMPILAÇÃO INTERROMPIDA — {ex.Message}", sucesso: false);
        }
        finally
        {
            _runner.Output -= AoSair;
        }
    }

    private async Task RodarGitAsync(string[] argumentos)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in argumentos) psi.ArgumentList.Add(a);

        try
        {
            using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            p.OutputDataReceived += (_, ev) =>
            {
                if (ev.Data is not null) Dispatcher.Invoke(() => Saida.Append("    " + ev.Data));
            };
            // git manda progresso para stderr; nao e erro
            p.ErrorDataReceived += (_, ev) =>
            {
                if (ev.Data is not null) Dispatcher.Invoke(() => Saida.Append("    " + ev.Data));
            };
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            await p.WaitForExitAsync();

            if (p.ExitCode != 0)
                Saida.Append($"[erro] git terminou com código {p.ExitCode}", OutputKind.Error);
        }
        catch (Exception ex)
        {
            Saida.Append($"[erro] {ex.Message}", OutputKind.Error);
        }
    }
}
