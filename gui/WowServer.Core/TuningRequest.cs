using System.Globalization;

namespace WowServer.Core;

/// <summary>
/// Multiplicadores de coleta. Traduz o que o usuario escolheu na tela para os
/// argumentos do tune-professions.ps1.
/// </summary>
public sealed class GatheringTuning
{
    public double Mining { get; set; }
    public double Herbalism { get; set; }
    public double Fishing { get; set; }
    public double Skinning { get; set; }
    public double Disenchanting { get; set; }
    public double Milling { get; set; }
    public double Prospecting { get; set; }

    public bool IsEmpty =>
        Mining <= 0 && Herbalism <= 0 && Fishing <= 0 && Skinning <= 0
        && Disenchanting <= 0 && Milling <= 0 && Prospecting <= 0;

    public IReadOnlyList<string> ToArguments(bool apply)
    {
        var args = new List<string>();
        void Add(string name, double value)
        {
            if (value > 0)
            {
                args.Add(name);
                args.Add(value.ToString("0.##", CultureInfo.InvariantCulture));
            }
        }

        Add("-Mining", Mining);
        Add("-Herbalism", Herbalism);
        Add("-Fishing", Fishing);
        Add("-Skinning", Skinning);
        Add("-Disenchanting", Disenchanting);
        Add("-Milling", Milling);
        Add("-Prospecting", Prospecting);

        if (apply) args.Add("-Apply");
        return args;
    }

    public static IReadOnlyList<string> ResetArguments() => new[] { "-Reset", "-Apply" };
}

/// <summary>
/// Chance de drop. Separado da quantidade porque mexe em outra coluna e tem
/// outro teto: chance e porcentagem, limitada a 100.
/// </summary>
public sealed class DropChanceTuning
{
    /// <summary>Itens exigidos por quest - o caso do "mate 10, colete 3".</summary>
    public double QuestItems { get; set; }

    /// <summary>Loot comum de criatura. Afeta o jogo inteiro.</summary>
    public double CreatureItems { get; set; }

    public bool IsEmpty => QuestItems <= 0 && CreatureItems <= 0;

    public IReadOnlyList<string> ToArguments(bool apply)
    {
        var args = new List<string>();
        if (QuestItems > 0)
        {
            args.Add("-QuestItems");
            args.Add(QuestItems.ToString("0.##", CultureInfo.InvariantCulture));
        }
        if (CreatureItems > 0)
        {
            args.Add("-CreatureItems");
            args.Add(CreatureItems.ToString("0.##", CultureInfo.InvariantCulture));
        }
        if (apply) args.Add("-Apply");
        return args;
    }

    public static IReadOnlyList<string> ResetArguments() => new[] { "-Reset", "-Apply" };
}

/// <summary>Combinacoes prontas, pra quem nao quer pensar em numero.</summary>
public sealed record TuningPreset(string Name, string Description, GatheringTuning Gathering, DropChanceTuning Drops);

public static class TuningPresets
{
    public static IReadOnlyList<TuningPreset> All { get; } = new[]
    {
        new TuningPreset(
            "Blizzlike",
            "Valores originais. Use para desfazer qualquer ajuste.",
            new GatheringTuning(),
            new DropChanceTuning()),

        new TuningPreset(
            "Suave",
            "Coleta 2x e itens de quest 2x. Tira o tedio sem trivializar.",
            new GatheringTuning { Mining = 2, Herbalism = 2, Fishing = 2, Skinning = 2 },
            new DropChanceTuning { QuestItems = 2 }),

        new TuningPreset(
            "Solo confortavel",
            "Coleta 3x, conversao 2x e itens de quest 3x. Bom para jogar sozinho "
            + "sem passar horas farmando material.",
            new GatheringTuning
            {
                Mining = 3, Herbalism = 3, Fishing = 3, Skinning = 3,
                Disenchanting = 2, Milling = 2, Prospecting = 2,
            },
            new DropChanceTuning { QuestItems = 3 }),

        new TuningPreset(
            "Sem paciencia",
            "Coleta 10x e itens de quest sempre. Enche a bolsa rapido - lembre "
            + "que minerio empilha de 20 em 20.",
            new GatheringTuning
            {
                Mining = 10, Herbalism = 10, Fishing = 10, Skinning = 10,
                Disenchanting = 5, Milling = 5, Prospecting = 5,
            },
            new DropChanceTuning { QuestItems = 10 }),
    };
}
