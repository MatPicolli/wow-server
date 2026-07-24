<#
.SYNOPSIS
    Gera authserver.conf e worldserver.conf a partir dos .dist e aponta o
    client pro seu servidor.

.DESCRIPTION
    - copia os .conf.dist pra .conf (nao sobrescreve .conf existente sem -Force)
    - preenche as strings de conexao com os dados do settings.psd1
    - aponta DataDir pra ServerDir\Data
    - escreve o realmlist.wtf dentro do client

.PARAMETER Force
    Sobrescreve .conf ja existentes.
#>
[CmdletBinding()]
param(
    [switch]$Force,
    [switch]$SkipClient
)

. "$PSScriptRoot\lib\common.ps1"

$settings   = Import-ServerSettings
$server     = $settings.ServerDir
$configsDir = Join-Path $server 'configs'
$m          = $settings.MySql

if (-not (Test-Path $configsDir)) {
    Write-Fail "'$configsDir' nao existe." "Rode 06-deploy.ps1 primeiro."
}

# ---------------------------------------------------------------------------
function Set-ConfValue {
    <#
        Substitui "Chave = valor" no arquivo de config. Se a chave nao existir,
        adiciona no fim. Ignora linhas comentadas.
    #>
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Key,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Value
    )

    $content = Get-Content -Raw -LiteralPath $Path

    # [^\r\n]* em vez de .*$ pra nao engolir o \r e bagunçar as quebras de linha
    $pattern = '(?m)^[ \t]*' + [regex]::Escape($Key) + '[ \t]*=[^\r\n]*'
    $line    = "$Key = $Value"

    if ($content -match $pattern) {
        # MatchEvaluator em vez de string: senao um '$' na senha vira
        # referencia de grupo no replacement
        $content = [regex]::Replace($content, $pattern, [Text.RegularExpressions.MatchEvaluator]{ param($mt) $line })
    } else {
        $content = $content.TrimEnd() + "`r`n`r`n$line`r`n"
    }

    Write-TextFileNoBom -Path $Path -Content $content
    Write-Ok "$Key = $Value"
}

function Initialize-Conf {
    param([string]$Name)
    $dist = Join-Path $configsDir "$Name.dist"
    $conf = Join-Path $configsDir $Name

    if (-not (Test-Path $dist)) {
        Write-Fail "'$dist' nao existe." "Rode 06-deploy.ps1."
    }
    if ((Test-Path $conf) -and -not $Force) {
        Write-Info "$Name ja existe, vou so atualizar as chaves (use -Force pra recriar do zero)"
    } else {
        Copy-Item $dist $conf -Force
        Write-Ok "$Name criado a partir do .dist"
    }
    return $conf
}

# --- strings de conexao ----------------------------------------------------
# formato: "IP;porta;usuario;senha;banco"
$authInfo  = "`"$($m.Host);$($m.Port);$($m.User);$($m.Password);$($m.AuthDb)`""
$worldInfo = "`"$($m.Host);$($m.Port);$($m.User);$($m.Password);$($m.WorldDb)`""
$charInfo  = "`"$($m.Host);$($m.Port);$($m.User);$($m.Password);$($m.CharDb)`""

# --- authserver.conf -------------------------------------------------------
Write-Step "authserver.conf"
$authConf = Initialize-Conf 'authserver.conf'
Set-ConfValue -Path $authConf -Key 'LoginDatabaseInfo' -Value $authInfo
Set-ConfValue -Path $authConf -Key 'LogsDir'           -Value "`"$($server -replace '\\', '/')/logs`""

# --- worldserver.conf ------------------------------------------------------
Write-Step "worldserver.conf"
$worldConf = Initialize-Conf 'worldserver.conf'
Set-ConfValue -Path $worldConf -Key 'LoginDatabaseInfo'     -Value $authInfo
Set-ConfValue -Path $worldConf -Key 'WorldDatabaseInfo'     -Value $worldInfo
Set-ConfValue -Path $worldConf -Key 'CharacterDatabaseInfo' -Value $charInfo
Set-ConfValue -Path $worldConf -Key 'DataDir'               -Value "`"$($server -replace '\\', '/')/Data`""
Set-ConfValue -Path $worldConf -Key 'LogsDir'               -Value "`"$($server -replace '\\', '/')/logs`""

# vmaps/mmaps so fazem sentido se foram extraidos
$dataDir = Join-Path $server 'Data'
$hasVmaps = Test-Path (Join-Path $dataDir 'vmaps')
$hasMmaps = Test-Path (Join-Path $dataDir 'mmaps')

Set-ConfValue -Path $worldConf -Key 'vmap.enableLOS'        -Value $(if ($hasVmaps) { '1' } else { '0' })
Set-ConfValue -Path $worldConf -Key 'vmap.enableHeight'     -Value $(if ($hasVmaps) { '1' } else { '0' })
Set-ConfValue -Path $worldConf -Key 'MoveMaps.Enable'       -Value $(if ($hasMmaps) { '1' } else { '0' })

if (-not $hasVmaps) { Write-Warn "vmaps ausentes - line-of-sight desligado (mobs enxergam atraves de parede)" }
if (-not $hasMmaps) { Write-Warn "mmaps ausentes - pathfinding desligado (mobs andam em linha reta)" }

# --- realmlist.wtf do client ----------------------------------------------
if (-not $SkipClient) {
    Write-Step "Apontando o client para $($settings.RealmAddress)"

    $client = $settings.ClientDir
    if (-not (Test-Path (Join-Path $client 'Wow.exe'))) {
        Write-Warn "ClientDir '$client' nao tem Wow.exe - pulando o realmlist.wtf"
    } else {
        $line = "set realmlist $($settings.RealmAddress)"
        $targets = @()

        # 3.3.5a le Data\<locale>\realmlist.wtf
        $dataPath = Join-Path $client 'Data'
        if (Test-Path $dataPath) {
            $locales = Get-ChildItem $dataPath -Directory -ErrorAction SilentlyContinue |
                       Where-Object { $_.Name -match '^[a-z]{2}[A-Z]{2}$' }
            foreach ($loc in $locales) { $targets += (Join-Path $loc.FullName 'realmlist.wtf') }
        }
        if (-not $targets) { $targets += (Join-Path $client 'realmlist.wtf') }

        foreach ($t in $targets) {
            Set-Content -LiteralPath $t -Value $line -Encoding ASCII
            Write-Ok ($t -replace [regex]::Escape($client), '<client>')
        }

        # o Battle.net antigo sobrescreve o realmlist; o Config.wtf tambem manda
        $configWtf = Join-Path $client 'WTF\Config.wtf'
        if (Test-Path $configWtf) {
            $cfg = Get-Content -Raw -LiteralPath $configWtf
            if ($cfg -match '(?m)^SET\s+realmList\s') {
                $cfg = [regex]::Replace($cfg, '(?m)^SET\s+realmList\s.*$', "SET realmList `"$($settings.RealmAddress)`"")
                Set-Content -LiteralPath $configWtf -Value $cfg -NoNewline -Encoding ASCII
                Write-Ok 'WTF\Config.wtf atualizado'
            }
        }
    }
}

Write-Host @"

    Configuracao pronta.

    Proximo passo - PRIMEIRO START (vai popular os bancos, demora ~10 min):
        .\scripts\start-server.ps1

    Deixe o worldserver terminar de aplicar os SQLs ate aparecer o prompt
    'AC>'. Depois, na mesma janela, crie sua conta:

        account create SEUUSER SUASENHA
        account set gmlevel SEUUSER 3 -1
"@ -ForegroundColor Gray
