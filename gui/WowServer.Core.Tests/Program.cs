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

// O nome do modulo Lua e o bug em pessoa: o CMake do core so liga a lib Lua ao
// alvo dos modulos dentro de 'if (SOURCE_MODULE MATCHES "mod-ale")', e
// SOURCE_MODULE e o nome da PASTA. Oferecer o endereco antigo faz a pasta sair
// como 'mod-eluna' e a compilacao para em 25 erros C1083 identicos.
Check("modulo Lua se chama mod-ale", ModuleCatalog.Find("mod-ale") is not null);
Check("catalogo nao oferece mais o nome antigo", ModuleCatalog.Find("mod-eluna") is null);

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

// ---- busca de itens -------------------------------------------------------
var sqlVazio = ItemBrowser.BuildQuery(new ItemFilter(), "acore_world");
Check("consulta sem filtro nao tem WHERE", !sqlVazio.Contains("WHERE"), sqlVazio);
Check("consulta usa o banco informado", sqlVazio.Contains("`acore_world`.item_template"));
Check("consulta tem limite", sqlVazio.Contains("LIMIT 200"));
Check("consulta traz o displayid", sqlVazio.Contains("displayid"));

var sqlFiltrado = ItemBrowser.BuildQuery(
    new ItemFilter { Name = "espada", Class = 2, Quality = 4, MinLevel = 60, MaxLevel = 80, Limit = 50 },
    "acore_world");
Check("filtra por nome", sqlFiltrado.Contains("name LIKE '%espada%'"), sqlFiltrado);
Check("filtra por classe", sqlFiltrado.Contains("class = 2"));
Check("filtra por qualidade", sqlFiltrado.Contains("Quality = 4"));
Check("filtra por faixa de nivel",
      sqlFiltrado.Contains("ItemLevel >= 60") && sqlFiltrado.Contains("ItemLevel <= 80"));
Check("respeita o limite pedido", sqlFiltrado.Contains("LIMIT 50"));

Check("limite tem teto",
      ItemBrowser.BuildQuery(new ItemFilter { Limit = 99999 }, "w").Contains($"LIMIT {ItemBrowser.LimiteMaximo}"));
Check("limite tem piso",
      ItemBrowser.BuildQuery(new ItemFilter { Limit = 0 }, "w").Contains("LIMIT 1"));

// Nome digitado vai para dentro de um LIKE. Sem escape, uma aspa fecharia a
// string e o resto viraria comando.
Check("escapa aspa simples", ItemBrowser.EscaparLike("O'Reilly") == "O''Reilly");
Check("escapa contrabarra", ItemBrowser.EscaparLike(@"a\b") == @"a\\b");
Check("escapa curinga %", ItemBrowser.EscaparLike("50%") == @"50\%");
Check("escapa curinga _", ItemBrowser.EscaparLike("a_b") == @"a\_b");
// A contrabarra tem que ser escapada ANTES: na ordem inversa, as que o proprio
// escape insere seriam dobradas de novo.
Check("ordem do escape nao dobra o que ele mesmo inseriu",
      ItemBrowser.EscaparLike("100%") == @"100\%");
Check("nome com aspa nao escapa da string",
      ItemBrowser.BuildQuery(new ItemFilter { Name = "O'Reilly" }, "w")
                 .Contains("name LIKE '%O''Reilly%'"));

// leitura de uma linha real do cliente mysql
var linha = string.Join("\t", new[]
{
    "12345", "Espada de Teste", "2", "7", "4", "13", "213", "80", "2", "0",
    "150.5", "250.75", "2600", "105", "48500", "1",
    "4", "35", "7", "42", "32", "18", "0", "0", "0", "0", "Uma espada de teste", "31265",
});
var item = ItemBrowser.ParseRow(linha);
Check("le a linha do mysql", item is not null);
Check("le entry e nome", item!.Entry == 12345 && item.Name == "Espada de Teste");
Check("le dano decimal", Math.Abs(item.DmgMin - 150.5) < 0.01, item.DmgMin.ToString());
Check("le tres atributos preenchidos", item.Stats.Count == 3, item.Stats.Count.ToString());
Check("descarta atributos zerados", item.Stats.All(s => s.Value != 0));
Check("le a descricao", item.Description == "Uma espada de teste");
Check("le o displayid, que liga ao icone", item.DisplayId == 31265, item.DisplayId.ToString());

Check("cabecalho nao vira item", ItemBrowser.ParseRow("entry\tname\tclass") is null);
Check("linha vazia nao vira item", ItemBrowser.ParseRow("") is null);
Check("linha curta nao vira item", ItemBrowser.ParseRow("1\t2\t3") is null);

Check("cor de epico", ItemBrowser.CorDaQualidade(4) == "#A335EE");
Check("cor de heranca", ItemBrowser.CorDaQualidade(7) == "#00CCFF");
Check("subclasse de arma", ItemBrowser.NomeDaSubclasse(2, 7) == "Espada (1 mão)");
Check("subclasse de armadura", ItemBrowser.NomeDaSubclasse(4, 4) == "Placas");
Check("classe sem subclasse nomeada", ItemBrowser.NomeDaSubclasse(0, 3) == "");

Check("dinheiro em ouro/prata/cobre", ItemBrowser.FormatarDinheiro(48500) == "4o 85p");
Check("dinheiro so em cobre", ItemBrowser.FormatarDinheiro(37) == "37c");
Check("dinheiro zero fica vazio", ItemBrowser.FormatarDinheiro(0) == "");

// balao
var balao = ItemTooltip.Build(item);
Check("balao comeca pelo nome", balao[0].Text == "Espada de Teste");
Check("nome sai na cor da qualidade", balao[0].Color == "#A335EE");
Check("balao mostra o vinculo", balao.Any(l => l.Text == "Vincula ao equipar"));
// O numero exato depende de arredondar ou truncar, e isso nao da para
// conferir sem o client na frente. O que se afirma aqui e que a faixa
// aparece, com os dois extremos.
Check("balao mostra a faixa de dano",
      balao.Any(l => l.Text.Contains("de dano") && l.Text.Contains(" - ")),
      string.Join(" | ", balao.Select(l => l.Text)));
// dps = media do dano / (delay em segundos) = 200.625 / 2.6
Check("balao calcula o dps", balao.Any(l => l.Text.Contains("77.2")),
      string.Join(" | ", balao.Select(l => l.Text)));
