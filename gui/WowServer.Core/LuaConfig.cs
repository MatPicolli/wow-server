using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace WowServer.Core;

/// <summary>Uma chave lida da tabela de configuracao Lua.</summary>
public sealed record LuaSetting(string Key, string Value, string Comment)
{
    /// <summary>O valor sem as aspas, quando e string.</summary>
    public string Unquoted =>
        Value.Length >= 2 && Value[0] == '"' && Value[^1] == '"' ? Value[1..^1] : Value;

    public bool IsBoolean =>
        Value is "true" or "false";

    public bool? AsBoolean => Value switch { "true" => true, "false" => false, _ => null };

    public double? AsNumber =>
        double.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
}

/// <summary>
/// Le e edita <c>CHAVE = valor,</c> dentro de uma tabela Lua, sem reescrever o
/// arquivo.
///
/// <para>
/// Feito para o <c>01_prestige_config.lua</c>, que e uma tabela
/// <c>Prestige.Config = { ... }</c> com um comentario acima de quase toda
/// chave explicando o que ela faz e o que quebra se ela estiver errada. Regerar
/// o arquivo jogaria isso fora, e e justamente esse texto que a tela mostra
/// como ajuda de cada campo — mesma razao do <see cref="ConfFile"/> e do
/// <c>Psd1Editor</c>.
/// </para>
///
/// <para>
/// Nao e um interpretador de Lua: le so <c>NOME = valor,</c> onde o valor e
/// numero, booleano ou string entre aspas duplas. Tabela aninhada e expressao
/// ficam de fora de proposito — o que este editor nao entende, ele nao mexe.
/// </para>
/// </summary>
public static class LuaConfig
{
    /// <summary>
    /// Uma chave da tabela. O <c>(?=\r?\n|$)</c> no lugar de <c>$</c> pelo
    /// mesmo motivo de sempre: em modo multilinha o <c>$</c> casa antes do
    /// <c>\n</c> e deixa o <c>\r</c> do CRLF de fora.
    /// </summary>
    private static Regex KeyPattern(string key) => new(
        @"(?m)^(?<lead>[ \t]*" + Regex.Escape(key) + @"[ \t]*=[ \t]*)"
        + @"(?<val>""(?:[^""\\\r\n]|\\.)*""|[^,\r\n]*?)"
        + @"(?<tail>[ \t]*,?[ \t]*(?:--[^\r\n]*)?)(?=\r?\n|$)");

    /// <summary>
    /// Chave -> maiuscula e sublinhado. So chaves nesse formato sao
    /// consideradas: e a convencao do arquivo, e evita confundir codigo Lua
    /// solto (<c>local x = 1</c>) com configuracao.
    /// </summary>
    private static readonly Regex LinhaDeChave = new(
        @"^[ \t]*(?<key>[A-Z][A-Z0-9_]*)[ \t]*=[ \t]*"
        + @"(?<val>""(?:[^""\\]|\\.)*""|[-+]?[0-9]*\.?[0-9]+|true|false)"
        + @"[ \t]*,?[ \t]*(?:--(?<inline>[^\r\n]*))?[ \t]*$");

    /// <summary>Toda chave de configuracao do arquivo, na ordem.</summary>
    public static IReadOnlyList<LuaSetting> Parse(string text)
    {
        var achados = new List<LuaSetting>();
        var comentario = new StringBuilder();

        foreach (var bruta in text.Split('\n'))
        {
            var linha = bruta.TrimEnd('\r');
            var limpa = linha.Trim();

            if (limpa.Length == 0)
            {
                // Linha em branco separa blocos de verdade neste arquivo -
                // diferente dos .conf do AzerothCore, onde o branco fica ENTRE
                // o comentario e a chave. Aqui o comentario vem colado.
                comentario.Clear();
                continue;
            }

            // Comentario de bloco --[[ ... ]] e cabecalho de secao: nao explica
            // uma chave, e arrastaria texto grande para o card.
            if (limpa.StartsWith("--[[", StringComparison.Ordinal) ||
                limpa.StartsWith("]]", StringComparison.Ordinal))
            {
                comentario.Clear();
                continue;
            }

            if (limpa.StartsWith("--", StringComparison.Ordinal))
            {
                var texto = limpa.TrimStart('-').Trim();

                // Linha de enfeite ('-----', '-- ---') nao explica nada.
                if (texto.Any(char.IsLetterOrDigit))
                {
                    if (comentario.Length > 0) comentario.Append(' ');
                    comentario.Append(texto);
                }
                continue;
            }

            var m = LinhaDeChave.Match(linha);
            if (!m.Success) { comentario.Clear(); continue; }

            var explicacao = comentario.ToString();
            var inline = m.Groups["inline"].Success ? m.Groups["inline"].Value.Trim() : "";
            if (inline.Length > 0)
                explicacao = explicacao.Length > 0 ? explicacao + " " + inline : inline;

            achados.Add(new LuaSetting(m.Groups["key"].Value, m.Groups["val"].Value, explicacao));
            comentario.Clear();
        }

        return achados;
    }

