using System.Text.Json;
using System.Text.Json.Serialization;

namespace WowServer.Core;

/// <summary>
/// Preferencias da interface, guardadas entre execucoes.
///
/// Separado do settings.psd1 de proposito: aquele e configuracao do servidor,
/// versionavel e editavel a mao; este e so o estado da janela do usuario.
/// Fica em %LOCALAPPDATA% para sobreviver a reclonar o repositorio.
/// </summary>
public sealed class UiState
{
    // Janela
    public double WindowWidth { get; set; } = 1440;
    public double WindowHeight { get; set; } = 880;
    public double WindowLeft { get; set; } = double.NaN;
    public double WindowTop { get; set; } = double.NaN;
    public int SelectedTab { get; set; }

    // Consoles
    public bool ConsoleWrap { get; set; } = true;
    public bool ServerSideBySide { get; set; } = true;

    // Ajustes de jogo: guarda o que foi digitado, mesmo sem ter aplicado
    public double Mining { get; set; }
    public double Herbalism { get; set; }
    public double Fishing { get; set; }
    public double Skinning { get; set; }
    public double Disenchanting { get; set; }
    public double Milling { get; set; }
    public double Prospecting { get; set; }
    public double QuestItems { get; set; }
    public double CreatureItems { get; set; }

    [JsonIgnore]
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WowServerManager", "ui-state.json");

    private static readonly JsonSerializerOptions Opcoes = new() { WriteIndented = true };

    public static UiState Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (!File.Exists(path)) return new UiState();
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<UiState>(json) ?? new UiState();
        }
        catch
        {
            // Arquivo corrompido nao pode impedir o programa de abrir.
            return new UiState();
        }
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(this, Opcoes));
        }
        catch
        {
            // Perder preferencia de janela nao justifica derrubar nada.
        }
    }

    public GatheringTuning ToGathering() => new()
    {
        Mining = Mining,
        Herbalism = Herbalism,
        Fishing = Fishing,
        Skinning = Skinning,
        Disenchanting = Disenchanting,
        Milling = Milling,
        Prospecting = Prospecting,
    };

    public DropChanceTuning ToDrops() => new()
    {
        QuestItems = QuestItems,
        CreatureItems = CreatureItems,
    };

    public void From(GatheringTuning g, DropChanceTuning d)
    {
        Mining = g.Mining;
        Herbalism = g.Herbalism;
        Fishing = g.Fishing;
        Skinning = g.Skinning;
        Disenchanting = g.Disenchanting;
        Milling = g.Milling;
        Prospecting = g.Prospecting;
        QuestItems = d.QuestItems;
        CreatureItems = d.CreatureItems;
    }

    /// <summary>
    /// Uma posicao gravada pode ter vindo de um monitor que nao existe mais.
    /// Restaurar cegamente abriria a janela fora da tela.
    /// </summary>
    public bool HasUsablePosition(double virtualLeft, double virtualTop,
                                  double virtualWidth, double virtualHeight)
    {
        if (double.IsNaN(WindowLeft) || double.IsNaN(WindowTop)) return false;

        // exige que uma faixa razoavel do topo da janela esteja visivel
        const double margem = 120;
        return WindowLeft + margem >= virtualLeft
            && WindowTop >= virtualTop
            && WindowLeft + margem <= virtualLeft + virtualWidth
            && WindowTop + margem <= virtualTop + virtualHeight;
    }
}
