namespace WowServer.Core;

/// <summary>
/// Monta a linha do <c>powershell -Command</c> usada quando um script precisa
/// de janela propria.
///
/// <para>
/// Dois casos levam a isso, e os dois impedem a saida redirecionada do
/// <see cref="ScriptRunner"/>: o script pede elevacao (processo elevado nao
/// aceita saida redirecionada) ou o script pergunta alguma coisa (o runner usa
/// <c>-NonInteractive</c>, onde <c>Read-Host</c> lanca excecao).
/// </para>
///
/// <para>
/// O <c>-NoExit</c> seria o jeito obvio de deixar a janela aberta, e esta
/// errado: com ele o codigo de saida que volta e o de fechar a janela, nao o do
/// script - uma senha errada voltaria como sucesso. Por isso o script roda
/// dentro de <c>-Command</c>, a janela espera um Enter e so entao devolve o
/// codigo original.
/// </para>
/// </summary>
public static class ConsoleScriptCommand
{
    public static string Build(string scriptPath, IEnumerable<string>? arguments = null)
    {
        // Aspa simples dobrada e como o PowerShell escapa dentro de string
        // literal; o caminho pode ter apostrofo.
        var partes = new List<string> { "& '" + scriptPath.Replace("'", "''") + "'" };

        foreach (var a in arguments ?? Array.Empty<string>())
        {
            // Parametro (-Apply) vai cru; valor vai entre aspas, porque pode ter
            // espaco - "C:\Program Files\..." e o caso de todo dia aqui.
            partes.Add(a.StartsWith('-') ? a : "'" + a.Replace("'", "''") + "'");
        }

        return string.Join(' ', partes) + "; "
            + "$c = $LASTEXITCODE; "
            + "if ($null -eq $c) { $c = 0 }; "
            + "Write-Host ''; "
            + "if ($c -ne 0) { Write-Host '=== FALHOU - leia a mensagem acima ===' -ForegroundColor Red }; "
            + "Read-Host 'Pressione Enter para fechar esta janela' | Out-Null; "
            + "exit $c";
    }
}