Check("balao mostra atributos por nome", balao.Any(l => l.Text == "+35 de Força"));
Check("balao mostra nivel do item", balao.Any(l => l.Text == "Nível do item: 213"));
Check("balao mostra o id", balao.Any(l => l.Text == "ID 12345"));

// item simples nao inventa linhas
var simples = ItemBrowser.ParseRow(string.Join("\t", new[]
{
    "999", "Pano de Linho", "7", "5", "1", "0", "1", "1", "0", "0",
    "0", "0", "0", "0", "10", "20",
    "0", "0", "0", "0", "0", "0", "0", "0", "0", "0", "", "1542",
}))!;
var balaoSimples = ItemTooltip.Build(simples);
Check("item sem dano nao mostra dps", !balaoSimples.Any(l => l.Text.Contains("por segundo")));
Check("item sem atributo nao lista atributos", simples.Stats.Count == 0);
Check("item sem vinculo nao mostra vinculo", !balaoSimples.Any(l => l.Text.StartsWith("Vincula")));

// ------------------------------------------- item customizado no client ---
Console.WriteLine("\n=== ClientItemCheck: item que o client nao mostra ===");

// Item feito a mao: entry fora da faixa do jogo e displayid da era ICC.
static ItemRow Customizado(int entry, int displayId) => new(
    entry, "Elmo de Herança", 4, 1, 7, 1, 1, 1, 1, 100,
    0, 0, 0, 0, 0, 1, Array.Empty<(int, int)>(), "", displayId);

var idsDoClient = new HashSet<int> { 1, 2, 42, 12345 };

var ok = ClientItemCheck.Check(Customizado(12345, 31657), idsDoClient);
Check("item normal nao acusa nada", ok.Count == 0,
      string.Join(" | ", ok.Select(x => x.Summary)));

var alto = ClientItemCheck.Check(Customizado(12345, 64190), idsDoClient);
Check("displayid da era ICC e acusado",
      alto.Any(x => x.Problem == ClientItemProblem.DisplayIdAltoDemais));
Check("limite e 32000 exclusivo",
      ClientItemCheck.Check(Customizado(12345, 32000), idsDoClient)
          .Any(x => x.Problem == ClientItemProblem.DisplayIdAltoDemais));
Check("logo abaixo do limite passa",
      ClientItemCheck.Check(Customizado(12345, 31999), idsDoClient).Count == 0);

var semDbc = ClientItemCheck.Check(Customizado(700000, 31657), idsDoClient);
Check("entry fora do Item.dbc e acusada",
      semDbc.Any(x => x.Problem == ClientItemProblem.ForaDoItemDbc));

Check("os dois problemas juntos saem juntos",
      ClientItemCheck.Check(Customizado(700000, 64190), idsDoClient).Count == 2);

// Sem o Item.dbc extraido nao da para afirmar ausencia. Acusar todo item
// seria pior que nao checar - e todo item do jogo cairia no aviso.
Check("sem Item.dbc nao acusa ausencia",
      !ClientItemCheck.Check(Customizado(700000, 31657), null)
          .Any(x => x.Problem == ClientItemProblem.ForaDoItemDbc));
Check("sem Item.dbc o displayid ainda e conferido",
      ClientItemCheck.Check(Customizado(700000, 64190), null)
          .Any(x => x.Problem == ClientItemProblem.DisplayIdAltoDemais));

Check("aviso curto some quando esta tudo bem",
      ClientItemCheck.ShortWarning(Customizado(12345, 31657), idsDoClient) is null);
Check("aviso curto aparece quando ha problema",
      ClientItemCheck.ShortWarning(Customizado(700000, 64190), idsDoClient)?.Contains("cliente") == true);

// o balao carrega o aviso, e sem a lista continua igual ao que sempre foi
var balaoQuebrado = ItemTooltip.Build(Customizado(700000, 64190), idsDoClient);
Check("balao mostra o aviso do client",
      balaoQuebrado.Any(l => l.Text.StartsWith("[cliente]")));
Check("aviso do client sai em vermelho",
      balaoQuebrado.Where(l => l.Text.StartsWith("[cliente]")).All(l => l.Color == ItemTooltip.Vermelho));
Check("aviso vai depois do ID, no fim",
      balaoQuebrado[^1].Text.StartsWith("[cliente]"));
Check("balao sem a lista nao ganha aviso de ausencia",
      !ItemTooltip.Build(Customizado(700000, 31657)).Any(l => l.Text.StartsWith("[cliente]")));

