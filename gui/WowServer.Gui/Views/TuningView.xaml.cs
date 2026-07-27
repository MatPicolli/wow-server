using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WowServer.Core;

namespace WowServer.Gui.Views;

public partial class TuningView : UserControl
{
    private readonly ScriptRunner _runner = Session.Current.CreateRunner();
    private bool _ocupado;

    public TuningView()
    {
        InitializeComponent();
        Saida.Title = "Resultado";

        foreach (var preset in TuningPresets.All)
        {
            var b = new Button
            {
                Content = preset.Name,
                Tag = preset,
                Margin = new Thickness(0, 0, 0, 6),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                ToolTip = preset.Description,
            };
            b.Click += Preset_Click;
            Perfis.Children.Add(b);
        }

        _runner.Output += linha =>
            Dispatcher.Invoke(() => Saida.Append(linha.Text, linha.Kind));

        MontarAjustesConf();

        // A view e construida antes de Session.Current.Loaded existir, entao o
        // valor atual so pode ser lido em Loaded - e SEM guardar com
        // 'if (Loaded is null)', porque outra tela pode ja ter carregado as
        // configuracoes e ai a releitura que interessa seria justamente a
        // pulada. Tambem em IsVisibleChanged: quem aplicar um ajuste por fora
        // (script, editor de texto) ve o novo valor ao voltar para a aba.
        Loaded += (_, _) => LerValoresAtuais();
        IsVisibleChanged += (_, e) => { if ((bool)e.NewValue) LerValoresAtuais(); };
    }

    /// <summary>Repoe o que o usuario tinha digitado na sessao anterior.</summary>
    public void LoadFrom(UiState estado)
    {
        Aplicar(estado.ToGathering(), estado.ToDrops());
        Saida.WrapEnabled = estado.ConsoleWrap;
    }

    public void SaveTo(UiState estado)
    {
        estado.From(LerColeta(), LerDrops());
        estado.ConsoleWrap = Saida.WrapEnabled;
    }

    private void Aplicar(GatheringTuning g, DropChanceTuning d)
    {
        TxtMining.Text = Fmt(g.Mining);
        TxtHerb.Text = Fmt(g.Herbalism);
        TxtFish.Text = Fmt(g.Fishing);
        TxtSkin.Text = Fmt(g.Skinning);
        TxtDisench.Text = Fmt(g.Disenchanting);
        TxtMill.Text = Fmt(g.Milling);
        TxtProsp.Text = Fmt(g.Prospecting);
        TxtQuest.Text = Fmt(d.QuestItems);
        TxtCreature.Text = Fmt(d.CreatureItems);
    }