    /// <summary>Chave -> valor cru, para consulta rapida.</summary>
    public static IReadOnlyDictionary<string, string> ReadValues(string path)
    {
        var mapa = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path)) return mapa;

        foreach (var s in Parse(File.ReadAllText(path))) mapa[s.Key] = s.Value;
        return mapa;
    }

    /// <summary>
    /// Troca o valor de uma chave. <c>null</c> quando a chave nao existe.
    ///
    /// <para>
    /// Nao criar a chave e deliberado: o Lua nao reclama de chave desconhecida
    /// na tabela, entao um nome errado passaria em silencio e simplesmente nao
    /// teria efeito nenhum.
    /// </para>
    /// </summary>
    public static string? SetValue(string text, string key, string value)
    {
        var re = KeyPattern(key);
        if (!re.IsMatch(text)) return null;

        return re.Replace(text, m => m.Groups["lead"].Value + value + m.Groups["tail"].Value, 1);
    }

    /// <summary>Aplica varias trocas; <paramref name="naoAchadas"/> recebe o que faltou.</summary>
    public static string SetValues(
        string text, IEnumerable<KeyValuePair<string, string>> changes, out List<string> naoAchadas)
    {
        naoAchadas = new List<string>();

        foreach (var (chave, valor) in changes)
        {
            var novo = SetValue(text, chave, valor);
            if (novo is null) naoAchadas.Add(chave);
            else text = novo;
        }

        return text;
    }

    /// <summary>
    /// Formata um valor digitado pelo usuario para a sintaxe Lua, conforme o
    /// tipo do valor que ja estava lá.
    ///
    /// <para>
    /// Devolve <c>null</c> quando o texto nao serve. Nao adivinhar tipo e o
    /// ponto: gravar <c>true</c> onde havia um numero, ou um numero onde havia
    /// string, produz um arquivo que carrega e se comporta de um jeito que nao
    /// da para explicar.
    /// </para>
    /// </summary>
    public static string? FormatLike(LuaSetting original, string digitado)
    {
        var t = (digitado ?? string.Empty).Trim();

        if (original.IsBoolean)
        {
            return t.ToLowerInvariant() switch
            {
                "true" or "sim" or "1" => "true",
                "false" or "nao" or "não" or "0" => "false",
                _ => null,
            };
        }

        if (original.AsNumber is not null)
        {
            // Virgula decimal e o que sai do teclado brasileiro; o arquivo e
            // lido com ponto.
            var n = t.Replace(',', '.');
            if (!double.TryParse(n, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                return null;

            // Inteiro continua inteiro: gravar 30.0 onde havia 30 funciona em
            // Lua mas suja a diferenca num diff.
            var eraInteiro = !original.Value.Contains('.');
            return eraInteiro && v == Math.Floor(v)
                ? ((long)v).ToString(CultureInfo.InvariantCulture)
                : v.ToString("0.0###", CultureInfo.InvariantCulture);
        }

        // String: aspas duplas, e escapa o que quebraria a linha.
        if (t.Contains('\n') || t.Contains('\r')) return null;
        return '"' + t.Replace("\\", "\\\\").Replace("\"", "\\\"") + '"';
    }

    /// <summary>
    /// Grava sem BOM. Alguns builds de Lua recusam um arquivo com BOM, com uma
    /// mensagem que nao fala de BOM nenhum — e o <c>Set-Content -Encoding UTF8</c>
    /// do PowerShell 5.1 escreve BOM por padrao.
    /// </summary>
    public static void Save(string path, string text) =>
        File.WriteAllText(path, text, new UTF8Encoding(false));
}
