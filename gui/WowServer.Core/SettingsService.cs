using System.Diagnostics;
using System.Text.Json;

namespace WowServer.Core;

public sealed class MySqlSettings
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 3306;
    public string RootUser { get; set; } = "root";
    public string User { get; set; } = "acore";
    public string Password { get; set; } = "acore";
    public string AuthDb { get; set; } = "acore_auth";
    public string WorldDb { get; set; } = "acore_world";
    public string CharDb { get; set; } = "acore_characters";
}

public sealed class ServerSettings
{
    public string Root { get; set; } = @"C:\AzerothCore";
    public string SourceDir { get; set; } = @"C:\AzerothCore\source";
    public string BuildDir { get; set; } = @"C:\AzerothCore\build";
    public string ServerDir { get; set; } = @"C:\AzerothCore\server";
    public string ClientDir { get; set; } = "";
    public string SourceRepository { get; set; } = "https://github.com/azerothcore/azerothcore-wotlk.git";
    public string SourceBranch { get; set; } = "master";
    public string BuildConfig { get; set; } = "RelWithDebInfo";
    public int Threads { get; set; }
    public string RealmName { get; set; } = "Meu Servidor";
    public string RealmAddress { get; set; } = "127.0.0.1";
    public bool ExtractVmaps { get; set; } = true;
    public bool ExtractMmaps { get; set; } = true;
    public MySqlSettings MySql { get; set; } = new();
}

/// <summary>
/// Le e grava o config/settings.psd1.
///
/// A leitura delega ao proprio PowerShell (Import-PowerShellDataFile), em vez
/// de escrever um parser de psd1 - o formato tem cantos e nao vale reimplementar.
/// A gravacao e cirurgica, via Psd1Editor, pra preservar os comentarios.
/// </summary>
public sealed class SettingsService
{
    private readonly string _repoRoot;

    public SettingsService(string repoRoot) => _repoRoot = repoRoot;

    public string SettingsPath => Path.Combine(_repoRoot, "config", "settings.psd1");
    public string ExamplePath => Path.Combine(_repoRoot, "config", "settings.example.psd1");

    public bool Exists => File.Exists(SettingsPath);

    /// <summary>Cria settings.psd1 a partir do exemplo, se ainda nao existir.</summary>
    public void EnsureExists()
    {
        if (Exists) return;
        if (!File.Exists(ExamplePath))
            throw new FileNotFoundException("settings.example.psd1 nao encontrado", ExamplePath);
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.Copy(ExamplePath, SettingsPath);
    }

    public async Task<ServerSettings> LoadAsync(CancellationToken ct = default)
    {
        EnsureExists();

        var command =
            $"$ErrorActionPreference='Stop'; " +
            $"Import-PowerShellDataFile -LiteralPath '{SettingsPath.Replace("'", "''")}' | " +
            $"ConvertTo-Json -Depth 6 -Compress";

        var json = await RunPowerShellAsync(command, ct).ConfigureAwait(false);
        return ParseJson(json);
    }

    /// <summary>Separado da execucao pra poder ser testado sem PowerShell.</summary>
    public static ServerSettings ParseJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var s = new ServerSettings();

        string Str(JsonElement e, string name, string fallback) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() ?? fallback : fallback;

        int Num(JsonElement e, string name, int fallback) =>
            e.TryGetProperty(name, out var v) && v.TryGetInt32(out var n) ? n : fallback;

