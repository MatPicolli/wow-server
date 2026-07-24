<#
.SYNOPSIS
    Clona (ou atualiza) o codigo-fonte do AzerothCore.

.PARAMETER Branch
    Branch do AzerothCore. 'master' e o padrao e e a branch estavel do 3.3.5a.
#>
[CmdletBinding()]
param(
    [string]$Repository = 'https://github.com/azerothcore/azerothcore-wotlk.git',
    [string]$Branch     = 'master'
)

. "$PSScriptRoot\lib\common.ps1"

$settings = Import-ServerSettings

if (-not (Test-Command 'git')) {
    Write-Fail "git nao encontrado no PATH." "Rode 01-install-prereqs.ps1 e reabra o PowerShell."
}

$src = $settings.SourceDir

if (Test-Path (Join-Path $src '.git')) {
    Write-Step "Atualizando o fonte em $src"
    Invoke-Checked -FilePath 'git' -Arguments @('fetch', 'origin', $Branch) -WorkingDirectory $src -What 'git fetch'

    $dirty = (git -C $src status --porcelain) | Out-String
    if ($dirty.Trim()) {
        Write-Warn "Voce tem alteracoes locais no fonte. Nao vou mexer no seu checkout."
        Write-Info "Pra atualizar mesmo assim: git -C `"$src`" stash && git -C `"$src`" pull"
    } else {
        Invoke-Checked -FilePath 'git' -Arguments @('checkout', $Branch)         -WorkingDirectory $src -What 'git checkout'
        Invoke-Checked -FilePath 'git' -Arguments @('reset', '--hard', "origin/$Branch") -WorkingDirectory $src -What 'git reset'
        Write-Ok "atualizado para origin/$Branch"
    }
} else {
    if ((Test-Path $src) -and (Get-ChildItem $src -Force | Select-Object -First 1)) {
        Write-Fail "'$src' existe e nao esta vazio, mas nao e um repositorio git." `
                   "Apague a pasta ou aponte SourceDir pra outro lugar em config\settings.psd1."
    }

    Write-Step "Clonando o AzerothCore ($Branch)"
    Write-Info "sao alguns GB, pode demorar"
    New-DirectoryIfMissing (Split-Path -Parent $src)
    Invoke-Checked -FilePath 'git' -Arguments @('clone', '--branch', $Branch, $Repository, $src) -What 'git clone'
    Write-Ok "clonado em $src"
}

$rev = (git -C $src rev-parse --short HEAD).Trim()
Write-Ok "commit atual: $rev"

Write-Host @"

    Proximo passo:
        .\scripts\03-build.ps1
"@ -ForegroundColor Gray
