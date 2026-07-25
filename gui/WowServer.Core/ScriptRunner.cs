using System.Diagnostics;

namespace WowServer.Core;

public enum OutputKind { Normal, Error }

public sealed record OutputLine(string Text, OutputKind Kind);

/// <summary>
/// Executa os scripts PowerShell do repositorio e transmite a saida linha a
/// linha.
///
/// A GUI nao reimplementa a instalacao: toda a logica testada - deteccao do
/// OpenSSL 3.x, nomes dos extractors, marcadores de etapa, idempotencia dos
/// multiplicadores - continua nos scripts. Aqui so orquestramos.
/// </summary>
public sealed class ScriptRunner
{
    private readonly string _repoRoot;

    public ScriptRunner(string repoRoot) => _repoRoot = repoRoot;

    public event Action<OutputLine>? Output;

    public string ScriptsDir => Path.Combine(_repoRoot, "scripts");

    public bool ScriptExists(string fileName) => File.Exists(Path.Combine(ScriptsDir, fileName));

    /// <param name="fileName">ex.: "03-build.ps1"</param>
    /// <param name="arguments">ex.: ["-Clean"] ou ["-Mining", "3"]</param>
    public async Task<int> RunAsync(
        string fileName,
        IEnumerable<string>? arguments = null,
        CancellationToken cancellationToken = default)
    {
        var script = Path.Combine(ScriptsDir, fileName);
        if (!File.Exists(script))
            throw new FileNotFoundException($"Script nao encontrado: {script}", script);

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            WorkingDirectory = _repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(script);
        foreach (var a in arguments ?? Array.Empty<string>()) psi.ArgumentList.Add(a);

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null) Output?.Invoke(new OutputLine(e.Data, OutputKind.Normal));
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) Output?.Invoke(new OutputLine(e.Data, OutputKind.Error));
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await using var reg = cancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch { /* ja morreu */ }
        });

        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        return process.ExitCode;
    }
}
