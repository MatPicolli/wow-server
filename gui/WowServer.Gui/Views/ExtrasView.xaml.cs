using System.Windows.Controls;

namespace WowServer.Gui.Views;

/// <summary>
/// Casca das sub-abas de Extras. Sem lógica: cada mod cuida de si na sua
/// própria view, e o TabControl só troca qual delas está visível.
/// </summary>
public partial class ExtrasView : UserControl
{
    public ExtrasView() => InitializeComponent();
}
