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

function Get-StageDir {
    <#
        Os dois lugares onde o resultado de uma etapa pode estar.

        A extracao roda dentro da pasta do client e, no fim, MOVE tudo para
        <ServerDir>\Data. Depois de uma instalacao concluida, portanto, nao
        sobra nada no client - e olhar so para la faria o script concluir que
        nunca extraiu nada e refazer horas de mmaps.
    #>
    param([string]$Name)
    return ,@((Join-Path $client $Name), (Join-Path $dataDir $Name))
}

function Test-ExtractedDir {
    param([string]$Name)
    foreach ($p in (Get-StageDir $Name)) {
        if (-not (Test-Path $p)) { continue }
        if (@(Get-ChildItem $p -File -ErrorAction SilentlyContinue).Count -gt 0) { return $true }
    }
    return $false
}

function Test-DirTemArquivo {
    param([string]$Path, [string]$Filter = '*')
    if (-not (Test-Path $Path)) { return $false }
    return (Get-FileCount -Path $Path -Filter $Filter) -gt 0
}

function Get-MmapsWorkDir {
    <#
        De onde rodar o mmaps_generator.

        Ele nao aceita parametro de pasta de dados: resolve 'maps' e 'vmaps'
        relativos ao diretorio de trabalho (Config.cpp usa o diretorio atual, e
        so cai para a pasta do executavel se o atual nao tiver 'maps').

        Durante a instalacao, os dados estao na pasta do client. Depois que ela
        termina, foram movidos para Data\ - e ai rodar a partir do client falha
        com "'maps' directory is empty or does not exist", que e exatamente o
        que acontece ao refazer os mmaps depois de trocar de core.
    #>
    foreach ($candidato in @($client, $dataDir)) {
        $temMaps  = Test-DirTemArquivo -Path (Join-Path $candidato 'maps')  -Filter '*.map'
        $temVmaps = Test-DirTemArquivo -Path (Join-Path $candidato 'vmaps') -Filter '*.vmtree'
        if ($temMaps -and $temVmaps) { return $candidato }
    }
    return $null
}

# Marcador gravado na pasta de saida quando uma etapa termina de verdade.
# "Tem arquivo na pasta" nao serve como criterio: se a geracao for interrompida
# no meio (Ctrl+C durante as horas de mmaps), a pasta fica com resultado
# parcial e a etapa seria considerada pronta - deixando o servidor com
# pathfinding pela metade, sem aviso nenhum.
$StageMarker = '.extract-complete'

