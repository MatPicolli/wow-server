using System.Text.RegularExpressions;

namespace WowServer.Core;

/// <summary>
/// Edicao cirurgica do config/settings.psd1.
///
/// Reescrever o arquivo inteiro a partir de um objeto perderia comentarios -
/// e o settings.psd1 e mais comentario explicativo do que valor. Entao aqui
/// trocamos so o trecho da chave pedida e deixamos o resto intacto.
/// </summary>
public static class Psd1Editor
{
    /// <summary>
    /// Padrao de uma linha "chave = valor".
    ///
    /// Captura em partes para trocar SO o valor:
    ///   lead - indentacao, nome, espacos e o '='  (mantem o alinhamento)
    ///   val  - string entre aspas (com '' escapado) ou token solto
    ///   tail - espacos e comentario de fim de linha (mantem a explicacao)
    ///
    /// Sem isso, reescrever a linha inteira apagaria tanto o alinhamento
    /// quanto os comentarios - e o settings.psd1 e feito para ser lido.
    /// </summary>
    private static string LinePattern(string key) =>
        @"(?m)^(?<lead>[ \t]*" + Regex.Escape(key) + @"[ \t]*=[ \t]*)"
        + @"(?<val>'(?:[^']|'')*'|""(?:[^""]|"""")*""|[^\s#]*)"
        + @"(?<tail>[ \t]*(?:#[^\r\n]*)?)$";

    /// <summary>Troca uma chave de primeiro nivel: Chave = valor</summary>
    public static string SetScalar(string text, string key, string rawValue)
    {
        var pattern = LinePattern(key);

        if (Regex.IsMatch(text, pattern))
        {
            return Regex.Replace(text, pattern,
                m => m.Groups["lead"].Value + rawValue + m.Groups["tail"].Value);
        }

        // Chave ausente: acrescenta antes do fecha-chaves final do hashtable.
        return AppendBeforeClosingBrace(text, $"    {key} = {rawValue}");
    }

    /// <summary>
    /// Troca uma chave dentro de um bloco aninhado, ex.: MySql = @{ ... }.
    /// So mexe dentro dos limites do bloco, pra nao acertar uma chave de mesmo
    /// nome que exista fora dele.
    /// </summary>
    public static string SetNested(string text, string section, string key, string rawValue)
    {
        var (start, end) = FindBlock(text, section);
        if (start < 0) return text;

        var block = text.Substring(start, end - start);
        var pattern = LinePattern(key);

        string updated;
        if (Regex.IsMatch(block, pattern))
        {
            updated = Regex.Replace(block, pattern,
                m => m.Groups["lead"].Value + rawValue + m.Groups["tail"].Value);
        }
        else
        {
            // insere logo depois do @{ que abre o bloco
            var open = block.IndexOf('{');
            if (open < 0) return text;
            updated = block.Insert(open + 1, Environment.NewLine + $"        {key} = {rawValue}");
        }

        return text.Substring(0, start) + updated + text.Substring(end);
    }

    /// <summary>Delimita "secao = @{ ... }" contando chaves, para suportar aninhamento.</summary>
    private static (int start, int end) FindBlock(string text, string section)
    {
        var header = Regex.Match(text, @"(?m)^[ \t]*" + Regex.Escape(section) + @"[ \t]*=[ \t]*@\{");
        if (!header.Success) return (-1, -1);

        var i = text.IndexOf('{', header.Index);
        if (i < 0) return (-1, -1);

        var depth = 0;
        for (var p = i; p < text.Length; p++)
        {
            if (text[p] == '{') depth++;
            else if (text[p] == '}')
            {
                depth--;
                if (depth == 0) return (header.Index, p + 1);
            }
        }
        return (-1, -1);
    }

    private static string AppendBeforeClosingBrace(string text, string line)
    {
        var last = text.LastIndexOf('}');
        if (last < 0) return text + Environment.NewLine + line + Environment.NewLine;
        return text.Insert(last, line + Environment.NewLine);
    }

    /// <summary>Envolve em aspas simples, escapando as que existirem no valor.</summary>
    public static string Quote(string value) => "'" + value.Replace("'", "''") + "'";

    public static string Bool(bool value) => value ? "$true" : "$false";
}
