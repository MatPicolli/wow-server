using System.Diagnostics;

namespace WowServer.Core;

public enum ServerRole { Auth, World }

public sealed record ServerOutput(ServerRole Role, string Text, OutputKind Kind);

/// <summary>
/// Sobe o authserver e o worldserver com a saida redirecionada, para a GUI
/// mostrar cada um no seu painel.
///
/// O worldserver tambem recebe a entrada padrao redirecionada: e por ela que
/// vao os comandos de GM ("account create", "reload creature_loot_template").
/// </summary>
public sealed class ServerController : IDisposable
{
    private readonly string _serverDir;
    private Process? _auth;
    private Process? _world;

    public ServerController(string serverDir) => _serverDir = serverDir;

    public event Action<ServerOutput>? Output;
    public event Action<ServerRole>? Exited;

    public bool AuthRunning => _auth is { HasExited: false };
    public bool WorldRunning => _world is { HasExited: false };

    public void StartAuth() => _auth = Start(ServerRole.Auth, "authserver.exe", "authserver.conf", redirectInput: false);
    public void StartWorld() => _world = Start(ServerRole.World, "worldserver.exe", "worldserver.conf", redirectInput: true);

    /// <summary>Envia um comando para o console do worldserver.</summary>
    public void SendWorldCommand(string command)
    {
        if (_world is null || _world.HasExited)
            throw new InvalidOperationException("O worldserver nao esta rodando.");

        _world.StandardInput.WriteLine(command);
        _world.StandardInput.Flush();
        Output?.Invoke(new ServerOutput(ServerRole.World, "> " + command, OutputKind.Normal));
    }

    /// <summary>
    /// Desligamento limpo: pede ao worldserver que salve e encerre. Matar o
    /// processo perde o que ainda nao foi gravado no banco.
    /// </summary>
    public void StopAll(int shutdownDelaySeconds = 0)
    {
        if (WorldRunning)
        {
            try { SendWorldCommand($"server shutdown {shutdownDelaySeconds}"); }
            catch { /* se ja caiu, segue */ }
        }
        if (AuthRunning)
        {
            try { _auth!.CloseMainWindow(); } catch { }
        }
    }

    public void KillAll()
    {
        foreach (var p in new[] { _world, _auth })
        {
            try { if (p is { HasExited: false }) p.Kill(entireProcessTree: true); }
            catch { }
        }
    }

    private Process Start(ServerRole role, string exeName, string confName, bool redirectInput)
    {
        var exe = Path.Combine(_serverDir, exeName);
        if (!File.Exists(exe))
            throw new FileNotFoundException(
                $"{exeName} nao encontrado em {_serverDir}. Rode a etapa de Montagem.", exe);

        var conf = Path.Combine(_serverDir, "configs", confName);

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = _serverDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = redirectInput,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(conf);

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null) Output?.Invoke(new ServerOutput(role, e.Data, OutputKind.Normal));
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) Output?.Invoke(new ServerOutput(role, e.Data, OutputKind.Error));
        };
        process.Exited += (_, _) => Exited?.Invoke(role);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    public void Dispose()
    {
        _auth?.Dispose();
        _world?.Dispose();
    }
}
