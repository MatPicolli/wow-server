# =============================================================================
#  Configuracao do servidor - COPIE ESTE ARQUIVO PARA settings.psd1 E EDITE
#
#      Copy-Item config\settings.example.psd1 config\settings.psd1
#
#  settings.psd1 esta no .gitignore (contem senhas), entao nunca vai pro git.
# =============================================================================

@{
    # -------------------------------------------------------------------------
    # Pastas. Deixe fora de "Program Files" e de pastas sincronizadas
    # (OneDrive/Dropbox) - o build gera dezenas de milhares de arquivos.
    # -------------------------------------------------------------------------
    Root      = 'C:\AzerothCore'
    SourceDir = 'C:\AzerothCore\source'   # codigo-fonte clonado
    BuildDir  = 'C:\AzerothCore\build'    # saida do CMake/MSVC
    ServerDir = 'C:\AzerothCore\server'   # servidor pronto pra rodar
    BoostDir  = 'C:\local\boost'          # onde o Boost sera instalado

    # -------------------------------------------------------------------------
    # OBRIGATORIO: caminho do seu client 3.3.5a (a pasta que tem Wow.exe).
    # -------------------------------------------------------------------------
    ClientDir = 'C:\Games\World of Warcraft 3.3.5a'

    # -------------------------------------------------------------------------
    # De onde vem o codigo-fonte do core.
    #
    # Alguns mods de bot nao sao modulos: eles precisam de alteracoes no
    # proprio core e por isso vivem num fork. Trocar aqui e o jeito certo -
    # assim o 02-clone-source.ps1 nao te devolve pro repositorio antigo na
    # proxima atualizacao.
    #
    #   AzerothCore oficial (padrao):
    #     'https://github.com/azerothcore/azerothcore-wotlk.git'  branch 'master'
    #
    #   Playerbots (bots que jogam como personagens de verdade):
    #     'https://github.com/mod-playerbots/azerothcore-wotlk.git'  branch 'Playerbot'
    #
    #   NPCBots (bots contratados como companheiros):
    #     'https://github.com/trickerer/AzerothCore-wotlk-with-NPCBots.git'  branch 'npcbots_3.3.5'
    #
    # Trocar de repositorio exige clonar de novo e recompilar:
    #     .\scripts\02-clone-source.ps1 -Force
    # -------------------------------------------------------------------------
    SourceRepository = 'https://github.com/azerothcore/azerothcore-wotlk.git'
    SourceBranch     = 'master'

    # -------------------------------------------------------------------------
    # Banco de dados
    # -------------------------------------------------------------------------
    MySql = @{
        Host     = '127.0.0.1'
        Port     = 3306
        RootUser = 'root'      # a senha do root e pedida na hora, nao fica salva
        User     = 'acore'
        Password = 'acore'     # troque se for expor o servidor pra fora da LAN
        AuthDb   = 'acore_auth'
        WorldDb  = 'acore_world'
        CharDb   = 'acore_characters'
        PlayerbotsDb = 'acore_playerbots'   # so o fork do Playerbots usa
    }

    # -------------------------------------------------------------------------
    # Realm
    #
    # RealmAddress e o endereco que o CLIENT usa pra conectar no worldserver.
    #   - so voce, na mesma maquina .............. 127.0.0.1
    #   - outros PCs da sua casa ................. seu IP local (ex: 192.168.0.10)
    #   - internet ............................... seu IP publico ou DNS
    # -------------------------------------------------------------------------
    RealmName    = 'Meu Servidor'
    RealmAddress = '127.0.0.1'

    # -------------------------------------------------------------------------
    # Build
    #   BuildConfig: RelWithDebInfo (recomendado) | Release | Debug
    #   Threads: 0 = usa todos os nucleos disponiveis
    # -------------------------------------------------------------------------
    BuildConfig = 'RelWithDebInfo'
    Threads     = 0

    # -------------------------------------------------------------------------
    # Extracao de dados do client
    #   ExtractMmaps = $false pula os mmaps (economiza horas, mas os mobs
    #   ficam sem pathfinding - andam atravessando parede). Deixe $true.
    # -------------------------------------------------------------------------
    ExtractVmaps = $true
    ExtractMmaps = $true
}
