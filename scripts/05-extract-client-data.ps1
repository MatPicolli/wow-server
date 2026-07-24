<#
.SYNOPSIS
    Extrai dbc, maps, vmaps e mmaps do seu client 3.3.5a.

.DESCRIPTION
    Os extractors precisam rodar DE DENTRO da pasta do client (leem os .MPQ de
    Data\). Este script copia os executaveis pra la, roda na ordem certa e
    depois move o resultado pra ServerDir\Data.

    Tempo estimado:
        dbc + maps ....... 5-15 min
        vmaps ............ 20-40 min
        mmaps ............ 1-6 HORAS  (e o gargalo; usa todos os nucleos)

    O script e retomavel: pastas ja extraidas sao puladas. Use -Force pra
    refazer uma etapa.

.PARAMETER Only
    Extrai so uma etapa: maps, vmaps ou mmaps.

.PARAMETER Force
    Refaz mesmo se a saida ja existir.
#>
[CmdletBinding()]
param(
    [ValidateSet('maps', 'vmaps', 'mmaps')]
    [string]$Only,
    [switch]$Force,
    [switch]$SkipMmaps,
    [int]$MmapThreads = 0
)

. "$PSScriptRoot\lib\common.ps1"

$settings = Import-ServerSettings
Assert-ClientDir -Settings $settings

$client  = $settings.ClientDir
$binDir  = Join-Path $settings.BuildDir "bin\$($settings.BuildConfig)"
$dataDir = Join-Path $settings.ServerDir 'Data'

if (-not (Test-Path (Join-Path $binDir 'mapextractor.exe'))) {
    Write-Fail "Nao achei os extractors em '$binDir'." `
               "Rode 03-build.ps1 com TOOLS_BUILD=all (o padrao)."
}

$free = Get-FreeSpaceGB -Path $client
if ($null -ne $free -and $free -lt 25) {
    Write-Warn "So $free GB livres em $([IO.Path]::GetPathRoot($client)). A extracao gera ~20 GB temporarios."
}

# --- copiar os extractors pro client ---------------------------------------
Write-Step "Copiando os extractors para a pasta do client"
$tools = @('mapextractor.exe', 'vmap4extractor.exe', 'vmap4assembler.exe', 'mmaps_generator.exe')
foreach ($t in $tools) {
    $srcTool = Join-Path $binDir $t
    if (Test-Path $srcTool) { Copy-Item $srcTool $client -Force }
    else { Write-Warn "$t nao existe em $binDir" }
}
# arquivos auxiliares que o mmaps_generator le, quando presentes
foreach ($aux in @('mmaps-config.yaml', 'offmesh.txt')) {
    $srcAux = Join-Path $binDir $aux
    if (Test-Path $srcAux) { Copy-Item $srcAux $client -Force }
}
Write-Ok "extractors copiados"

function Test-ExtractedDir {
    param([string]$Name)
    $p = Join-Path $client $Name
    return (Test-Path $p) -and ((Get-ChildItem $p -File -ErrorAction SilentlyContinue | Measure-Object).Count -gt 0)
}

function Invoke-Extractor {
    param([string]$Exe, [string[]]$Arguments = @(), [string]$Label)
    Write-Info "rodando $Exe ..."
    $started = Get-Date
    Invoke-Checked -FilePath (Join-Path $client $Exe) -Arguments $Arguments -WorkingDirectory $client -What $Label
    Write-Ok ("$Label concluido em {0:hh\:mm\:ss}" -f ((Get-Date) - $started))
}

$doMaps  = (-not $Only) -or $Only -eq 'maps'
$doVmaps = ((-not $Only) -or $Only -eq 'vmaps') -and $settings.ExtractVmaps
$doMmaps = ((-not $Only) -or $Only -eq 'mmaps') -and $settings.ExtractMmaps -and (-not $SkipMmaps)

# --- 1. dbc + maps + Cameras -----------------------------------------------
if ($doMaps) {
    Write-Step "1/4  dbc, maps e Cameras  (~5-15 min)"
    if ((Test-ExtractedDir 'dbc') -and (Test-ExtractedDir 'maps') -and -not $Force) {
        Write-Ok "ja extraido, pulando (use -Force pra refazer)"
    } else {
        Invoke-Extractor -Exe 'mapextractor.exe' -Label 'extracao de dbc/maps'
    }
}

# --- 2. Buildings ----------------------------------------------------------
if ($doVmaps) {
    Write-Step "2/4  Buildings, materia-prima dos vmaps  (~20-40 min)"
    if ((Test-ExtractedDir 'Buildings') -and -not $Force) {
        Write-Ok "ja extraido, pulando"
    } else {
        Invoke-Extractor -Exe 'vmap4extractor.exe' -Label 'extracao de Buildings'
    }

    # --- 3. montar os vmaps ------------------------------------------------
    Write-Step "3/4  Montando os vmaps  (~5-10 min)"
    if ((Test-ExtractedDir 'vmaps') -and -not $Force) {
        Write-Ok "ja montado, pulando"
    } else {
        New-DirectoryIfMissing (Join-Path $client 'vmaps')
        Invoke-Extractor -Exe 'vmap4assembler.exe' -Arguments @('Buildings', 'vmaps') -Label 'montagem dos vmaps'
    }
}

# --- 4. mmaps --------------------------------------------------------------
if ($doMmaps) {
    Write-Step "4/4  mmaps - pathfinding  (1-6 HORAS)"
    if ((Test-ExtractedDir 'mmaps') -and -not $Force) {
        Write-Ok "ja extraido, pulando"
    } else {
        if (-not (Test-ExtractedDir 'vmaps')) {
            Write-Fail "Os mmaps precisam dos vmaps, e a pasta vmaps esta vazia." `
                       "Rode antes: .\scripts\05-extract-client-data.ps1 -Only vmaps"
        }
        New-DirectoryIfMissing (Join-Path $client 'mmaps')

        $threads = if ($MmapThreads -gt 0) { $MmapThreads } else { Get-ThreadCount -Settings $settings }
        Write-Info "usando $threads threads - a maquina vai ficar pesada"
        Write-Info "NAO feche a janela; termina quando aparecer 'Press any key'"
        Invoke-Extractor -Exe 'mmaps_generator.exe' -Arguments @('--threads', "$threads") -Label 'geracao dos mmaps'
    }
}