// Item.dbc de verdade, escrito byte a byte no formato do 3.3.5a e lido de
// volta pelo nosso proprio leitor de DBC.
var dbcDir = Path.Combine(Path.GetTempPath(), "itemdbc-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(dbcDir, "dbc"));
try
{
    Check("sem o arquivo devolve null, nao vazio",
          ClientItemCheck.ReadClientItemIds(dbcDir) is null);

    // 8 campos de 4 bytes; campo 0 = ID.
    var entradas = new[] { 25, 56806, 700000 };
    using (var ms = new MemoryStream())
    using (var w = new BinaryWriter(ms))
    {
        w.Write(new[] { (byte)'W', (byte)'D', (byte)'B', (byte)'C' });
        w.Write((uint)entradas.Length);   // recordCount
        w.Write((uint)8);                 // fieldCount
        w.Write((uint)32);                // recordSize = 8 * 4
        w.Write((uint)1);                 // stringBlockSize
        foreach (var id in entradas)
        {
            w.Write((uint)id);
            for (var c = 1; c < 8; c++) w.Write((uint)0);
        }
        w.Write((byte)0);                 // bloco de strings
        File.WriteAllBytes(Path.Combine(dbcDir, "dbc", "Item.dbc"), ms.ToArray());
    }

    var lidos = ClientItemCheck.ReadClientItemIds(dbcDir);
    Check("le as entries do Item.dbc", lidos is not null && lidos.Count == 3);
    Check("le o primeiro id", lidos!.Contains(25));
    Check("le o ultimo id", lidos.Contains(700000));
    Check("nao inventa id", !lidos.Contains(26));
}
finally
{
    try { Directory.Delete(dbcDir, true); } catch { }
}

// ------------------------------------------------ arquivos .conf ---------
Console.WriteLine("\n=== ConfFile: ler e editar .conf sem reescrever ===");

// CRLF de proposito: e o que o Windows escreve, e um '$' de regex em modo
// multilinha casaria antes do \r e nao acharia o fim da linha.
var confTexto = string.Join("\r\n", new[]
{
    "# Este e o cabecalho do arquivo.",
    "",
    "#    Rate.XP.Kill",
    "#        Description: Multiplicador de XP por abate.",
    "#        Default:     1",
    "",
    "Rate.XP.Kill = 3",
    "",
    "#    PlayerbotsDatabaseInfo",
    "#        Description: String de conexao.",
    "",
    "PlayerbotsDatabaseInfo = \"127.0.0.1;3306;acore;acore;acore_playerbots\"",
    "",
    "AhBot.Account = 0    # id da conta do bot",
    "",
});

var confEntradas = ConfFile.Parse(confTexto);
Check("acha as tres chaves", confEntradas.Count == 3, confEntradas.Count.ToString());
Check("le o valor simples", confEntradas[0].Value == "3");
Check("guarda o comentario que explica",
      confEntradas[0].Comment.Contains("Multiplicador de XP"), confEntradas[0].Comment);
Check("tira as aspas quando pedido",
      confEntradas[1].Unquoted == "127.0.0.1;3306;acore;acore;acore_playerbots",
      confEntradas[1].Unquoted);
Check("comentario no fim da linha nao vira valor",
      confEntradas[2].Value == "0", "[" + confEntradas[2].Value + "]");

// Trocar um valor nao pode mexer em mais nada.
var trocado = ConfFile.SetValue(confTexto, "Rate.XP.Kill", "5")!;
Check("trocou o valor", ConfFile.Parse(trocado)[0].Value == "5");
Check("preservou o CRLF", trocado.Contains("\r\n") && !trocado.Contains("\n\r\n\r"));
Check("preservou os comentarios",
      trocado.Contains("Description: Multiplicador de XP por abate."));
Check("nao duplicou a chave",
      ConfFile.Parse(trocado).Count(x => x.Key == "Rate.XP.Kill") == 1);
Check("as outras chaves ficaram intactas",
      ConfFile.Parse(trocado)[2].Value == "0");

// O mesmo texto em LF puro tem que funcionar igual - literal escrito no Linux.
var confLf = confTexto.Replace("\r\n", "\n");
Check("funciona em LF tambem", ConfFile.Parse(ConfFile.SetValue(confLf, "Rate.XP.Kill", "7")!)[0].Value == "7");

Check("mantem as aspas de quem tinha",
      ConfFile.SetValue(confTexto, "PlayerbotsDatabaseInfo", "1.2.3.4;3306;u;p;d")!
              .Contains("\"1.2.3.4;3306;u;p;d\""));
Check("preserva o comentario de fim de linha",
      ConfFile.SetValue(confTexto, "AhBot.Account", "102")!.Contains("# id da conta do bot"));
Check("chave que nao existe devolve null",
      ConfFile.SetValue(confTexto, "NaoExiste", "1") is null);

var multi = ConfFile.SetValues(confTexto, new[]
{
    new KeyValuePair<string, string>("Rate.XP.Kill", "10"),
    new KeyValuePair<string, string>("Inventada", "1"),
}, out var confFaltando);
Check("aplica varias de uma vez", ConfFile.Parse(multi)[0].Value == "10");
Check("relata a chave que nao existia",
      confFaltando.Count == 1 && confFaltando[0] == "Inventada");

// Ler de disco, gravar sem BOM - um BOM quebra o parser do AzerothCore.
var confDir = Path.Combine(Path.GetTempPath(), "conf-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(confDir);
try
{
    var alvo = Path.Combine(confDir, "mod_teste.conf");
    ConfFile.Save(alvo, confTexto);

    var bytes = File.ReadAllBytes(alvo);
    Check("gravou sem BOM", !(bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF));

    var lidos = ConfFile.ReadValues(alvo);
    Check("le do disco", lidos["Rate.XP.Kill"] == "3");
    Check("busca ignora maiusculas", lidos["rate.xp.kill"] == "3");
    Check("arquivo inexistente devolve vazio, nao estoura",
          ConfFile.ReadValues(Path.Combine(confDir, "nao-existe.conf")).Count == 0);

    var copia = ConfFile.Backup(alvo);
    Check("fez copia antes de gravar", File.Exists(copia));
    Check("a copia tem o conteudo original", File.ReadAllText(copia).Contains("Rate.XP.Kill = 3"));

    // --- descoberta dos .conf de cada modulo ---
    Console.WriteLine("\n=== ModuleConfigs: ligar modulo ao .conf ===");

    var modDir = Path.Combine(confDir, "server", "configs", "modules");
    Directory.CreateDirectory(modDir);
    foreach (var nome in new[]
             {
                 "mod_ahbot.conf", "playerbots.conf", "playerbots_rpg.conf",
                 "mod_ale.conf", "mod_solocraft.conf", "orfao.conf",
                 "mod_ahbot.conf.dist",
             })
    {
        File.WriteAllText(Path.Combine(modDir, nome), "Chave = 1\n");
    }

    Check("normaliza tirando o prefixo mod", ModuleConfigs.Normalize("mod-ah-bot") == "ahbot");
    Check("normaliza o nome do arquivo", ModuleConfigs.Normalize("mod_ahbot") == "ahbot");
    Check("nome sem prefixo fica igual", ModuleConfigs.Normalize("playerbots") == "playerbots");
    Check("liga mod-ah-bot a mod_ahbot.conf", ModuleConfigs.Matches("mod-ah-bot", "mod_ahbot.conf"));
    Check("liga mod-playerbots a playerbots.conf", ModuleConfigs.Matches("mod-playerbots", "playerbots.conf"));
    Check("liga o .conf com sufixo", ModuleConfigs.Matches("mod-playerbots", "playerbots_rpg.conf"));
    Check("nao liga modulos diferentes", !ModuleConfigs.Matches("mod-ah-bot", "mod_solocraft.conf"));

    var instalados = new[] { "mod-ah-bot", "mod-playerbots", "mod-ale", "mod-solocraft" };
    var achadosConf = ModuleConfigs.Discover(Path.Combine(confDir, "server"), instalados);

    Check("ignora o .conf.dist, que e so modelo",
          achadosConf.All(c => !c.FileName.EndsWith(".dist", StringComparison.Ordinal)),
          string.Join(",", achadosConf.Select(c => c.FileName)));
    Check("achou os seis .conf", achadosConf.Count == 6, achadosConf.Count.ToString());
    Check("o .conf sem dono fica sem dono",
          achadosConf.Single(c => c.FileName == "orfao.conf").ModuleName == "");
    Check("playerbots fica com os dois arquivos",
          achadosConf.Count(c => c.ModuleName == "mod-playerbots") == 2);

    var soDoAhbot = ModuleConfigs.ForModule(Path.Combine(confDir, "server"), "mod-ah-bot");
    Check("ForModule traz so o do modulo pedido",
          soDoAhbot.Count == 1 && soDoAhbot[0].FileName == "mod_ahbot.conf");
    Check("modulo sem .conf devolve lista vazia",
          ModuleConfigs.ForModule(Path.Combine(confDir, "server"), "mod-transmog").Count == 0);
    Check("pasta inexistente devolve lista vazia",
          ModuleConfigs.Discover(Path.Combine(confDir, "nada"), instalados).Count == 0);
}
finally
{
    try { Directory.Delete(confDir, true); } catch { }
}

// --------------------------------------------- valor atual do ajuste ------
Console.WriteLine("\n=== ConfigStatus: mostrar o que esta no arquivo ===");

var ajusteXp = ConfigTuning.Find("Rate.XP.Kill")!;

Check("1 e 1.0 sao o mesmo valor", ConfigStatus.SameValue("1", "1.0"));
Check("espacos nao contam", ConfigStatus.SameValue(" 3 ", "3"));
Check("valores diferentes sao diferentes", !ConfigStatus.SameValue("3", "5"));
Check("texto nao numerico compara como texto", ConfigStatus.SameValue("Console Server", "console server"));

Check("igual ao padrao nao e mudanca", !ConfigStatus.IsChanged(ajusteXp, "1"));
Check("1.0 tambem nao e mudanca", !ConfigStatus.IsChanged(ajusteXp, "1.0"));
Check("valor diferente e mudanca", ConfigStatus.IsChanged(ajusteXp, "3"));
Check("sem valor lido nao afirma mudanca", !ConfigStatus.IsChanged(ajusteXp, null));

Check("sem arquivo lido diz que nao leu",
      ConfigStatus.Describe(ajusteXp, null).Contains("não li o arquivo"));
Check("mostra o valor de agora",
      ConfigStatus.Describe(ajusteXp, "3").Contains("agora 3"));
Check("diz quando o valor e o padrao",
      ConfigStatus.Describe(ajusteXp, "1").Contains("é o padrão"));
Check("mostra a chave para procurar no arquivo",
      ConfigStatus.Describe(ajusteXp, "3").Contains("Rate.XP.Kill"));

// ------------------------------------------- pasta com nome antigo --------
Console.WriteLine("\n=== RenamedModules: mod-eluna precisa virar mod-ale ===");

var renDir = Path.Combine(Path.GetTempPath(), "ren-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(renDir);
try
{
    Check("pasta inexistente nao acusa nada",
          RenamedModules.Detect(Path.Combine(renDir, "nada")).Count == 0);

    Directory.CreateDirectory(Path.Combine(renDir, "mod-ah-bot"));
    Check("instalacao limpa nao acusa nada", RenamedModules.Detect(renDir).Count == 0);

    Directory.CreateDirectory(Path.Combine(renDir, "mod-eluna"));
    var ren = RenamedModules.Detect(renDir);
    Check("acha a pasta com nome antigo", ren.Count == 1);
    Check("aponta o nome certo", ren[0].Correct == "mod-ale");
    Check("sem conflito quando so a antiga existe", !ren[0].Conflict);
    Check("explica sem falar em conflito", ren[0].Explanation.Contains("lua.h"));

    Directory.CreateDirectory(Path.Combine(renDir, "mod-ale"));
    var ren2 = RenamedModules.Detect(renDir);
    Check("acusa conflito com as duas", ren2[0].Conflict);
    Check("no conflito manda remover", ren2[0].Explanation.Contains("remova"));

    Directory.Delete(Path.Combine(renDir, "mod-eluna"));
    Check("so o nome certo nao acusa nada", RenamedModules.Detect(renDir).Count == 0);
}
finally
{
    try { Directory.Delete(renDir, true); } catch { }
}

// ------------------------------------ janela de console para script ------
Console.WriteLine("\n=== ConsoleScriptCommand ===");

var cmd = ConsoleScriptCommand.Build(@"C:\repo\scripts\start-mysql.ps1", new[] { "-Automatic" });
Check("chama o script", cmd.StartsWith(@"& 'C:\repo\scripts\start-mysql.ps1' -Automatic"), cmd);
Check("guarda o exit code antes de qualquer outra coisa",
      cmd.Contains("$c = $LASTEXITCODE"));
// -NoExit devolveria o codigo de fechar a janela, nao o do script: uma senha
// errada voltaria como sucesso.
Check("nao usa -NoExit", !cmd.Contains("-NoExit"));
Check("espera o Enter antes de fechar", cmd.Contains("Read-Host"));
Check("devolve o codigo do script no fim", cmd.TrimEnd().EndsWith("exit $c"));
Check("script sem argumento tambem funciona",
      ConsoleScriptCommand.Build(@"C:\r\s.ps1").StartsWith(@"& 'C:\r\s.ps1'; "));
Check("apostrofo no caminho e escapado",
      ConsoleScriptCommand.Build(@"C:\Mateus's\s.ps1").Contains(@"C:\Mateus''s\s.ps1"));
Check("valor com espaco vai entre aspas",
      ConsoleScriptCommand.Build(@"C:\s.ps1", new[] { "-Name", "Meu Servidor" })
          .Contains("-Name 'Meu Servidor'"));

// cartas de correio
Check("nenhum item, nenhuma carta", MailPlan.Build(Array.Empty<int>(), "Mateus").Count == 0);
Check("12 itens cabem numa carta", MailPlan.Build(Enumerable.Range(1, 12).ToList(), "Mateus").Count == 1);
Check("13 itens viram duas cartas", MailPlan.Build(Enumerable.Range(1, 13).ToList(), "Mateus").Count == 2);
Check("25 itens viram tres cartas", MailPlan.Build(Enumerable.Range(1, 25).ToList(), "Mateus").Count == 3);

var cartas = MailPlan.Build(Enumerable.Range(1, 13).ToList(), "Mateus");
Check("carta comeca com send items", cartas[0].StartsWith("send items Mateus "));
Check("carta numerada quando ha mais de uma", cartas[0].Contains("1/2") && cartas[1].Contains("2/2"));
Check("primeira carta leva 12 ids", cartas[0].Split(' ').Count(p => int.TryParse(p, out _)) == 12);
Check("segunda carta leva o resto", cartas[1].TrimEnd().EndsWith(" 13"));
Check("carta unica nao e numerada",
      !MailPlan.Build(new[] { 1 }, "Mateus")[0].Contains("1/1"));

var addItens = MailPlan.BuildAddItem(new[] { 42943, 42944 });
Check("additem sai um por item", addItens.Count == 2);
Check("additem leva o ponto do chat", addItens[0] == ".additem 42943");

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

    // O projeto WPF nao compila neste ambiente, entao tres erros que o
    // compilador pegaria sao conferidos aqui: XAML que nao e XML valido,
    // handler citado num evento e que nao existe no code-behind, e
    // StaticResource que nao esta definido em lugar nenhum. Qualquer um dos
    // tres so apareceria como a janela morrendo ao abrir, na maquina do
    // usuario.
    foreach (var arquivo in xamls)
    {
        var nome = Path.GetFileName(arquivo);
        var xmlValido = true;
        try { System.Xml.Linq.XDocument.Load(arquivo); } catch { xmlValido = false; }
        Check($"{nome} e XML valido", xmlValido);
    }

    var eventos = new System.Text.RegularExpressions.Regex(
        @"\b(?:Click|SelectionChanged|TextChanged|Checked|Unchecked|MouseEnter|KeyDown|RequestNavigate|IsVisibleChanged)=""([^""]+)""");

    foreach (var arquivo in xamls)
    {
        var codigo = arquivo + ".cs";
        if (!File.Exists(codigo)) continue;

        var corpo = File.ReadAllText(codigo);
        foreach (System.Text.RegularExpressions.Match m in eventos.Matches(File.ReadAllText(arquivo)))
        {
            var handler = m.Groups[1].Value;
            Check($"handler {handler} existe em {Path.GetFileName(codigo)}",
                  System.Text.RegularExpressions.Regex.IsMatch(
                      corpo, @"\bvoid\s+" + System.Text.RegularExpressions.Regex.Escape(handler) + @"\s*\("));
        }
    }

    var appXaml = Path.Combine(repo, "WowServer.Gui", "App.xaml");
    var chavesGlobais = new HashSet<string>(
        System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(appXaml), @"x:Key=""([^""]+)""")
            .Select(m => m.Groups[1].Value));

    var recursosFaltando = new List<string>();
    foreach (var arquivo in xamls)
    {
        var texto = File.ReadAllText(arquivo);
        var locais = new HashSet<string>(
            System.Text.RegularExpressions.Regex.Matches(texto, @"x:Key=""([^""]+)""")
                .Select(m => m.Groups[1].Value));

        foreach (System.Text.RegularExpressions.Match m in
                 System.Text.RegularExpressions.Regex.Matches(texto, @"\{StaticResource ([^}]+)\}"))
        {
            var chave = m.Groups[1].Value.Trim();
            if (chave.StartsWith("{", StringComparison.Ordinal)) continue;  // {x:Type ...}
            if (!chavesGlobais.Contains(chave) && !locais.Contains(chave))
                recursosFaltando.Add($"{Path.GetFileName(arquivo)}: {chave}");
        }
    }
    Check("todo StaticResource usado existe", recursosFaltando.Count == 0,
          string.Join(", ", recursosFaltando));

    // Script que ninguem alcanca pela interface e uma funcionalidade que so
    // existe para quem abre o PowerShell. Este teste falha de proposito quando
    // um script novo entra sem botao - ou o botao aparece, ou o script entra na
    // lista de excecoes abaixo, com o motivo escrito.
    var pastaScripts = Path.Combine(Directory.GetParent(repo)!.FullName, "scripts");
    if (Directory.Exists(pastaScripts))
    {
        // A interface faz estes tres por conta propria, com o console ao vivo:
        // subir e desligar o servidor passam pelo ServerController, e a
        // instalacao completa e a lista de etapas do InstallPlan.
        var semBotaoDeProposito = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "setup-all.ps1", "start-server.ps1", "stop-server.ps1",
        };

        var citados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var arquivo in Directory.GetFiles(repo, "*.cs", SearchOption.AllDirectories))
        {
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(
                         File.ReadAllText(arquivo), @"""([A-Za-z0-9._-]+\.ps1)"""))
            {
                citados.Add(m.Groups[1].Value);
            }
        }

        var semBotao = Directory.GetFiles(pastaScripts, "*.ps1")
            .Select(Path.GetFileName)
            .Where(n => n is not null && !citados.Contains(n) && !semBotaoDeProposito.Contains(n))
            .ToList();

        Check("todo script tem como ser usado pela interface",
              semBotao.Count == 0, string.Join(", ", semBotao));
    }
}


