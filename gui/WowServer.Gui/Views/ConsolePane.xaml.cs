using System.Collections.ObjectModel;
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

    public ConsolePane()
    {
        InitializeComponent();
        Linhas.ItemsSource = _linhas;
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
