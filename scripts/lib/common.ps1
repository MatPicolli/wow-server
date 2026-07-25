# =============================================================================
#  Funcoes compartilhadas por todos os scripts. Dot-source no topo de cada um:
#      . "$PSScriptRoot\lib\common.ps1"
# =============================================================================

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

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
    if ($Password) { $mysqlArgs += "--password=$Password" }
    if ($Database) { $mysqlArgs += $Database }

    if ($File) {
        $out = Get-Content -Raw -LiteralPath $File | & $exe @mysqlArgs 2>&1
    } else {
        $mysqlArgs += @('-e', $Sql)
        $out = & $exe @mysqlArgs 2>&1
    }

    if ($LASTEXITCODE -ne 0) {
        Write-Host ($out | Out-String) -ForegroundColor DarkGray
        Write-Fail "Comando MySQL falhou (exit code $LASTEXITCODE)."
    }
    return ($out | Out-String)
}

function Read-MySqlRootPassword {
    $sec = Read-Host "Senha do usuario root do MySQL" -AsSecureString
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($sec)
    try   { return [Runtime.InteropServices.Marshal]::PtrToStringAuto($bstr) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
}

# ------------------------------------------------------------ diversos -----

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