// ---- icones: BLP e MPQ ----------------------------------------------------
// Os dois formatos sao construidos aqui e lidos de volta pelo codigo de
// verdade. Nao substitui testar contra um client real, mas prova que o
// decodificador entende arquivos validos - e um erro de bit aparece na hora.

static byte[] MontarBlp(byte encoding, byte alphaDepth, byte alphaEncoding,
                        int w, int h, byte[] corpo, byte[]? paleta = null)
{
    var cabecalho = new byte[148 + 256 * 4];
    cabecalho[0] = (byte)'B'; cabecalho[1] = (byte)'L';
    cabecalho[2] = (byte)'P'; cabecalho[3] = (byte)'2';
    BitConverter.GetBytes((uint)1).CopyTo(cabecalho, 4);
    cabecalho[8] = encoding; cabecalho[9] = alphaDepth; cabecalho[10] = alphaEncoding;
    BitConverter.GetBytes((uint)w).CopyTo(cabecalho, 12);
    BitConverter.GetBytes((uint)h).CopyTo(cabecalho, 16);

    var offsetCorpo = (uint)cabecalho.Length;
    BitConverter.GetBytes(offsetCorpo).CopyTo(cabecalho, 20);
    BitConverter.GetBytes((uint)corpo.Length).CopyTo(cabecalho, 20 + 16 * 4);

    if (paleta is not null) paleta.CopyTo(cabecalho, 148);

    var todo = new byte[cabecalho.Length + corpo.Length];
    cabecalho.CopyTo(todo, 0);
    corpo.CopyTo(todo, cabecalho.Length);
    return todo;
}

