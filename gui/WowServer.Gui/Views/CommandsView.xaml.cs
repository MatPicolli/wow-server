using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WowServer.Core;

namespace WowServer.Gui.Views;

public sealed class ComandoItem
{
    public required string Titulo { get; init; }
    public required string Categoria { get; init; }
    public required string Descricao { get; init; }
    public required string TextoExibido { get; init; }
    public required string Selo { get; init; }
    public required Brush CorSelo { get; init; }
    public required bool NoConsole { get; init; }
    public string? AvisoPreencher { get; init; }

    public Visibility VisibilidadeEnviar => NoConsole ? Visibility.Visible : Visibility.Collapsed;
    public Visibility VisibilidadeAviso =>
        AvisoPreencher is null ? Visibility.Collapsed : Visibility.Visible;

    public string TextoBusca => $"{Titulo} {Categoria} {Descricao} {TextoExibido}";
}

public partial class CommandsView : UserControl
{
    private readonly ScriptRunner _runner = Session.Current.CreateRunner();
    private List<ComandoItem> _todos = new();

    public CommandsView()
    {
        InitializeComponent();
        Saida.Title = "Saída";

        _runner.Output += linha =>
            Dispatcher.Invoke(() => Saida.Append(linha.Text, linha.Kind));

        Recarregar();
    }

    private void Recarregar()
    {
        var res = Application.Current.Resources;
        var itens = new List<ComandoItem>();

        foreach (var c in GmCommands.All)
        {
            var noConsole = c.Target == CommandTarget.Console;

            itens.Add(new ComandoItem
            {
                Titulo = c.Title,
                Categoria = c.Category,
                Descricao = c.Description,

                // No chat do jogo o comando leva ponto na frente; no console do
                // worldserver, nao. Mostrar ja do jeito certo evita a duvida.
                TextoExibido = noConsole ? c.Template : "." + c.Template,

                Selo = noConsole ? "console" : "no jogo",
                CorSelo = (Brush)(noConsole ? res["Ok"] : res["Accent"]),
                NoConsole = noConsole,
                AvisoPreencher = c.NeedsFilling
                    ? "Troque antes de usar: " + string.Join(", ", c.Placeholders.Select(p => $"<{p}>"))
                    : null,
            });
        }

        _todos = itens;
        AplicarFiltro();
    }

    private void Filtro_Changed(object sender, RoutedEventArgs e) => AplicarFiltro();

    private void AplicarFiltro()
    {
        // TextChanged dispara durante o InitializeComponent, antes de a lista existir.
        if (Lista is null || CaixaFiltro is null) return;

        var termo = CaixaFiltro.Text.Trim();
        IEnumerable<ComandoItem> visiveis = _todos;

        if (termo.Length > 0)
        {
            var alvo = SemAcento(termo);
            visiveis = visiveis.Where(
                i => SemAcento(i.TextoBusca).Contains(alvo, StringComparison.OrdinalIgnoreCase));
        }

        Lista.ItemsSource = visiveis
            .OrderBy(i => GmCommands.Categories.ToList().IndexOf(i.Categoria))
            .ToList();
    }

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

    private ComandoItem? Achar(object sender) =>
        sender is Button { Tag: string titulo }
            ? _todos.FirstOrDefault(i => i.Titulo == titulo)
            : null;

    private void Copiar_Click(object sender, RoutedEventArgs e)
    {
        var item = Achar(sender);
        if (item is null) return;

        try
        {
            Clipboard.SetDataObject(item.TextoExibido, copy: true);
            Saida.Append($"copiado: {item.TextoExibido}");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Não consegui copiar: " + ex.Message, "Área de transferência ocupada");
        }
    }

    private void Enviar_Click(object sender, RoutedEventArgs e)
    {
        var item = Achar(sender);
        if (item is null) return;

        // Marcador nao trocado viraria um comando sem sentido no servidor.
        if (item.AvisoPreencher is not null)
        {
            MessageBox.Show(
                $"Este comando tem partes para preencher:\n\n{item.TextoExibido}\n\n"
                + "Use 'Copiar', troque o que está entre < >, e mande pela caixa de comando "
                + "na aba Servidor.",
                "Falta preencher");
            return;
        }

        EnviarAoServidor(item.TextoExibido);
    }

    /// <returns>true se o comando saiu.</returns>
    private bool EnviarAoServidor(string comando)
    {
        var servidor = Session.Current.Server;
        if (servidor is null || !servidor.WorldRunning)
        {
            MessageBox.Show(
                "O worldserver não está rodando. Ligue o servidor na aba Servidor e tente de novo.",
                "Servidor desligado");
            return false;
        }

        servidor.SendWorldCommand(comando);
        Saida.Append($"> {comando}");
        return true;
    }

    private async void HerancaListar_Click(object sender, RoutedEventArgs e)
    {
        Saida.Append("==> itens de herança que existem no banco");
        await _runner.RunAsync("gm-heirlooms.ps1", new[] { "-Listar" });
    }

    private async void Heranca_Click(object sender, RoutedEventArgs e)
    {
        var personagem = CaixaPersonagem.Text.Trim();
        if (personagem.Length == 0)
        {
            MessageBox.Show("Escreva o nome do personagem que vai receber as cartas.", "Falta o nome");
            return;
        }

        var servidor = Session.Current.Server;
        if (servidor is null || !servidor.WorldRunning)
        {
            MessageBox.Show(
                "O worldserver precisa estar rodando para enviar o correio.\n\n"
                + "Ligue na aba Servidor e tente de novo.",
                "Servidor desligado");
            return;
        }

        // O script consulta o banco e imprime os comandos; quem envia e a GUI,
        // porque so ela tem o worldserver na mao. Assim o script continua util
        // sozinho, para quem preferir copiar e colar.
        Saida.Append($"==> montando o kit de herança para {personagem}");

        var comandos = new List<string>();
        void Coletar(OutputLine linha)
        {
            var t = linha.Text.Trim();
            if (t.StartsWith("send items ", StringComparison.OrdinalIgnoreCase))
                comandos.Add(t);
        }

        _runner.Output += Coletar;
        int codigo;
        try
        {
            codigo = await _runner.RunAsync("gm-heirlooms.ps1", new[] { "-Character", personagem });
        }
        finally
        {
            _runner.Output -= Coletar;
        }

        if (codigo != 0)
        {
            Saida.Append("[erro] não consegui montar a lista — veja a mensagem acima", OutputKind.Error);
            return;
        }

        if (comandos.Count == 0)
        {
            Saida.Append("[aviso] nenhum comando foi gerado");
            return;
        }

        var resposta = MessageBox.Show(
            $"Enviar {comandos.Count} carta(s) de itens de herança para '{personagem}'?\n\n"
            + "Elas chegam no correio do jogo. Se o personagem estiver logado, pode ser "
            + "preciso sair e entrar para o correio aparecer.",
            "Confirmar envio", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (resposta != MessageBoxResult.Yes)
        {
            Saida.Append("    envio cancelado");
            return;
        }

        foreach (var c in comandos)
        {
            if (!EnviarAoServidor(c)) return;
        }

        Saida.Append($"[ok] {comandos.Count} carta(s) enviada(s)");
    }
}
