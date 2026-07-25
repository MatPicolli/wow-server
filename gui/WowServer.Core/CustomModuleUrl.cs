using System.Diagnostics.CodeAnalysis;

namespace WowServer.Core;

/// <summary>
/// Valida o endereco de um modulo que o usuario cola na mao e descobre em que
/// pasta ele vai ficar.
///
/// O catalogo cobre uma dezena de modulos; o AzerothCore tem muito mais, e
/// aparecem novos. Em vez de deixar a lista envelhecer, a GUI aceita qualquer
/// repositorio - mas o nome da pasta vem da URL, e a URL vem digitada, entao
/// aqui e onde se garante que ela nao vira um caminho perigoso nem uma opcao de
/// linha de comando disfarcada.
/// </summary>
public static class CustomModuleUrl
{
    /// <summary>
    /// Diz se a URL serve e devolve o nome da pasta de destino.
    /// </summary>
    /// <param name="folderName">Nome da pasta dentro de modules/.</param>
    /// <param name="error">Mensagem pronta para mostrar, quando nao serve.</param>
    public static bool TryParse(
        string? url,
        [NotNullWhen(true)] out string? folderName,
        [NotNullWhen(false)] out string? error)
    {
        folderName = null;
        error = null;

        var limpo = (url ?? string.Empty).Trim();

        if (limpo.Length == 0)
        {
            error = "Cole o endereço do repositório.";
            return false;
        }

        // Um argumento que comeca com '-' seria lido pelo git como opcao.
        if (limpo.StartsWith("-", StringComparison.Ordinal))
        {
            error = "Endereço inválido: não pode começar com '-'.";
            return false;
        }

        if (limpo.Any(char.IsWhiteSpace))
        {
            error = "O endereço não pode conter espaços.";
            return false;
        }

        var ehHttp = limpo.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                  || limpo.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
        var ehSsh = limpo.StartsWith("git@", StringComparison.OrdinalIgnoreCase)
                 || limpo.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase);

        if (!ehHttp && !ehSsh)
        {
            error = "Use um endereço https:// (ou ssh) de um repositório git. "
                  + "Exemplo: https://github.com/azerothcore/mod-transmog";
            return false;
        }

        // O array explicito e obrigatorio: Split('/', ':', opcoes) liga em
        // Split(char, int, StringSplitOptions) e o ':' vira 'count'.
        var partes = limpo.TrimEnd('/')
                          .Split(new[] { '/', ':' }, StringSplitOptions.RemoveEmptyEntries);
        var ultimo = partes.Length > 0 ? partes[^1] : string.Empty;

        if (ultimo.Length == 0)
        {
            error = "Não consegui descobrir o nome do módulo nesse endereço.";
            return false;
        }

        // A checagem vem ANTES de tirar o '.git': depois dela, ".git" viraria
        // string vazia e o nome acabaria herdado do dono do repositorio. O nome
        // vira caminho de pasta, e '..' escaparia de modules/ para dentro do
        // codigo-fonte do core.
        if (ultimo.StartsWith(".", StringComparison.Ordinal))
        {
            error = $"Nome de módulo inválido: '{ultimo}'.";
            return false;
        }

        if (ultimo.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            ultimo = ultimo[..^4];

        if (ultimo.Length == 0)
        {
            error = "Não consegui descobrir o nome do módulo nesse endereço.";
            return false;
        }

        if (!ultimo.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
        {
            error = $"O nome '{ultimo}' tem caracteres que não valem para uma pasta.";
            return false;
        }

        folderName = ultimo;
        return true;
    }

    /// <summary>
    /// Aviso a mostrar antes de instalar, ou null quando nao ha o que avisar.
    /// Separado do erro de proposito: nada aqui impede a instalacao.
    /// </summary>
    public static string? Advice(string folderName) =>
        folderName.StartsWith("mod-", StringComparison.OrdinalIgnoreCase)
            ? null
            : $"'{folderName}' não começa com 'mod-'. Módulos do AzerothCore seguem "
            + "essa convenção — confira se é mesmo um módulo, e não o repositório do core.";
}
