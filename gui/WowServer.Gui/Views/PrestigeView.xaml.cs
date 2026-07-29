using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WowServer.Core;

namespace WowServer.Gui.Views;

/// <summary>Uma linha da lista de ajustes do prestígio.</summary>
public sealed class PrestigeLinha
{
    public required PrestigeSetting Setting { get; init; }
    public required LuaSetting Atual { get; init; }

    /// <summary>O que está na caixa. Começa com o valor do arquivo.</summary>
    public string Valor { get; set; } = "";

    public string Rotulo => Setting.Label;
    public string Detalhe => $"{Setting.Category}  ·  {Setting.Key}";

    /// <summary>A explicação vem do arquivo; o catálogo só dá o rótulo.</summary>
    public string Explicacao =>
        Atual.Comment.Length > 0 ? Atual.Comment : Setting.Description;

    public string Aviso => Setting.Warning ?? "";
    public Visibility VisibilidadeAviso =>
        Setting.Warning is null ? Visibility.Collapsed : Visibility.Visible;

    public string TextoBusca => $"{Setting.Label} {Setting.Key} {Setting.Category} {Explicacao}";

    /// <summary>Mostra booleano como sim/não; o resto vai como está no arquivo.</summary>
    public static string ParaTela(LuaSetting s) => s.AsBoolean switch
    {
        true => "sim",
        false => "não",
        _ => s.Unquoted,
    };
}

public sealed class SituacaoLinha
{
    public required string Texto { get; init; }
    public required Brush Cor { get; init; }
}

public partial class PrestigeView : UserControl
{
    private readonly ScriptRunner _runner = Session.Current.CreateRunner();

    private readonly List<PrestigeLinha> _ajustes = new();
    private string _texto = string.Empty;
    private bool _ocupado;

    public PrestigeView()
    {
        InitializeComponent();
        Saida.Title = "Saída";

        _runner.Output += linha =>
            Dispatcher.Invoke(() => Saida.Append(linha.Text, linha.Kind));

        // A view nasce antes de Session.Current.Loaded existir, então ler o
        // arquivo tem que acontecer em Loaded — e sem guardar com
        // 'if (Loaded is null)': outra tela pode já ter carregado, e aí o guard
        // pularia justamente a leitura que interessa.
        Loaded += (_, _) => Recarregar();
        IsVisibleChanged += (_, e) => { if (e.NewValue is true) Recarregar(); };
    }

    /// <summary>
    /// O config que a interface edita é o do repositório, não a cópia
    /// instalada: instalar copia daqui para o servidor, então editar a cópia
    /// seria perdido na próxima instalação.
    /// </summary>
    private string CaminhoConfig =>
        Path.Combine(Session.Current.RepoRoot, "mods", "prestige", "lua_scripts",
                     "01_prestige_config.lua");

    private string PastaLuaInstalada =>
        Path.Combine(Session.Current.Loaded?.ServerDir ?? "", "lua_scripts");

    // -------------------------------------------------------------- leitura --

    private void Recarregar()
    {
        LerConfig();
        ConferirSituacao();
    }

    private void LerConfig()
    {
        _ajustes.Clear();

        if (!File.Exists(CaminhoConfig))
        {
            Saida.Append($"[erro] não achei {CaminhoConfig}", OutputKind.Error);
            Aplicar();
            return;
        }

        try
        {
            _texto = File.ReadAllText(CaminhoConfig);
            var doArquivo = LuaConfig.Parse(_texto)
                .ToDictionary(s => s.Key, s => s, StringComparer.Ordinal);

            foreach (var s in PrestigeSettings.All)
            {
                if (!doArquivo.TryGetValue(s.Key, out var atual)) continue;

                _ajustes.Add(new PrestigeLinha
                {
                    Setting = s,
                    Atual = atual,
                    Valor = PrestigeLinha.ParaTela(atual),
                });
            }
        }
        catch (Exception ex)
        {
            Saida.Append($"[erro] não consegui ler o config: {ex.Message}", OutputKind.Error);
        }

        Aplicar();
    }

