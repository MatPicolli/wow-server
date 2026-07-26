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

    /// <summary>Nome da pasta em modules/ — o que os scripts recebem.</summary>
    public required string Pasta { get; init; }

    public required string Categoria { get; init; }
    public required string Resumo { get; init; }
    public required string Detalhes { get; init; }
    public required string Repositorio { get; init; }
    public required string Selo { get; init; }
    public required Brush CorSelo { get; init; }
    public required string TextoBotao { get; init; }
    public required bool PodeInstalar { get; init; }
    public required AcaoModulo Acao { get; init; }
    public string? Alerta { get; init; }
    public string? PassoManual { get; init; }
    public required bool Instalado { get; init; }

    public Visibility VisibilidadeRemover => Instalado ? Visibility.Visible : Visibility.Collapsed;
    public Visibility VisibilidadeAlerta => Alerta is null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility VisibilidadePassoManual =>
        PassoManual is null ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Texto onde o filtro procura.</summary>
    public string TextoBusca => $"{Nome} {Categoria} {Resumo} {Detalhes} {Repositorio}";
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

        // A deteccao de core incompativel depende das configuracoes, que no
        // construtor quase nunca estao lidas ainda.
        //
        // A versao anterior so redesenhava se Session.Current.Loaded fosse
        // null - e isso errava justamente quando OUTRA tela (a de
        // Configuracoes) ja tinha carregado: encontrando o valor preenchido,
        // esta aqui pulava o redesenho e mantinha na tela os cards montados
        // sem configuracao nenhuma. O Playerbots aparecia como "instalado" em
        // vez de "core incompativel", e o botao de corrigir nunca surgia.
        Loaded += async (_, _) => await AtualizarComConfiguracoesAsync();

        // Voltar para esta aba tem que refletir o que mudou fora dela - trocar
        // o core pelo script, por exemplo, ou instalar um modulo pelo git.
        IsVisibleChanged += async (_, e) =>
        {
            if (e.NewValue is true) await AtualizarComConfiguracoesAsync();
        };
    }

    /// <summary>
    /// De onde o codigo-fonte foi realmente clonado. null quando nao ha clone,
    /// ou quando o git nao respondeu.
    /// </summary>
    private string? _origemCore;

    private async Task AtualizarComConfiguracoesAsync()
    {
        if (Session.Current.Loaded is null)
        {
            try
            {
                Session.Current.Loaded = await Session.Current.Settings.LoadAsync();
            }
            catch (Exception ex)
            {
                Saida.Append($"[aviso] não consegui ler as configurações: {ex.Message}");
            }
        }

        _origemCore = await DescobrirOrigemDoCoreAsync();
        Recarregar();
    }

    /// <summary>
    /// Le a origem do clone que esta no disco.
    ///
    /// O settings.psd1 diz a intencao, o git diz o fato, e os dois podem
    /// discordar: uma troca de core que grava as configuracoes e falha no clone
    /// deixa exatamente esse estado. Julgar pela intencao faria a tela dizer que
    /// esta tudo certo enquanto a compilacao continua falhando.
    /// </summary>
    private async Task<string?> DescobrirOrigemDoCoreAsync()
    {
        var origem = Session.Current.Loaded?.SourceDir;
        if (string.IsNullOrWhiteSpace(origem)) return null;
        if (!Directory.Exists(Path.Combine(origem, ".git"))) return null;

        var url = await CapturarGitAsync(new[] { "-C", origem, "remote", "get-url", "origin" });
        return string.IsNullOrWhiteSpace(url) ? null : url.Trim();
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
            // sem git no PATH nao da para verificar; o card diz isso
            return string.Empty;
        }
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

    /// <summary>Todos os cards, antes do filtro.</summary>
    private List<ModuloItem> _todos = new();

    private void Recarregar()
    {
        var res = Application.Current.Resources;
        var itens = new List<ModuloItem>();
        var cfg = Session.Current.Loaded;
        var instaladosSoltos = ModulosInstaladosForaDoCatalogo();

        foreach (var m in ModuleCatalog.All)
        {
            var instalado = Directory.Exists(Path.Combine(ModulesDir, m.Name));

            // Um modulo de fork instalado sobre o core errado compila contra
            // assinaturas que nao existem e falha com dezenas de erros
            // C2660. Mostrar isso aqui evita uma compilacao inteira perdida.
            //
            // O que vale e o clone que esta no disco, nao o que o settings.psd1
            // pretende. Comparar com a intencao ja escondeu o problema: uma
            // troca anterior gravou 'Playerbot' nas configuracoes e falhou no
            // clone, entao a tela dava tudo certo enquanto o rebuild.ps1 - que
            // olha o git - recusava compilar.
            var coreReal = _origemCore ?? cfg?.SourceRepository;

            var coreErrado = instalado
                && m.Status == ModuleStatus.ExigeFork
                && m.ForkRepository is not null
                && coreReal is not null
                && !ModuleCatalog.SatisfiesFork(m, coreReal);

            var (selo, cor) = m.Status switch
            {
                ModuleStatus.Testado => ("testado", (Brush)res["Ok"]),
                ModuleStatus.ExigeFork => ("troca o core", (Brush)res["Warn"]),
                _ => ("oficial", (Brush)res["Accent"]),
            };

            // Sem saber a origem do core nao da para julgar. Dizer "instalado"
            // nesse caso seria afirmar algo que nao foi verificado.
            var semConfig = instalado && m.Status == ModuleStatus.ExigeFork && coreReal is null;

            if (instalado) (selo, cor) = ("instalado", (Brush)res["Ok"]);
            if (semConfig) (selo, cor) = ("core não verificado", (Brush)res["Warn"]);
            if (coreErrado) (selo, cor) = ("core incompatível", (Brush)res["Err"]);

            var acao = coreErrado ? AcaoModulo.CorrigirCore
                     : instalado ? AcaoModulo.Nenhuma
                     : AcaoModulo.Instalar;

            itens.Add(new ModuloItem
            {
                Nome = m.DisplayName,
                Pasta = m.Name,
                Instalado = instalado,
                Categoria = m.Category,
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
                Alerta = coreErrado ? MontarAlertaDeCore(m, coreReal!, cfg) : null,
                PassoManual = m.ManualStep,
            });
        }

        // Modulos instalados por URL nao estao no catalogo, mas some-los da tela
        // seria pior: o usuario perderia de vista o que esta compilando junto.
        foreach (var nome in instaladosSoltos)
        {
            itens.Add(new ModuloItem
            {
                Nome = nome,
                Pasta = nome,
                Instalado = true,
                Categoria = ModuleCatalog.OutrosCategoria,
                Resumo = "Instalado por URL, fora do catálogo.",
                Detalhes = "Este módulo não faz parte da lista curada, então não há "
                         + "descrição nem instruções aqui. Consulte o README do "
                         + "repositório. Ele entra na compilação como qualquer outro.",
                Repositorio = UrlDeOrigem(nome) ?? "",
                Selo = "instalado",
                CorSelo = (Brush)res["Ok"],
                Acao = AcaoModulo.Nenhuma,
                PodeInstalar = false,
                TextoBotao = "Já instalado",
            });
        }

        _todos = itens;
        AplicarFiltro();
    }

    /// <summary>
    /// Texto do aviso de core incompativel. Quando o settings.psd1 discorda do
    /// disco, os dois aparecem: essa diferenca e sintoma de uma troca de core
    /// que foi gravada e nao chegou a clonar, e esconde-la deixaria o usuario
    /// procurando o problema no lugar errado.
    /// </summary>
    private string MontarAlertaDeCore(CatalogModule m, string coreReal, ServerSettings? cfg)
    {
        var texto = $"Este módulo precisa do código '{m.ForkBranch}' de {m.ForkRepository}, "
                  + $"mas o que está em disco veio de {coreReal}. Sem trocar, a compilação falha.";

        if (_origemCore is not null && cfg is not null
            && !ModuleCatalog.SameRepository(_origemCore, cfg.SourceRepository))
        {
            texto += $"\n\nAtenção: as configurações já apontam para {cfg.SourceRepository}, "
                   + "mas o código no disco não. Uma troca anterior foi gravada e não chegou "
                   + "a clonar. O botão abaixo refaz o clone.";
        }

        return texto;
    }

    /// <summary>Pastas em modules/ que o catalogo nao conhece.</summary>
    private List<string> ModulosInstaladosForaDoCatalogo()
    {
        var soltos = new List<string>();
        if (!Directory.Exists(ModulesDir)) return soltos;

        var conhecidos = ModuleCatalog.All
            .Select(m => m.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var dir in Directory.GetDirectories(ModulesDir))
            {
                var nome = Path.GetFileName(dir);
                if (nome.StartsWith(".", StringComparison.Ordinal)) continue;
                if (!conhecidos.Contains(nome)) soltos.Add(nome);
            }
        }
        catch (Exception ex)
        {
            Saida.Append($"[aviso] não consegui listar {ModulesDir}: {ex.Message}");
        }

        soltos.Sort(StringComparer.OrdinalIgnoreCase);
        return soltos;
    }

    /// <summary>De onde a pasta foi clonada, para o botao "Abrir no navegador".</summary>
    private string? UrlDeOrigem(string nomeDaPasta)
    {
        try
        {
            var config = Path.Combine(ModulesDir, nomeDaPasta, ".git", "config");
            if (!File.Exists(config)) return null;

            foreach (var linha in File.ReadLines(config))
            {
                var t = linha.Trim();
                if (!t.StartsWith("url", StringComparison.OrdinalIgnoreCase)) continue;

                var igual = t.IndexOf('=');
                if (igual > 0) return t[(igual + 1)..].Trim();
            }
        }
        catch
        {
            // sem a URL o card so perde um botao
        }
        return null;
    }

    private void Filtro_Changed(object sender, RoutedEventArgs e) => AplicarFiltro();

    private void AplicarFiltro()
    {
        // Chamado por TextChanged, que dispara durante o InitializeComponent -
        // antes de a lista existir.
        if (Lista is null || CaixaFiltro is null) return;

        var termo = CaixaFiltro.Text.Trim();
        var ocultarInstalados = ChkSoNaoInstalados?.IsChecked == true;

        IEnumerable<ModuloItem> visiveis = _todos;

        if (ocultarInstalados)
            visiveis = visiveis.Where(i => i.Acao != AcaoModulo.Nenhuma);

        if (termo.Length > 0)
        {
            // Sem acentos e sem caixa: quem procura "leilao" tem que achar
            // "Casa de Leilões".
            var alvo = SemAcento(termo);
            visiveis = visiveis.Where(
                i => SemAcento(i.TextoBusca).Contains(alvo, StringComparison.OrdinalIgnoreCase));
        }

        var lista = visiveis
            .OrderBy(i => IndiceDaCategoria(i.Categoria))
            .ThenBy(i => i.Nome, StringComparer.CurrentCulture)
            .ToList();

        Lista.ItemsSource = lista;
    }

    private static int IndiceDaCategoria(string categoria)
    {
        var i = ModuleCatalog.Categories.ToList().IndexOf(categoria);
        return i < 0 ? int.MaxValue : i;
    }

    /// <summary>
    /// Tira acentos para comparar. Decompor em NFD separa a letra do acento, e
    /// os acentos ficam na categoria NonSpacingMark - basta descartar essa.
    /// </summary>
    private static string SemAcento(string texto)
    {
        var decomposto = texto.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(decomposto.Length);

        foreach (var c in decomposto)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }

        return sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
    }

    private void Abrir_Click(object sender, RoutedEventArgs e)
    {
        // Modulo instalado por URL pode nao ter origem conhecida; abrir string
        // vazia lanca Win32Exception.
        if (sender is not Button { Tag: string url } || string.IsNullOrWhiteSpace(url))
        {
            Saida.Append("[aviso] não sei de onde este módulo veio");
            return;
        }
        AbrirNoNavegador(url);
    }

    private void Link_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        AbrirNoNavegador(e.Uri.ToString());
        e.Handled = true;
    }

    private void AbrirNoNavegador(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Saida.Append($"[aviso] não consegui abrir {url}: {ex.Message}");
        }
    }

    /// <summary>
    /// Instala um modulo que nao esta no catalogo, a partir da URL colada.
    /// </summary>
    private async void InstalarUrl_Click(object sender, RoutedEventArgs e)
    {
        var url = CaixaUrl.Text;

        if (!CustomModuleUrl.TryParse(url, out var pasta, out var erro))
        {
            MessageBox.Show(erro, "Endereço inválido");
            return;
        }

        if (ModuleCatalog.All.Any(m => string.Equals(m.Name, pasta, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(
                $"'{pasta}' já está no catálogo — use o card dele na lista, que traz "
                + "as instruções pós-instalação.",
                "Já está na lista");
            return;
        }

        if (Directory.Exists(Path.Combine(ModulesDir, pasta)))
        {
            MessageBox.Show($"'{pasta}' já está instalado.", "Nada a fazer");
            return;
        }

        var aviso = CustomModuleUrl.Advice(pasta);
        var texto = $"Instalar '{pasta}' a partir de:\n{url.Trim()}\n\n"
                  + "Este módulo não foi testado aqui. Se ele não compilar, o servidor "
                  + "atual continua funcionando — basta apagar a pasta e recompilar.";
        if (aviso is not null) texto += "\n\n" + aviso;

        if (MessageBox.Show(texto, "Instalar módulo de fora da lista",
                            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        Directory.CreateDirectory(ModulesDir);
        var destino = Path.Combine(ModulesDir, pasta);

        Saida.Append($"==> clonando {pasta}");
        await RodarGitAsync(new[] { "clone", "--recurse-submodules", url.Trim(), destino });

        if (!Directory.Exists(destino))
        {
            Saida.Append("[erro] o clone não criou a pasta — confira o endereço", OutputKind.Error);
            return;
        }

        CaixaUrl.Clear();
        Recarregar();

        Saida.Append("");
        Saida.Append("==> agora use 'Recompilar' para o módulo entrar no servidor");
        Saida.Append("    leia o README do repositório: muitos módulos precisam de ajuste no .conf");
    }

    /// <summary>
    /// Remove um modulo instalado.
    ///
    /// Roda o script duas vezes de proposito: a primeira sem -Apply, so para
    /// mostrar no console o que sera apagado, e so entao pergunta. Assim a
    /// confirmacao vem depois de ver o tamanho da pasta e se ha trabalho local
    /// nao commitado, em vez de antes.
    /// </summary>
    private async void Remover_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string pasta } || string.IsNullOrWhiteSpace(pasta)) return;

        if (!_runner.ScriptExists("remove-module.ps1"))
        {
            Saida.Append("[erro] scripts\\remove-module.ps1 não encontrado — atualize o repositório", OutputKind.Error);
            return;
        }

        Saida.Append($"==> o que aconteceria ao remover {pasta}");
        var previa = await _runner.RunAsync("remove-module.ps1", new[] { "-Name", pasta });

        // Saida != 0 aqui e o script recusando (alteracoes locais, por
        // exemplo). O motivo ja esta no console; perguntar depois disso seria
        // oferecer algo que vai falhar.
        if (previa != 0)
        {
            MessageBox.Show(
                $"Não dá para remover '{pasta}' assim — o motivo está no console ao lado.",
                "Remoção recusada");
            return;
        }

        var resposta = MessageBox.Show(
            $"Apagar a pasta do módulo '{pasta}'?\n\n"
            + "O console ao lado mostra exatamente o que será apagado.\n\n"
            + "O que NÃO é desfeito: o SQL que o módulo já aplicou no banco. "
            + "Na prática isso raramente incomoda — sobram tabelas sem uso.\n\n"
            + "Depois disso, use Recompilar para o servidor deixar de incluí-lo.",
            "Remover módulo", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (resposta != MessageBoxResult.Yes)
        {
            Saida.Append("    remoção cancelada");
            return;
        }

        var codigo = await _runner.RunAsync("remove-module.ps1", new[] { "-Name", pasta, "-Apply" });
        if (codigo != 0)
        {
            Saida.Append($"[erro] a remoção falhou (código {codigo})", OutputKind.Error);
            return;
        }

        Recarregar();
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
                + "  • é feito um backup do banco automaticamente, antes de tudo\n"
                + "  • o código-fonte é apagado e clonado de novo\n"
                + "  • os outros módulos instalados são reinstalados sozinhos,\n"
                + "    porque a pasta de módulos vive dentro do código-fonte\n"
                + "  • depois você clica em Recompilar (cerca de 20 minutos)\n\n"
                + "O que é preservado:\n"
                + "  • os dados extraídos do seu WoW (as horas de extração NÃO se repetem)\n"
                + "  • seus personagens e contas\n"
                + "  • suas configurações\n\n"
                + "Dá para ver o que vai acontecer, sem alterar nada, rodando antes:\n"
                + "    .\\scripts\\switch-core.ps1 -Playerbots\n\n"
                + "Continuar?",
                "Isso troca o código do servidor", MessageBoxButton.YesNo, MessageBoxImage.Warning);

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

    /// <summary>
    /// Troca o core pelo fork que o modulo exige.
    ///
    /// Tudo que decide o que apagar, o que preservar e quais modulos reclonar
    /// mora em switch-core.ps1, nao aqui. Isso vale para os dois caminhos: quem
    /// nao usa a GUI roda o mesmo script e obtem o mesmo resultado, e uma
    /// correcao beneficia os dois.
    /// </summary>
    /// <returns>true se o core ficou pronto para receber o modulo.</returns>
    private async Task<bool> TrocarParaForkAsync(CatalogModule modulo)
    {
        if (modulo.ForkRepository is null || modulo.ForkBranch is null) return true;

        if (!_runner.ScriptExists("switch-core.ps1"))
        {
            Saida.Append("[erro] scripts\\switch-core.ps1 não encontrado — atualize o repositório", OutputKind.Error);
            return false;
        }

        var progresso = new BuildProgressTracker();
        void AoSair(OutputLine linha) => progresso.Feed(linha.Text);
        _runner.Output += AoSair;

        int codigo;
        try
        {
            // -NoBuild: a compilacao vem depois, com a barra de progresso da
            // tela. Deixar o script compilar aqui esconderia o andamento.
            codigo = await RodarTrocaAsync(modulo, pularBackup: false);

            // 3 = o MySQL nao estava no ar, entao nao houve backup e nada foi
            // alterado. Na linha de comando basta repetir com -SkipBackup;
            // aqui o usuario nao tem onde digitar isso, e sem a pergunta ele
            // ficaria sem saida.
            if (codigo == 3)
            {
                var seguir = MessageBox.Show(
                    "O MySQL não está rodando, então não deu para fazer o backup.\n\n"
                    + "A troca do código do servidor NÃO mexe no banco: seus personagens "
                    + "e contas ficam onde estão. O backup é precaução para a compilação "
                    + "seguinte, quando o worldserver aplica SQL.\n\n"
                    + "Continuar sem backup?\n\n"
                    + "(Cancelar é seguro: nada foi alterado ainda. Você pode iniciar o "
                    + "MySQL e tentar de novo.)",
                    "Sem backup", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (seguir != MessageBoxResult.Yes)
                {
                    Saida.Append("    troca cancelada — nada foi alterado");
                    return false;
                }

                codigo = await RodarTrocaAsync(modulo, pularBackup: true);
            }
        }
        finally
        {
            _runner.Output -= AoSair;
        }

        if (codigo != 0)
        {
            Saida.Append($"[erro] a troca do core falhou (código {codigo})", OutputKind.Error);
            if (progresso.FirstFailure is not null)
                Saida.Append("       " + progresso.FirstFailure, OutputKind.Error);
            Saida.Append("       o módulo NÃO foi instalado — sobre o core errado ele não compila", OutputKind.Error);
            return false;
        }

        // O script gravou o settings.psd1; a sessao ainda tem o valor antigo em
        // memoria, e sem recarregar o card continuaria acusando core errado.
        try
        {
            Session.Current.Loaded = await Session.Current.Settings.LoadAsync();
        }
        catch (Exception ex)
        {
            Saida.Append($"[aviso] não consegui reler as configurações: {ex.Message}");
        }

        return true;
    }

    private Task<int> RodarTrocaAsync(CatalogModule modulo, bool pularBackup)
    {
        var argumentos = new List<string>
        {
            "-Repository", modulo.ForkRepository!,
            "-Branch", modulo.ForkBranch!,
            "-Apply",
            "-NoBuild",
        };
        if (pularBackup) argumentos.Add("-SkipBackup");

        return _runner.RunAsync("switch-core.ps1", argumentos);
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
        // --recurse-submodules: alguns modulos trazem dependencias como
        // submodulo, e sem ele a pasta do modulo parece completa enquanto a
        // dependencia fica vazia - a compilacao so morre meia hora depois.
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
            else if (!progresso.Started)
            {
                // Nada foi compilado: uma verificacao barrou antes. Mandar
                // procurar linha com 'error' aqui so faria perder tempo - a
                // causa ja esta escrita, e e uma linha so.
                Saida.AppendBanner("A COMPILAÇÃO NEM COMEÇOU", sucesso: false);
                if (progresso.FirstFailure is not null)
                    Saida.Append("Motivo: " + progresso.FirstFailure, OutputKind.Error);
                Saida.Append("Nada foi alterado. Resolva o que está acima e clique em Recompilar de novo.");
            }
            else
            {
                Saida.AppendBanner(
                    $"COMPILAÇÃO FALHOU após {duracao:hh\\:mm\\:ss} — {progresso.Errors} erro(s)",
                    sucesso: false);
                if (progresso.FirstFailure is not null)
                    Saida.Append("Primeiro erro: " + progresso.FirstFailure, OutputKind.Error);
                Saida.Append("Os erros seguintes costumam ser consequência desse.");
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
