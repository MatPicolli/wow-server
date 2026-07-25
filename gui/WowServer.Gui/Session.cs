using System.IO;
using WowServer.Core;

namespace WowServer.Gui;

/// <summary>
/// Estado compartilhado entre as telas: onde esta o repositorio, quem roda os
/// scripts, quem fala com o servidor.
/// </summary>
public sealed class Session
{
    private static Session? _instancia;
    public static Session Current => _instancia ??= new Session();

    private Session()
    {
        RepoRoot = LocalizarRepositorio();
        Settings = new SettingsService(RepoRoot);
    }

    public string RepoRoot { get; }
    public SettingsService Settings { get; }

    /// <summary>
    /// Um runner por tela, de proposito.
    ///
    /// Se todas compartilhassem a mesma instancia, o evento Output seria
    /// entregue a todos os assinantes: rodar um ajuste de multiplicador
    /// despejaria a saida tambem no console da Instalacao e no de Modulos.
    /// O runner e barato - so guarda o caminho do repositorio.
    /// </summary>
    public ScriptRunner CreateRunner() => new(RepoRoot);
    public ServerController? Server { get; set; }

    public ServerSettings? Loaded { get; set; }

    /// <summary>
    /// Sobe a partir do executavel procurando a pasta que contem 'scripts' e
    /// 'config' - assim funciona tanto rodando de bin\Debug quanto de uma
    /// copia publicada dentro do repositorio.
    /// </summary>
    private static string LocalizarRepositorio()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            var temScripts = Directory.Exists(Path.Combine(dir.FullName, "scripts"));
            var temConfig = Directory.Exists(Path.Combine(dir.FullName, "config"));
            if (temScripts && temConfig) return dir.FullName;
            dir = dir.Parent;
        }

        // Ultimo recurso: a pasta de trabalho atual. A tela de Configuracoes
        // mostra qual caminho foi escolhido, entao da pra perceber se errou.
        return Directory.GetCurrentDirectory();
    }

    public bool RepoValido =>
        Directory.Exists(Path.Combine(RepoRoot, "scripts"))
        && File.Exists(Path.Combine(RepoRoot, "scripts", "00-check-prereqs.ps1"));
}
