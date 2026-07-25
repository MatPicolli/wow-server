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

$free = Get-FreeSpaceGB -Path $client
if ($null -ne $free -and $free -lt 25) {
    Write-Warn "So $free GB livres em $([IO.Path]::GetPathRoot($client)). A extracao gera ~20 GB temporarios."
}

# --- copiar os extractors pro client ---------------------------------------
function Resolve-ToolName {
    <#
        O AzerothCore renomeou os extractors de 'mapextractor' para
        'map_extractor' em algum ponto. Aceita as duas grafias e devolve a que
        existir de fato em $binDir.
    #>
    param([string[]]$Names)
    foreach ($n in $Names) {
        if (Test-Path (Join-Path $binDir $n)) { return $n }
    }
    return $null
}

$exeMapExtractor   = Resolve-ToolName @('map_extractor.exe',   'mapextractor.exe')
$exeVmapExtractor  = Resolve-ToolName @('vmap4_extractor.exe', 'vmap4extractor.exe')
$exeVmapAssembler  = Resolve-ToolName @('vmap4_assembler.exe', 'vmap4assembler.exe')
$exeMmapsGenerator = Resolve-ToolName @('mmaps_generator.exe')

if (-not $exeMapExtractor -and -not $exeVmapExtractor -and -not $exeMmapsGenerator) {
    Write-Fail "Nao achei nenhum extractor em '$binDir'." `
               "Rode 03-build.ps1 com TOOLS_BUILD=all (o padrao)."
}

Write-Step "Copiando os extractors para a pasta do client"
$tools = @($exeMapExtractor, $exeVmapExtractor, $exeVmapAssembler, $exeMmapsGenerator) |
         Where-Object { $_ }
foreach ($t in $tools) {
    Copy-Item (Join-Path $binDir $t) $client -Force
    Write-Ok $t
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

function Test-StageComplete {
    <#
        "Tem arquivo na pasta" nao serve pra vmaps e mmaps: se a geracao for
        interrompida no meio (Ctrl+C durante as horas de mmaps), a pasta fica
        com resultado parcial e a etapa seria considerada pronta - deixando o
        servidor com pathfinding pela metade, sem aviso nenhum.

        Aqui exigimos um arquivo-indice por mapa (.vmtree / .mmap). Se nao der
        pra saber quantos mapas existem, cai no teste antigo.
    #>
    param([string]$Name, [string]$IndexFilter, [int]$ExpectedTotal)

    $dir = Join-Path $client $Name
    if (-not (Test-Path $dir)) { return $false }
    if ($ExpectedTotal -le 0) { return (Test-ExtractedDir $Name) }

    $done = Get-FileCount -Path $dir -Filter $IndexFilter
    if ($done -ge $ExpectedTotal) { return $true }

    if ($done -gt 0) {
        Write-Warn "$Name esta incompleto ($done de $ExpectedTotal mapas) - provavelmente foi interrompido. Vou continuar de onde parou."
    }
    return $false
}

function Invoke-Extractor {
    param(
        [string]$Exe,
        [string[]]$Arguments = @(),
        [string]$Label,
        [string]$WatchDir,
        [string]$WatchFilter = '*',
        [int]$ExpectedTotal = 0
    )

    Write-Info "rodando $Exe ..."
    $started = Get-Date

    $code = Invoke-ProcessWithProgress `
                -FilePath (Join-Path $client $Exe) `
                -Arguments $Arguments `
                -WorkingDirectory $client `
                -Activity $Label `
                -WatchDir $WatchDir `
                -WatchFilter $WatchFilter `
                -ExpectedTotal $ExpectedTotal

    if ($code -ne 0) { Write-Fail "$Label falhou (exit code $code)." }
    Write-Ok ("$Label concluido em {0:hh\:mm\:ss}" -f ((Get-Date) - $started))
}

# Quantos mapas o client tem. Vira o total das barras de vmaps e mmaps, e a
# base pra saber se essas etapas terminaram mesmo.
$mapCount = Get-ExtractedMapCount -MapsDir (Join-Path $client 'maps')
if ($mapCount -eq 0) {
    $mapCount = Get-ExtractedMapCount -MapsDir (Join-Path $dataDir 'maps')
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
        if (-not $exeMapExtractor) { Write-Fail "Extractor de mapas nao encontrado em '$binDir'." "Rode 03-build.ps1." }
        Invoke-Extractor -Exe $exeMapExtractor -Label 'Extraindo dbc e maps' `
                         -WatchDir (Join-Path $client 'maps') -WatchFilter '*.map'
    }
}

# --- 2. Buildings ----------------------------------------------------------
if ($doVmaps) {
    Write-Step "2/4  Buildings, materia-prima dos vmaps  (~20-40 min)"
    if ((Test-ExtractedDir 'Buildings') -and -not $Force) {
        Write-Ok "ja extraido, pulando"
    } else {
        if (-not $exeVmapExtractor) { Write-Fail "Extractor de vmaps nao encontrado em '$binDir'." "Rode 03-build.ps1." }
        Invoke-Extractor -Exe $exeVmapExtractor -Label 'Extraindo Buildings' `
                         -WatchDir (Join-Path $client 'Buildings') -WatchFilter '*'
    }

    # --- 3. montar os vmaps ------------------------------------------------
    Write-Step "3/4  Montando os vmaps  (~5-10 min)"
    if ((Test-StageComplete -Name 'vmaps' -IndexFilter '*.vmtree' -ExpectedTotal $mapCount) -and -not $Force) {
        Write-Ok "ja montado, pulando"
    } else {
        New-DirectoryIfMissing (Join-Path $client 'vmaps')
        if (-not $exeVmapAssembler) { Write-Fail "Montador de vmaps nao encontrado em '$binDir'." "Rode 03-build.ps1." }
        Invoke-Extractor -Exe $exeVmapAssembler -Arguments @('Buildings', 'vmaps') -Label 'Montando vmaps' `
                         -WatchDir (Join-Path $client 'vmaps') -WatchFilter '*.vmtree' -ExpectedTotal $mapCount
    }
}

# --- 4. mmaps --------------------------------------------------------------
if ($doMmaps) {
    Write-Step "4/4  mmaps - pathfinding  (1-6 HORAS)"
    if ((Test-StageComplete -Name 'mmaps' -IndexFilter '*.mmap' -ExpectedTotal $mapCount) -and -not $Force) {
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
        if ($mapCount -gt 0) { Write-Info "$mapCount mapas a processar" }
        if (-not $exeMmapsGenerator) { Write-Fail "Gerador de mmaps nao encontrado em '$binDir'." "Rode 03-build.ps1." }
        Invoke-Extractor -Exe $exeMmapsGenerator -Arguments @('--threads', "$threads") -Label 'Gerando mmaps' `
                         -WatchDir (Join-Path $client 'mmaps') -WatchFilter '*.mmap' -ExpectedTotal $mapCount
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
