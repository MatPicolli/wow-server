<#
.SYNOPSIS
    Compila a interface grafica.

.PARAMETER Run
    Abre a GUI depois de compilar.

.PARAMETER Publish
    Gera um executavel unico em gui\publish, sem precisar do SDK pra rodar.

.PARAMETER Test
    Roda os testes do Core.
#>
[CmdletBinding()]
param(
    [switch]$Run,
    [switch]$Publish,
    [switch]$Test
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host ''
    Write-Host '    [erro] dotnet nao encontrado no PATH.' -ForegroundColor Red
    Write-Host '    -> Instale o .NET SDK: https://dotnet.microsoft.com/download' -ForegroundColor Yellow
    Write-Host ''
    exit 1
}

Write-Host "==> SDK: $(dotnet --version)" -ForegroundColor Cyan

if ($Test) {
    Write-Host '==> testes do Core' -ForegroundColor Cyan
    dotnet run --project WowServer.Core.Tests
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

if ($Publish) {
    Write-Host '==> publicando executavel unico' -ForegroundColor Cyan
    dotnet publish WowServer.Gui `
        -c Release -r win-x64 --self-contained false `
        -p:PublishSingleFile=true `
        -o publish
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Write-Host ''
    Write-Host "    [ok] gerado em $(Join-Path $PSScriptRoot 'publish')" -ForegroundColor Green
    Write-Host '    Importante: rode o executavel de dentro da pasta do repositorio -' -ForegroundColor Gray
    Write-Host '    ele procura as pastas scripts\ e config\ subindo a partir de onde esta.' -ForegroundColor Gray
    exit 0
}

Write-Host '==> compilando' -ForegroundColor Cyan
dotnet build WowServer.Gui -c Debug
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if ($Run) {
    Write-Host '==> abrindo' -ForegroundColor Cyan
    dotnet run --project WowServer.Gui
}
