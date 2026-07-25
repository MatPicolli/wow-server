<#
.SYNOPSIS
    Copia os binarios compilados e as DLLs necessarias para ServerDir.

.DESCRIPTION
    Junta num lugar so tudo que o servidor precisa pra rodar:
      - authserver.exe / worldserver.exe (+ .pdb)
      - os .conf.dist
      - libmysql.dll (do MySQL)
      - libcrypto-3-x64.dll, libssl-3-x64.dll, legacy.dll (do OpenSSL)

    Rode de novo toda vez que recompilar.
#>
[CmdletBinding()]
param()

. "$PSScriptRoot\lib\common.ps1"

$settings = Import-ServerSettings
$binDir   = Join-Path $settings.BuildDir "bin\$($settings.BuildConfig)"
$server   = $settings.ServerDir

if (-not (Test-Path (Join-Path $binDir 'worldserver.exe'))) {
    Write-Fail "Nao achei worldserver.exe em '$binDir'." "Rode 03-build.ps1 primeiro."
}

New-DirectoryIfMissing $server
New-DirectoryIfMissing (Join-Path $server 'configs')
New-DirectoryIfMissing (Join-Path $server 'logs')

# --- binarios --------------------------------------------------------------
Write-Step "Copiando os binarios"
foreach ($f in @('authserver.exe', 'worldserver.exe', 'authserver.pdb', 'worldserver.pdb')) {
    $from = Join-Path $binDir $f
    if (Test-Path $from) {
        Copy-Item $from $server -Force
        Write-Ok $f
    } elseif ($f -notlike '*.pdb') {
        Write-Fail "$f nao existe em $binDir." "A compilacao nao terminou. Rode 03-build.ps1."
    }
}

# --- configs (.dist) -------------------------------------------------------
Write-Step "Copiando os modelos de configuracao (.conf.dist)"
$distSources = @(
    (Join-Path $binDir 'configs')
    $binDir
)
$foundDist = $false
foreach ($dir in $distSources) {
    if (-not (Test-Path $dir)) { continue }
    $dists = Get-ChildItem $dir -Filter '*.conf.dist' -File -ErrorAction SilentlyContinue
    foreach ($d in $dists) {
        Copy-Item $d.FullName (Join-Path $server 'configs') -Force
        Write-Ok $d.Name
        $foundDist = $true
    }
    if ($foundDist) { break }
}
if (-not $foundDist) {
    Write-Warn "Nao achei nenhum .conf.dist. Procure por 'worldserver.conf.dist' dentro de '$($settings.BuildDir)' e copie na mao pra '$server\configs'."
}

# Modulos instalados em source\modules geram os proprios .conf.dist numa
# subpasta 'modules'. Sem copiar isso, o modulo sobe sem configuracao.
$moduleDist = Join-Path $binDir 'configs\modules'
if (Test-Path $moduleDist) {
    $moduleTarget = Join-Path $server 'configs\modules'
    New-DirectoryIfMissing $moduleTarget

    $modDists = Get-ChildItem $moduleDist -Filter '*.conf.dist' -File -ErrorAction SilentlyContinue
    foreach ($d in $modDists) {
        Copy-Item $d.FullName $moduleTarget -Force
        Write-Ok "modules\$($d.Name)"
    }
    if ($modDists) { Write-Info "$($modDists.Count) config(s) de modulo - o 07-configure.ps1 gera os .conf" }
}

# --- DLLs do MySQL ---------------------------------------------------------
Write-Step "Copiando as DLLs do MySQL"
$mysqlDir = Find-MySqlDir
if (-not $mysqlDir) { Write-Fail "MySQL nao encontrado." "Rode 01-install-prereqs.ps1." }

$libmysql = Get-ChildItem (Join-Path $mysqlDir 'lib') -Filter 'libmysql.dll' -Recurse -File -ErrorAction SilentlyContinue |
            Select-Object -First 1
if (-not $libmysql) {
    $libmysql = Get-ChildItem (Join-Path $mysqlDir 'bin') -Filter 'libmysql.dll' -File -ErrorAction SilentlyContinue |
                Select-Object -First 1
}
if ($libmysql) {
    Copy-Item $libmysql.FullName $server -Force
    Write-Ok 'libmysql.dll'
} else {
    Write-Fail "libmysql.dll nao encontrada em '$mysqlDir'." `
               "Reabra o MySQL Installer e adicione os Development Components."
}

# --- DLLs do OpenSSL -------------------------------------------------------
Write-Step "Copiando as DLLs do OpenSSL"
$sslDir = Find-OpenSslDir
if (-not $sslDir) { Write-Fail "OpenSSL nao encontrado." "Instale a Win64 OpenSSL v3.x.x." }

$sslBin = Join-Path $sslDir 'bin'
foreach ($dll in @('libcrypto-3-x64.dll', 'libssl-3-x64.dll')) {
    $from = Join-Path $sslBin $dll
    if (Test-Path $from) {
        Copy-Item $from $server -Force
        Write-Ok $dll
    } else {
        Write-Fail "$dll nao existe em '$sslBin'." `
                   "Sua instalacao do OpenSSL provavelmente e 4.x. O AzerothCore precisa da 3.x."
    }
}

# legacy.dll fica no diretorio de engines e nem sempre existe
$legacy = Get-ChildItem $sslDir -Filter 'legacy.dll' -Recurse -File -ErrorAction SilentlyContinue | Select-Object -First 1
if ($legacy) {
    Copy-Item $legacy.FullName $server -Force
    Write-Ok 'legacy.dll'
} else {
    Write-Info "legacy.dll nao encontrada - normal em varias builds do OpenSSL 3.x, pode ignorar"
}

# --- Data ------------------------------------------------------------------
$dataDir = Join-Path $server 'Data'
if (Test-Path (Join-Path $dataDir 'dbc')) {
    Write-Ok "Data\ ja tem os dados extraidos do client"
} else {
    Write-Warn "Nao vi Data\dbc em '$server'. Rode 05-extract-client-data.ps1."
}

Write-Host @"

    Servidor montado em: $server

    Proximo passo:
        .\scripts\07-configure.ps1
"@ -ForegroundColor Gray