        bool Flag(JsonElement e, string name, bool fallback) =>
            e.TryGetProperty(name, out var v)
                ? v.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => fallback,
                }
                : fallback;

        s.Root = Str(root, "Root", s.Root);
        s.SourceDir = Str(root, "SourceDir", s.SourceDir);
        s.BuildDir = Str(root, "BuildDir", s.BuildDir);
        s.ServerDir = Str(root, "ServerDir", s.ServerDir);
        s.ClientDir = Str(root, "ClientDir", s.ClientDir);
        s.SourceRepository = Str(root, "SourceRepository", s.SourceRepository);
        s.SourceBranch = Str(root, "SourceBranch", s.SourceBranch);
        s.BuildConfig = Str(root, "BuildConfig", s.BuildConfig);
        s.Threads = Num(root, "Threads", s.Threads);
        s.RealmName = Str(root, "RealmName", s.RealmName);
        s.RealmAddress = Str(root, "RealmAddress", s.RealmAddress);
        s.ExtractVmaps = Flag(root, "ExtractVmaps", s.ExtractVmaps);
        s.ExtractMmaps = Flag(root, "ExtractMmaps", s.ExtractMmaps);

        if (root.TryGetProperty("MySql", out var my) && my.ValueKind == JsonValueKind.Object)
        {
            s.MySql.Host = Str(my, "Host", s.MySql.Host);
            s.MySql.Port = Num(my, "Port", s.MySql.Port);
            s.MySql.RootUser = Str(my, "RootUser", s.MySql.RootUser);
            s.MySql.User = Str(my, "User", s.MySql.User);
            s.MySql.Password = Str(my, "Password", s.MySql.Password);
            s.MySql.AuthDb = Str(my, "AuthDb", s.MySql.AuthDb);
            s.MySql.WorldDb = Str(my, "WorldDb", s.MySql.WorldDb);
            s.MySql.CharDb = Str(my, "CharDb", s.MySql.CharDb);
        }

        return s;
    }

    public void Save(ServerSettings s)
    {
        EnsureExists();
        var text = File.ReadAllText(SettingsPath);
        text = ApplyTo(text, s);
        // sem BOM: o Import-PowerShellDataFile engasga com BOM em algumas versoes
        File.WriteAllText(SettingsPath, text, new System.Text.UTF8Encoding(false));
    }

    /// <summary>Funcao pura, para poder ser testada isoladamente.</summary>
    public static string ApplyTo(string text, ServerSettings s)
    {
        text = Psd1Editor.SetScalar(text, "Root", Psd1Editor.Quote(s.Root));
        text = Psd1Editor.SetScalar(text, "SourceDir", Psd1Editor.Quote(s.SourceDir));
        text = Psd1Editor.SetScalar(text, "BuildDir", Psd1Editor.Quote(s.BuildDir));
        text = Psd1Editor.SetScalar(text, "ServerDir", Psd1Editor.Quote(s.ServerDir));
        text = Psd1Editor.SetScalar(text, "ClientDir", Psd1Editor.Quote(s.ClientDir));
        text = Psd1Editor.SetScalar(text, "SourceRepository", Psd1Editor.Quote(s.SourceRepository));
        text = Psd1Editor.SetScalar(text, "SourceBranch", Psd1Editor.Quote(s.SourceBranch));
        text = Psd1Editor.SetScalar(text, "BuildConfig", Psd1Editor.Quote(s.BuildConfig));
        text = Psd1Editor.SetScalar(text, "Threads", s.Threads.ToString());
        text = Psd1Editor.SetScalar(text, "RealmName", Psd1Editor.Quote(s.RealmName));
        text = Psd1Editor.SetScalar(text, "RealmAddress", Psd1Editor.Quote(s.RealmAddress));
        text = Psd1Editor.SetScalar(text, "ExtractVmaps", Psd1Editor.Bool(s.ExtractVmaps));
        text = Psd1Editor.SetScalar(text, "ExtractMmaps", Psd1Editor.Bool(s.ExtractMmaps));

        text = Psd1Editor.SetNested(text, "MySql", "Host", Psd1Editor.Quote(s.MySql.Host));
        text = Psd1Editor.SetNested(text, "MySql", "Port", s.MySql.Port.ToString());
        text = Psd1Editor.SetNested(text, "MySql", "RootUser", Psd1Editor.Quote(s.MySql.RootUser));
        text = Psd1Editor.SetNested(text, "MySql", "User", Psd1Editor.Quote(s.MySql.User));
        text = Psd1Editor.SetNested(text, "MySql", "Password", Psd1Editor.Quote(s.MySql.Password));
        text = Psd1Editor.SetNested(text, "MySql", "AuthDb", Psd1Editor.Quote(s.MySql.AuthDb));
        text = Psd1Editor.SetNested(text, "MySql", "WorldDb", Psd1Editor.Quote(s.MySql.WorldDb));
        text = Psd1Editor.SetNested(text, "MySql", "CharDb", Psd1Editor.Quote(s.MySql.CharDb));

        return text;
    }

    private static async Task<string> RunPowerShellAsync(string command, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(command);

        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException("Nao consegui iniciar o powershell.exe");

        var stdout = await p.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
        var stderr = await p.StandardError.ReadToEndAsync().ConfigureAwait(false);
        await p.WaitForExitAsync(ct).ConfigureAwait(false);

        if (p.ExitCode != 0)
            throw new InvalidOperationException($"Falha ao ler settings.psd1: {stderr.Trim()}");

        return stdout.Trim();
    }
}
