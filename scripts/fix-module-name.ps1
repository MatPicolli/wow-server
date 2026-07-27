<#
.SYNOPSIS
    Renomeia a pasta de um modulo que o core so reconhece por outro nome.

.DESCRIPTION
    Hoje ha um caso: o Eluna virou 'mod-ale' (Azeroth Lua Engine). O
    modules/CMakeLists.txt do core so liga a biblioteca Lua ao alvo dos modulos
    dentro de

        if (SOURCE_MODULE MATCHES "mod-ale")

    e SOURCE_MODULE e o NOME DA PASTA em modules/. Quem clonou pelo endereco
    antigo - que o GitHub redireciona, entao o codigo veio certo - fica com a
    pasta 'mod-eluna', a condicao nunca casa, e a compilacao morre em 25 erros
    C1083 identicos reclamando de 'lua.h', depois de ter compilado a biblioteca
    Lua com sucesso.

    Mostra o que faria; so mexe com -Apply.

.PARAMETER Apply
    Renomeia de verdade.

.EXAMPLE
    .\scripts\fix-module-name.ps1
    .\scripts\fix-module-name.ps1 -Apply
#>
[CmdletBinding()]
param(
    [switch]$Apply
)

. "$PSScriptRoot\lib\common.ps1"

$settings   = Import-ServerSettings
$modulesDir = Join-Path $settings.SourceDir 'modules'

Write-Step "Modulos com nome antigo em $modulesDir"

$achados = Get-RenamedModule $modulesDir

if ($achados.Count -eq 0) {
    Write-Ok 'nenhum - nao ha o que renomear'
    exit 0
}

$mexeu = 0

foreach ($r in $achados) {
    $de   = Join-Path $modulesDir $r.Atual
    $para = Join-Path $modulesDir $r.Correto

    if ($r.Conflito) {
        # Renomear aqui juntaria duas copias do mesmo codigo na compilacao.
        # Quem decide qual fica e o usuario, entao este script nao apaga nada.
        Write-Warn "$($r.Atual) e $($r.Correto) existem os dois"
        Write-Info "  o mesmo codigo entraria duas vezes na compilacao"
        Write-Info "  remova o antigo: .\scripts\remove-module.ps1 -Name $($r.Atual) -Apply"
        continue
    }

    Write-Info "$($r.Atual)  ->  $($r.Correto)"

    if (-not $Apply) { continue }

    Rename-Item -LiteralPath $de -NewName $r.Correto
    Write-Ok "renomeado para $($r.Correto)"
    $mexeu++
}

if (-not $Apply) {
    Write-Host ''
    Write-Info 'nada foi alterado. Para renomear de verdade:'
    Write-Info '  .\scripts\fix-module-name.ps1 -Apply'
    exit 0
}

if ($mexeu -gt 0) {
    Write-Step 'Proximo passo'
    Write-Info 'compile de novo: .\scripts\rebuild.ps1'
    Write-Info 'o CMake reconfigura sozinho ao ver a pasta mudar.'
}

exit 0
