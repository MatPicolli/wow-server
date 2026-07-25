using WowServer.Core;

// Testes sem framework externo: rodam com "dotnet run" em qualquer plataforma,
// sem restaurar pacote nenhum. Cobrem a logica que nao depende de Windows.

var falhas = 0;
var total = 0;

void Check(string nome, bool condicao, string? detalhe = null)
{
    total++;
    if (condicao)
    {
        Console.WriteLine($"  [ok]    {nome}");
    }
    else
    {
        falhas++;
        Console.WriteLine($"  [FALHA] {nome}");
        if (detalhe is not null) Console.WriteLine($"          {detalhe}");
    }
}

// ---------------------------------------------------------------- psd1 -----
Console.WriteLine("\n=== Psd1Editor: edicao preservando comentarios ===");

const string exemplo = """
@{
    # Pastas do servidor
    Root      = 'C:\AzerothCore'
    ClientDir = 'C:\Games\WoW'   # onde esta o Wow.exe

    # Banco de dados
    MySql = @{
        Host     = '127.0.0.1'
        Port     = 3306
        User     = 'acore'
        Password = 'acore'
    }

    RealmName = 'Meu Servidor'
    ExtractMmaps = $true
}
""";

var r1 = Psd1Editor.SetScalar(exemplo, "ClientDir", Psd1Editor.Quote(@"D:\WoW-3.3.5a"));
Check("troca chave de primeiro nivel", r1.Contains(@"ClientDir = 'D:\WoW-3.3.5a'"));
Check("preserva comentarios", r1.Contains("# Pastas do servidor") && r1.Contains("# Banco de dados"));
Check("nao mexe nas outras chaves", r1.Contains(@"Root      = 'C:\AzerothCore'"));

var r2 = Psd1Editor.SetNested(exemplo, "MySql", "Password", Psd1Editor.Quote("senha-nova"));
Check("troca chave aninhada", r2.Contains("Password = 'senha-nova'"));

Check("preserva o alinhamento original",
      Psd1Editor.SetNested(exemplo, "MySql", "Host", Psd1Editor.Quote("192.168.0.10"))
                .Contains("Host     = '192.168.0.10'"));

Check("preserva comentario no fim da linha",
      r1.Contains("# onde esta o Wow.exe"),
      r1.Split('\n').FirstOrDefault(l => l.Contains("ClientDir")));

// O teste que importa para SetNested: uma chave de mesmo nome existindo
// dentro e fora do bloco. So a de dentro pode mudar.
const string duplicada = """
@{
    Host = 'valor-de-fora'
    MySql = @{
        Host = 'valor-de-dentro'
    }
}
""";
var r3 = Psd1Editor.SetNested(duplicada, "MySql", "Host", Psd1Editor.Quote("trocado"));
Check("SetNested nao vaza para fora do bloco",
      r3.Contains("Host = 'valor-de-fora'") && r3.Contains("Host = 'trocado'")
      && !r3.Contains("valor-de-dentro"),
      r3.Replace("\n", " | "));

var r4 = Psd1Editor.SetScalar(exemplo, "ExtractMmaps", Psd1Editor.Bool(false));
Check("grava booleano no formato do PowerShell", r4.Contains("ExtractMmaps = $false"));

var r5 = Psd1Editor.SetScalar(exemplo, "ChaveInexistente", "42");
Check("acrescenta chave ausente", r5.Contains("ChaveInexistente = 42"));
Check("chave acrescentada fica dentro do hashtable",
      r5.TrimEnd().EndsWith("}", StringComparison.Ordinal));

Check("escapa aspas simples no valor",
      Psd1Editor.Quote("senha'com'aspas") == "'senha''com''aspas'");

var comAcento = Psd1Editor.SetScalar(exemplo, "RealmName", Psd1Editor.Quote("Servidor do Mateus Picollí"));
Check("preserva acentuacao", comAcento.Contains("Picollí"));

// ------------------------------------------------------- settings json -----
Console.WriteLine("\n=== SettingsService: leitura do JSON vindo do PowerShell ===");

const string json = """
{"Root":"C:\\AzerothCore","SourceDir":"C:\\AzerothCore\\source","BuildDir":"C:\\AzerothCore\\build",
 "ServerDir":"C:\\AzerothCore\\server","ClientDir":"D:\\WoW-3.3.5a","BuildConfig":"RelWithDebInfo",
 "Threads":12,"RealmName":"Meu Servidor","RealmAddress":"127.0.0.1","ExtractVmaps":true,
 "ExtractMmaps":false,"SourceBranch":"Playerbot",
 "SourceRepository":"https://github.com/mod-playerbots/azerothcore-wotlk.git",
 "MySql":{"Host":"127.0.0.1","Port":3306,"RootUser":"root","User":"acore","Password":"senha",
          "AuthDb":"acore_auth","WorldDb":"acore_world","CharDb":"acore_characters"}}
""";

var s = SettingsService.ParseJson(json);
Check("le caminho do client", s.ClientDir == @"D:\WoW-3.3.5a", s.ClientDir);
Check("le inteiro", s.Threads == 12, s.Threads.ToString());
Check("le booleano true", s.ExtractVmaps);
Check("le booleano false", !s.ExtractMmaps);
Check("le bloco aninhado", s.MySql.Password == "senha" && s.MySql.Port == 3306);
Check("le repositorio de fork", s.SourceBranch == "Playerbot");