# --- mover pro servidor ----------------------------------------------------
Write-Step "Movendo os dados extraidos para $dataDir"
New-DirectoryIfMissing $dataDir

$moved = @()
foreach ($folder in @('dbc', 'maps', 'vmaps', 'mmaps', 'Cameras')) {
    $from = Join-Path $client $folder
    if (-not (Test-Path $from)) { continue }

    $to = Join-Path $dataDir $folder
    if (Test-Path $to) {
        if (-not $Force) {
            Write-Info "$folder ja existe no destino, mantendo"
            continue
        }
        Remove-Item $to -Recurse -Force
    }
    Move-Item $from $to
    $moved += $folder
}
if ($moved) { Write-Ok "movido: $($moved -join ', ')" }

# Buildings so serve pra gerar os vmaps; sao dezenas de GB de lixo depois disso
$buildings = Join-Path $client 'Buildings'
if (Test-Path $buildings) {
    $sizeGB = [math]::Round(((Get-ChildItem $buildings -Recurse -File | Measure-Object Length -Sum).Sum / 1GB), 1)
    Write-Info "A pasta Buildings ($sizeGB GB) so serve pra montar os vmaps e pode ser apagada:"
    Write-Info "  Remove-Item -Recurse -Force `"$buildings`""
}

# limpa os executaveis copiados
foreach ($t in $tools) {
    $copied = Join-Path $client $t
    if (Test-Path $copied) { Remove-Item $copied -Force -ErrorAction SilentlyContinue }
}

# --- conferencia final -----------------------------------------------------
Write-Step "Conferindo"
$required = @('dbc', 'maps')
$optional = @('vmaps', 'mmaps', 'Cameras')
$fail = $false

foreach ($f in $required) {
    $p = Join-Path $dataDir $f
    if ((Test-Path $p) -and (Get-ChildItem $p -File | Select-Object -First 1)) {
        $n = (Get-ChildItem $p -File -Recurse | Measure-Object).Count
        Write-Ok ("{0,-10} {1} arquivos" -f $f, $n)
    } else {
        Write-Host "    [falta] $f (obrigatorio)" -ForegroundColor Red
        $fail = $true
    }
}
foreach ($f in $optional) {
    $p = Join-Path $dataDir $f
    if ((Test-Path $p) -and (Get-ChildItem $p -File | Select-Object -First 1)) {
        $n = (Get-ChildItem $p -File -Recurse | Measure-Object).Count
        Write-Ok ("{0,-10} {1} arquivos" -f $f, $n)
    } else {
        Write-Warn "$f ausente (o servidor sobe, mas com limitacoes)"
    }
}

if ($fail) { Write-Fail "Faltam dados obrigatorios." "Rode de novo com -Force." }

Write-Host @"

    Proximo passo:
        .\scripts\06-deploy.ps1
"@ -ForegroundColor Gray
