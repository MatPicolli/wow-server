namespace WowServer.Core;

/// <summary>Uma magia do Spell.dbc do cliente.</summary>
public sealed record SpellEntry(int Id, string Name);

/// <summary>
/// Le os nomes das magias do <c>Spell.dbc</c> extraido do cliente.
///
/// <para>
/// Serve para pendurar num item uma magia que <b>ja existe</b>. Criar uma magia
/// nova nao e possivel pelo servidor: efeito, nome e icone de toda magia moram
/// neste arquivo, que e do cliente - por isso um "buff novo" exigiria entregar
/// um Spell.dbc alterado dentro de um MPQ de patch.
/// </para>
/// </summary>
public static class SpellDbc
{
    /// <summary>Quantidade de colunas do Spell.dbc do 3.3.5a (build 12340).</summary>
    public const int CamposEsperados = 234;

    /// <summary>
    /// Coluna do nome em ingles.
    ///
    /// <para>
    /// O bloco de nome tem 16 idiomas seguidos de uma mascara de bits; o
    /// primeiro deles e o enUS. Como esse indice depende do layout exato do
    /// arquivo, ele so e usado quando a contagem de colunas confere com a do
    /// 3.3.5a, e o resultado ainda passa por uma conferencia de sanidade — ver
    /// <see cref="Read"/>.
    /// </para>
    /// </summary>
    public const int CampoNome = 136;

    /// <summary>
    /// Devolve id -> nome, ou <c>null</c> quando o arquivo nao existe ou nao e
    /// o Spell.dbc que este codigo sabe ler.
    ///
    /// <para>
    /// Devolver <c>null</c> em vez de uma lista com lixo dentro e proposital:
    /// nome errado numa lista de magias levaria a prender no item a magia
    /// errada, e isso so apareceria dentro do jogo.
    /// </para>
    /// </summary>
    public static IReadOnlyList<SpellEntry>? Read(string dataDir)
    {
        var caminho = Path.Combine(dataDir, "dbc", "Spell.dbc");
        if (!File.Exists(caminho)) return null;

        DbcFile.Table t;
        try { t = DbcFile.Read(caminho); }
        catch { return null; }

        if (t.Fields != CamposEsperados || t.Records == 0) return null;

        var achados = new List<SpellEntry>(t.Records);
        var comNome = 0;

        for (var i = 0; i < t.Records; i++)
        {
            var id = (int)DbcFile.Field(t, i, 0);
            var nome = DbcFile.Text(t, DbcFile.Field(t, i, CampoNome));

            if (nome.Length > 0) comNome++;
            achados.Add(new SpellEntry(id, nome));
        }

        // Conferencia de sanidade: se a coluna estivesse errada, quase tudo
        // sairia vazio (offset apontando para dentro do bloco de strings em
        // lugar nenhum). Num Spell.dbc de verdade a grande maioria tem nome.
        if (comNome * 2 < t.Records) return null;

        return achados;
    }

    /// <summary>
    /// Procura por nome ou por ID. Termo numerico casa o ID exato primeiro.
    /// </summary>
    public static IReadOnlyList<SpellEntry> Search(
        IReadOnlyList<SpellEntry> todas, string termo, int limite = 60)
    {
        var t = termo.Trim();
        if (t.Length == 0) return Array.Empty<SpellEntry>();

        var resultado = new List<SpellEntry>();

        if (int.TryParse(t, out var id))
        {
            var exata = todas.FirstOrDefault(s => s.Id == id);
            if (exata is not null) resultado.Add(exata);
        }

        foreach (var s in todas)
        {
            if (resultado.Count >= limite) break;
            if (s.Name.Length == 0) continue;
            if (!s.Name.Contains(t, StringComparison.OrdinalIgnoreCase)) continue;
            if (resultado.Any(x => x.Id == s.Id)) continue;
            resultado.Add(s);
        }

        return resultado;
    }
}