Check("reconhece um BLP2", BlpImage.IsBlp(MontarBlp(3, 8, 0, 1, 1, new byte[4])));
Check("recusa o que nao e BLP", !BlpImage.IsBlp(new byte[] { 1, 2, 3, 4 }));

// BGRA direto: o caminho mais simples, serve de referencia
var bgra = new byte[] { 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120, 130, 140, 150, 160 };
var direto = BlpImage.Decode(MontarBlp(3, 8, 0, 2, 2, bgra));
Check("BGRA: tamanho lido", direto.Width == 2 && direto.Height == 2);
Check("BGRA: pixels intactos", direto.Bgra.SequenceEqual(bgra));

// paleta: indice por pixel + alfa separado
var paleta = new byte[256 * 4];
paleta[0] = 1; paleta[1] = 2; paleta[2] = 3;          // cor 0
paleta[4] = 200; paleta[5] = 150; paleta[6] = 100;    // cor 1
var comPaleta = BlpImage.Decode(
    MontarBlp(1, 8, 0, 2, 1, new byte[] { 1, 0, 250, 30 }, paleta));
Check("paleta: primeira cor", comPaleta.Bgra[0] == 200 && comPaleta.Bgra[1] == 150 && comPaleta.Bgra[2] == 100);
Check("paleta: segunda cor", comPaleta.Bgra[4] == 1 && comPaleta.Bgra[5] == 2 && comPaleta.Bgra[6] == 3);
Check("paleta: alfa separado", comPaleta.Bgra[3] == 250 && comPaleta.Bgra[7] == 30);