    private void Filtro_Changed(object sender, TextChangedEventArgs e) => Aplicar();

    private void Aplicar()
    {
        // TextChanged dispara durante o InitializeComponent, antes de a lista existir.
        if (ListaAjustes is null || CaixaFiltro is null) return;

        var termo = CaixaFiltro.Text.Trim();
        IEnumerable<PrestigeLinha> visiveis = _ajustes;

        if (termo.Length > 0)
        {
            visiveis = visiveis.Where(
                l => l.TextoBusca.Contains(termo, StringComparison.OrdinalIgnoreCase));
        }

        ListaAjustes.ItemsSource = visiveis
            .OrderBy(l => PrestigeSettings.Categories.ToList().IndexOf(l.Setting.Category))
            .ThenBy(l => l.Setting.Label, StringComparer.CurrentCulture)
            .ToList();

        DesenharCurva();
    }

    /// <summary>
    /// Mostra a curva que os valores digitados produzem. É o jeito de ver na
    /// hora se os últimos prestígios estão virando custo sem benefício, que é o
    /// erro que o README do mod avisa e que ninguém percebe olhando os números
    /// separados.
    /// </summary>
    private void DesenharCurva()
    {
        if (TxtCurva is null) return;

        double Ler(string chave, double padrao)
        {
            var linha = _ajustes.FirstOrDefault(l => l.Setting.Key == chave);
            if (linha is null) return padrao;

            var t = (linha.Valor ?? "").Trim().Replace(',', '.');
            return double.TryParse(t, System.Globalization.NumberStyles.Float,
                                   System.Globalization.CultureInfo.InvariantCulture, out var v)
                ? v : padrao;
        }

        var max = (int)Ler("MAX_PRESTIGE", 10);
        var step = Ler("XP_STEP", 0.4);
        var cap = Ler("XP_CAP", 5.0);

        var curva = PrestigeSettings.XpCurve(max, step, cap);

        TxtCurva.Text = curva.Count == 0
            ? "Sem limite de prestígios — não há curva para desenhar."
            : string.Join("\n", curva.Select(p => $"  {p.Prestigio,3}  →  {p.Multiplicador:0.0#}× de XP"));

        var aviso = PrestigeSettings.CurveWarning(max, step, cap);
        TxtAvisoCurva.Text = aviso ?? "";
        TxtAvisoCurva.Visibility = aviso is null ? Visibility.Collapsed : Visibility.Visible;
    }

    // ------------------------------------------------------------- situação --

    private void Reconferir_Click(object sender, RoutedEventArgs e) => Recarregar();

