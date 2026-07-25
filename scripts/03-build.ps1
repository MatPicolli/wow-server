<#
.SYNOPSIS
    Configura com CMake e compila o AzerothCore (authserver, worldserver e extractors).

.PARAMETER Clean
    Apaga o cache do CMake antes de configurar. Use quando trocar de versao do
    MySQL/OpenSSL/Boost, ou quando o CMake insistir em achar caminho antigo.

.NOTES
    A compilacao leva de 15 a 60 minutos dependendo da maquina.
#>
[CmdletBinding()]
param(
    [switch]$Clean,
    [switch]$SkipTools
)

. "$PSScriptRoot\lib\common.ps1"

$settings = Import-ServerSettings
$src      = $settings.SourceDir
$build    = $settings.BuildDir
$config   = $settings.BuildConfig

if (-not (Test-Path (Join-Path $src 'CMakeLists.txt'))) {
    Write-Fail "Nao achei o fonte em '$src'." "Rode 02-clone-source.ps1 primeiro."
}
if (-not (Test-Command 'cmake')) {
    Write-Fail "cmake nao encontrado no PATH." "Rode 01-install-prereqs.ps1 e reabra o PowerShell."
}

$generator = Find-VsGenerator
if (-not $generator) {
    Write-Fail "Nao achei um Visual Studio com toolchain C++." `
               "Instale o VS 2022 Community com o workload 'Desenvolvimento para desktop com C++'."
}
Write-Info "generator: $generator"

# --- Boost -----------------------------------------------------------------
$boost = Find-BoostDir
if (-not $boost) {
    Write-Fail "Boost nao encontrado." "Rode 01-install-prereqs.ps1 ou defina BOOST_ROOT."
}
$env:BOOST_ROOT = ($boost -replace '\\', '/').TrimEnd('/')
Write-Info "BOOST_ROOT: $env:BOOST_ROOT"

# --- OpenSSL ---------------------------------------------------------------
$openssl = Find-OpenSslDir
if (-not $openssl) {
    Write-Fail "OpenSSL nao encontrado." "Instale a Win64 OpenSSL v3.x.x (nao Light) de slproweb.com."
}
if (-not (Test-Path (Join-Path $openssl 'bin\libcrypto-3-x64.dll'))) {
    Write-Fail "O OpenSSL em '$openssl' nao tem libcrypto-3-x64.dll (provavelmente e 4.x)." `
               "O AzerothCore precisa da linha 3.x. Desinstale e instale a Win64 OpenSSL v3.x.x."
}
Write-Info "OpenSSL: $openssl"

# --- MySQL -----------------------------------------------------------------
$mysql = Find-MySqlDir
if (-not $mysql) { Write-Fail "MySQL nao encontrado." "Rode 01-install-prereqs.ps1." }
if (-not (Test-Path (Join-Path $mysql 'lib\libmysql.lib'))) {
    Write-Fail "Faltam as bibliotecas de desenvolvimento do MySQL em '$mysql\lib'." `
               "Reabra o MySQL Installer e inclua os Development Components."
}
Write-Info "MySQL: $mysql"

# --- configure -------------------------------------------------------------
if ($Clean -and (Test-Path $build)) {
    Write-Step "Limpando o cache do CMake"
    Remove-Item (Join-Path $build 'CMakeCache.txt')   -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $build 'CMakeFiles')       -Recurse -Force -ErrorAction SilentlyContinue
    Write-Ok "cache removido"
}

New-DirectoryIfMissing $build

Write-Step "Configurando com o CMake"
$cmakeArgs = @(
    '-S', $src
    '-B', $build
    '-G', $generator
    '-A', 'x64'
    "-DCMAKE_INSTALL_PREFIX=$($settings.ServerDir)"
    "-DTOOLS_BUILD=$(if ($SkipTools) { 'none' } else { 'all' })"
    '-DSCRIPTS=static'
    '-DMODULES=static'
    '-DWITH_WARNINGS=0'
    "-DMYSQL_INCLUDE_DIR=$(Join-Path $mysql 'include')"
    "-DMYSQL_LIBRARY=$(Join-Path $mysql 'lib\libmysql.lib')"
    "-DOPENSSL_ROOT_DIR=$openssl"
    "-DBOOST_ROOT=$env:BOOST_ROOT"
)
& cmake @cmakeArgs
$configureCode = $LASTEXITCODE

# O CMake 4.x removeu a compatibilidade com projetos que pedem
# cmake_minimum_required abaixo de 3.5. O CMakeLists principal do AzerothCore
# declara 3.16, entao passa - mas alguma dependencia embutida pode nao passar.
# CMAKE_POLICY_VERSION_MINIMUM=3.5 restaura o comportamento antigo.
$cmakeVersion = Get-CMakeVersion
if ($configureCode -ne 0 -and $cmakeVersion -and $cmakeVersion.Major -ge 4) {
    Write-Warn "A configuracao falhou com CMake $cmakeVersion."
    Write-Info "tentando de novo com -DCMAKE_POLICY_VERSION_MINIMUM=3.5 (compatibilidade com CMake 4.x)..."

    & cmake @($cmakeArgs + '-DCMAKE_POLICY_VERSION_MINIMUM=3.5')
    $configureCode = $LASTEXITCODE

    if ($configureCode -eq 0) {
        Write-Ok "configurado com o flag de compatibilidade"
    }
}

if ($configureCode -ne 0) {
    Write-Fail "A configuracao do CMake falhou (exit code $configureCode)." `
               "Leia a PRIMEIRA mensagem de erro acima, nao a ultima. Se falar em Boost, confira que BOOST_ROOT usa barras normais."
}
Write-Ok "configurado"

# --- build -----------------------------------------------------------------
$threads = Get-ThreadCount -Settings $settings
Write-Step "Compilando ($config, $threads threads)"
Write-Info "de 15 a 60 minutos - pode ir fazer outra coisa"

$started = Get-Date
Invoke-Checked -FilePath 'cmake' `
               -Arguments @('--build', $build, '--config', $config, '--parallel', "$threads") `
               -What 'compilacao'

$elapsed = (Get-Date) - $started
Write-Ok ("compilado em {0:hh\:mm\:ss}" -f $elapsed)

# --- conferir a saida ------------------------------------------------------
$binDir = Join-Path $build "bin\$config"
$expected = @('authserver.exe', 'worldserver.exe')
if (-not $SkipTools) { $expected += @('mapextractor.exe', 'vmap4extractor.exe', 'vmap4assembler.exe', 'mmaps_generator.exe') }

$missing = $expected | Where-Object { -not (Test-Path (Join-Path $binDir $_)) }
if ($missing) {
    Write-Fail "A compilacao terminou mas faltam binarios: $($missing -join ', ')" `
               "Veja o log acima. Se foi so um projeto que falhou, rode de novo - as vezes e paralelismo."
}

Write-Ok "binarios em $binDir"
Write-Host @"

    Proximo passo:
        .\scripts\04-setup-database.ps1
"@ -ForegroundColor Gray
