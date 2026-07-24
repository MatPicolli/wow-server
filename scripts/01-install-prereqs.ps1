<#
.SYNOPSIS
    Instala as dependencias de build do AzerothCore no Windows.

.DESCRIPTION
    Instala via winget: Git, CMake, Visual Studio 2022 Community (workload C++),
    MySQL e OpenSSL 3.x. O Boost e baixado direto do archives.boost.io porque
    o AzerothCore precisa dos binarios pre-compilados pra MSVC.

    Idempotente: pula o que ja estiver instalado.

.NOTES
    PRECISA rodar como Administrador.
#>
[CmdletBinding()]
param(
    [switch]$SkipVisualStudio,
    [string]$BoostVersion = '1.86.0'
)

. "$PSScriptRoot\lib\common.ps1"

Assert-Admin
$settings = Import-ServerSettings

Write-Step "Verificando o winget"
if (-not (Test-Command 'winget')) {
    Write-Fail "winget nao encontrado." `
               "Instale o 'App Installer' pela Microsoft Store, ou siga a instalacao manual em docs\passo-a-passo.md."
}
Write-Ok "winget disponivel"

# ---------------------------------------------------------------------------
function Install-WingetPackage {
    param(
        [string[]]$Ids,          # tentados em ordem ate um funcionar
        [string]$FriendlyName,
        [string]$ManualUrl,
        [string[]]$ExtraArgs = @()
    )

    foreach ($id in $Ids) {
        $installed = winget list --id $id --exact 2>$null | Out-String
        if ($LASTEXITCODE -eq 0 -and $installed -match [regex]::Escape($id)) {
            Write-Ok "$FriendlyName ja instalado ($id)"
            return $true
        }
    }

    foreach ($id in $Ids) {
        Write-Info "instalando $FriendlyName via winget ($id)..."
        $wingetArgs = @(
            'install', '--id', $id, '--exact',
            '--accept-package-agreements', '--accept-source-agreements',
            '--disable-interactivity'
        ) + $ExtraArgs

        winget @wingetArgs
        if ($LASTEXITCODE -eq 0) {
            Write-Ok "$FriendlyName instalado"
            return $true
        }
        Write-Info "  '$id' nao funcionou, tentando proximo candidato..."
    }

    Write-Warn "Nao consegui instalar $FriendlyName automaticamente."
    if ($ManualUrl) { Write-Warn "Instale na mao: $ManualUrl" }
    return $false
}

# --------------------------------------------------------------------- git --
Write-Step "Git"
Install-WingetPackage -Ids @('Git.Git') -FriendlyName 'Git' -ManualUrl 'https://git-scm.com/download/win' | Out-Null

# ------------------------------------------------------------------- cmake --
Write-Step "CMake (>= 3.27)"
Install-WingetPackage -Ids @('Kitware.CMake') -FriendlyName 'CMake' -ManualUrl 'https://cmake.org/download/' | Out-Null

# ---------------------------------------------------------- visual studio --
if ($SkipVisualStudio) {
    Write-Step "Visual Studio (pulado por -SkipVisualStudio)"
} else {
    Write-Step "Visual Studio 2022 Community + workload C++"
    if (Find-VsGenerator) {
        Write-Ok "Visual Studio com toolchain C++ ja instalado"
    } else {
        Write-Info "Isso baixa varios GB e pode demorar bastante..."
        Install-WingetPackage `
            -Ids @('Microsoft.VisualStudio.2022.Community') `
            -FriendlyName 'Visual Studio 2022 Community' `
            -ManualUrl 'https://visualstudio.microsoft.com/vs/community/' `
            -ExtraArgs @(
                '--override',
                '--quiet --wait --norestart --add Microsoft.VisualStudio.Workload.NativeDesktop --includeRecommended'
            ) | Out-Null
    }
}

# ------------------------------------------------------------------- mysql --
Write-Step "MySQL Server"
if (Find-MySqlDir) {
    Write-Ok "MySQL ja instalado em $(Find-MySqlDir)"
} else {
    Install-WingetPackage -Ids @('Oracle.MySQL') -FriendlyName 'MySQL Server' `
        -ManualUrl 'https://dev.mysql.com/downloads/installer/' | Out-Null
}

$mysqlDir = Find-MySqlDir
if ($mysqlDir) {
    if (-not (Test-Path (Join-Path $mysqlDir 'lib\libmysql.lib'))) {
        Write-Warn "Achei o MySQL em '$mysqlDir' mas nao vi lib\libmysql.lib."
        Write-Warn "Reabra o MySQL Installer e adicione o componente 'MySQL Server' com os Development Components."
    }
    # deixa o mysql.exe acessivel no PATH da maquina
    $bin = Join-Path $mysqlDir 'bin'
    $machinePath = [Environment]::GetEnvironmentVariable('Path', 'Machine')
    if ($machinePath -notlike "*$bin*") {
        [Environment]::SetEnvironmentVariable('Path', "$machinePath;$bin", 'Machine')
        $env:Path = "$env:Path;$bin"
        Write-Ok "adicionado ao PATH: $bin"
    }
}