    /// <summary>
    /// O que está instalado e o que falta. Cada linha é uma coisa que, faltando,
    /// faz o mod não funcionar sem dar erro nenhum.
    /// </summary>
    private void ConferirSituacao()
    {
        if (ListaSituacao is null) return;

        var res = Application.Current.Resources;
        var linhas = new List<SituacaoLinha>();

        void Ok(string t) => linhas.Add(new SituacaoLinha { Texto = "✓ " + t, Cor = (Brush)res["Ok"] });
        void Falta(string t) => linhas.Add(new SituacaoLinha { Texto = "✕ " + t, Cor = (Brush)res["Err"] });
        void Nota(string t) => linhas.Add(new SituacaoLinha { Texto = "! " + t, Cor = (Brush)res["Warn"] });

        var cfg = Session.Current.Loaded;

        // 1. o engine Lua
        var modules = Path.Combine(cfg?.SourceDir ?? "", "modules");
        if (Directory.Exists(Path.Combine(modules, "mod-ale")))
            Ok("mod-ale instalado — é o engine que executa estes scripts");
        else if (Directory.Exists(Path.Combine(modules, "mod-eluna")))
            Nota("o módulo Lua está como mod-eluna. Renomeie na aba Módulos e recompile, "
                 + "senão o engine nem entra na compilação");
        else
            Falta("mod-ale não está instalado. Sem o engine Lua, estes scripts nunca rodam");

        // 2. os scripts
        var pasta = PastaLuaInstalada;
        var instalados = Directory.Exists(pasta)
            ? Directory.GetFiles(pasta, "*prestige*.lua").Length
            : 0;
        var noRepo = Directory.Exists(Path.GetDirectoryName(CaminhoConfig)!)
            ? Directory.GetFiles(Path.GetDirectoryName(CaminhoConfig)!, "*.lua").Length
            : 0;

        if (instalados == 0) Falta($"nenhum script instalado em {pasta}");
        else if (instalados < noRepo) Nota($"{instalados} de {noRepo} scripts instalados — falta reinstalar");
        else Ok($"{instalados} scripts instalados");

        // 3. StackTracePlus
        if (Directory.Exists(Path.Combine(pasta, "extensions")))
            Ok("extensions\\ presente — erro de Lua vem com número de linha");
        else
            Nota("extensions\\ não está lá. Sem o StackTracePlus, erro de Lua aparece "
                 + "sem apontar arquivo nem linha");

        // 4. o addon (cosmético)
        var addon = Path.Combine(cfg?.ClientDir ?? "", "Interface", "AddOns", "PrestigeUI");
        if (Directory.Exists(addon))
            Ok("addon PrestigeUI no client");
        else
            Nota("addon não instalado. É só o ícone: todos os bônus funcionam sem ele");

        ListaSituacao.ItemsSource = linhas;
    }

    // ---------------------------------------------------------- instalação --

    /// <summary>
    /// Devolve o código de saída, ou -1 se nem rodou.
    ///
    /// O código importa: 2 significa que parte entrou e parte não — tipicamente
    /// o NPC. Tratar isso como sucesso faria a tela dizer "instalado" com o
    /// jogador sem ter onde clicar.
    /// </summary>
    private async Task<int> RodarAsync(string[] argumentos)
    {
        if (_ocupado) return -1;

        if (!_runner.ScriptExists("install-prestige.ps1"))
        {
            Saida.Append("[erro] scripts\\install-prestige.ps1 não encontrado — atualize o repositório",
                         OutputKind.Error);
            return -1;
        }

        _ocupado = true;
        try
        {
            Saida.Append($"==> install-prestige.ps1 {string.Join(' ', argumentos)}");
            return await _runner.RunAsync("install-prestige.ps1", argumentos);
        }
        catch (Exception ex)
        {
            Saida.Append($"[erro] {ex.Message}", OutputKind.Error);
            return -1;
        }
        finally
        {
            _ocupado = false;
            ConferirSituacao();
        }
    }

    private async void Previa_Click(object sender, RoutedEventArgs e) =>
        await RodarAsync(Array.Empty<string>());