function Set-StageComplete {
    param([string]$Name, [string]$Dir)
    if (-not $Dir) { $Dir = Join-Path $client $Name }
    if (Test-Path $Dir) {
        Set-Content -LiteralPath (Join-Path $Dir $StageMarker) `
                    -Value (Get-Date -Format 'o') -Encoding ASCII
    }
}

function Clear-OutputDir {
    <#
        O vmap4_extractor se recusa a rodar se a pasta de saida nao estiver
        vazia ("Your output directory seems to be polluted"). Entao, quando a
        decisao e mesmo refazer a etapa, a saida parcial tem que sair antes.
    #>
    param([string]$Name)
    $dir = Join-Path $client $Name
    if (-not (Test-Path $dir)) { return }
    Write-Info "limpando saida parcial em $Name\ ..."
    Remove-Item (Join-Path $dir '*') -Recurse -Force -ErrorAction SilentlyContinue
}

function Test-StageComplete {
    <#
        -IndexFilter/-ExpectedTotal sao um plano B pra instalacoes que ja
        existiam antes do marcador: um arquivo-indice por mapa (.vmtree /
        .mmap) sugere que a etapa terminou. E so heuristica - nem todo mapa
        necessariamente gera indice - entao o marcador manda.
    #>
    param([string]$Name, [string]$IndexFilter, [int]$ExpectedTotal = 0)

    # Data\ primeiro: e onde o resultado fica depois de uma instalacao que deu
    # certo, e o criterio la e mais simples. O move so acontece quando todas as
    # etapas pedidas terminaram - qualquer falha ou Ctrl+C aborta o script
    # antes. Entao arquivo em Data\ significa etapa concluida, mesmo sem
    # marcador (instalacoes feitas antes desta versao nao tem um).
    $emData = Join-Path $dataDir $Name
    if (Test-Path $emData) {
        if (@(Get-ChildItem $emData -File -ErrorAction SilentlyContinue).Count -gt 0) {
            return $true
        }
    }

    $dir = Join-Path $client $Name
    if (-not (Test-Path $dir)) { return $false }

    if (Test-Path (Join-Path $dir $StageMarker)) { return $true }

    $hasFiles = Test-ExtractedDir $Name

    if ($IndexFilter -and $ExpectedTotal -gt 0) {
        $done = Get-FileCount -Path $dir -Filter $IndexFilter
        if ($done -ge $ExpectedTotal) {
            Set-StageComplete -Name $Name   # adota o que ja estava pronto
            return $true
        }
        if ($done -gt 0) {
            Write-Warn "$Name parece incompleto ($done de $ExpectedTotal mapas com indice) - vou rodar de novo."
            return $false
        }
    }

    if ($hasFiles) {
        Write-Warn "$Name tem arquivos mas nao foi marcado como concluido - provavelmente interrompido. Vou rodar de novo."
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
        [int]$ExpectedTotal = 0,

        # De onde rodar. O padrao e a pasta do client, que e onde estao os MPQ e
        # onde a instalacao normal acontece. O gerador de mmaps precisa do
        # contrario: ele le maps\ e vmaps\ relativos ao diretorio de trabalho, e
        # depois de uma instalacao concluida esses dados moraram para Data\.
        [string]$WorkDir
    )

    if (-not $WorkDir) { $WorkDir = $client }

    Write-Info "rodando $Exe ..."
    if ($WorkDir -ne $client) { Write-Info "a partir de $WorkDir" }
    $started = Get-Date

    $code = Invoke-ProcessWithProgress `
                -FilePath (Join-Path $client $Exe) `
                -Arguments $Arguments `
                -WorkingDirectory $WorkDir `
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
    if ((Test-StageComplete -Name 'maps') -and (Test-ExtractedDir 'dbc') -and -not $Force) {
        Write-Ok "ja extraido, pulando (use -Force pra refazer)"
    } else {
        if (-not $exeMapExtractor) { Write-Fail "Extractor de mapas nao encontrado em '$binDir'." "Rode 03-build.ps1." }
        Invoke-Extractor -Exe $exeMapExtractor -Label 'Extraindo dbc e maps' `
                         -WatchDir (Join-Path $client 'maps') -WatchFilter '*.map'
        Set-StageComplete -Name 'maps'
        Set-StageComplete -Name 'dbc'
    }
}

# --- 2. Buildings ----------------------------------------------------------
if ($doVmaps) {
    Write-Step "2/4  Buildings, materia-prima dos vmaps  (~20-40 min)"
    # Buildings so existe pra alimentar os vmaps. Se os vmaps ja estao
    # completos, Buildings cumpriu seu papel - reextrair 40 minutos dele por
    # causa de um marcador ausente nao faz sentido nenhum.
    if ((Test-StageComplete -Name 'vmaps' -IndexFilter '*.vmtree' -ExpectedTotal $mapCount) -and -not $Force) {
        Set-StageComplete -Name 'Buildings'
    }

    if ((Test-StageComplete -Name 'Buildings') -and -not $Force) {
        Write-Ok "ja extraido, pulando"
    } else {
        if (-not $exeVmapExtractor) { Write-Fail "Extractor de vmaps nao encontrado em '$binDir'." "Rode 03-build.ps1." }
        Clear-OutputDir -Name 'Buildings'
        Invoke-Extractor -Exe $exeVmapExtractor -Label 'Extraindo Buildings' `
                         -WatchDir (Join-Path $client 'Buildings') -WatchFilter '*'
        Set-StageComplete -Name 'Buildings'
    }

    # --- 3. montar os vmaps ------------------------------------------------
    Write-Step "3/4  Montando os vmaps  (~5-10 min)"
    if ((Test-StageComplete -Name 'vmaps' -IndexFilter '*.vmtree' -ExpectedTotal $mapCount) -and -not $Force) {
        Write-Ok "ja montado, pulando"
    } else {
        if (-not $exeVmapAssembler) { Write-Fail "Montador de vmaps nao encontrado em '$binDir'." "Rode 03-build.ps1." }
        Clear-OutputDir -Name 'vmaps'
        New-DirectoryIfMissing (Join-Path $client 'vmaps')
        Invoke-Extractor -Exe $exeVmapAssembler -Arguments @('Buildings', 'vmaps') -Label 'Montando vmaps' `
                         -WatchDir (Join-Path $client 'vmaps') -WatchFilter '*.vmtree' -ExpectedTotal $mapCount
        Set-StageComplete -Name 'vmaps'
    }
}

