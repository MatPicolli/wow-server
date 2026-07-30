using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WowServer.Core;

namespace WowServer.Gui.Views;

public partial class HeirloomsView : UserControl
{
    private readonly ScriptRunner _runner = Session.Current.CreateRunner();
    private bool _ocupado;

    /// <summary>Faixa que o conjunto ocupa. Igual ao padrao do script.</summary>
    private const int PrimeiroEntry = 700000;
    private const int TotalPecas = 48;

    public HeirloomsView()
    {
        InitializeComponent();
        Saida.Title = "Saída";

        _runner.Output += linha =>
            Dispatcher.Invoke(() => Saida.Append(linha.Text, linha.Kind));

        // Sem guardar com 'if (Loaded is null)': outra tela pode ter carregado
        // as configuracoes primeiro, e ai o guard pularia a leitura que importa.
        Loaded += async (_, _) => await ConferirAsync();
        IsVisibleChanged += async (_, e) => { if (e.NewValue is true) await ConferirAsync(); };
    }

    private async Task<int> RodarAsync(string[] argumentos)
    {
        if (_ocupado) return -1;

        if (!_runner.ScriptExists("install-heirlooms.ps1"))
        {
            Saida.Append("[erro] scripts\\install-heirlooms.ps1 não encontrado — atualize o repositório",
                         OutputKind.Error);
            return -1;
        }

        _ocupado = true;
        try
        {
            Saida.Append($"==> install-heirlooms.ps1 {string.Join(' ', argumentos)}");
            return await _runner.RunAsync("install-heirlooms.ps1", argumentos);
        }
        catch (Exception ex)
        {
            Saida.Append($"[erro] {ex.Message}", OutputKind.Error);
            return -1;
        }
        finally
        {
            _ocupado = false;
        }
    }

    private async void Previa_Click(object sender, RoutedEventArgs e) =>
        await RodarAsync(Array.Empty<string>());

