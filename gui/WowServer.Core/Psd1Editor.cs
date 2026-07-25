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
    /// O fim da linha usa lookahead (?=\r?\n|$) em vez de ancora $.
    ///
    /// Em modo multiline o $ do .NET casa imediatamente antes do \n - o que
    /// deixa o \r do CRLF no caminho. Como nenhum grupo consome \r, o padrao
    /// simplesmente nao casava em arquivos gravados no Windows, toda chave era
    /// tratada como inexistente e acabava duplicada no fim do arquivo. O
    /// lookahead aceita as duas convencoes sem consumir nada.
    private static string LinePattern(string key) =>
        @"(?m)^(?<lead>[ \t]*" + Regex.Escape(key) + @"[ \t]*=[ \t]*)"
        + @"(?<val>'(?:[^']|'')*'|""(?:[^""]|"""")*""|[^\s#]*)"
        + @"(?<tail>[ \t]*(?:#[^\r\n]*)?)(?=\r?\n|$)";

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

    /// <summary>
    /// Chaves declaradas mais de uma vez no mesmo nivel.
    ///
    /// Import-PowerShellDataFile recusa hashtable com chave repetida, e o
    /// arquivo inteiro deixa de ser legivel. Como a duplicacao so aparece
    /// depois de gravar, verificamos antes de escrever.
    /// </summary>
    public static IReadOnlyList<string> FindDuplicateKeys(string text)
    {
        var porNivel = new Dictionary<int, Dictionary<string, int>>();
        var duplicadas = new List<string>();
        var nivel = 0;

        foreach (var bruta in text.Split('\n'))
        {
            var linha = bruta.TrimEnd('\r');

            var semComentario = linha.TrimStart().StartsWith("#", StringComparison.Ordinal)
                ? string.Empty
                : linha;

            var m = Regex.Match(semComentario, @"^[ \t]*(?<k>[A-Za-z_]\w*)[ \t]*=");
            if (m.Success)
            {
                var contagem = porNivel.TryGetValue(nivel, out var mapa)
                    ? mapa
                    : porNivel[nivel] = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                var chave = m.Groups["k"].Value;
                contagem[chave] = contagem.GetValueOrDefault(chave) + 1;
                if (contagem[chave] == 2) duplicadas.Add(chave);
            }

            // profundidade depois de contar a chave: 'MySql = @{' declara
            // MySql no nivel de fora e so entao abre um nivel novo
            foreach (var c in semComentario)
            {
                if (c == '{') nivel++;
                else if (c == '}') nivel--;
            }
        }

        return duplicadas;
    }

    /// <summary>Envolve em aspas simples, escapando as que existirem no valor.</summary>
    public static string Quote(string value) => "'" + value.Replace("'", "''") + "'";

    public static string Bool(bool value) => value ? "$true" : "$false";
}