# --- 4. mmaps --------------------------------------------------------------
if ($doMmaps) {
    Write-Step "4/4  mmaps - pathfinding  (1-6 HORAS)"
    if ((Test-StageComplete -Name 'mmaps' -IndexFilter '*.mmap' -ExpectedTotal $mapCount) -and -not $Force) {
        Write-Ok "ja extraido, pulando"
    } else {
        # O gerador le maps\ e vmaps\ a partir do diretorio de trabalho, entao o
        # que importa nao e se os dados existem, e sim se existem JUNTOS num
        # lugar so - na pasta do client durante a instalacao, ou em Data\ depois.
        $mmapsDir = Get-MmapsWorkDir
        if (-not $mmapsDir) {
            Write-Fail 'nao achei maps\ e vmaps\ juntos nem na pasta do client nem em Data\.' `
                       "Rode antes: .\scripts\05-extract-client-data.ps1 -Only vmaps"
        }

        $saidaMmaps = Join-Path $mmapsDir 'mmaps'
        New-DirectoryIfMissing $saidaMmaps

        # Com -Force a ideia e refazer: tiles antigos sobrando seriam recusados
        # pelo servidor do mesmo jeito, e ainda enganariam a barra de progresso,
        # que conta arquivos na pasta de saida.
        if ($Force -and (Test-DirTemArquivo -Path $saidaMmaps -Filter '*.mmtile')) {
            Write-Info "limpando os mmaps antigos em $saidaMmaps ..."
            Remove-Item (Join-Path $saidaMmaps '*') -Recurse -Force -ErrorAction SilentlyContinue
        }

        $threads = if ($MmapThreads -gt 0) { $MmapThreads } else { Get-ThreadCount -Settings $settings }
        Write-Info "usando $threads threads - a maquina vai ficar pesada"
        Write-Info "NAO feche a janela; termina quando aparecer 'Press any key'"
        if ($mapCount -gt 0) { Write-Info "$mapCount mapas a processar" }
        if (-not $exeMmapsGenerator) { Write-Fail "Gerador de mmaps nao encontrado em '$binDir'." "Rode 03-build.ps1." }

        Invoke-Extractor -Exe $exeMmapsGenerator -Arguments @('--threads', "$threads") -Label 'Gerando mmaps' `
                         -WorkDir $mmapsDir `
                         -WatchDir $saidaMmaps -WatchFilter '*.mmap' -ExpectedTotal $mapCount

        # Marcador so na pasta do client. Se a geracao foi direto em Data\, o
        # que prova a conclusao e a presenca dos arquivos - Data\ nao recebe
        # arquivo de controle, porque quem a le e o worldserver.
        if ($mmapsDir -eq $client) { Set-StageComplete -Name 'mmaps' }
    }
}

# --- mover pro servidor ----------------------------------------------------
Write-Step "Movendo os dados extraidos para $dataDir"
New-DirectoryIfMissing $dataDir

$moved = @()
foreach ($folder in @('dbc', 'maps', 'vmaps', 'mmaps', 'Cameras')) {
    $from = Join-Path $client $folder
    if (-not (Test-Path $from)) { continue }

    # O marcador nao vai junto: Data\ e lida pelo worldserver, e nao ha por que
    # plantar arquivo estranho la. A prova de que a etapa terminou passa a ser
    # a propria presenca dos arquivos em Data\ - ver Test-StageComplete.
    $marker = Join-Path $from $StageMarker
    if (Test-Path $marker) { Remove-Item $marker -Force -ErrorAction SilentlyContinue }

    $to = Join-Path $dataDir $folder

    # Pasta de origem vazia nunca substitui o destino. Sem isso, um diretorio
    # esquecido no client - o caso de refazer os mmaps, que agora sao gerados
    # direto em Data\ - faria o -Force apagar o resultado bom e por um vazio no
    # lugar. Horas de trabalho por uma pasta abandonada.
    if (-not (Test-DirTemArquivo -Path $from)) {
        if (Test-DirTemArquivo -Path $to) {
            Write-Info "$folder no client esta vazio; mantendo o que ja esta em Data\"
        }
        Remove-Item $from -Recurse -Force -ErrorAction SilentlyContinue
        continue
    }

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
