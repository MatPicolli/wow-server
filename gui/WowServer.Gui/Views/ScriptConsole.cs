using System.Diagnostics;
using WowServer.Core;

namespace WowServer.Gui.Views;

/// <summary>
/// Roda um script numa janela de console propria, em vez de com a saida
/// redirecionada para um painel.
///
/// <para>
/// Dois motivos levam a isso, e os dois impedem o caminho normal do
/// <see cref="ScriptRunner"/>:
/// </para>
/// <list type="bullet">
///   <item><description>
///     <b>elevacao</b> — processo elevado nao aceita saida redirecionada. Em vez
///     de rodar a GUI inteira como administrador, so este processo sobe com
///     'runas' e o Windows pede a confirmacao.
///   </description></item>
///   <item><description>
///     <b>pergunta</b> — o runner usa <c>-NonInteractive</c>, onde
///     <c>Read-Host</c> lanca excecao. A senha do root do MySQL e pedida assim
///     de proposito: ela nao fica salva em lugar nenhum.
///   </description></item>
/// </list>
/// </summary>
public static class ScriptConsole
{
    /// <summary>
    /// Devolve o codigo de saida do script, ou -1 quando a janela nem abriu
    /// (elevacao recusada, tipicamente).
    /// </summary>
    public static async Task<int> RunAsync(
        string scriptsDir, string workingDirectory, string script,
        IEnumerable<string>? arguments = null, bool admin = false)
    {
        var caminho = System.IO.Path.Combine(scriptsDir, script);

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -ExecutionPolicy Bypass -Command "
                        + "\"" + ConsoleScriptCommand.Build(caminho, arguments) + "\"",
            WorkingDirectory = workingDirectory,
            UseShellExecute = true,
        };

        // 'runas' so quando e mesmo necessario: pedir UAC para digitar uma senha
        // de banco seria pedir privilegio a toa.
        if (admin) psi.Verb = "runas";

        try
        {
            using var p = Process.Start(psi);
            if (p is null) return -1;
            await p.WaitForExitAsync().ConfigureAwait(true);
            return p.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return -1;   // elevacao recusada
        }
    }
}