// DXT1: bloco de cor unica. c0 = branco puro em RGB565.
var dxt1 = new byte[8];
BitConverter.GetBytes((ushort)0xFFFF).CopyTo(dxt1, 0);   // c0 branco
BitConverter.GetBytes((ushort)0x0000).CopyTo(dxt1, 2);   // c1 preto
BitConverter.GetBytes((uint)0).CopyTo(dxt1, 4);          // todos indice 0
var img1 = BlpImage.Decode(MontarBlp(2, 0, 0, 4, 4, dxt1));
Check("DXT1: 4x4 decodificado", img1.Width == 4 && img1.Height == 4);
Check("DXT1: branco vira 255 em todo canal",
      img1.Bgra[0] == 255 && img1.Bgra[1] == 255 && img1.Bgra[2] == 255,
      $"{img1.Bgra[0]},{img1.Bgra[1]},{img1.Bgra[2]}");
Check("DXT1: opaco", img1.Bgra[3] == 255);

// DXT5: alfa constante em 255
var dxt5 = new byte[16];
dxt5[0] = 255; dxt5[1] = 255;                            // a0 = a1 = 255
BitConverter.GetBytes((ushort)0xFFFF).CopyTo(dxt5, 8);
BitConverter.GetBytes((ushort)0x0000).CopyTo(dxt5, 10);
var img5 = BlpImage.Decode(MontarBlp(2, 8, 7, 4, 4, dxt5));
Check("DXT5: decodifica", img5.Width == 4);
Check("DXT5: alfa cheio", img5.Bgra[3] == 255, img5.Bgra[3].ToString());

Check("BLP truncado e recusado",
      Throws(() => BlpImage.Decode(new byte[] { (byte)'B', (byte)'L', (byte)'P', (byte)'2' })));
Check("encoding desconhecido e recusado",
      Throws(() => BlpImage.Decode(MontarBlp(9, 0, 0, 2, 2, new byte[16]))));

// PNG: cabecalho, e o zlib de dentro tem que abrir
var png = BlpImage.ToPng(direto);
Check("PNG comeca com a assinatura",
      png[0] == 0x89 && png[1] == (byte)'P' && png[2] == (byte)'N' && png[3] == (byte)'G');
Check("PNG tem IHDR e IEND",
      System.Text.Encoding.ASCII.GetString(png).Contains("IHDR")
      && System.Text.Encoding.ASCII.GetString(png).Contains("IEND"));
Check("PNG declara o tamanho certo",
      png[16] == 0 && png[17] == 0 && png[18] == 0 && png[19] == 2
      && png[23] == 2, "largura/altura no IHDR");

static bool Throws(Action a) { try { a(); return false; } catch { return true; } }

// --- MPQ: um arquivo montado a mao, lido de volta
static byte[] MontarMpq(string nomeInterno, byte[] conteudo, bool comprimir)
{
    const int entradasHash = 4;
    var corpo = conteudo;

    if (comprimir)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(0x02);   // metodo zlib
        using (var z = new System.IO.Compression.ZLibStream(
                   ms, System.IO.Compression.CompressionLevel.Optimal, true))
            z.Write(conteudo);
        corpo = ms.ToArray();
    }

    var cabecalho = 32;
    var posDados = cabecalho;
    var posHash = posDados + corpo.Length;
    var posBloco = posHash + entradasHash * 16;
    var total = posBloco + 16;

    var mpq = new byte[total];
    // Mesmo layout que o leitor espera; se um dos dois estiver errado, o
    // arquivo montado aqui nao abre.
    BitConverter.GetBytes(0x1A51504Du).CopyTo(mpq, 0);
    BitConverter.GetBytes((uint)cabecalho).CopyTo(mpq, 4);
    BitConverter.GetBytes((uint)total).CopyTo(mpq, 8);
    BitConverter.GetBytes((ushort)0).CopyTo(mpq, 12);      // formatVersion
    BitConverter.GetBytes((ushort)3).CopyTo(mpq, 14);      // setor = 512 << 3
    BitConverter.GetBytes((uint)posHash).CopyTo(mpq, 16);
    BitConverter.GetBytes((uint)posBloco).CopyTo(mpq, 20);
    BitConverter.GetBytes((uint)entradasHash).CopyTo(mpq, 24);
    BitConverter.GetBytes((uint)1).CopyTo(mpq, 28);

    corpo.CopyTo(mpq, posDados);

    // tabela hash: tudo vazio (0xFFFFFFFF), menos a entrada do arquivo
    var hash = new uint[entradasHash * 4];
    for (var i = 0; i < hash.Length; i++) hash[i] = 0xFFFFFFFF;

    var slot = (int)(MpqArchive.Hash(nomeInterno, 0) & (entradasHash - 1));
    hash[slot * 4 + 0] = MpqArchive.Hash(nomeInterno, 1);
    hash[slot * 4 + 1] = MpqArchive.Hash(nomeInterno, 2);
    hash[slot * 4 + 2] = 0;
    hash[slot * 4 + 3] = 0;

    var bloco = new uint[4];
    bloco[0] = (uint)posDados;
    bloco[1] = (uint)corpo.Length;
    bloco[2] = (uint)conteudo.Length;
    bloco[3] = 0x80000000 | 0x01000000 | (comprimir ? 0x00000200u : 0u);

    Criptografar(hash, MpqArchive.Hash("(hash table)", 3));
    Criptografar(bloco, MpqArchive.Hash("(block table)", 3));

    Buffer.BlockCopy(hash, 0, mpq, posHash, hash.Length * 4);
    Buffer.BlockCopy(bloco, 0, mpq, posBloco, bloco.Length * 4);
    return mpq;
}