# ----------------------------------------------------------------- openssl --
Write-Step "OpenSSL 3.x (a versao 4.x NAO serve)"
$sslDir = Find-OpenSslDir
$needsSsl = $true
if ($sslDir -and (Test-Path (Join-Path $sslDir 'bin\libcrypto-3-x64.dll'))) {
    Write-Ok "OpenSSL 3.x ja instalado em $sslDir"
    $needsSsl = $false
} elseif ($sslDir) {
    Write-Warn "Achei OpenSSL em '$sslDir' mas sem libcrypto-3-x64.dll - provavelmente e a 4.x."
    Write-Warn "O AzerothCore linka contra a 3.x. Desinstale e instale a linha LTS 3.x."
}

if ($needsSsl) {
    # a LTS ainda e a 3.x; os pacotes nao-LTS ja pularam pra 4.x
    Install-WingetPackage `
        -Ids @('ShiningLight.OpenSSL.LTS.Dev', 'ShiningLight.OpenSSL.Dev') `
        -FriendlyName 'OpenSSL (dev, Win64)' `
        -ManualUrl 'https://slproweb.com/products/Win32OpenSSL.html  (escolha "Win64 OpenSSL v3.x.x" - NAO a Light, NAO a 4.x)' | Out-Null

    $sslDir = Find-OpenSslDir
    if ($sslDir -and -not (Test-Path (Join-Path $sslDir 'bin\libcrypto-3-x64.dll'))) {
        Write-Warn "A instalacao do OpenSSL nao gerou libcrypto-3-x64.dll."
        Write-Warn "Baixe manualmente a 'Win64 OpenSSL v3.x.x' (nao Light) em https://slproweb.com/products/Win32OpenSSL.html"
        Write-Warn "Na instalacao, escolha copiar as DLLs para 'The OpenSSL binaries (/bin) directory'."
    }
}

# ------------------------------------------------------------------- boost --
Write-Step "Boost >= 1.78 (binarios pre-compilados MSVC 14.3)"
$boostDir = Find-BoostDir
if ($boostDir -and (Test-Path (Join-Path $boostDir 'boost'))) {
    Write-Ok "Boost ja instalado em $boostDir"
} else {
    $underscored = $BoostVersion -replace '\.', '_'
    $installer   = "boost_${underscored}-msvc-14.3-64.exe"
    $target      = "$($settings.BoostDir)_$underscored"   # ex: C:\local\boost_1_86_0

    $downloads = Join-Path $AcRepoDir 'downloads'
    New-DirectoryIfMissing $downloads
    $localExe = Join-Path $downloads $installer

    if (-not (Test-Path $localExe)) {
        $urls = @(
            "https://archives.boost.io/release/$BoostVersion/binaries/$installer"
            "https://sourceforge.net/projects/boost/files/boost-binaries/$BoostVersion/$installer/download"
        )
        $downloaded = $false
        foreach ($url in $urls) {
            Write-Info "baixando $installer ..."
            try {
                Invoke-WebRequest -Uri $url -OutFile $localExe -UseBasicParsing
                $downloaded = $true
                break
            } catch {
                Write-Info "  falhou em $url"
            }
        }
        if (-not $downloaded) {
            Write-Fail "Nao consegui baixar os binarios do Boost." `
                       "Baixe '$installer' de https://archives.boost.io/release/$BoostVersion/binaries/ , instale em '$target' e rode este script de novo."
        }
    }

    Write-Info "instalando Boost em $target (silencioso, alguns minutos)..."
    # instalador Inno Setup
    Invoke-Checked -FilePath $localExe -Arguments @('/VERYSILENT', '/SP-', '/SUPPRESSMSGBOXES', "/DIR=$target") -What 'instalador do Boost'
    $boostDir = $target
    Write-Ok "Boost instalado em $target"
}

# BOOST_ROOT precisa usar barras normais, senao o CMake do AzerothCore nao acha
if ($boostDir) {
    $boostRoot = ($boostDir -replace '\\', '/').TrimEnd('/')
    [Environment]::SetEnvironmentVariable('BOOST_ROOT', $boostRoot, 'Machine')
    $env:BOOST_ROOT = $boostRoot
    Write-Ok "BOOST_ROOT = $boostRoot"
}

# ---------------------------------------------------------------------------
Write-Step "Pronto"
Write-Host @"
    Feche e reabra o PowerShell pra pegar as variaveis de ambiente novas.

    Proximo passo:
        .\scripts\02-clone-source.ps1
"@ -ForegroundColor Gray
