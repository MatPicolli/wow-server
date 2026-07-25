namespace WowServer.Core;

public sealed record InstallStep(
    string Id,
    string Title,
    string Description,
    string Script,
    string[] Arguments,
    string Duration,
    bool RequiresAdmin = false);

/// <summary>
/// A instalacao completa, na ordem. A GUI mostra isso como uma lista de
/// etapas e vai marcando conforme cada script termina com sucesso.
/// </summary>
public static class InstallPlan
{
    public static IReadOnlyList<InstallStep> Steps { get; } = new[]
    {
        new InstallStep(
            "prereqs",
            "1. Dependencias",
            "Baixa e instala Visual Studio 2022 (compilador C++), CMake, MySQL, "
            + "OpenSSL 3.x e Boost. Precisa de privilegio de administrador.",
            "01-install-prereqs.ps1", Array.Empty<string>(),
            "30-60 min", RequiresAdmin: true),

        new InstallStep(
            "check",
            "2. Conferencia",
            "Verifica em segundos se tudo que o build precisa esta no lugar, "
            + "em vez de descobrir no meio da compilacao.",
            "00-check-prereqs.ps1", Array.Empty<string>(),
            "instantaneo"),

        new InstallStep(
            "clone",
            "3. Codigo-fonte",
            "Clona o AzerothCore (ou o fork configurado, se voce escolheu um "
            + "com bots integrados).",
            "02-clone-source.ps1", Array.Empty<string>(),
            "5-15 min"),

        new InstallStep(
            "build",
            "4. Compilacao",
            "Compila o servidor e as ferramentas de extracao.",
            "03-build.ps1", Array.Empty<string>(),
            "15-60 min"),

        new InstallStep(
            "database",
            "5. Banco de dados",
            "Cria o usuario e os tres bancos. Ficam vazios: o servidor os "
            + "popula sozinho no primeiro start.",
            "04-setup-database.ps1", Array.Empty<string>(),
            "1 min"),

        new InstallStep(
            "extract",
            "6. Dados do client",
            "Extrai mapas, modelos e dados de navegacao do seu WoW. E a etapa "
            + "mais demorada - os mmaps sozinhos levam horas.",
            "05-extract-client-data.ps1", Array.Empty<string>(),
            "1-6 horas"),

        new InstallStep(
            "deploy",
            "7. Montagem",
            "Junta binarios, DLLs e configuracoes na pasta do servidor.",
            "06-deploy.ps1", Array.Empty<string>(),
            "1 min"),

        new InstallStep(
            "configure",
            "8. Configuracao",
            "Gera os arquivos .conf e aponta o client para o servidor.",
            "07-configure.ps1", Array.Empty<string>(),
            "1 min"),
    };

    /// <summary>Etapa opcional, rodada depois do primeiro start do worldserver.</summary>
    public static InstallStep RealmAddress { get; } = new(
        "realm",
        "Endereco do realm",
        "Grava no banco o endereco que o client usa para achar o mundo. "
        + "So funciona depois que o servidor subiu uma vez e criou a tabela.",
        "08-set-realm-address.ps1", Array.Empty<string>(),
        "instantaneo");
}
