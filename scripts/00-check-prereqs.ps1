<#
.SYNOPSIS
    Confere se tudo que o build precisa esta no lugar. Nao instala nada.

.DESCRIPTION
    Rode antes do build pra descobrir o que falta sem esperar 30 minutos
    de compilacao pra tomar erro.
#>
[CmdletBinding()]
param()

. "$PSScriptRoot\lib\common.ps1"

$settings = Import-ServerSettings
$problems = @()

function Check {
    param([string]$Name, [scriptblock]$Test, [string]$FixHint)
    $result = $null
    try { $result = & $Test } catch { $result = $null }
    if ($result) {
        Write-Ok ("{0,-22} {1}" -f $Name, $result)
        return $true
    }
    Write-Host ("    [falta] {0,-15} {1}" -f $Name, $FixHint) -ForegroundColor Red
    $script:problems += $Name
    return $false
}

Write-Step "Ferramentas de build"

Check 'git' {
    if (Test-Command 'git') { (git --version) }
} 'instale o Git ou rode 01-install-prereqs.ps1' | Out-Null

Check 'cmake' {
    if (Test-Command 'cmake') {
        $v = (cmake --version | Select-Object -First 1)
        if ($v -match '(\d+)\.(\d+)') {
            $maj = [int]$Matches[1]; $min = [int]$Matches[2]
            if ($maj -lt 3 -or ($maj -eq 3 -and $min -lt 27)) {
                Write-Warn "CMake $maj.$min e mais antigo que o minimo 3.27"
            }
        }
        $v
    }
} 'instale o CMake >= 3.27' | Out-Null

Check 'Visual Studio' { Find-VsGenerator } 'instale o VS 2022 com o workload "Desenvolvimento para desktop com C++"' | Out-Null

Write-Step "Bibliotecas"

Check 'Boost' {
    $d = Find-BoostDir
    if ($d -and (Test-Path (Join-Path $d 'boost'))) { $d }
} 'rode 01-install-prereqs.ps1, ou defina BOOST_ROOT (com barras /)' | Out-Null

if ($env:BOOST_ROOT -and $env:BOOST_ROOT -match '\\') {
    Write-Warn "BOOST_ROOT esta com barras invertidas ('$env:BOOST_ROOT'). O CMake do AzerothCore quer barras normais: $($env:BOOST_ROOT -replace '\\','/')"
}

Check 'OpenSSL 3.x' {
    $d = Find-OpenSslDir
    if ($d -and (Test-Path (Join-Path $d 'bin\libcrypto-3-x64.dll'))) { $d }
} 'instale a "Win64 OpenSSL v3.x.x" (NAO Light, NAO 4.x) de slproweb.com' | Out-Null

Check 'MySQL (dev libs)' {
    $d = Find-MySqlDir
    if ($d -and (Test-Path (Join-Path $d 'lib\libmysql.lib'))) { $d }
} 'instale o MySQL 8.x com os Development Components' | Out-Null

Check 'mysql.exe' { Get-MySqlExe } 'adicione a pasta bin\ do MySQL ao PATH' | Out-Null

Write-Step "Client do WoW"

Check 'Wow.exe' {
    $exe = Join-Path $settings.ClientDir 'Wow.exe'
    if (Test-Path $exe) { $settings.ClientDir }
} "ajuste ClientDir em config\settings.psd1 (valor atual: $($settings.ClientDir))" | Out-Null

$build = Get-ClientBuild -ClientDir $settings.ClientDir
if ($build) {
    if ($build -eq '12340') { Write-Ok ("{0,-22} {1}" -f 'build do client', "$build (3.3.5a)") }
    else { Write-Warn "build do client e $build; o AzerothCore so aceita 12340 (3.3.5a)" }
}

Write-Step "Espaco em disco"
foreach ($pair in @(
    @{ Name = 'fonte+build'; Path = $settings.Root;      Need = 60 },
    @{ Name = 'servidor';    Path = $settings.ServerDir; Need = 25 }
)) {
    $free = Get-FreeSpaceGB -Path $pair.Path
    if ($null -eq $free) { continue }
    if ($free -lt $pair.Need) {
        Write-Warn "$($pair.Name): $free GB livres em $([IO.Path]::GetPathRoot($pair.Path)) - recomendo pelo menos $($pair.Need) GB"
    } else {
        Write-Ok ("{0,-22} {1} GB livres" -f $pair.Name, $free)
    }
}

Write-Host ''
if ($problems.Count -gt 0) {
    Write-Host "  Faltando: $($problems -join ', ')" -ForegroundColor Red
    Write-Host "  Rode .\scripts\01-install-prereqs.ps1 como Administrador." -ForegroundColor Yellow
    exit 1
}

Write-Host "  Tudo certo. Pode seguir pro 02-clone-source.ps1" -ForegroundColor Green

# exit 0 explicito: sem isso o $LASTEXITCODE fica com o valor do ultimo
# comando nativo que rodou, e o setup-all.ps1 acha que falhou
exit 0