    private async void Instalar_Click(object sender, RoutedEventArgs e)
    {
        await RodarAsync(Array.Empty<string>());

        var r = MessageBox.Show(
            "Instalar o Sistema de Prestígio?\n\n"
            + "O console ao lado mostra o que será feito: duas tabelas no banco de "
            + "personagens, os scripts Lua, o NPC e o addon.\n\n"
            + "Instalar não mexe em personagem nenhum. O que é destrutivo é usar o "
            + "NPC depois — e isso não tem desfazer. Faça um backup do banco antes de "
            + "liberar para uso, e teste com um personagem descartável.",
            "Instalar o prestígio", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (r != MessageBoxResult.Yes) return;

        var codigo = await RodarAsync(new[] { "-Apply" });

        if (codigo == 2)
        {
            // O script já explicou o que faltou no console; aqui é para o aviso
            // não passar batido quem estava olhando a caixa de diálogo.
            MessageBox.Show(
                "Instalou em parte.\n\n"
                + "As tabelas e os scripts Lua estão no lugar, mas alguma coisa ficou "
                + "faltando — o console ao lado diz o que, e o painel Situação também.\n\n"
                + "O caso comum é o NPC: sem ele o mod está instalado mas não há onde "
                + "clicar para prestigiar.",
                "Instalação incompleta", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else if (codigo == 0)
        {
            Saida.Append("[ok] instalado — reinicie o worldserver para o ALE carregar os scripts");
        }
    }

    private async void Desinstalar_Click(object sender, RoutedEventArgs e)
    {
        await RodarAsync(new[] { "-Uninstall" });

        var r = MessageBox.Show(
            "Remover os scripts do prestígio?\n\n"
            + "As tabelas character_prestige* NÃO são apagadas: elas guardam o "
            + "prestígio que os personagens já conquistaram. Reinstalar depois "
            + "recupera tudo.",
            "Desinstalar", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;

        await RodarAsync(new[] { "-Uninstall", "-Apply" });
    }

    private async void Backup_Click(object sender, RoutedEventArgs e)
    {
        if (_ocupado) return;
        _ocupado = true;
        try
        {
            Saida.Append("==> backup dos bancos de personagens e contas");
            await _runner.RunAsync("backup-db.ps1");
        }
        catch (Exception ex) { Saida.Append($"[erro] {ex.Message}", OutputKind.Error); }
        finally { _ocupado = false; }
    }

    // -------------------------------------------------------------- salvar --

    private void Salvar_Click(object sender, RoutedEventArgs e)
    {
        if (_ajustes.Count == 0) return;

        var trocas = new List<KeyValuePair<string, string>>();
        var resumo = new List<string>();

        foreach (var linha in _ajustes)
        {
            if (!PrestigeSettings.TryFormat(linha.Setting, linha.Atual, linha.Valor,
                                            out var formatado, out var erro))
            {
                MessageBox.Show($"{linha.Setting.Label}: {erro}", "Valor inválido");
                return;
            }

            if (formatado == linha.Atual.Value) continue;

            trocas.Add(new KeyValuePair<string, string>(linha.Setting.Key, formatado));
            resumo.Add($"  {linha.Setting.Key}:  {linha.Atual.Value}  ->  {formatado}");
        }

        if (trocas.Count == 0)
        {
            MessageBox.Show("Nenhum valor foi alterado.", "Nada a salvar");
            return;
        }

        var r = MessageBox.Show(
            $"Gravar {trocas.Count} alteração(ões) em 01_prestige_config.lua?\n\n"
            + string.Join("\n", resumo.Take(15))
            + (resumo.Count > 15 ? $"\n  ... e mais {resumo.Count - 15}" : "")
            + "\n\nUma cópia do original é guardada antes. Isto grava no arquivo do "
            + "repositório; use Instalar para levar ao servidor.",
            "Salvar ajustes", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;

        try
        {
            var copia = ConfFile.Backup(CaminhoConfig);
            var novo = LuaConfig.SetValues(_texto, trocas, out var naoAchadas);
            LuaConfig.Save(CaminhoConfig, novo);

            Saida.Append($"[ok] gravado — cópia do original: {Path.GetFileName(copia)}");

            // Chave que o parser leu e o regex não achou na hora de gravar seria
            // bug do editor, não do usuário: aparece em vez de sumir calado.
            if (naoAchadas.Count > 0)
                Saida.Append("[aviso] não consegui gravar: " + string.Join(", ", naoAchadas),
                             OutputKind.Error);

            var jaInstalado = Directory.Exists(PastaLuaInstalada)
                && Directory.GetFiles(PastaLuaInstalada, "*prestige*.lua").Length > 0;

            if (jaInstalado)
            {
                Saida.Append("    o mod já está instalado: clique em Instalar para levar o");
                Saida.Append("    config novo ao servidor, e depois 'reload eluna' no console");
            }

            LerConfig();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Não consegui gravar: " + ex.Message, "Erro");
        }
    }
}
