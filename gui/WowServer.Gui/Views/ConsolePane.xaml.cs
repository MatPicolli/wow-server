using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WowServer.Core;

namespace WowServer.Gui.Views;

public sealed record ConsoleLine(string Text, Brush Cor);

public partial class ConsolePane : UserControl
{
    /// <summary>
    /// Teto de linhas guardadas. O worldserver despeja dezenas de milhares de
    /// linhas aplicando SQL no primeiro start; sem limite, a memoria cresce
    /// sem parar e a rolagem trava.
    /// </summary>
    private const int MaxLinhas = 5000;

    private readonly ObservableCollection<ConsoleLine> _linhas = new();
    private bool _rolagemAutomatica = true;

    /// <summary>
    /// Todos os painéis vivos, para o botão "limpar tudo".
    ///
    /// WeakReference de propósito: cada aba cria o seu, e uma lista comum
    /// manteria vivo para sempre qualquer painel que a interface descartasse.
    /// </summary>
    private static readonly List<WeakReference<ConsolePane>> _todos = new();

    /// <summary>Esvazia todos os painéis abertos, em qualquer aba.</summary>
    public static void LimparTodos()
    {
        lock (_todos)
        {
            // A varredura aproveita para descartar as referências mortas.
            _todos.RemoveAll(r => !r.TryGetTarget(out _));

            foreach (var referencia in _todos)
            {
                if (referencia.TryGetTarget(out var painel))
                    painel.Dispatcher.Invoke(painel.Clear);
            }
        }
    }

    /// <summary>
    /// Modo de quebra das linhas. DependencyProperty (e nao propriedade
    /// comum) porque o DataTemplate se liga a ela: assim alternar o
    /// checkbox reflete nas linhas ja exibidas, sem recriar a lista.
    /// </summary>
    public static readonly DependencyProperty QuebraProperty =
        DependencyProperty.Register(
            nameof(Quebra), typeof(TextWrapping), typeof(ConsolePane),
            new PropertyMetadata(TextWrapping.Wrap));

    public TextWrapping Quebra
    {
        get => (TextWrapping)GetValue(QuebraProperty);
        set => SetValue(QuebraProperty, value);
    }

    /// <summary>Atalho booleano, para ligar ao estado salvo da interface.</summary>
    public bool WrapEnabled
    {
        get => Quebra == TextWrapping.Wrap;
        set
        {
            Quebra = value ? TextWrapping.Wrap : TextWrapping.NoWrap;
            ChkQuebrar.IsChecked = value;
        }
    }

    public event Action<bool>? WrapChanged;

    public ConsolePane()
    {
        InitializeComponent();
        Linhas.ItemsSource = _linhas;

        lock (_todos) _todos.Add(new WeakReference<ConsolePane>(this));

        // So agora todos os elementos existem, entao marcar o checkbox e
        // seguro - o handler vai encontrar a ListBox montada.
        ChkQuebrar.IsChecked = true;
    }

    private void Quebrar_Changed(object sender, RoutedEventArgs e)
    {
        // Cinto de seguranca: se algum dia este evento voltar a disparar
        // durante a construcao, sair calado e melhor do que derrubar a janela
        // com uma excecao dentro de um setter de propriedade.
        if (Linhas is null || ChkQuebrar is null) return;

        var ligado = ChkQuebrar.IsChecked == true;
        Quebra = ligado ? TextWrapping.Wrap : TextWrapping.NoWrap;

        // Sem rolagem horizontal o item nao tem como exceder o viewport, que e
        // justamente o que permite a quebra; com NoWrap ela precisa voltar.
        ScrollViewer.SetHorizontalScrollBarVisibility(
            Linhas, ligado ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto);

        WrapChanged?.Invoke(ligado);
    }

    public event Action<string>? CommandSubmitted;

    public string Title
    {
        get => (string)Titulo.Text;
        set => Titulo.Text = value;
    }