// O inverso do Descriptografar do leitor; se um dos dois estiver errado, o
// arquivo montado aqui nao abre.
static void Criptografar(uint[] dados, uint chave)
{
    uint semente = 0xEEEEEEEE;
    for (var i = 0; i < dados.Length; i++)
    {
        semente += TabelaTeste()[0x400 + (chave & 0xFF)];
        var original = dados[i];
        dados[i] = original ^ (chave + semente);
        chave = ((~chave << 0x15) + 0x11111111) | (chave >> 0x0B);
        semente = original + semente + (semente << 5) + 3;
    }
}

static uint[] TabelaTeste()
{
    var t = new uint[0x500];
    uint s = 0x00100001;
    for (uint i = 0; i < 0x100; i++)
        for (uint j = 0; j < 5; j++)
        {
            s = (s * 125 + 3) % 0x2AAAAB; var a = (s & 0xFFFF) << 16;
            s = (s * 125 + 3) % 0x2AAAAB; var b = s & 0xFFFF;
            t[i + j * 0x100] = a | b;
        }
    return t;
}

Check("hash do MPQ trata / e \\ igual",
      MpqArchive.Hash(@"Interface\Icons\x.blp", 0) == MpqArchive.Hash("Interface/Icons/x.blp", 0));
Check("hash do MPQ ignora caixa",
      MpqArchive.Hash(@"INTERFACE\ICONS\X.BLP", 1) == MpqArchive.Hash(@"interface\icons\x.blp", 1));

var tmpMpq = Path.Combine(Path.GetTempPath(), $"teste-{Guid.NewGuid():N}.MPQ");
var conteudoIcone = System.Text.Encoding.ASCII.GetBytes(
    string.Concat(Enumerable.Repeat("ICONE-DE-TESTE-", 40)));

foreach (var comprimido in new[] { false, true })
{
    File.WriteAllBytes(tmpMpq, MontarMpq(@"Interface\Icons\INV_Sword_39.blp", conteudoIcone, comprimido));
    var rotulo = comprimido ? "comprimido" : "cru";

    using var mpq = MpqArchive.Open(tmpMpq);
    var lido = mpq.Read(@"Interface\Icons\INV_Sword_39.blp");

    Check($"MPQ {rotulo}: acha o arquivo", lido is not null);
    Check($"MPQ {rotulo}: conteudo confere", lido is not null && lido.SequenceEqual(conteudoIcone));
    Check($"MPQ {rotulo}: acha com barra normal",
          mpq.Read("Interface/Icons/INV_Sword_39.blp") is not null);
    Check($"MPQ {rotulo}: arquivo ausente devolve null",
          mpq.Read(@"Interface\Icons\NaoExiste.blp") is null);
}

File.Delete(tmpMpq);

var naoMpq = Path.Combine(Path.GetTempPath(), $"teste-{Guid.NewGuid():N}.bin");
File.WriteAllBytes(naoMpq, new byte[600]);
Check("arquivo que nao e MPQ e recusado", Throws(() => MpqArchive.Open(naoMpq).Dispose()));
File.Delete(naoMpq);


// ---- DBC e nomes de icone -------------------------------------------------
static byte[] MontarDbc(int registros, int campos, (uint[] linha, string texto)[] dados)
{
    var strings = new MemoryStream();
    strings.WriteByte(0);   // offset 0 = string vazia, como no formato real

    var linhas = new List<uint[]>();
    foreach (var (linha, texto) in dados)
    {
        var copia = (uint[])linha.Clone();
        if (texto.Length > 0)
        {
            copia[^1] = (uint)strings.Length;
            var bytes = System.Text.Encoding.UTF8.GetBytes(texto);
            strings.Write(bytes);
            strings.WriteByte(0);
        }
        linhas.Add(copia);
    }

    var corpoStrings = strings.ToArray();
    var b = new byte[20 + registros * campos * 4 + corpoStrings.Length];
    b[0] = (byte)'W'; b[1] = (byte)'D'; b[2] = (byte)'B'; b[3] = (byte)'C';
    BitConverter.GetBytes((uint)registros).CopyTo(b, 4);
    BitConverter.GetBytes((uint)campos).CopyTo(b, 8);
    BitConverter.GetBytes((uint)(campos * 4)).CopyTo(b, 12);
    BitConverter.GetBytes((uint)corpoStrings.Length).CopyTo(b, 16);

    var p = 20;
    foreach (var linha in linhas)
        foreach (var v in linha) { BitConverter.GetBytes(v).CopyTo(b, p); p += 4; }

    corpoStrings.CopyTo(b, p);
    return b;
}

// 6 campos, o icone no campo 5 - o layout do ItemDisplayInfo do 3.3.5a
var dbcPath = Path.Combine(Path.GetTempPath(), $"idi-{Guid.NewGuid():N}.dbc");
File.WriteAllBytes(dbcPath, MontarDbc(3, 6, new (uint[], string)[]
{
    (new uint[] { 100, 0, 0, 0, 0, 0 }, "INV_Sword_39"),
    (new uint[] { 200, 0, 0, 0, 0, 0 }, "Spell_Holy_Heal"),
    (new uint[] { 300, 0, 0, 0, 0, 0 }, "INV_Misc_Bag_08"),
}));

var icones = IconExtractor.ReadIconNames(dbcPath);
Check("DBC: le todos os registros", icones.Count == 3, icones.Count.ToString());
Check("DBC: mapeia displayid para icone", icones[100] == "INV_Sword_39", icones.GetValueOrDefault(100u, "?"));
Check("DBC: le o ultimo registro", icones[300] == "INV_Misc_Bag_08");

var tabela = DbcFile.Read(dbcPath);
Check("DBC: acha o campo do icone sozinho",
      IconExtractor.DescobrirCampoDoIcone(tabela) == 5,
      IconExtractor.DescobrirCampoDoIcone(tabela).ToString());
File.Delete(dbcPath);

