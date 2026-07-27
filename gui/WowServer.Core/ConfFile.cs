using System.Text;
using System.Text.RegularExpressions;

namespace WowServer.Core;

/// <summary>Uma chave lida de um arquivo .conf, com o comentario que a explica.</summary>
public sealed record ConfEntry(string Key, string Value, string Comment)
{
    /// <summary>O valor sem as aspas, quando o arquivo as usa.</summary>
    public string Unquoted =>
        Value.Length >= 2 && Value[0] == '"' && Value[^1] == '"' ? Value[1..^1] : Value;
}

/// <summary>
/// Le e edita os .conf do AzerothCore (worldserver.conf, os de modulo) sem
/// reescrever o arquivo.
///
/// <para>
/// O formato e <c>Chave = Valor</c>, com comentarios em <c>#</c>. Os arquivos
/// sao quase todos comentario - cada chave vem precedida de um paragrafo
/// explicando o que ela faz, e e isso que a tela mostra. Por isso a edicao e
/// cirurgica: troca-se o valor de uma linha e nada mais e tocado.
/// </para>
/// </summary>
public static class ConfFile
{
    /// <summary>
    /// Uma chave e seu valor. <c>(?=\r?\n|$)</c> no lugar de <c>$</c> porque em
    /// modo multilinha o <c>$</c> casa ANTES do \n e deixa o \r do CRLF de fora
    /// - e estes arquivos sao CRLF. Foi esse detalhe que uma vez duplicou toda
    /// chave de um settings.psd1 em vez de substituir.
    /// </summary>
    private static Regex KeyPattern(string key) => new(
        @"(?m)^(?<lead>[ \t]*" + Regex.Escape(key) + @"[ \t]*=[ \t]*)"
        + @"(?<val>""[^""\r\n]*""|[^\r\n#]*?)"
        + @"(?<tail>[ \t]*(?:#[^\r\n]*)?)(?=\r?\n|$)");

    /// <summary>
    /// Ate onde vale acumular explicacao. Passar disso enche o card de texto
    /// sem ajudar - o resto continua no arquivo, para quem quiser ler la.
    /// </summary>
    private const int LimiteComentario = 500;

    /// <summary>
    /// Diz se uma linha de comentario e explicacao ou enfeite.
    ///
    /// <para>
    /// Nao basta procurar letra: varios modulos abrem o .conf com um banner em
    /// ASCII art, que tem letras de sobra e nao explica nada - o Solocraft faz
    /// isso, e o banner dele virava a "explicacao" da primeira chave. Aqui
    /// exige-se que a linha seja majoritariamente texto.
    /// </para>
    /// </summary>
    private static bool IsProse(string line)
    {
        if (line.Length == 0) return false;

        var letras = line.Count(char.IsLetterOrDigit);
        if (letras < 2) return false;

        var visiveis = line.Count(c => !char.IsWhiteSpace(c));
        return visiveis == 0 || letras * 2 >= visiveis;
    }

    /// <summary>Toda chave do arquivo, na ordem em que aparece.</summary>
    public static IReadOnlyList<ConfEntry> Parse(string text)
    {
        var achados = new List<ConfEntry>();
        var comentario = new StringBuilder();

        foreach (var bruta in text.Split('\n'))
        {
            var linha = bruta.TrimEnd('\r');
            var limpa = linha.Trim();

            // Linha em branco NAO encerra o paragrafo. Nos .conf do AzerothCore
            // o normal e justamente
            //
            //     #    Rate.XP.Kill
            //     #        Description: Experience rates
            //     <linha em branco>
            //     Rate.XP.Kill = 1
            //
            // e tratar o branco como fim do comentario jogava fora a explicacao
            // de TODAS as chaves do arquivo - que e a parte mais util aqui.
            if (limpa.Length == 0) continue;

            if (limpa[0] == '#')
            {
                var texto = limpa.TrimStart('#').Trim();

                if (IsProse(texto) && comentario.Length < LimiteComentario)
                {
                    if (comentario.Length > 0) comentario.Append(' ');
                    comentario.Append(texto);
                }
                continue;
            }

            var igual = linha.IndexOf('=');
            if (igual <= 0) { comentario.Clear(); continue; }

            var chave = linha[..igual].Trim();
            if (chave.Length == 0) { comentario.Clear(); continue; }

            var resto = linha[(igual + 1)..];

            // Comentario no fim da linha so conta fora das aspas: um valor como
            // "127.0.0.1;3306;acore;senha#1;acore_world" e legitimo.
            var valor = resto.Trim();
            if (valor.Length == 0 || valor[0] != '"')
            {
                var cerquilha = resto.IndexOf('#');
                if (cerquilha >= 0) valor = resto[..cerquilha].Trim();
            }

            achados.Add(new ConfEntry(chave, valor, comentario.ToString()));
            comentario.Clear();
        }

        return achados;
    }

    /// <summary>Chave -> valor. A ultima ocorrencia vence, como no proprio core.</summary>
    public static IReadOnlyDictionary<string, string> ReadValues(string path)
    {
        var mapa = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return mapa;

        foreach (var e in Parse(File.ReadAllText(path))) mapa[e.Key] = e.Unquoted;
        return mapa;
    }

    /// <summary>
    /// Troca o valor de uma chave. Devolve <c>null</c> quando a chave nao existe
    /// no arquivo.
    ///
    /// <para>
    /// Nao criar a chave e deliberado: o servidor ignora em silencio o que nao
    /// conhece, entao uma chave inventada nao daria erro nenhum e simplesmente
    /// nao teria efeito - exatamente o tipo de bug que ninguem encontra.
    /// </para>
    /// </summary>
    public static string? SetValue(string text, string key, string value)
    {
        var re = KeyPattern(key);
        if (!re.IsMatch(text)) return null;

        // Se o arquivo usava aspas nesta chave, mantem: e o que o parser do
        // core espera em campos de texto como as strings de conexao.
        return re.Replace(text, m =>
        {
            var antigo = m.Groups["val"].Value;
            var novo = antigo.StartsWith('"') && !value.StartsWith('"') ? '"' + value + '"' : value;
            return m.Groups["lead"].Value + novo + m.Groups["tail"].Value;
        }, 1);
    }

    /// <summary>
    /// Aplica varias trocas de uma vez. <paramref name="naoAchadas"/> recebe as
    /// chaves que nao existiam.
    /// </summary>
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
    /// Grava sem BOM. Um BOM no inicio quebra o parser de configuracao do
    /// AzerothCore, e e o que o Set-Content -Encoding UTF8 do PowerShell 5.1
    /// escreve por padrao.
    /// </summary>
    public static void Save(string path, string text) =>
        File.WriteAllText(path, text, new UTF8Encoding(false));

    /// <summary>
    /// Copia o arquivo para <c>&lt;nome&gt;.bak-aaaa-MM-dd_HHmm</c> antes de
    /// gravar, e devolve o caminho da copia. Uma por minuto: repetir o mesmo
    /// backup a cada clique so esconderia o original entre dezenas de copias.
    /// </summary>
    public static string Backup(string path)
    {
        var copia = path + ".bak-" + DateTime.Now.ToString("yyyy-MM-dd_HHmm");
        if (!File.Exists(copia)) File.Copy(path, copia);
        return copia;
    }
}
