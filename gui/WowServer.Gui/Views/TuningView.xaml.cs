using System.Globalization;
using System.Windows;
using System.Windows.Controls;
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
    }

    private void Preset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: TuningPreset p }) return;

        TxtMining.Text = Fmt(p.Gathering.Mining);
        TxtHerb.Text = Fmt(p.Gathering.Herbalism);
        TxtFish.Text = Fmt(p.Gathering.Fishing);
        TxtSkin.Text = Fmt(p.Gathering.Skinning);
        TxtDisench.Text = Fmt(p.Gathering.Disenchanting);
        TxtMill.Text = Fmt(p.Gathering.Milling);
        TxtProsp.Text = Fmt(p.Gathering.Prospecting);
        TxtQuest.Text = Fmt(p.Drops.QuestItems);
        TxtCreature.Text = Fmt(p.Drops.CreatureItems);

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
}