Check("DBC invalido e recusado", Throws(() =>
{
    var ruim = Path.Combine(Path.GetTempPath(), $"ruim-{Guid.NewGuid():N}.dbc");
    File.WriteAllBytes(ruim, new byte[] { 1, 2, 3, 4, 5 });
    try { DbcFile.Read(ruim); } finally { File.Delete(ruim); }
}));

Check("nome de icone plausivel", IconExtractor.ParecemNomeDeIcone("INV_Sword_39"));
Check("caminho nao e nome de icone", !IconExtractor.ParecemNomeDeIcone(@"Interface\Icons\x"));
Check("com extensao nao e nome de icone", !IconExtractor.ParecemNomeDeIcone("x.blp"));
Check("vazio nao e nome de icone", !IconExtractor.ParecemNomeDeIcone(""));

Check("monta o caminho dentro do MPQ",
      IconExtractor.CaminhoNoMpq("INV_Sword_39") == @"Interface\Icons\INV_Sword_39.blp");

// Os patches tem que ser consultados antes dos arquivos base: eles substituem.
var clienteFalso = Path.Combine(Path.GetTempPath(), $"cli-{Guid.NewGuid():N}");
Directory.CreateDirectory(Path.Combine(clienteFalso, "Data", "ptBR"));
foreach (var n in new[] { "common.MPQ", "common-2.MPQ", "patch.MPQ", "patch-3.MPQ" })
    File.WriteAllBytes(Path.Combine(clienteFalso, "Data", n), new byte[16]);
File.WriteAllBytes(Path.Combine(clienteFalso, "Data", "ptBR", "patch-ptBR-2.MPQ"), new byte[16]);

var ordem = IconExtractor.ListarMpqs(clienteFalso).Select(Path.GetFileName).ToList();
Check("acha os MPQ da raiz e do idioma", ordem.Count == 5, string.Join(", ", ordem));
Check("patches vem antes dos arquivos base",
      ordem.TakeWhile(n => n!.StartsWith("patch", StringComparison.OrdinalIgnoreCase)).Count() == 3,
      string.Join(", ", ordem));
Check("patch-3 consultado antes de patch",
      ordem.IndexOf("patch-3.MPQ") < ordem.IndexOf("patch.MPQ"), string.Join(", ", ordem));
Check("le o numero do patch",
      IconExtractor.NumeroDoPatch("patch-3.MPQ") == 3
      && IconExtractor.NumeroDoPatch("patch-ptBR-2.MPQ") == 2
      && IconExtractor.NumeroDoPatch("patch.MPQ") == 0
      && IconExtractor.NumeroDoPatch("common.MPQ") == 0);
Directory.Delete(clienteFalso, true);

Check("client sem pasta Data devolve lista vazia",
      IconExtractor.ListarMpqs(Path.Combine(Path.GetTempPath(), "nao-existe-" + Guid.NewGuid())).Count == 0);



// ---- ponta a ponta: MPQ -> BLP -> PNG -------------------------------------
// O caminho inteiro, com os mesmos formatos reais: um BLP DXT1 guardado dentro
// de um MPQ comprimido, extraido e convertido. Se qualquer elo estiver errado,
// isto quebra.
var clienteE2E = Path.Combine(Path.GetTempPath(), $"e2e-{Guid.NewGuid():N}");
Directory.CreateDirectory(Path.Combine(clienteE2E, "Data"));

var blocoDxt = new byte[8];
BitConverter.GetBytes((ushort)0xF800).CopyTo(blocoDxt, 0);   // c0 = vermelho puro
BitConverter.GetBytes((ushort)0x0000).CopyTo(blocoDxt, 2);
BitConverter.GetBytes((uint)0).CopyTo(blocoDxt, 4);
var iconeBlp = MontarBlp(2, 0, 0, 4, 4, blocoDxt);

File.WriteAllBytes(
    Path.Combine(clienteE2E, "Data", "patch-3.MPQ"),
    MontarMpq(@"Interface\Icons\INV_Sword_39.blp", iconeBlp, comprimir: true));

var saidaIcones = Path.Combine(clienteE2E, "icons");
var res = IconExtractor.Extract(clienteE2E, saidaIcones, new[] { "INV_Sword_39", "NaoExiste_99" });

Check("extraiu o icone que existe", res.Extraidos == 1, $"{res.Extraidos}");
Check("contou o que nao existe", res.NaoAchados == 1, $"{res.NaoAchados}");
Check("nenhuma falha de decodificacao", res.Falharam == 0, $"{res.Falharam}");

var pngGerado = Path.Combine(saidaIcones, "INV_Sword_39.png");
Check("gravou o PNG", File.Exists(pngGerado));

var bytesPng = File.ReadAllBytes(pngGerado);
Check("PNG valido na saida",
      bytesPng[0] == 0x89 && bytesPng[1] == (byte)'P' && bytesPng[2] == (byte)'N');

// a cor tem que ter sobrevivido a compressao do MPQ e ao DXT
var decodificado = BlpImage.Decode(iconeBlp);
Check("vermelho puro atravessa o caminho todo",
      decodificado.Bgra[2] == 255 && decodificado.Bgra[1] == 0 && decodificado.Bgra[0] == 0,
      $"B={decodificado.Bgra[0]} G={decodificado.Bgra[1]} R={decodificado.Bgra[2]}");

// segunda passada nao refaz o que ja existe
var res2 = IconExtractor.Extract(clienteE2E, saidaIcones, new[] { "INV_Sword_39" });
Check("nao reextrai o que ja esta em disco", res2.JaExistiam == 1 && res2.Extraidos == 0,
      $"ja={res2.JaExistiam} novos={res2.Extraidos}");

// progresso e chamado uma vez por icone
var vistos = new List<string>();
IconExtractor.Extract(clienteE2E, saidaIcones, new[] { "A", "B", "C" },
                      (feitos, totalIcones, nome) => vistos.Add($"{feitos}/{totalIcones} {nome}"));
Check("progresso reportado por icone", vistos.Count == 3, string.Join(" | ", vistos));
Check("progresso conta certo", vistos[2] == "3/3 C", string.Join(" | ", vistos));

Check("client sem MPQ levanta erro claro", Throws(() =>
    IconExtractor.Extract(Path.Combine(Path.GetTempPath(), "vazio-" + Guid.NewGuid()),
                          saidaIcones, new[] { "X" })));

Directory.Delete(clienteE2E, true);


Console.WriteLine($"\n{total - falhas}/{total} testes passaram (final)");
return falhas == 0 ? 0 : 1;