    private async void Criar_Click(object sender, RoutedEventArgs e)
    {
        await RodarAsync(Array.Empty<string>());

        var r = MessageBox.Show(
            "Criar as 48 peças no item_template?\n\n"
            + "O console ao lado lista cada uma antes. O banco do mundo é copiado "
            + "primeiro, e o comando apaga a entry antes de inserir — então dá para "
            + "rodar de novo depois de ajustar.\n\n"
            + "Isto não mexe em personagem nenhum: só cria itens.",
            "Criar o conjunto", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;

        await RodarAsync(new[] { "-Apply" });
        await ConferirAsync();
    }

    private async void Apagar_Click(object sender, RoutedEventArgs e)
    {
        await RodarAsync(new[] { "-Uninstall" });

        var r = MessageBox.Show(
            "Apagar as 48 peças?\n\n"
            + "Peça que um personagem tenha na bolsa ou no correio vira item "
            + "inválido: o cliente mostra \"item desconhecido\" e o servidor descarta "
            + "no login.\n\n"
            + "Se a ideia é só corrigir alguma coisa, use Criar de novo — ele "
            + "sobrescreve sem passar por aqui.",
            "Apagar o conjunto", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (r != MessageBoxResult.Yes) return;

        await RodarAsync(new[] { "-Uninstall", "-Apply" });
        await ConferirAsync();
    }

    /// <summary>
    /// Manda tudo que for qualidade herança, não só as 48: o gm-heirlooms.ps1
    /// consulta por Quality = 7, então pega o que qualquer módulo tenha
    /// adicionado, sem ID inventado no meio.
    /// </summary>
    private async void Enviar_Click(object sender, RoutedEventArgs e)
    {
        var nome = (CaixaPersonagem.Text ?? "").Trim();
        if (nome.Length == 0)
        {
            MessageBox.Show("Escreva o nome do personagem que vai receber.", "Falta o personagem");
            return;
        }

        if (_ocupado) return;
        _ocupado = true;
        try
        {
            Saida.Append($"==> enviando as heranças para {nome}");
            await _runner.RunAsync("gm-heirlooms.ps1", new[] { "-Character", nome });
        }
        catch (Exception ex) { Saida.Append($"[erro] {ex.Message}", OutputKind.Error); }
        finally { _ocupado = false; }
    }

    private async void Reconferir_Click(object sender, RoutedEventArgs e) => await ConferirAsync();

    /// <summary>
    /// Conta o que existe no banco, pelo gm-items.ps1 — que só executa SELECT.
    /// </summary>
    private async Task ConferirAsync()
    {
        if (ListaSituacao is null) return;

        var res = Application.Current.Resources;
        var linhas = new List<SituacaoLinha>();

        var db = Session.Current.Loaded?.MySql.WorldDb;
        if (string.IsNullOrWhiteSpace(db) || !_runner.ScriptExists("gm-items.ps1"))
        {
            linhas.Add(new SituacaoLinha
            {
                Texto = "! Não consegui consultar o banco ainda.",
                Cor = (Brush)res["Warn"],
            });
            ListaSituacao.ItemsSource = linhas;
            return;
        }

        var ultimo = PrimeiroEntry + TotalPecas - 1;
        var linhasSql = await ConsultarAsync(
            $"SELECT COUNT(*), SUM(Quality = 7), SUM(spellid_1 = 57353), "
            + $"COUNT(DISTINCT InventoryType), SUM(displayid >= 32000) "
            + $"FROM {db}.item_template WHERE entry BETWEEN {PrimeiroEntry} AND {ultimo}");

        var campos = linhasSql.Count > 0
            ? linhasSql[0].Split('\t').Select(c => int.TryParse(c.Trim(), out var v) ? v : 0).ToArray()
            : Array.Empty<int>();

        if (campos.Length < 5)
        {
            linhas.Add(new SituacaoLinha
            {
                Texto = "! Não consegui ler a contagem — o MySQL está no ar?",
                Cor = (Brush)res["Warn"],
            });
        }
        else
        {
            var (total, heranca, comXp, slots, altos) =
                (campos[0], campos[1], campos[2], campos[3], campos[4]);

            void Ok(string t) => linhas.Add(new SituacaoLinha { Texto = "✓ " + t, Cor = (Brush)res["Ok"] });
            void Falta(string t) => linhas.Add(new SituacaoLinha { Texto = "✕ " + t, Cor = (Brush)res["Err"] });
            void Nota(string t) => linhas.Add(new SituacaoLinha { Texto = "! " + t, Cor = (Brush)res["Warn"] });

            if (total == 0) Falta($"nenhuma peça criada (faixa {PrimeiroEntry}–{ultimo})");
            else if (total < TotalPecas) Nota($"{total} de {TotalPecas} peças — falta criar o resto");
            else Ok($"{total} peças no banco");

            if (total > 0)
            {
                if (heranca == total) Ok("todas com qualidade herança — entram no envio em massa");
                else Nota($"{heranca} de {total} com qualidade herança");

                if (comXp == total) Ok("todas com o feitiço de +10% de XP ao equipar");
                else Falta($"{comXp} de {total} com o feitiço de XP — as outras não dão bônus");

                if (slots >= 8) Ok($"{slots} slots diferentes cobertos");
                else Nota($"só {slots} slot(s) diferente(s) — esperava 8");

                if (altos > 0)
                    Nota($"{altos} peça(s) com displayid ≥ 32000: o cliente mostra '?' nelas");
                else Ok("nenhum displayid alto — todos resolvem no cliente");
            }
        }

        ListaSituacao.ItemsSource = linhas;
    }

    private async Task<List<string>> ConsultarAsync(string sql)
    {
        var recebidas = new List<string>();
        void Coletar(OutputLine l)
        {
            if (l.Kind == OutputKind.Normal && l.Text.Length > 0) recebidas.Add(l.Text);
        }

        _runner.Output += Coletar;
        try { await _runner.RunAsync("gm-items.ps1", new[] { "-Query", sql }); }
        finally { _runner.Output -= Coletar; }

        // A primeira linha é o cabeçalho do cliente do MySQL.
        return recebidas.Count > 1 ? recebidas.Skip(1).ToList() : new List<string>();
    }
}
