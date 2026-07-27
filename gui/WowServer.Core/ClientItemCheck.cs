namespace WowServer.Core;

/// <summary>
/// O que impede o cliente de mostrar um item direito.
/// </summary>
public enum ClientItemProblem
{
    /// <summary>A entry nao existe no Item.dbc do cliente.</summary>
    ForaDoItemDbc,
    /// <summary>O displayid esta acima do que o cliente 3.3.5a resolve.</summary>
    DisplayIdAltoDemais,
}

/// <summary>Um problema encontrado, ja com o texto pronto para a tela.</summary>
public sealed record ClientItemIssue(ClientItemProblem Problem, string Summary, string Detail);

/// <summary>
/// Confere se um item customizado vai aparecer certo no cliente.
///
/// <para>
/// Vale para qualquer item criado a mao no <c>item_template</c>. O servidor
/// aceita numeros que o cliente nao resolve, e o resultado nao e um erro em
/// lugar nenhum - e uma peca sem icone, que nao equipa com o botao direito e
/// nao faz som. As duas regras abaixo sairam de varias rodadas perdidas no set
/// de heranca de 48 pecas (ver docs/servidor-instalado.md, secao 7.4).
/// </para>
/// </summary>
public static class ClientItemCheck
{
    /// <summary>
    /// Maior displayid que o cliente 3.3.5a resolve na pratica.
    ///
    /// <para>
    /// O ItemDisplayInfo.dbc do servidor vai ate 68742, entao a validacao do
    /// lado do servidor passa com qualquer numero de la. O cliente nao: com um
    /// displayid da era ICC (64190-65131) o item aparece como '?'. As heranças
    /// originais do jogo, que aparecem certas, ficam entre 6337 e 31657 - dai o
    /// limite. E um teto empirico, nao uma constante do core.
    /// </para>
    /// </summary>
    public const int DisplayIdMaximo = 32000;

    /// <summary>
    /// Le as entries do Item.dbc do cliente extraido em <c>Data\dbc</c>.
    ///
    /// <para>
    /// Devolve <c>null</c> quando o arquivo nao existe - o que e diferente de
    /// devolver um conjunto vazio. Sem o arquivo nao da para afirmar que um
    /// item falta nele, e acusar todo item de ausente seria pior que nao
    /// checar.
    /// </para>
    /// </summary>
    public static HashSet<int>? ReadClientItemIds(string dataDir)
    {
        var caminho = Path.Combine(dataDir, "dbc", "Item.dbc");
        if (!File.Exists(caminho)) return null;

        var t = DbcFile.Read(caminho);

        // Item.dbc do 3.3.5a: 8 campos de 4 bytes, o campo 0 e o ID.
        //   0 ID | 1 ClassID | 2 SubclassID | 3 SoundOverrideSubclassID
        //   4 MaterialID | 5 DisplayInfoID | 6 InventoryType | 7 SheatheType
        var ids = new HashSet<int>(t.Records);
        for (var i = 0; i < t.Records; i++) ids.Add((int)DbcFile.Field(t, i, 0));

        return ids;
    }

    /// <summary>
    /// Problemas de um item. <paramref name="clientItemIds"/> vindo de
    /// <see cref="ReadClientItemIds"/>; <c>null</c> pula essa checagem.
    /// </summary>
    public static IReadOnlyList<ClientItemIssue> Check(ItemRow item, HashSet<int>? clientItemIds)
    {
        var achados = new List<ClientItemIssue>();

        if (item.DisplayId >= DisplayIdMaximo)
        {
            achados.Add(new ClientItemIssue(
                ClientItemProblem.DisplayIdAltoDemais,
                $"displayid {item.DisplayId} é alto demais para o cliente 3.3.5a",
                "O item aparece como '?'. O servidor aceita esse número — o "
                + $"ItemDisplayInfo.dbc dele vai bem além —, mas o cliente não o "
                + $"resolve. Escolha um displayid abaixo de {DisplayIdMaximo}: as "
                + "heranças originais do jogo, que aparecem certas, usam de 6337 "
                + "a 31657."));
        }

        if (clientItemIds is not null && !clientItemIds.Contains(item.Entry))
        {
            achados.Add(new ClientItemIssue(
                ClientItemProblem.ForaDoItemDbc,
                $"a entry {item.Entry} não está no Item.dbc do cliente",
                "Sem ícone na bolsa, o botão direito não equipa e não sai som ao "
                + "equipar. O painel de personagem mostra a peça normalmente, "
                + "porque esse caminho usa o displayid que vem no pacote de "
                + "equipamento visível e não consulta o Item.dbc — é por isso que "
                + "o problema parece intermitente. A correção é entregar ao "
                + "cliente um Item.dbc com essa entry, dentro de um MPQ de patch."));
        }

        return achados;
    }

    /// <summary>
    /// Uma linha curta para a lista, ou <c>null</c> quando esta tudo bem.
    /// </summary>
    public static string? ShortWarning(ItemRow item, HashSet<int>? clientItemIds)
    {
        var achados = Check(item, clientItemIds);
        if (achados.Count == 0) return null;

        return "o cliente não mostra este item direito: "
             + string.Join("; ", achados.Select(a => a.Summary));
    }
}
