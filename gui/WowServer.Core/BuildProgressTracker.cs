using System.Text.RegularExpressions;

namespace WowServer.Core;

/// <summary>
/// Acompanha o andamento de uma compilacao lendo a saida do MSBuild.
///
/// O MSBuild nao informa progresso, mas ecoa o nome de cada arquivo antes de
/// compila-lo. Contar essas linhas contra uma estimativa do total de fontes da
/// uma nocao razoavel de quanto falta - o suficiente para nao ficar olhando
/// uma tela parada por meia hora.
/// </summary>
public sealed class BuildProgressTracker
{
    // Uma linha "  Foo.cpp" e um arquivo entrando na fila de compilacao.
    // Precisa ser so o nome: linhas de erro trazem caminho completo e
    // parenteses ("...\Foo.cpp(50,41): error C2660"), e nao contam.
    private static readonly Regex Compilando = new(
        @"^\s+(?<f>[A-Za-z0-9_\-.+]+\.(?:cpp|cxx|cc|c))\s*$",
        RegexOptions.Compiled);

    // "foo.vcxproj -> C:\...\foo.lib" marca um projeto concluido.
    private static readonly Regex ProjetoPronto = new(
        @"^\s*(?<p>[A-Za-z0-9_\-.+]+)\.vcxproj\s*->\s*(?<o>.+)$",
        RegexOptions.Compiled);

    private static readonly Regex Erro = new(@":\s*(?:fatal\s+)?error\s+[A-Z]+\d+", RegexOptions.Compiled);
    private static readonly Regex Aviso = new(@":\s*warning\s+[A-Z]+\d+", RegexOptions.Compiled);

    // Os scripts marcam falha com "[erro] ...". Uma dessas antes do primeiro
    // arquivo significa que o build nem chegou a comecar - dizer "0 erro(s)" e
    // mandar procurar a primeira linha com 'error' so confunde.
    private static readonly Regex ErroDeScript = new(@"^\s*\[erro\]\s*(?<m>.+)$", RegexOptions.Compiled);

    public int CompiledFiles { get; private set; }
    public int FinishedProjects { get; private set; }
    public int Errors { get; private set; }
    public int Warnings { get; private set; }

    /// <summary>Primeira falha vista, seja do compilador ou de um script.</summary>
    public string? FirstFailure { get; private set; }

    /// <summary>
    /// Se o compilador chegou a rodar. Falso quando algo barrou antes - uma
    /// verificacao do rebuild.ps1, CMake nao configurado, fonte ausente.
    /// </summary>
    public bool Started => CompiledFiles > 0 || FinishedProjects > 0;

    /// <summary>Estimativa de arquivos a compilar. 0 = desconhecido.</summary>
    public int EstimatedTotal { get; set; }

    public string? LastFile { get; private set; }
    public string? LastProject { get; private set; }

    public void Reset()
    {
        CompiledFiles = 0;
        FinishedProjects = 0;
        Errors = 0;
        Warnings = 0;
        LastFile = null;
        LastProject = null;
        FirstFailure = null;
    }

    public void Feed(string linha)
    {
        if (string.IsNullOrEmpty(linha)) return;

        if (Erro.IsMatch(linha))
        {
            Errors++;
            FirstFailure ??= linha.Trim();
            return;
        }
        if (Aviso.IsMatch(linha)) { Warnings++; return; }

        // Nao incrementa Errors: e falha de script, nao do compilador, e
        // misturar as duas contagens produziria "1 erro(s)" para algo que
        // nunca chegou a compilar.
        var s = ErroDeScript.Match(linha);
        if (s.Success)
        {
            FirstFailure ??= s.Groups["m"].Value.Trim();
            return;
        }

        var m = Compilando.Match(linha);
        if (m.Success)
        {
            CompiledFiles++;
            LastFile = m.Groups["f"].Value;
            return;
        }

        var p = ProjetoPronto.Match(linha);
        if (p.Success)
        {
            FinishedProjects++;
            LastProject = p.Groups["p"].Value;
        }
    }

    /// <summary>Porcentagem, ou null quando nao ha estimativa de total.</summary>
    public double? Percent =>
        EstimatedTotal > 0
            ? Math.Min(100d, CompiledFiles * 100d / EstimatedTotal)
            : null;

    /// <summary>Tempo restante por regra de tres. Null enquanto for cedo demais para valer algo.</summary>
    public TimeSpan? Estimate(TimeSpan decorrido)
    {
        if (EstimatedTotal <= 0 || CompiledFiles < 20) return null;
        if (CompiledFiles >= EstimatedTotal) return TimeSpan.Zero;

        var porArquivo = decorrido.TotalSeconds / CompiledFiles;
        return TimeSpan.FromSeconds(porArquivo * (EstimatedTotal - CompiledFiles));
    }

    public string Describe(TimeSpan decorrido)
    {
        var partes = new List<string>();

        if (EstimatedTotal > 0)
            partes.Add($"{CompiledFiles} de ~{EstimatedTotal} arquivos");
        else if (CompiledFiles > 0)
            partes.Add($"{CompiledFiles} arquivos");

        partes.Add($"{decorrido:hh\\:mm\\:ss}");

        var falta = Estimate(decorrido);
        if (falta is { TotalSeconds: > 30 })
            partes.Add($"faltam ~{falta.Value:hh\\:mm}");

        if (Errors > 0) partes.Add($"{Errors} erro(s)");

        return string.Join("  |  ", partes);
    }

    /// <summary>
    /// Estimativa do total contando os fontes do core e dos modulos.
    ///
    /// Nem todo .cpp da arvore entra na compilacao, entao isso e aproximado de
    /// proposito - serve para a barra andar, nao para prometer exatidao.
    /// </summary>
    public static int EstimateSourceCount(string sourceDir)
    {
        if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir)) return 0;

        var total = 0;
        foreach (var sub in new[] { "src", "modules" })
        {
            var dir = Path.Combine(sourceDir, sub);
            if (!Directory.Exists(dir)) continue;

            try
            {
                total += Directory.EnumerateFiles(dir, "*.cpp", SearchOption.AllDirectories).Count();
            }
            catch
            {
                // pasta inacessivel nao justifica derrubar a compilacao
            }
        }
        return total;
    }
}
