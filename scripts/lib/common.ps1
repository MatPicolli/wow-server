# =============================================================================
#  Funcoes compartilhadas por todos os scripts. Dot-source no topo de cada um:
#      . "$PSScriptRoot\lib\common.ps1"
# =============================================================================

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Saida em UTF-8.
#
# Sem isso, o PowerShell escreve com a pagina de codigo do console (CP850 no
# Windows em portugues) enquanto ferramentas como o MSBuild ja emitem UTF-8.
# Quem estiver lendo o fluxo recebe duas codificacoes misturadas e acentos
# viram coisas como "funÃ§Ã£o". Vale tanto para a GUI, que le por pipe, quanto
# para o console normal.
try {
    [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
    $OutputEncoding = [Console]::OutputEncoding
} catch {
    # Alguns hosts nao permitem trocar; nao e motivo para abortar nada.
}

# Capturado no momento do dot-source: scripts/lib -> scripts -> raiz do repo
$AcLibDir  = $PSScriptRoot
$AcRepoDir = Split-Path -Parent (Split-Path -Parent $AcLibDir)

# ----------------------------------------------------------------- saida ----

function Write-Step { param([string]$Message)
    Write-Host ''
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Write-Ok   { param([string]$Message) Write-Host "    [ok] $Message"   -ForegroundColor Green }
function Write-Info { param([string]$Message) Write-Host "    $Message"        -ForegroundColor Gray  }
function Write-Warn { param([string]$Message) Write-Host "    [aviso] $Message" -ForegroundColor Yellow }

function Write-Fail {
    param([string]$Message, [string]$Hint)
    Write-Host ''
    Write-Host "    [erro] $Message" -ForegroundColor Red
    if ($Hint) { Write-Host "    -> $Hint" -ForegroundColor Yellow }
    Write-Host ''
    throw $Message
}

# ------------------------------------------------------------ settings -----

function Import-ServerSettings {
    <#
        Le config/settings.psd1, valida o basico e devolve um hashtable.
    #>
    $path = Join-Path $AcRepoDir 'config\settings.psd1'
    if (-not (Test-Path $path)) {
        Write-Fail "config\settings.psd1 nao existe." `
                   "Rode:  Copy-Item config\settings.example.psd1 config\settings.psd1   e edite o arquivo."
    }

    $s = Import-PowerShellDataFile -Path $path

    foreach ($key in @('Root','SourceDir','BuildDir','ServerDir','ClientDir','MySql')) {
        if (-not $s.ContainsKey($key)) { Write-Fail "settings.psd1 esta sem a chave obrigatoria '$key'." }
    }

    # Sob Set-StrictMode -Version Latest, ler uma chave que nao existe num
    # hashtable lanca excecao - nao devolve $null. Entao as chaves opcionais
    # ganham default aqui, e o resto do codigo pode usar $settings.X a vontade.
    $defaults = @{
        SourceRepository = 'https://github.com/azerothcore/azerothcore-wotlk.git'
        SourceBranch     = 'master'
        BuildConfig  = 'RelWithDebInfo'
        Threads      = 0
        BoostDir     = 'C:\local\boost'
        RealmName    = 'Meu Servidor'
        RealmAddress = '127.0.0.1'
        ExtractVmaps = $true
        ExtractMmaps = $true
    }
    foreach ($k in $defaults.Keys) {
        if (-not $s.ContainsKey($k)) { $s[$k] = $defaults[$k] }
    }

    foreach ($k in @('Host','Port','RootUser','User','Password','AuthDb','WorldDb','CharDb')) {
        if (-not $s.MySql.ContainsKey($k)) { Write-Fail "settings.psd1: a secao MySql esta sem a chave '$k'." }
    }

    if ($s.ClientDir -eq 'C:\Games\World of Warcraft 3.3.5a' -and -not (Test-Path $s.ClientDir)) {
        Write-Fail "ClientDir ainda esta com o valor de exemplo e a pasta nao existe." `
                   "Edite config\settings.psd1 e aponte ClientDir para a pasta que contem o Wow.exe."
    }

    return $s
}

function Assert-ClientDir {
    param([hashtable]$Settings)
    $exe = Join-Path $Settings.ClientDir 'Wow.exe'
    if (-not (Test-Path $exe)) {
        Write-Fail "Nao achei Wow.exe em '$($Settings.ClientDir)'." `
                   "Corrija ClientDir em config\settings.psd1."
    }
    $build = Get-ClientBuild -ClientDir $Settings.ClientDir
    if ($build -and $build -ne '12340') {
        Write-Warn "O client parece ser build $build, e nao 12340 (3.3.5a). O AzerothCore so aceita 12340."
    }
}

function Get-ClientBuild {
    <#
        Le a build do client a partir do nome dos arquivos em Data\.
        Devolve string ou $null se nao der pra determinar.
    #>
    param([string]$ClientDir)
    $exe = Join-Path $ClientDir 'Wow.exe'
    if (-not (Test-Path $exe)) { return $null }
    try {
        $v = (Get-Item $exe).VersionInfo.FileVersion   # ex: "3, 3, 5, 12340"
        if ($v -match '(\d{4,5})\s*$') { return $Matches[1] }
    } catch { }
    return $null
}

# ------------------------------------------------------- ferramentas -------

function Test-Command {
    param([string]$Name)
    return [bool](Get-Command $Name -ErrorAction SilentlyContinue)
}

function Assert-Admin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $pr = New-Object Security.Principal.WindowsPrincipal($id)
    if (-not $pr.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        Write-Fail "Este script precisa rodar como Administrador." `
                   "Abra o PowerShell com 'Executar como administrador' e rode de novo."
    }
}

function Invoke-Checked {
    <#
        Roda um executavel e aborta se o exit code nao for 0.
    #>
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [string[]]$Arguments = @(),
        [string]$WorkingDirectory,
        [string]$What = 'comando'
    )
    $prev = $null
    $code = -1
    if ($WorkingDirectory) { $prev = Get-Location; Set-Location $WorkingDirectory }
    try {
        & $FilePath @Arguments
        $code = $LASTEXITCODE
    } finally {
        if ($prev) { Set-Location $prev }
    }
    if ($code -ne 0) { Write-Fail "$What falhou (exit code $code)." }
}

function Get-ThreadCount {
    param([hashtable]$Settings)
    if ($Settings.Threads -gt 0) { return [int]$Settings.Threads }

    $n = [Environment]::ProcessorCount
    if ($n -lt 1) { $n = 1 }
    return [int]$n
}

# --------------------------------------------- localizar dependencias ------

function Find-MySqlDir {
    <#
        Devolve a pasta raiz da instalacao do MySQL (a que contem bin\ e lib\).
    #>
    $candidates = @()
    $candidates += Get-ChildItem 'C:\Program Files\MySQL' -Directory -Filter 'MySQL Server *' -ErrorAction SilentlyContinue
    $candidates += Get-ChildItem 'C:\Program Files\MariaDB *' -Directory -ErrorAction SilentlyContinue

    $best = $candidates | Sort-Object Name -Descending | Select-Object -First 1
    if ($best) { return $best.FullName }

    # ultimo recurso: derivar do mysql.exe no PATH
    $cmd = Get-Command mysql.exe -ErrorAction SilentlyContinue
    if ($cmd) { return (Split-Path -Parent (Split-Path -Parent $cmd.Source)) }

    return $null
}

function Find-OpenSslDir {
    $paths = @(
        'C:\Program Files\OpenSSL-Win64'
        'C:\Program Files\OpenSSL'
        'C:\OpenSSL-Win64'
    )
    foreach ($p in $paths) { if (Test-Path (Join-Path $p 'bin')) { return $p } }
    return $null
}

function Find-BoostDir {
    <#
        Prefere BOOST_ROOT; senao procura C:\local\boost*.
    #>
    if ($env:BOOST_ROOT -and (Test-Path $env:BOOST_ROOT)) { return $env:BOOST_ROOT }
    $d = Get-ChildItem 'C:\local' -Directory -Filter 'boost*' -ErrorAction SilentlyContinue |
         Sort-Object Name -Descending | Select-Object -First 1
    if ($d) { return $d.FullName }
    return $null
}

function Get-CMakeVersion {
    <#
        Devolve a versao do cmake no PATH como [version], ou $null.
    #>
    if (-not (Test-Command 'cmake')) { return $null }
    $line = (cmake --version | Select-Object -First 1)
    if ($line -match '(\d+)\.(\d+)(?:\.(\d+))?') {
        $patch = if ($Matches[3]) { $Matches[3] } else { '0' }
        return [version]("{0}.{1}.{2}" -f $Matches[1], $Matches[2], $patch)
    }
    return $null
}

function Find-VsGenerator {
    <#
        Devolve o nome do generator do CMake pra versao do Visual Studio instalada.
    #>
    $vswhere = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path $vswhere)) { return $null }

    $ver = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationVersion
    if (-not $ver) { return $null }

    $major = [int]($ver -split '\.')[0]
    switch ($major) {
        18 { return 'Visual Studio 18 2026' }
        17 { return 'Visual Studio 17 2022' }
        16 { return 'Visual Studio 16 2019' }
        default { return $null }
    }
}

