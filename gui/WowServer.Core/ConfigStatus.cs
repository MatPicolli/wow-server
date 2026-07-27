using System.Globalization;

namespace WowServer.Core;

/// <summary>
/// Compara o que esta no worldserver.conf com o padrao do jogo, para a tela
/// poder mostrar o valor de agora em vez de nascer em branco.
/// </summary>
public static class ConfigStatus
{
    /// <summary>
    /// Diz se os dois textos significam o mesmo numero.
    ///
    /// <para>
    /// Comparar como texto acusaria mudanca em "1" contra "1.0", que e o mesmo
    /// valor escrito de outro jeito - e o arquivo do AzerothCore usa as duas
    /// formas na mesma pagina. Quando algum dos lados nao for numero, cai na
    /// comparacao de texto, que e o certo para chave que nao e numerica.
    /// </para>
    /// </summary>
    public static bool SameValue(string? a, string? b)
    {
        var x = (a ?? string.Empty).Trim();
        var y = (b ?? string.Empty).Trim();

        if (double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var nx) &&
            double.TryParse(y, NumberStyles.Float, CultureInfo.InvariantCulture, out var ny))
        {
            return Math.Abs(nx - ny) < 1e-9;
        }

        return string.Equals(x, y, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Se o valor no arquivo ja foi mexido em relacao ao padrao.</summary>
    public static bool IsChanged(ConfigSetting setting, string? atual) =>
        !string.IsNullOrWhiteSpace(atual) && !SameValue(atual, setting.Default);

    /// <summary>
    /// A linha de baixo do card: categoria, chave e em que pe esta o valor.
    /// </summary>
    public static string Describe(ConfigSetting setting, string? atual)
    {
        var inicio = $"{setting.Category}  ·  {setting.Key}  ·  ";

        if (string.IsNullOrWhiteSpace(atual))
        {
            // Sem arquivo lido nao da para afirmar nada sobre o servidor - e
            // dizer "padrao X" aqui daria a entender que foi conferido.
            return inicio + $"padrão {setting.Default}  ·  não li o arquivo ainda";
        }

        var t = atual.Trim();

        return IsChanged(setting, atual)
            ? inicio + $"agora {t}  (padrão {setting.Default})"
            : inicio + $"agora {t}  ·  é o padrão";
    }
}
