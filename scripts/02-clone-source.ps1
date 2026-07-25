<#
.SYNOPSIS
    Clona (ou atualiza) o codigo-fonte do core.

.DESCRIPTION
    Por padrao usa SourceRepository e SourceBranch do config\settings.psd1.
    Mods de bot que exigem alteracoes no core (Playerbots, NPCBots) vivem em
    forks - configure la e rode este script com -Force.

.PARAMETER Repository
    Sobrescreve o SourceRepository do settings.psd1.

.PARAMETER Branch
    Sobrescreve o SourceBranch do settings.psd1.

.PARAMETER Force
    Apaga o fonte existente e clona de novo. Necessario ao trocar de
    repositorio. O build precisa ser refeito depois (03-build.ps1).
#>
[CmdletBinding()]
param(
    [string]$Repository,
    [string]$Branch,
    [switch]$Force
)

. "$PSScriptRoot\lib\common.ps1"

$settings = Import-ServerSettings

if (-not $Repository) { $Repository = $settings.SourceRepository }
if (-not $Branch)     { $Branch     = $settings.SourceBranch }

if (-not (Test-Command 'git')) {
    Write-Fail "git nao encontrado no PATH." "Rode 01-install-prereqs.ps1 e reabra o PowerShell."
}

$src = $settings.SourceDir

function Get-NormalizedRemote {
    # Compara URLs ignorando .git no fim, barra final e maiusculas
    param([string]$Url)
    if (-not $Url) { return '' }
    return ($Url.Trim().TrimEnd('/') -replace '\.git$', '').ToLowerInvariant()
}

Write-Info "repositorio: $Repository"
Write-Info "branch:      $Branch"

# --- checkout existente ----------------------------------------------------
if ((Test-Path (Join-Path $src '.git')) -and -not $Force) {

    # Trocar de fork so no settings.psd1 sem reclonar deixaria o script
    # atualizando o repositorio errado - e um 'reset --hard' te tiraria do
    # branch do mod sem avisar. Melhor barrar.
    $currentRemote = ''
    try { $currentRemote = (git -C $src remote get-url origin 2>$null | Out-String).Trim() } catch { }

    if ((Get-NormalizedRemote $currentRemote) -ne (Get-NormalizedRemote $Repository)) {
        Write-Fail "O fonte em '$src' aponta pra outro repositorio." @"
atual:     $currentRemote
desejado:  $Repository

Pra trocar (apaga o fonte e clona de novo; o build tera que ser refeito):
    .\scripts\02-clone-source.ps1 -Force
"@
    }

    Write-Step "Atualizando o fonte em $src"
    Invoke-Checked -FilePath 'git' -Arguments @('fetch', 'origin', $Branch) -WorkingDirectory $src -What 'git fetch'

    $dirty = (git -C $src status --porcelain) | Out-String
    if ($dirty.Trim()) {
        Write-Warn "Voce tem alteracoes locais no fonte. Nao vou mexer no seu checkout."
        Write-Info "Pra atualizar mesmo assim: git -C `"$src`" stash && git -C `"$src`" pull"
    } else {
        Invoke-Checked -FilePath 'git' -Arguments @('checkout', $Branch)                  -WorkingDirectory $src -What 'git checkout'
        Invoke-Checked -FilePath 'git' -Arguments @('reset', '--hard', "origin/$Branch")  -WorkingDirectory $src -What 'git reset'
        Write-Ok "atualizado para origin/$Branch"
    }

} else {
    # --- clone novo --------------------------------------------------------
    if (Test-Path $src) {
        $naoVazio = Get-ChildItem $src -Force -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($naoVazio) {
            if (-not $Force) {
                Write-Fail "'$src' existe e nao esta vazio, mas nao e um repositorio git." `
                           "Apague a pasta, rode com -Force, ou aponte SourceDir pra outro lugar."
            }
            Write-Step "Apagando o fonte anterior"
            Write-Info "isso pode demorar - sao dezenas de milhares de arquivos"
            Remove-Item $src -Recurse -Force
            Write-Ok 'removido'
        }
    }

    Write-Step "Clonando ($Branch)"
    Write-Info "sao alguns GB, pode demorar"
    New-DirectoryIfMissing (Split-Path -Parent $src)
    Invoke-Checked -FilePath 'git' `
                   -Arguments @('clone', '--branch', $Branch, $Repository, $src) `
                   -What 'git clone'
    Write-Ok "clonado em $src"
}

$rev = (git -C $src rev-parse --short HEAD).Trim()
$br  = (git -C $src rev-parse --abbrev-ref HEAD).Trim()
Write-Ok "commit atual: $rev ($br)"

Write-Host @"

    Proximo passo:
        .\scripts\03-build.ps1
"@ -ForegroundColor Gray
