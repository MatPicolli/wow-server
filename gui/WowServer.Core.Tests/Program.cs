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
// Rodado com LF e com CRLF: o bug que corrompeu um settings.psd1 real so
// aparecia com fim de linha do Windows, e passou batido porque os literais
// deste arquivo usam LF.
const string exemploBase = """
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

foreach (var (convencao, quebra) in new[] { ("LF", "\n"), ("CRLF", "\r\n") })
{
Console.WriteLine($"\n=== Psd1Editor com {convencao} ===");

string N(string t) => t.Replace("\r\n", "\n").Replace("\n", quebra);

var exemplo = N(exemploBase);

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
const string duplicadaBase = """
@{
    Host = 'valor-de-fora'
    MySql = @{
        Host = 'valor-de-dentro'
    }
}
""";
var duplicada = N(duplicadaBase);
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

Check($"[{convencao}] nao duplica chave ao editar",
      Psd1Editor.FindDuplicateKeys(r1).Count == 0,
      string.Join(",", Psd1Editor.FindDuplicateKeys(r1)));

var todas = SettingsService.ApplyTo(exemplo, new ServerSettings { ClientDir = @"D:\WoW" });
Check($"[{convencao}] ApplyTo completo nao duplica nada",
      Psd1Editor.FindDuplicateKeys(todas).Count == 0,
      string.Join(",", Psd1Editor.FindDuplicateKeys(todas)));
Check($"[{convencao}] ApplyTo preserva o valor gravado",
      todas.Contains(@"ClientDir = 'D:\WoW'"));
}

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
var editado = SettingsService.ApplyTo(exemploBase, s);
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

// -------------------------------------------------------------- shutdown ---
Console.WriteLine("\n=== ServerController: comando de desligamento ===");

// cs_server.cpp rejeita delay <= 0 com "Incorrect values." e nao desliga nada.
Check("atraso 0 vira 1 (o core rejeita zero)",
      ServerController.BuildShutdownCommand(0) == "server shutdown 1",
      ServerController.BuildShutdownCommand(0));
Check("atraso negativo tambem vira 1",
      ServerController.BuildShutdownCommand(-5) == "server shutdown 1",
      ServerController.BuildShutdownCommand(-5));
Check("atraso valido e preservado",
      ServerController.BuildShutdownCommand(300) == "server shutdown 300",
      ServerController.BuildShutdownCommand(300));

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

// --------------------------------------------------------------- uistate ---
Console.WriteLine("\n=== UiState: persistencia entre execucoes ===");

var estadoPath = Path.Combine(Path.GetTempPath(), $"ui-{Guid.NewGuid():N}.json");
try
{
    var salvo = new UiState
    {
        WindowWidth = 1600, WindowHeight = 900,
        WindowLeft = 100, WindowTop = 50,
        SelectedTab = 3, ConsoleWrap = false, ServerSideBySide = false,
        Mining = 3, Herbalism = 2.5, QuestItems = 4,
    };
    salvo.Save(estadoPath);
    Check("grava o arquivo", File.Exists(estadoPath));

    var lido = UiState.Load(estadoPath);
    Check("restaura tamanho da janela", lido.WindowWidth == 1600 && lido.WindowHeight == 900);
    Check("restaura aba selecionada", lido.SelectedTab == 3);
    Check("restaura quebra de linha", !lido.ConsoleWrap);
    Check("restaura layout dos paineis", !lido.ServerSideBySide);
    Check("restaura multiplicadores com decimal",
          lido.Mining == 3 && lido.Herbalism == 2.5 && lido.QuestItems == 4,
          $"{lido.Mining}/{lido.Herbalism}/{lido.QuestItems}");

    var restaurado = lido.ToGathering().ToArguments(apply: false);
    Check("converte de volta para os argumentos do script",
          restaurado.Contains("-Mining") && restaurado.Contains("2.5"),
          string.Join(' ', restaurado));

    File.WriteAllText(estadoPath, "{ isso nao e json valido");
    var corrompido = UiState.Load(estadoPath);
    Check("arquivo corrompido nao impede abrir", corrompido.WindowWidth == 1440);

    Check("arquivo inexistente devolve padrao",
          UiState.Load(Path.Combine(Path.GetTempPath(), "nao-existe-ui.json")).ConsoleWrap);

    // posicao vinda de um monitor que nao existe mais
    var fora = new UiState { WindowLeft = 9000, WindowTop = 9000 };
    Check("rejeita posicao fora da area visivel",
          !fora.HasUsablePosition(0, 0, 1920, 1080));
    var dentro = new UiState { WindowLeft = 200, WindowTop = 100 };
    Check("aceita posicao valida", dentro.HasUsablePosition(0, 0, 1920, 1080));
    Check("sem posicao gravada usa o padrao",
          !new UiState().HasUsablePosition(0, 0, 1920, 1080));
}
finally
{
    try { File.Delete(estadoPath); } catch { }
}

// ---------------------------------------------------------- build progress -
Console.WriteLine("\n=== BuildProgressTracker: linhas reais do MSBuild ===");

var bt = new BuildProgressTracker();

// linhas de arquivo entrando na compilacao
foreach (var l in new[] { "  AuctionHouseBot.cpp", "  ABConfig.cpp", "  Message.cpp",
                          "  ALE_SC.cpp", "  adt.cpp" })
    bt.Feed(l);
Check("conta arquivos compilados", bt.CompiledFiles == 5, bt.CompiledFiles.ToString());
Check("guarda o ultimo arquivo", bt.LastFile == "adt.cpp", bt.LastFile);

// linha de erro do log real - menciona .cpp mas NAO e progresso
bt.Feed(@"C:\AzerothCore\source\modules\mod-playerbots\src\Ai\Base\Actions\BattleGroundJoinAction.cpp(241,17): error C2065: 'ARENA_TYPE_NONE': identificador não declarado [C:\AzerothCore\build\modules\modules.vcxproj]");
Check("linha de erro nao conta como arquivo compilado", bt.CompiledFiles == 5, bt.CompiledFiles.ToString());
Check("linha de erro conta como erro", bt.Errors == 1, bt.Errors.ToString());

bt.Feed(@"C:\AzerothCore\source\modules\mod-eluna\src\LuaEngine\ALECompat.h(12,1): error C1083: Não é possível abrir arquivo incluir: 'lua.h': No such file or directory [C:\AzerothCore\build\modules\modules.vcxproj]");
Check("erro C1083 tambem conta", bt.Errors == 2, bt.Errors.ToString());

// projeto concluido
bt.Feed(@"  zlib.vcxproj -> C:\AzerothCore\build\deps\zlib\RelWithDebInfo\zlib.lib");
Check("conta projeto concluido", bt.FinishedProjects == 1);
Check("guarda o nome do projeto", bt.LastProject == "zlib", bt.LastProject);

// linhas que nao sao nem uma coisa nem outra
var antes = (bt.CompiledFiles, bt.FinishedProjects, bt.Errors);
foreach (var l in new[] { "-- Configuring done (2.7s)", "  |   +- mod-ah-bot", "",
                          "  Please define _WIN32_WINNT or _WIN32_WINDOWS appropriately." })
    bt.Feed(l);
Check("ignora ruido do cmake e do msbuild",
      (bt.CompiledFiles, bt.FinishedProjects, bt.Errors) == antes);

// porcentagem e estimativa
Check("sem total nao ha porcentagem", bt.Percent is null);
bt.EstimatedTotal = 100;
Check("com total ha porcentagem", Math.Abs(bt.Percent!.Value - 5) < 0.01, bt.Percent?.ToString());

var cedo = new BuildProgressTracker { EstimatedTotal = 1000 };
for (var i = 0; i < 5; i++) cedo.Feed($"  f{i}.cpp");
Check("nao estima com poucos arquivos", cedo.Estimate(TimeSpan.FromSeconds(10)) is null);

var maduro = new BuildProgressTracker { EstimatedTotal = 1000 };
for (var i = 0; i < 100; i++) maduro.Feed($"  f{i}.cpp");
var falta = maduro.Estimate(TimeSpan.FromSeconds(60));
Check("estima quando ja tem amostra",
      falta is not null && Math.Abs(falta.Value.TotalSeconds - 540) < 1,
      falta?.ToString());

Check("descricao menciona progresso e erros",
      bt.Describe(TimeSpan.FromMinutes(2)).Contains("de ~100") && bt.Describe(TimeSpan.FromMinutes(2)).Contains("erro"),
      bt.Describe(TimeSpan.FromMinutes(2)));

bt.Reset();
Check("reset zera tudo", bt is { CompiledFiles: 0, Errors: 0, FinishedProjects: 0 });

Check("estimativa de fontes em pasta inexistente devolve 0",
      BuildProgressTracker.EstimateSourceCount("/nao/existe") == 0);

// ---- comparacao de repositorio -------------------------------------------
// Errar aqui e caro nos dois sentidos: um falso positivo esconde o core
// incompativel e deixa o usuario perder uma compilacao inteira; um falso
// negativo manda trocar o core sem necessidade, o que apaga o codigo-fonte.
const string fork = "https://github.com/mod-playerbots/azerothcore-wotlk";
const string forkAntigo = "https://github.com/liyunfan1223/azerothcore-wotlk";

Check("repo identico", ModuleCatalog.SameRepository(fork, fork));
Check("sufixo .git ignorado", ModuleCatalog.SameRepository(fork + ".git", fork));
Check("barra final ignorada", ModuleCatalog.SameRepository(fork + "/", fork));
Check("'.git/' junto ignorado", ModuleCatalog.SameRepository(fork + ".git/", fork));
Check("espacos em volta ignorados", ModuleCatalog.SameRepository("  " + fork + ".git  ", fork));
Check("caixa ignorada",
      ModuleCatalog.SameRepository("https://github.com/Mod-Playerbots/AzerothCore-WotLK", fork));
Check("url ssh reconhecida",
      ModuleCatalog.SameRepository("git@github.com:mod-playerbots/azerothcore-wotlk.git", fork));
Check("credencial embutida ignorada",
      ModuleCatalog.SameRepository("https://user:token@github.com/mod-playerbots/azerothcore-wotlk.git", fork));
Check("prefixo de proxy ignorado",
      ModuleCatalog.SameRepository("http://proxy@127.0.0.1:8080/git/mod-playerbots/azerothcore-wotlk.git", fork));
Check("core oficial e diferente do fork",
      !ModuleCatalog.SameRepository("https://github.com/azerothcore/azerothcore-wotlk.git", fork));
Check("outro dono e diferente",
      !ModuleCatalog.SameRepository(forkAntigo, fork));
Check("mesmo dono, repo diferente",
      !ModuleCatalog.SameRepository("https://github.com/mod-playerbots/mod-playerbots", fork));

// '.git' no meio do nome nao pode ser removido - so o sufixo conta
Check("'.git' dentro do nome preservado",
      ModuleCatalog.SameRepository("https://github.com/dono/repo.github.io",
                                   "https://github.com/dono/repo.github.io"));
Check("'.gitX' nao confunde com sufixo",
      !ModuleCatalog.SameRepository("https://github.com/dono/repo.github.io",
                                    "https://github.com/dono/repo"));

// o Playerbots do catalogo tem que apontar mesmo para o fork
var pb = ModuleCatalog.All.First(m => m.Name == "mod-playerbots");
Check("Playerbots exige fork", pb.Status == ModuleStatus.ExigeFork);
Check("Playerbots aponta para o fork",
      pb.ForkRepository is not null && ModuleCatalog.SameRepository(pb.ForkRepository, fork),
      pb.ForkRepository ?? "(nulo)");
Check("branch do fork e Playerbot", pb.ForkBranch == "Playerbot", pb.ForkBranch ?? "(nulo)");

// Um alias mal resolvido manda reclonar o core - operacao que apaga a pasta
// de fontes inteira, modules/ junto. Falso positivo aqui e destrutivo.
Check("fork canonico satisfaz", ModuleCatalog.SatisfiesFork(pb, fork));
Check("fork canonico com .git satisfaz", ModuleCatalog.SatisfiesFork(pb, fork + ".git"));
Check("endereco antigo do fork tambem satisfaz",
      ModuleCatalog.SatisfiesFork(pb, forkAntigo));
Check("endereco antigo com .git tambem satisfaz",
      ModuleCatalog.SatisfiesFork(pb, forkAntigo + ".git"));
Check("core oficial nao satisfaz o Playerbots",
      !ModuleCatalog.SatisfiesFork(pb, "https://github.com/azerothcore/azerothcore-wotlk.git"));
Check("repositorio do modulo nao serve como core",
      !ModuleCatalog.SatisfiesFork(pb, pb.Repository));

// modulo comum nao exige core nenhum
var ahbot = ModuleCatalog.All.First(m => m.Name == "mod-ah-bot");
Check("modulo sem fork aceita qualquer core",
      ModuleCatalog.SatisfiesFork(ahbot, "https://github.com/azerothcore/azerothcore-wotlk.git"));

// ---- integridade do catalogo ---------------------------------------------
Check("catalogo sem nomes repetidos",
      ModuleCatalog.All.Select(m => m.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()
      == ModuleCatalog.All.Count);
Check("catalogo sem titulos repetidos",
      ModuleCatalog.All.Select(m => m.DisplayName).Distinct().Count() == ModuleCatalog.All.Count);

// O nome e o nome da pasta em modules/ - se nao bater com o final da URL, o
// card diz "instalado" para uma pasta que nunca vai existir.
foreach (var m in ModuleCatalog.All)
{
    CustomModuleUrl.TryParse(m.Repository, out var derivado, out _);
    Check($"nome de {m.Name} bate com a URL",
          string.Equals(derivado, m.Name, StringComparison.OrdinalIgnoreCase),
          derivado ?? "(nao parseou)");
}

foreach (var m in ModuleCatalog.All)
    Check($"categoria de {m.Name} e conhecida",
          ModuleCatalog.Categories.Contains(m.Category), m.Category);

Check("fork so aparece em modulo marcado como ExigeFork",
      ModuleCatalog.All.All(m => m.ForkRepository is null || m.Status == ModuleStatus.ExigeFork));
Check("todo ExigeFork traz repositorio e branch",
      ModuleCatalog.All.Where(m => m.Status == ModuleStatus.ExigeFork)
                       .All(m => m.ForkRepository is not null && m.ForkBranch is not null));
Check("catalogo tem mais de uma categoria em uso",
      ModuleCatalog.All.Select(m => m.Category).Distinct().Count() > 1);

// ---- URL colada pelo usuario ---------------------------------------------
// O final da URL vira nome de pasta dentro do codigo-fonte do core, entao o
// que passa daqui precisa ser inofensivo como caminho e como argumento de git.
static string? Pasta(string u) =>
    CustomModuleUrl.TryParse(u, out var n, out _) ? n : null;

Check("https simples", Pasta("https://github.com/azerothcore/mod-transmog") == "mod-transmog");
Check("https com .git", Pasta("https://github.com/azerothcore/mod-transmog.git") == "mod-transmog");
Check("https com barra final", Pasta("https://github.com/azerothcore/mod-transmog/") == "mod-transmog");
Check("https com .git e barra", Pasta("https://github.com/azerothcore/mod-transmog.git/") == "mod-transmog");
Check("espacos em volta", Pasta("  https://github.com/azerothcore/mod-transmog  ") == "mod-transmog");
Check("ssh", Pasta("git@github.com:azerothcore/mod-transmog.git") == "mod-transmog");
Check("ssh://", Pasta("ssh://git@github.com/azerothcore/mod-transmog.git") == "mod-transmog");
Check("http tambem serve", Pasta("http://git.local/x/mod-foo.git") == "mod-foo");
Check("underline e ponto no nome", Pasta("https://github.com/x/mod_foo.bar") == "mod_foo.bar");

Check("vazio recusado", Pasta("") is null);
Check("so espacos recusado", Pasta("   ") is null);
Check("null recusado", !CustomModuleUrl.TryParse(null, out _, out _));
Check("caminho local recusado", Pasta(@"C:\AzerothCore\source") is null);
Check("sem esquema recusado", Pasta("github.com/x/mod-foo") is null);
Check("com espaco no meio recusado", Pasta("https://github.com/x/mod foo") is null);

// '..' escaparia de modules/ e escreveria dentro do codigo-fonte do core
Check("'..' recusado", Pasta("https://github.com/x/..") is null);
Check("nome comecando com ponto recusado", Pasta("https://github.com/x/.git") is null);
Check("'.' recusado", Pasta("https://github.com/x/.") is null);

// uma URL que comeca com '-' viraria opcao de linha de comando para o git
Check("argumento disfarcado de opcao recusado", Pasta("--upload-pack=algo") is null);

// mensagem de erro serve para mostrar na tela
CustomModuleUrl.TryParse("nada disso", out _, out var msg);
Check("erro traz mensagem util", !string.IsNullOrWhiteSpace(msg) && msg!.Length > 10, msg ?? "(vazia)");

Check("aviso para nome fora da convencao", CustomModuleUrl.Advice("azerothcore-wotlk") is not null);
Check("sem aviso para nome mod-", CustomModuleUrl.Advice("mod-transmog") is null);

// ---- falha antes de compilar ---------------------------------------------
// Foi o caso real: rebuild.ps1 barrou o core errado em 0 segundos, e a tela
// disse "COMPILACAO FALHOU - 0 erro(s)" mandando procurar linha com 'error'.
var barrado = new BuildProgressTracker();
barrado.Feed("==> Modulos em C:\\AzerothCore\\source\\modules");
barrado.Feed("    [erro] mod-playerbots exige o core de https://github.com/x/y");
Check("erro de script nao vira erro de compilador", barrado.Errors == 0, barrado.Errors.ToString());
Check("build nem comecou", !barrado.Started);
Check("motivo capturado",
      barrado.FirstFailure is not null && barrado.FirstFailure.StartsWith("mod-playerbots exige"),
      barrado.FirstFailure ?? "(nulo)");

var compilou = new BuildProgressTracker();
compilou.Feed("  Foo.cpp");
Check("build comecou apos um arquivo", compilou.Started);
compilou.Feed(@"C:\x\Bar.cpp(10,1): error C2660: primeiro");
compilou.Feed(@"C:\x\Baz.cpp(11,1): error C2660: segundo");
Check("dois erros contados", compilou.Errors == 2, compilou.Errors.ToString());
Check("guarda o primeiro erro, nao o ultimo",
      compilou.FirstFailure is not null && compilou.FirstFailure.Contains("primeiro"),
      compilou.FirstFailure ?? "(nulo)");

compilou.Reset();
Check("reset limpa o motivo", compilou.FirstFailure is null && !compilou.Started);

// ---- comandos de GM -------------------------------------------------------
Check("catalogo de comandos nao esta vazio", GmCommands.All.Count > 0);
Check("todo comando tem categoria conhecida",
      GmCommands.All.All(c => GmCommands.Categories.Contains(c.Category)));
Check("titulos nao se repetem",
      GmCommands.All.Select(c => c.Title).Distinct().Count() == GmCommands.All.Count);

// O ponto e do chat do jogo; no console do worldserver ele nao entra. Guardar
// o template sem ponto e acrescentar so na exibicao mantem os dois certos.
Check("nenhum template comeca com ponto",
      GmCommands.All.All(c => !c.Template.StartsWith('.')),
      GmCommands.All.FirstOrDefault(c => c.Template.StartsWith('.'))?.Title ?? "");

Check("achou marcadores simples",
      GmCommands.FindPlaceholders("account create <usuario> <senha>")
                .SequenceEqual(new[] { "usuario", "senha" }));
Check("marcador repetido conta uma vez",
      GmCommands.FindPlaceholders("<a> e <a>").Count == 1);
Check("sem marcador devolve vazio", GmCommands.FindPlaceholders("saveall").Count == 0);
Check("texto com espaco dentro nao e marcador",
      GmCommands.FindPlaceholders("vida < 50 e mana > 10").Count == 0);
Check("'<>' vazio nao e marcador", GmCommands.FindPlaceholders("a <> b").Count == 0);
Check("marcador sem fechar e ignorado", GmCommands.FindPlaceholders("a <b").Count == 0);

Check("Fill troca o marcador",
      GmCommands.Fill("account create <usuario> <senha>",
          new Dictionary<string, string> { ["usuario"] = "mateus", ["senha"] = "1234" })
      == "account create mateus 1234");
Check("Fill ignora valor vazio",
      GmCommands.Fill("tele <lugar>", new Dictionary<string, string> { ["lugar"] = "  " })
      == "tele <lugar>");
Check("Fill tira espaco em volta",
      GmCommands.Fill("tele <lugar>", new Dictionary<string, string> { ["lugar"] = " dalaran " })
      == "tele dalaran");
Check("Fill deixa marcador desconhecido no lugar",
      GmCommands.Fill("tele <lugar>", new Dictionary<string, string> { ["outro"] = "x" })
      == "tele <lugar>");

// Comando de console sem marcador e o unico que a GUI manda sozinha; se um
// deles precisasse de preenchimento, ela enviaria lixo para o servidor.
var prontos = GmCommands.All
    .Where(c => c.Target == CommandTarget.Console && !c.NeedsFilling)
    .Select(c => c.Title)
    .ToList();
Check("ha comandos de console prontos para enviar", prontos.Count > 0, string.Join(", ", prontos));

// shutdown 0 e recusado pelo servidor (LANG_BAD_VALUE): o template tem que
// deixar o tempo a cargo do usuario, nao chutar zero
var desligar = GmCommands.All.First(c => c.Template.StartsWith("server shutdown"));
Check("shutdown pede o tempo em vez de fixar", desligar.NeedsFilling, desligar.Template);

// ---- ajuda dos campos -----------------------------------------------------
Check("catalogo de ajuda nao esta vazio", FieldHelp.All.Count > 0);
Check("chaves de ajuda nao se repetem",
      FieldHelp.All.Select(e => e.Key).Distinct(StringComparer.Ordinal).Count() == FieldHelp.All.Count);
Check("toda ajuda tem titulo e descricao",
      FieldHelp.All.All(e => !string.IsNullOrWhiteSpace(e.Title)
                          && !string.IsNullOrWhiteSpace(e.Description)));

// O pedido era "exemplos que podem ser escritos ou selecionados, cada um
// explicando" - entao entrada sem exemplo, ou exemplo sem explicacao, e falha.
foreach (var e in FieldHelp.All)
{
    Check($"ajuda '{e.Key}' tem exemplos", e.Examples.Count > 0);
    Check($"exemplos de '{e.Key}' vem explicados",
          e.Examples.All(x => !string.IsNullOrWhiteSpace(x.Value)
                           && !string.IsNullOrWhiteSpace(x.Meaning)));
}

// ---- ajustes do worldserver.conf ------------------------------------------
Check("catalogo de ajustes nao esta vazio", ConfigTuning.All.Count > 0);
Check("chaves de ajuste nao se repetem",
      ConfigTuning.All.Select(c => c.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count()
      == ConfigTuning.All.Count);
Check("toda categoria de ajuste e conhecida",
      ConfigTuning.All.All(c => ConfigTuning.Categories.Contains(c.Category)));

// Chave inventada nao daria erro nenhum: o servidor ignora o que nao conhece,
// e o ajuste simplesmente nao teria efeito. Por isso o formato e conferido.
Check("chaves de ajuste tem formato de chave de conf",
      ConfigTuning.All.All(c => System.Text.RegularExpressions.Regex.IsMatch(
          c.Key, @"^[A-Za-z][A-Za-z0-9._]*$")),
      ConfigTuning.All.FirstOrDefault(c => !System.Text.RegularExpressions.Regex.IsMatch(
          c.Key, @"^[A-Za-z][A-Za-z0-9._]*$"))?.Key ?? "");

// O valor padrao vem do worldserver.conf.dist e e mostrado ao usuario; se nao
// passasse na propria validacao, a tela estaria sugerindo algo recusavel.
foreach (var c in ConfigTuning.All)
{
    Check($"ajuste '{c.Key}' tem exemplos explicados",
          c.Examples.Count > 0 && c.Examples.All(x => !string.IsNullOrWhiteSpace(x.Value)
                                                   && !string.IsNullOrWhiteSpace(x.Meaning)));
    Check($"padrao de '{c.Key}' e valido para ele proprio",
          ConfigTuning.TryParse(c, c.Default, out _, out var motivo), motivo ?? "");
}

var xp = ConfigTuning.Find("Rate.XP.Kill")!;
Check("acha ajuste por chave", xp is not null);
Check("busca de ajuste ignora caixa", ConfigTuning.Find("rate.xp.kill") is not null);
Check("ajuste inexistente devolve null", ConfigTuning.Find("Rate.Inventada") is null);

Check("aceita inteiro", ConfigTuning.TryParse(xp, "3", out var v1, out _) && v1 == "3");
Check("aceita decimal com ponto", ConfigTuning.TryParse(xp, "1.5", out var v2, out _) && v2 == "1.5");
// Teclado brasileiro produz virgula; o arquivo de conf le com ponto.
Check("aceita decimal com virgula", ConfigTuning.TryParse(xp, "1,5", out var v3, out _) && v3 == "1.5");
Check("tira espacos", ConfigTuning.TryParse(xp, "  2  ", out var v4, out _) && v4 == "2");
Check("recusa texto", !ConfigTuning.TryParse(xp, "muito", out _, out _));
Check("recusa vazio", !ConfigTuning.TryParse(xp, "", out _, out _));
Check("recusa negativo", !ConfigTuning.TryParse(xp, "-1", out _, out _));

var velocidade = ConfigTuning.Find("Rate.MoveSpeed.Player")!;
Check("respeita o minimo do ajuste", !ConfigTuning.TryParse(velocidade, "0", out _, out _));
Check("respeita o maximo do ajuste", !ConfigTuning.TryParse(velocidade, "99", out _, out _));
Check("aceita dentro da faixa", ConfigTuning.TryParse(velocidade, "1.5", out _, out _));

var profissoes = ConfigTuning.Find("MaxPrimaryTradeSkill")!;
Check("inteiro recusa decimal", !ConfigTuning.TryParse(profissoes, "2.5", out _, out _));
Check("inteiro aceita inteiro", ConfigTuning.TryParse(profissoes, "4", out _, out _));
Check("inteiro respeita o teto", !ConfigTuning.TryParse(profissoes, "12", out _, out _));

var durabilidade = ConfigTuning.Find("DurabilityLoss.OnDeath")!;
Check("porcentagem recusa acima de 100", !ConfigTuning.TryParse(durabilidade, "150", out _, out _));
Check("porcentagem aceita 0", ConfigTuning.TryParse(durabilidade, "0", out _, out _));

Check("ajuda do ajuste sai montada",
      xp.Help.Examples.Count > 0 && xp.Help.Title.Contains("Rate.XP.Kill"));

Check("Find acha uma chave existente", FieldHelp.Find("db.host") is not null);
Check("Find devolve null para chave inexistente", FieldHelp.Find("nao.existe") is null);
Check("Find diferencia maiuscula", FieldHelp.Find("DB.HOST") is null);

// A checagem que realmente importa: cada Chave="..." usada no XAML precisa
// existir aqui. Como o projeto WPF nao compila em todo ambiente, um erro de
// digitacao passaria despercebido ate virar um balao vazio na tela do usuario.
var raiz = AppContext.BaseDirectory;
string? repo = null;
for (var pasta = new DirectoryInfo(raiz); pasta is not null; pasta = pasta.Parent)
{
    if (Directory.Exists(Path.Combine(pasta.FullName, "WowServer.Gui"))) { repo = pasta.FullName; break; }
    if (Directory.Exists(Path.Combine(pasta.FullName, "gui", "WowServer.Gui")))
    {
        repo = Path.Combine(pasta.FullName, "gui");
        break;
    }
}

if (repo is null)
{
    Check("achei o projeto da interface para conferir as chaves", false, raiz);
}
else
{
    var xamls = Directory.GetFiles(Path.Combine(repo, "WowServer.Gui"), "*.xaml",
                                   SearchOption.AllDirectories);
    Check("achei arquivos .xaml", xamls.Length > 0, repo);

    var usadas = new List<(string Chave, string Arquivo)>();
    foreach (var arquivo in xamls)
    {
        foreach (System.Text.RegularExpressions.Match m in
                 System.Text.RegularExpressions.Regex.Matches(
                     File.ReadAllText(arquivo), @"Chave=""([^""]*)"""))
        {
            usadas.Add((m.Groups[1].Value, Path.GetFileName(arquivo)));
        }
    }

    Check("a interface usa o catalogo de ajuda", usadas.Count > 0);

    foreach (var (chave, arquivo) in usadas)
    {
        Check($"chave '{chave}' usada em {arquivo} existe no catalogo",
              FieldHelp.Find(chave) is not null);
    }

    // Entrada nao usada nao quebra nada, mas e texto escrito a toa.
    var orfas = FieldHelp.All
        .Select(e => e.Key)
        .Where(k => !usadas.Any(u => u.Chave == k))
        .ToList();
    Check("nenhuma ajuda ficou sem campo que a use", orfas.Count == 0, string.Join(", ", orfas));
}

Console.WriteLine($"\n{total - falhas}/{total} testes passaram (final)");
return falhas == 0 ? 0 : 1;