    private void Preset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: TuningPreset p }) return;

        Aplicar(p.Gathering, p.Drops);

        Saida.Append($"==> perfil aplicado nos campos: {p.Name}");
        Saida.Append($"    {p.Description}");
        Saida.Append("    revise os valores e use 'Ver o que mudaria' antes de aplicar");
    }

    // Multiplicador 1 significa "nao mexer": os scripts esperam ausencia do
    // argumento, nao o valor 1.
    private static string Fmt(double v) => v <= 0 ? "1" : v.ToString("0.##", CultureInfo.InvariantCulture);

    private static double Ler(TextBox caixa)
    {
        var texto = caixa.Text.Trim().Replace(',', '.');
        if (!double.TryParse(texto, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            return 0;
        return v <= 1 ? 0 : v;   // 1 ou menos = sem alteracao
    }

    private GatheringTuning LerColeta() => new()
    {
        Mining = Ler(TxtMining),
        Herbalism = Ler(TxtHerb),
        Fishing = Ler(TxtFish),
        Skinning = Ler(TxtSkin),
        Disenchanting = Ler(TxtDisench),
        Milling = Ler(TxtMill),
        Prospecting = Ler(TxtProsp),
    };

    private DropChanceTuning LerDrops() => new()
    {
        QuestItems = Ler(TxtQuest),
        CreatureItems = Ler(TxtCreature),
    };

    private async void Preview_Click(object sender, RoutedEventArgs e) => await ExecutarAsync(aplicar: false);
    private async void Aplicar_Click(object sender, RoutedEventArgs e) => await ExecutarAsync(aplicar: true);

    private async void Reset_Click(object sender, RoutedEventArgs e)
    {
        var r = MessageBox.Show(
            "Isso devolve quantidades e chances aos valores originais do jogo.\n\nContinuar?",
            "Restaurar original", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;

        await RodarAsync("tune-professions.ps1", GatheringTuning.ResetArguments());
        await RodarAsync("tune-drop-chance.ps1", DropChanceTuning.ResetArguments());
        AvisarReload();
    }

    private async Task ExecutarAsync(bool aplicar)
    {
        var coleta = LerColeta();
        var drops = LerDrops();

        if (coleta.IsEmpty && drops.IsEmpty)
        {
            MessageBox.Show(
                "Nenhum multiplicador acima de 1.\n\nDeixe em 1 o que não quer mexer.",
                "Nada a fazer");
            return;
        }

        if (aplicar)
        {
            var r = MessageBox.Show(
                "Isso grava no banco de dados do servidor.\n\n"
                + "Dá pra desfazer com 'Restaurar original'. Continuar?",
                "Aplicar", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
        }

        if (!coleta.IsEmpty)
            await RodarAsync("tune-professions.ps1", coleta.ToArguments(aplicar));

        if (!drops.IsEmpty)
            await RodarAsync("tune-drop-chance.ps1", drops.ToArguments(aplicar));

        if (aplicar) AvisarReload();
    }

    private void AvisarReload()
    {
        Saida.Append("");
        Saida.Append("==> para valer sem reiniciar, envie no console do worldserver:");
        Saida.Append("    reload gameobject_loot_template");
        Saida.Append("    reload creature_loot_template");
    }

    private async Task RodarAsync(string script, IReadOnlyList<string> argumentos)
    {
        if (_ocupado) return;
        _ocupado = true;
        try
        {
            Saida.Append($"==> {script} {string.Join(' ', argumentos)}");
            await _runner.RunAsync(script, argumentos);
        }
        catch (Exception ex)
        {
            Saida.Append($"[erro] {ex.Message}", OutputKind.Error);
        }
        finally
        {
            _ocupado = false;
        }
    }

    // ---------------------------------------------------- worldserver.conf ---

    private readonly List<AjusteConfItem> _ajustesConf = new();

    /// <summary>
    /// Monta a lista de ajustes do worldserver.conf. Chamado do construtor, uma
    /// vez - o catalogo nao muda em tempo de execucao.
    /// </summary>
    private void MontarAjustesConf()
    {
        foreach (var s in ConfigTuning.All)
            _ajustesConf.Add(new AjusteConfItem(s));

        FiltrarConf();
    }

    /// <summary>
    /// Le o worldserver.conf e mostra, em cada linha, o valor que está lá
    /// agora.
    ///
    /// Sem isso a tela nascia toda em branco e não havia como saber o que já
    /// tinha sido aplicado: fechar e reabrir a GUI apagava a única pista, que
    /// era o que o usuário tinha acabado de digitar. O campo de digitar
    /// continua vazio de propósito — em branco significa "não mexer nesta
    /// chave", e preencher tudo faria o Aplicar reescrever o arquivo inteiro.
    /// </summary>
    private void LerValoresAtuais()
    {
        var server = Session.Current.Loaded?.ServerDir;
        var caminho = string.IsNullOrWhiteSpace(server)
            ? null
            : System.IO.Path.Combine(server, "configs", "worldserver.conf");

        IReadOnlyDictionary<string, string> atuais =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (caminho is not null && System.IO.File.Exists(caminho))
        {
            try { atuais = ConfFile.ReadValues(caminho); }
            catch (Exception ex) { Saida.Append($"[aviso] não consegui ler o worldserver.conf: {ex.Message}"); }
        }

        foreach (var item in _ajustesConf)
            item.Atual = atuais.TryGetValue(item.Setting.Key, out var v) ? v.Trim() : null;

        // O ItemsControl recebe uma lista nova a cada filtro, entao nao ha
        // binding vivo para notificar - refazer a lista e o que redesenha.
        FiltrarConf();
    }

    private void FiltroConf_Changed(object sender, RoutedEventArgs e) => FiltrarConf();

    private void FiltrarConf()
    {
        // TextChanged dispara durante o InitializeComponent, antes de a lista existir.
        if (ListaConf is null || CaixaFiltroConf is null) return;

        var termo = CaixaFiltroConf.Text.Trim();
        IEnumerable<AjusteConfItem> visiveis = _ajustesConf;

        if (termo.Length > 0)
        {
            visiveis = visiveis.Where(
                i => i.TextoBusca.Contains(termo, StringComparison.OrdinalIgnoreCase));
        }

        ListaConf.ItemsSource = visiveis
            .OrderBy(i => ConfigTuning.Categories.ToList().IndexOf(i.Setting.Category))
            .ThenBy(i => i.Setting.Label, StringComparer.CurrentCulture)
            .ToList();
    }

    /// <summary>
    /// Junta o que foi preenchido num unico argumento "chave=valor,chave=valor".
    /// Devolve null quando algum valor nao serve - a mensagem ja foi mostrada.
    /// </summary>
    private string? MontarArgumentoConf()
    {
        var pares = new List<string>();

        foreach (var item in _ajustesConf)
        {
            if (string.IsNullOrWhiteSpace(item.Valor)) continue;

            if (!ConfigTuning.TryParse(item.Setting, item.Valor, out var valor, out var erro))
            {
                MessageBox.Show($"{item.Setting.Label}: {erro}", "Valor inválido");
                return null;
            }

            pares.Add($"{item.Setting.Key}={valor}");
        }

        if (pares.Count == 0)
        {
            MessageBox.Show(
                "Preencha pelo menos um ajuste. Os campos em branco são deixados como estão.",
                "Nada a fazer");
            return null;
        }

        // Uma string so, separada por virgula: o ScriptRunner usa -File, e nesse
        // modo o PowerShell entrega um array como uma unica string. O script
        // quebra pela virgula do outro lado.
        return string.Join(",", pares);
    }

    private async void PreviewConf_Click(object sender, RoutedEventArgs e)
    {
        var arg = MontarArgumentoConf();
        if (arg is null) return;
        await RodarAsync("tune-config.ps1", new[] { "-Setting", arg });
    }

    private async void AplicarConf_Click(object sender, RoutedEventArgs e)
    {
        var arg = MontarArgumentoConf();
        if (arg is null) return;

        var r = MessageBox.Show(
            "Isso grava no worldserver.conf.\n\n"
            + "Os valores originais são guardados na primeira vez, então dá para voltar "
            + "atrás depois. O arquivo inteiro também é copiado antes.\n\n"
            + "As mudanças só valem quando o servidor for reiniciado. Continuar?",
            "Aplicar no servidor", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;

        await RodarAsync("tune-config.ps1", new[] { "-Setting", arg, "-Apply" });

        // Reler do arquivo, nao assumir que gravou: se o script recusou uma
        // chave, a tela tem que mostrar o valor que sobrou de verdade.
        foreach (var item in _ajustesConf) item.Valor = string.Empty;
        LerValoresAtuais();
    }

    private async void ResetConf_Click(object sender, RoutedEventArgs e)
    {
        var r = MessageBox.Show(
            "Isso devolve todos os ajustes do worldserver.conf aos valores que tinham "
            + "antes da primeira alteração.\n\nContinuar?",
            "Restaurar original", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;

        await RodarAsync("tune-config.ps1", new[] { "-Reset", "-Apply" });
        foreach (var item in _ajustesConf) item.Valor = string.Empty;
        LerValoresAtuais();
    }

    private async void ListarConf_Click(object sender, RoutedEventArgs e)
    {
        var chaves = string.Join(",", ConfigTuning.All.Select(s => s.Key));
        await RodarAsync("tune-config.ps1", new[] { "-List", "-Setting", chaves });
    }
}

/// <summary>Uma linha da lista de ajustes do worldserver.conf.</summary>
public sealed class AjusteConfItem
{
    public AjusteConfItem(ConfigSetting setting) => Setting = setting;

    public ConfigSetting Setting { get; }

    /// <summary>O que o usuario digitou. Vazio = nao mexer nesta chave.</summary>
    public string Valor { get; set; } = string.Empty;

    /// <summary>
    /// O que esta no worldserver.conf agora. Null quando o arquivo nao existe
    /// ou nao traz a chave.
    /// </summary>
    public string? Atual { get; set; }

    public string Rotulo => Setting.Label;
    public FieldHelpEntry Ajuda => Setting.Help;

    public string Detalhe => ConfigStatus.Describe(Setting, Atual);

    /// <summary>Destaque para o que ja foi mexido, para achar de relance.</summary>
    public Brush CorDetalhe => (Brush)Application.Current.Resources[
        ConfigStatus.IsChanged(Setting, Atual) ? "Accent" : "TextDim"];

    public string TextoBusca => $"{Setting.Label} {Setting.Category} {Setting.Key} {Setting.Description}";
}