    /// <summary>Mostra a barra de entrada de comandos (so faz sentido no worldserver).</summary>
    public bool AcceptsCommands
    {
        get => BarraComando.Visibility == Visibility.Visible;
        set => BarraComando.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetState(string texto, bool rodando)
    {
        Estado.Text = texto;
        Indicador.Fill = rodando
            ? (Brush)Application.Current.Resources["Ok"]
            : (Brush)Application.Current.Resources["TextDim"];
    }

    public void Append(string texto, OutputKind kind = OutputKind.Normal)
    {
        var cor = kind == OutputKind.Error
            ? (Brush)Application.Current.Resources["Err"]
            : PelaConvencaoDosScripts(texto);

        _linhas.Add(new ConsoleLine(texto, cor));

        while (_linhas.Count > MaxLinhas) _linhas.RemoveAt(0);

        if (_rolagemAutomatica && _linhas.Count > 0)
            Linhas.ScrollIntoView(_linhas[^1]);
    }

    public void Clear() => _linhas.Clear();

    /// <summary>
    /// Os scripts marcam as linhas com [ok], [aviso], [erro] e "==>". Colorir
    /// por esses marcadores da o mesmo destaque que existe no PowerShell.
    /// </summary>
    private static Brush PelaConvencaoDosScripts(string texto)
    {
        var res = Application.Current.Resources;
        if (texto.Contains("[erro]", StringComparison.Ordinal))  return (Brush)res["Err"];
        if (texto.Contains("[aviso]", StringComparison.Ordinal)) return (Brush)res["Warn"];
        if (texto.Contains("[ok]", StringComparison.Ordinal))    return (Brush)res["Ok"];
        if (texto.StartsWith("==>", StringComparison.Ordinal))   return (Brush)res["Accent"];
        if (texto.StartsWith("> ", StringComparison.Ordinal))    return (Brush)res["Accent"];
        return (Brush)res["Text"];
    }

    public void SetAutoScroll(bool ligado) => _rolagemAutomatica = ligado;

    /// <summary>
    /// Mostra a faixa de progresso. Sem porcentagem (percent = null) a barra
    /// vira indeterminada - util quando se sabe que algo esta rodando mas nao
    /// quanto falta.
    /// </summary>
    public void ShowProgress(string rotulo, double? percent, string detalhe = "")
    {
        FaixaProgresso.Visibility = Visibility.Visible;
        RotuloProgresso.Text = rotulo;
        DetalheProgresso.Text = detalhe;

        if (percent is null)
        {
            Barra.IsIndeterminate = true;
        }
        else
        {
            Barra.IsIndeterminate = false;
            Barra.Value = Math.Clamp(percent.Value, 0, 100);
        }
    }

    public void HideProgress() => FaixaProgresso.Visibility = Visibility.Collapsed;

    /// <summary>Encerramento visivel de uma tarefa longa, com destaque de cor.</summary>
    public void AppendBanner(string texto, bool sucesso)
    {
        var res = Application.Current.Resources;
        var cor = (Brush)(sucesso ? res["Ok"] : res["Err"]);
        var borda = new string('─', Math.Max(20, texto.Length + 4));

        _linhas.Add(new ConsoleLine("", cor));
        _linhas.Add(new ConsoleLine(borda, cor));
        _linhas.Add(new ConsoleLine("  " + texto, cor));
        _linhas.Add(new ConsoleLine(borda, cor));
        _linhas.Add(new ConsoleLine("", cor));

        while (_linhas.Count > MaxLinhas) _linhas.RemoveAt(0);
        if (_rolagemAutomatica && _linhas.Count > 0) Linhas.ScrollIntoView(_linhas[^1]);
    }

    /// <summary>Todo o conteudo do painel como texto puro.</summary>
    public string GetText() => string.Join(Environment.NewLine, _linhas.Select(l => l.Text));

    private void LimparTudo_Click(object sender, RoutedEventArgs e)
    {
        LimparTodos();
        Estado.Text = "consoles limpos";
    }

    private void Copiar_Click(object sender, RoutedEventArgs e)
    {
        if (_linhas.Count == 0)
        {
            Estado.Text = "nada para copiar";
            return;
        }

        // A area de transferencia pode estar momentaneamente presa por outro
        // programa; SetText tem retry embutido e devolve sem lancar.
        try
        {
            Clipboard.SetDataObject(GetText(), copy: true);
            Estado.Text = $"{_linhas.Count} linhas copiadas";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Não consegui copiar: " + ex.Message + "\n\nUse 'salvar...' como alternativa.",
                "Área de transferência ocupada");
        }
    }

    private void Salvar_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Salvar o conteúdo do console",
            Filter = "Texto|*.txt|Todos|*.*",
            FileName = $"{Titulo.Text.Replace(" ", "-")}-{DateTime.Now:yyyyMMdd-HHmm}.txt",
        };

        if (dlg.ShowDialog() != true) return;

        try
        {
            // UTF-8 sem BOM: abre certo em qualquer editor e nao suja o inicio
            // do arquivo se voce for colar o conteudo em outro lugar.
            File.WriteAllText(dlg.FileName, GetText(), new UTF8Encoding(false));
            Estado.Text = "salvo";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Não consegui salvar");
        }
    }

    private void Enviar_Click(object sender, RoutedEventArgs e) => Submeter();

    private void CaixaComando_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Submeter();
    }

    private void Submeter()
    {
        var texto = CaixaComando.Text.Trim();
        if (texto.Length == 0) return;
        CaixaComando.Clear();
        CommandSubmitted?.Invoke(texto);
    }
}