var faltando = SettingsService.ParseJson("""{"Root":"C:\\X"}""");
Check("usa padrao quando a chave nao existe",
      faltando.MySql.User == "acore" && faltando.BuildConfig == "RelWithDebInfo");

// ida e volta
var editado = SettingsService.ApplyTo(exemplo, s);
Check("ida e volta grava o client", editado.Contains(@"ClientDir = 'D:\WoW-3.3.5a'"));
Check("ida e volta grava a senha do banco", editado.Contains("Password = 'senha'"));
Check("ida e volta preserva comentarios", editado.Contains("# Banco de dados"));

// --------------------------------------------------------- multiplicador ---
Console.WriteLine("\n=== TuningRequest: montagem dos argumentos ===");

var g = new GatheringTuning { Mining = 3, Herbalism = 2.5, Fishing = 0 };
var argsG = g.ToArguments(apply: false);
Check("inclui so o que foi preenchido",
      argsG.Contains("-Mining") && argsG.Contains("-Herbalism") && !argsG.Contains("-Fishing"),
      string.Join(' ', argsG));
Check("formata decimal com ponto, nao virgula",
      argsG.Contains("2.5"), string.Join(' ', argsG));
Check("sem -Apply no preview", !argsG.Contains("-Apply"));
Check("com -Apply quando pedido", g.ToArguments(apply: true).Contains("-Apply"));
Check("detecta vazio", new GatheringTuning().IsEmpty);

var d = new DropChanceTuning { QuestItems = 3 };
var argsD = d.ToArguments(apply: true);
Check("drop de quest vira argumento",
      argsD.Contains("-QuestItems") && argsD.Contains("3") && argsD.Contains("-Apply"),
      string.Join(' ', argsD));

// ------------------------------------------------------------- catalogo ----
Console.WriteLine("\n=== Catalogo e plano de instalacao ===");

Check("catalogo tem modulos", ModuleCatalog.All.Count > 0);
Check("acha por nome", ModuleCatalog.Find("mod-ah-bot") is not null);
Check("busca ignora maiusculas", ModuleCatalog.Find("MOD-AH-BOT") is not null);
Check("playerbots marcado como fork",
      ModuleCatalog.Find("mod-playerbots")?.Status == ModuleStatus.ExigeFork);
Check("modulo de fork traz repositorio e branch",
      ModuleCatalog.Find("mod-playerbots")?.ForkBranch == "Playerbot");
Check("todo modulo tem repositorio",
      ModuleCatalog.All.All(m => m.Repository.StartsWith("https://", StringComparison.Ordinal)));

Check("plano tem 8 etapas", InstallPlan.Steps.Count == 8, InstallPlan.Steps.Count.ToString());
Check("ids sao unicos",
      InstallPlan.Steps.Select(x => x.Id).Distinct().Count() == InstallPlan.Steps.Count);
Check("primeira etapa exige admin", InstallPlan.Steps[0].RequiresAdmin);
Check("toda etapa aponta um script .ps1",
      InstallPlan.Steps.All(x => x.Script.EndsWith(".ps1", StringComparison.Ordinal)));

Check("presets existem", TuningPresets.All.Count >= 3);
Check("preset Blizzlike e neutro",
      TuningPresets.All[0].Gathering.IsEmpty && TuningPresets.All[0].Drops.IsEmpty);

// ---------------------------------------------------------------- tailer ---
Console.WriteLine("\n=== LogTailer: leitura de log em uso ===");

var tmp = Path.Combine(Path.GetTempPath(), $"tail-{Guid.NewGuid():N}.log");
try
{
    // arquivo ja com conteudo antes do tailer comecar
    File.WriteAllText(tmp, "linha antiga 1\nlinha antiga 2\n");

    var recebidas = new List<string>();
    using var tailer = new LogTailer(tmp, TimeSpan.FromMilliseconds(50));
    tailer.Line += l => { lock (recebidas) recebidas.Add(l); };
    tailer.Start(fromStart: true);

    await Task.Delay(200);
    lock (recebidas)
        Check("le o que ja existia", recebidas.Count == 2, string.Join(" | ", recebidas));

    // escreve com o arquivo aberto para escrita, como o servidor faz
    using (var w = new FileStream(tmp, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
    using (var sw = new StreamWriter(w))
    {
        sw.WriteLine("linha nova 3");
        sw.Flush();
        await Task.Delay(200);
        lock (recebidas)
            Check("le linha nova com o arquivo aberto por outro processo",
                  recebidas.Count == 3 && recebidas[2] == "linha nova 3",
                  string.Join(" | ", recebidas));
    }

    // reinicio do servidor: appender abre em modo 'w' e trunca
    File.WriteAllText(tmp, "apos reinicio\n");
    await Task.Delay(200);
    lock (recebidas)
        Check("detecta truncamento e nao perde a linha seguinte",
              recebidas.Contains("apos reinicio"), string.Join(" | ", recebidas));

    var semArquivo = new LogTailer(Path.Combine(Path.GetTempPath(), "nao-existe.log"),
                                   TimeSpan.FromMilliseconds(50));
    semArquivo.Start();
    await Task.Delay(150);
    semArquivo.Dispose();
    Check("nao explode se o arquivo ainda nao existe", true);
}
finally
{
    try { File.Delete(tmp); } catch { }
}

Console.WriteLine($"\n{total - falhas}/{total} testes passaram (final)");
return falhas == 0 ? 0 : 1;