# ------------------------------------------------------------- MySQL -------

function Get-MySqlExe {
    $cmd = Get-Command mysql.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    $dir = Find-MySqlDir
    if ($dir) {
        $exe = Join-Path $dir 'bin\mysql.exe'
        if (Test-Path $exe) { return $exe }
    }
    return $null
}

function Get-MySqlDumpExe {
    foreach ($name in @('mysqldump.exe', 'mariadb-dump.exe')) {
        $cmd = Get-Command $name -ErrorAction SilentlyContinue
        if ($cmd) { return $cmd.Source }
    }
    $dir = Find-MySqlDir
    if ($dir) {
        foreach ($name in @('mysqldump.exe', 'mariadb-dump.exe')) {
            $exe = Join-Path $dir "bin\$name"
            if (Test-Path $exe) { return $exe }
        }
    }
    return $null
}

function Invoke-MySql {
    <#
        Executa SQL e devolve a saida como texto.
        -Sql roda uma query; -File roda um arquivo .sql.
    #>
    param(
        [hashtable]$Settings,
        [string]$Sql,
        [string]$File,
        [string]$User,
        [string]$Password,
        [string]$Database
    )

    $exe = Get-MySqlExe
    if (-not $exe) {
        Write-Fail "mysql.exe nao encontrado." `
                   "Instale o MySQL (scripts\01-install-prereqs.ps1) e adicione a pasta bin\ ao PATH."
    }

    $m = $Settings.MySql
    $mysqlArgs = @(
        "--host=$($m.Host)"
        "--port=$($m.Port)"
        "--user=$User"
        "--protocol=TCP"
    )
    if ($Database) { $mysqlArgs += $Database }

    # A senha vai por MYSQL_PWD em vez de --password. Com --password o cliente
    # imprime "[Warning] Using a password on the command line interface can be
    # insecure." no stderr toda vez, o que sujava a saida e se misturava com
    # erros de verdade. De quebra, a senha deixa de aparecer na linha de
    # comando do processo.
    $prevPwd = $env:MYSQL_PWD
    if ($Password) { $env:MYSQL_PWD = $Password }

    # Com $ErrorActionPreference = 'Stop', o '2>&1' de um comando nativo faz o
    # PowerShell transformar cada linha de stderr em excecao na hora - antes de
    # chegarmos no teste do $LASTEXITCODE. O resultado e que a mensagem real do
    # mysql ("Access denied", "Can't connect") se perdia e o usuario so via o
    # texto generico do catch. Aqui soltamos a preferencia so nesta chamada.
    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $code = -1
    try {
        if ($File) {
            $out = Get-Content -Raw -LiteralPath $File | & $exe @mysqlArgs 2>&1
        } else {
            $mysqlArgs += @('-e', $Sql)
            $out = & $exe @mysqlArgs 2>&1
        }
        $code = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $prevEap
        $env:MYSQL_PWD = $prevPwd
    }

    # Cada linha de stderr vira um ErrorRecord. Passar isso por Out-String
    # renderiza com toda a decoracao do PowerShell (posicao no arquivo, o
    # trecho de codigo, CategoryInfo...), enterrando a mensagem do MySQL no
    # meio de ruido. Convertendo cada um pro texto puro, sobra so o que
    # interessa.
    $text = (@($out) | ForEach-Object {
        if ($_ -is [System.Management.Automation.ErrorRecord]) { $_.ToString() } else { $_ }
    }) -join [Environment]::NewLine
    $text = $text.Trim()

    if ($code -ne 0) {
        if ($text) {
            Write-Host ''
            Write-Host '    resposta do mysql.exe:' -ForegroundColor Yellow
            foreach ($line in ($text -split "`r?`n")) {
                Write-Host "      $line" -ForegroundColor Gray
            }
            Write-Host ''
        }
        throw "mysql.exe falhou (exit code $code)."
    }
    return $text
}

function Read-MySqlRootPassword {
    $sec = Read-Host "Senha do usuario root do MySQL" -AsSecureString
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($sec)
    try   { return [Runtime.InteropServices.Marshal]::PtrToStringAuto($bstr) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
}

# ------------------------------------------------------------ diversos -----

function Get-ExtractedMapCount {
    <#
        Quantos mapas distintos existem na pasta 'maps' extraida.

        Os arquivos seguem o padrao <mapId 3 digitos><tileX 2><tileY 2>.map,
        entao os 3 primeiros caracteres identificam o mapa. Esse numero e a
        base pra saber quantos .vmtree e .mmap devem existir no fim.
        Devolve 0 se nao der pra determinar.
    #>
    param([string]$MapsDir)

    if (-not $MapsDir -or -not (Test-Path $MapsDir)) { return 0 }

    $ids = New-Object 'System.Collections.Generic.HashSet[string]'
    try {
        foreach ($f in [IO.Directory]::EnumerateFiles($MapsDir, '*.map')) {
            $name = [IO.Path]::GetFileNameWithoutExtension($f)
            if ($name.Length -ge 3) { [void]$ids.Add($name.Substring(0, 3)) }
        }
    } catch { return 0 }

    return $ids.Count
}

function Get-FileCount {
    <#
        Conta arquivos rapido (sem Get-ChildItem, que fica caro em pastas com
        dezenas de milhares de arquivos e seria consultado a cada poucos
        segundos durante a extracao).
    #>
    param([string]$Path, [string]$Filter = '*')

    if (-not $Path -or -not (Test-Path $Path)) { return 0 }
    try   { return ([IO.Directory]::GetFiles($Path, $Filter)).Length }
    catch { return 0 }
}

function Get-EmptySubmodulePath {
    <#
        Devolve os caminhos de submodulo declarados no .gitmodules que existem
        so como pasta vazia.

        Um clone sem --recurse-submodules deixa exatamente esse estado: o modulo
        parece instalado, mas o codigo da dependencia nao esta la. Foi assim que
        o Eluna passou pela listagem e so falhou no meio da compilacao, com
        "lua.h: No such file or directory".
    #>
    param([string]$ModulePath)

    # ',' em todo return: sem ela, um array vazio vira $null na saida da funcao.
    $arquivo = Join-Path $ModulePath '.gitmodules'
    if (-not (Test-Path $arquivo)) { return ,@() }

    $vazios = @()
    foreach ($linha in (Get-Content $arquivo -ErrorAction SilentlyContinue)) {
        # 'path = src/LuaEngine/lua'; o valor pode vir com espacos em volta
        if ($linha -notmatch '^\s*path\s*=\s*(.+?)\s*$') { continue }

        $relativo = $Matches[1]
        $destino  = Join-Path $ModulePath ($relativo -replace '/', [IO.Path]::DirectorySeparatorChar)

        if (-not (Test-Path $destino)) { $vazios += $relativo; continue }

        try {
            if (-not [IO.Directory]::EnumerateFileSystemEntries($destino).GetEnumerator().MoveNext()) {
                $vazios += $relativo
            }
        } catch { }
    }

    return ,@($vazios)
}

function Invoke-ProcessWithProgress {
    <#
        Roda um executavel mostrando uma barra de progresso enquanto ele
        trabalha, e devolve o exit code.

        O processo continua ligado ao console (sem redirecionar saida), entao
        ele imprime normalmente e nada muda no comportamento dele - inclusive
        se pedir tecla no fim. A barra do Write-Progress e desenhada numa area
        separada, no topo, sem se misturar com essa saida.

        O progresso vem de contar arquivos que vao aparecendo em -WatchDir.
        Com -ExpectedTotal > 0 sai porcentagem e estimativa de tempo restante;
        sem ele, so contador e tempo decorrido.
    #>
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [string[]]$Arguments = @(),
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [Parameter(Mandatory)][string]$Activity,
        [string]$WatchDir,
        [string]$WatchFilter = '*',
        [int]$ExpectedTotal = 0,
        [int]$PollSeconds = 5
    )

    $startParams = @{
        FilePath         = $FilePath
        WorkingDirectory = $WorkingDirectory
        NoNewWindow      = $true
        PassThru         = $true
    }
    if ($Arguments.Count -gt 0) { $startParams['ArgumentList'] = $Arguments }

    $proc    = Start-Process @startParams
    $started = Get-Date

    # Ler .Handle uma vez faz o objeto Process guardar o handle nativo. Sem
    # isso o .ExitCode volta $null depois que o processo termina, e um
    # comando que funcionou perfeitamente acaba reportado como falha.
    try { $null = $proc.Handle } catch { }

    try {
        while (-not $proc.HasExited) {
            Start-Sleep -Seconds $PollSeconds

            $elapsed = (Get-Date) - $started
            $count   = Get-FileCount -Path $WatchDir -Filter $WatchFilter
            $status  = "{0:hh\:mm\:ss} decorrido" -f $elapsed

            if ($ExpectedTotal -gt 0) {
                $status += " | $count de $ExpectedTotal"
                $pct = [math]::Min(100, [math]::Max(0, [int](($count / $ExpectedTotal) * 100)))

                # ETA por regra de tres simples: so faz sentido depois que
                # alguns itens ficaram prontos
                if ($count -ge 2 -and $count -lt $ExpectedTotal) {
                    $perItem   = $elapsed.TotalSeconds / $count
                    $remaining = [TimeSpan]::FromSeconds($perItem * ($ExpectedTotal - $count))
                    $status   += (" | faltam ~{0:hh\:mm}" -f $remaining)
                }

                Write-Progress -Activity $Activity -Status $status -PercentComplete $pct
            } else {
                if ($count -gt 0) { $status += " | $count arquivos gerados" }
                Write-Progress -Activity $Activity -Status $status
            }
        }
    } finally {
        Write-Progress -Activity $Activity -Completed
    }

    $proc.WaitForExit()

    $code = $proc.ExitCode
    if ($null -eq $code) {
        Write-Warn "Nao consegui ler o exit code de $([IO.Path]::GetFileName($FilePath)) - assumindo sucesso."
        Write-Warn "A conferencia no fim da etapa valida o resultado de verdade."
        $code = 0
    }
    return $code
}

function Write-TextFileNoBom {
    <#
        Set-Content -Encoding UTF8 grava BOM no Windows PowerShell 5.1, e o
        parser de config do AzerothCore engasga com BOM. Aqui gravamos UTF-8
        puro, sem BOM.
    #>
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Content
    )
    $utf8NoBom = New-Object Text.UTF8Encoding($false)
    [IO.File]::WriteAllText($Path, $Content, $utf8NoBom)
}

function Test-MySqlReachable {
    <#
        Diz se ha algo escutando na porta do MySQL.

        E so um TCP connect: prova que o servidor esta no ar, nao que a senha
        esteja certa. Serve exatamente para separar "o MySQL nao esta rodando"
        de "a credencial esta errada" - dois problemas com solucoes diferentes,
        que o erro cru do mysqldump nao distingue.

        Nao usa o cliente mysql de proposito: precisa responder rapido e
        funcionar mesmo antes de o cliente estar no PATH.
    #>
    param(
        [Parameter(Mandatory)][hashtable]$Settings,
        [int]$TimeoutMs = 1500
    )

    $m = $Settings.MySql
    $cliente = $null
    try {
        $cliente = [Net.Sockets.TcpClient]::new()
        $async = $cliente.BeginConnect($m.Host, $m.Port, $null, $null)

        # Sem timeout explicito, uma porta filtrada trava o script por ~20s.
        if (-not $async.AsyncWaitHandle.WaitOne($TimeoutMs)) { return $false }

        $cliente.EndConnect($async)
        return $true
    } catch {
        return $false
    } finally {
        if ($cliente) { try { $cliente.Close() } catch { } }
    }
}

function Get-Psd1DuplicateKey {
    <#
        Chaves declaradas mais de uma vez no mesmo nivel do arquivo.

        Import-PowerShellDataFile recusa um psd1 com chave repetida, e todo o
        resto dos scripts para de funcionar junto. Ja aconteceu: uma substituicao
        que nao casava acabou anexando cada chave em vez de trocar.
    #>
    param([Parameter(Mandatory)][string]$Text)

    $contagem = @{}
    foreach ($linha in ($Text -split "`r?`n")) {
        if ($linha -match "^\s*(?<k>[A-Za-z_][A-Za-z0-9_]*)\s*=") {
            $k = $Matches['k']
            if ($contagem.ContainsKey($k)) { $contagem[$k]++ } else { $contagem[$k] = 1 }
        }
    }

    # A virgula nao e enfeite: devolver @() de uma funcao entrega $null, porque
    # o pipeline desenrola o array vazio. Com ',' o array sai inteiro, e o
    # chamador pode ler .Count sem se preocupar.
    return ,@($contagem.Keys | Where-Object { $contagem[$_] -gt 1 } | Sort-Object)
}

function Set-ServerSetting {
    <#
        Troca o valor de uma chave em config/settings.psd1, preservando os
        comentarios - o arquivo e feito para ser lido e editado a mao, entao
        regerar tudo destruiria a parte mais util dele.

        Cuidados que este codigo carrega, todos por bug ja visto:

        * O ancora '$' em modo multiline casa ANTES do \n e deixa o \r do CRLF
          de fora. Um padrao terminado em '$' simplesmente nao casa em arquivo
          escrito no Windows - e ai a chave era anexada no fim em vez de
          substituida, produzindo duplicatas que o Import-PowerShellDataFile
          rejeita. Por isso o final e (?=\r?\n|$).
        * A gravacao passa por Write-TextFileNoBom: BOM quebra o parser.
        * Antes de gravar, o resultado e validado. Melhor falhar aqui do que
          deixar o arquivo ilegivel para todos os outros scripts.
    #>
    param(
        [Parameter(Mandatory)][string]$Key,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Value,
        [string]$Path
    )

    if (-not $Path) { $Path = Join-Path $AcRepoDir 'config\settings.psd1' }
    if (-not (Test-Path $Path)) { Write-Fail "nao achei $Path" }

    $texto = [IO.File]::ReadAllText($Path)

    # Aspas simples no PowerShell: o unico escape e dobrar a aspa.
    $literal = "'" + ($Value -replace "'", "''") + "'"

    $padrao = '(?m)^(?<lead>[ \t]*' + [regex]::Escape($Key) + '[ \t]*=[ \t]*)' +
              '(?<val>''(?:[^'']|'''')*''|"(?:[^"]|"")*"|[^\s#]*)' +
              '(?<tail>[ \t]*(?:#[^\r\n]*)?)(?=\r?\n|$)'

    $re = [regex]::new($padrao)
    $achados = $re.Matches($texto)

    if ($achados.Count -gt 1) {
        Write-Fail "a chave '$Key' aparece $($achados.Count) vezes em $Path." `
                   'Rode .\scripts\repair-settings.ps1 antes.'
    }

    if ($achados.Count -eq 1) {
        $novo = $re.Replace($texto, { param($m)
            $m.Groups['lead'].Value + $literal + $m.Groups['tail'].Value
        }, 1)
    } else {
        # Chave ausente e normal: varias sao opcionais e ganham default no
        # Import-ServerSettings. Entra antes da chave de fechamento.
        $fecha = $texto.LastIndexOf('}')
        if ($fecha -lt 0) { Write-Fail "$Path nao parece um psd1 valido (sem '}')." }

        $quebra = if ($texto -match "`r`n") { "`r`n" } else { "`n" }
        $novo = $texto.Substring(0, $fecha) + "    $Key = $literal$quebra" + $texto.Substring($fecha)
    }

    $duplicadas = Get-Psd1DuplicateKey -Text $novo
    if ($duplicadas.Count -gt 0) {
        Write-Fail "a alteracao criaria chave repetida: $($duplicadas -join ', ')." `
                   'Nada foi gravado.'
    }

    # Validar de verdade: parsear antes de sobrescrever o arquivo bom.
    $temp = [IO.Path]::GetTempFileName()
    try {
        Write-TextFileNoBom -Path $temp -Content $novo
        $null = Import-PowerShellDataFile -Path $temp
    } catch {
        Remove-Item $temp -Force -ErrorAction SilentlyContinue
        Write-Fail "a alteracao deixaria $Path ilegivel: $_" 'Nada foi gravado.'
    }
    Remove-Item $temp -Force -ErrorAction SilentlyContinue

    Write-TextFileNoBom -Path $Path -Content $novo
}

function New-DirectoryIfMissing {
    param([string]$Path)
    if (-not (Test-Path $Path)) { New-Item -ItemType Directory -Path $Path -Force | Out-Null }
}

function Get-FreeSpaceGB {
    param([string]$Path)
    $root = [IO.Path]::GetPathRoot($Path)
    if (-not $root) { return $null }
    $name = $root.Trim('\').TrimEnd(':')
    $drive = Get-PSDrive -Name $name -ErrorAction SilentlyContinue
    if ($drive -and $null -ne $drive.Free) { return [math]::Round($drive.Free / 1GB, 1) }
    return $null
}
