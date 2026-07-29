--[[ =====================================================================
     Sistema de Prestigio - Configuracao
     Ordem de carga: 01 (os arquivos sao lidos em ordem alfabetica)
     =====================================================================

     Tudo que voce vai querer ajustar sem reler codigo mora aqui.
     Separar config de logica nao e enfeite: te deixa mudar numero de
     balanceamento e dar .reload eluna sem risco de quebrar a logica.
========================================================================]]

Prestige = Prestige or {}

Prestige.Config = {

    -- ---------------------------------------------------------------
    -- Elegibilidade
    -- ---------------------------------------------------------------
    MIN_LEVEL      = 30,
    MAX_LEVEL      = 79,

    -- Precisa ser true para o bonus de nivel maximo existir: sem isso
    -- um personagem 80 nem consegue prestigiar, e a recompensa fica
    -- inalcancavel.
    ALLOW_MAX_LEVEL = true,
    SERVER_MAX_LEVEL = 80,

    MAX_PRESTIGE   = 10,   -- teto. 0 = sem limite (nao recomendado)

    -- ---------------------------------------------------------------
    -- Multiplicador de XP
    -- ---------------------------------------------------------------
    -- Formula: 1 + (contador * XP_STEP), limitada por XP_CAP.
    --
    -- Com XP_STEP = 0.4 e MAX_PRESTIGE = 10 a curva fica:
    --   1 -> x1.4   4 -> x2.6   7 -> x3.8   10 -> x5.0
    --
    -- Os dois valores sao calibrados juntos: 1 + (10 * 0.4) = 5.0,
    -- exatamente o teto. Se voce mexer em MAX_PRESTIGE sem recalcular
    -- XP_STEP, os ultimos prestigios viram custo sem beneficio -- o
    -- jogador perde tudo e o multiplicador nao sobe.
    XP_STEP        = 0.4,
    XP_CAP         = 5.0,  -- teto do multiplicador final

    -- ---------------------------------------------------------------
    -- Icone / aura visual
    -- ---------------------------------------------------------------
    -- LEIA O README antes de mexer. Resumo: o cliente 3.3.5a so
    -- desenha icone de magia que ele ja conhece do proprio DBC.
    -- Magia customizada em spell_dbc NAO aparece sem patch de cliente.
    -- Entao a saida e reaproveitar uma magia existente e inofensiva.
    --
    -- 0 = desativado. Teste in-game com  .aura <id>  antes de fixar.
    AURA_SPELL_ID  = 0,

    -- ---------------------------------------------------------------
    -- Itens
    -- ---------------------------------------------------------------
    MAIL_SUBJECT   = "Pertences do Prestigio",
    MAIL_BODY      = "Seus pertences foram guardados durante a ascensao.",
    MAIL_DELAY     = 0,      -- segundos ate a carta chegar

    -- Teto de seguranca. Cada pilha vira UMA carta (limitacao da API).
    -- Acima disso, abortamos e mandamos o jogador limpar a mochila.
    MAX_ITEM_STACKS = 60,

    -- Banco NAO e esvaziado: ja e armazenamento seguro e permanente.
    INCLUDE_BANK   = false,

    -- ---------------------------------------------------------------
    -- Reset
    -- ---------------------------------------------------------------
    RESET_TALENTS     = true,
    RESET_ACHIEVEMENTS = true,

    -- Cuidado: apaga TODAS as magias e re-ensina as iniciais de classe.
    -- Sem isso, um personagem nivel 1 sai por ai com magias de nivel 80.
    -- Com isso, as magias de profissao tambem somem -- por isso o
    -- snapshot em character_prestige_skills.
    RESET_SPELLS      = true,

    -- ---------------------------------------------------------------
    -- Bonus de nivel maximo
    -- ---------------------------------------------------------------
    -- Concedido a quem prestigia estando no nivel maximo do servidor.
    -- E um estado booleano, NAO acumulativo: prestigiar duas vezes no
    -- 80 nao dobra os multiplicadores.
    MAXLEVEL_BONUS_ENABLED = true,

    MAXLEVEL_REP_MULT      = 3.0,   -- reputacao
    MAXLEVEL_SKILL_MULT    = 3.0,   -- pontos de profissao por sucesso
    MAXLEVEL_LOOT_MULT     = 3.0,   -- recursos coletados
    MAXLEVEL_HONOR_MULT    = 3.0,   -- honra de PvP

    -- Intervalo do polling de honra, em ms. Nao existe hook de honra
    -- no ALE, entao comparamos o total periodicamente. Menor = bonus
    -- mais imediato e mais consultas; maior = economia e atraso
    -- visivel. 5s e um meio-termo razoavel.
    MAXLEVEL_HONOR_POLL_MS = 5000,

    -- ---------------------------------------------------------------
    -- NPC
    -- ---------------------------------------------------------------
    NPC_ENTRY      = 190000,  -- entry do seu NPC em creature_template
    GOSSIP_TEXT_ID = 100,     -- npc_text.ID do cabecalho

    -- ---------------------------------------------------------------
    -- Debug
    -- ---------------------------------------------------------------
    DEBUG = true,
}

-- IDs de skill das profissoes (3.3.5a). Estes sao preservados.
Prestige.PROFESSION_SKILLS = {
    [171] = "Alquimia",       [164] = "Ferraria",
    [333] = "Encantamento",   [202] = "Engenharia",
    [182] = "Herbalismo",     [773] = "Escrita",
    [755] = "Joalheria",      [165] = "Couraria",
    [186] = "Mineracao",      [393] = "Esfolamento",
    [197] = "Alfaiataria",    [129] = "Primeiros Socorros",
    [185] = "Culinaria",      [356] = "Pesca",
    [762] = "Montaria",
}

-- Moedas especiais preservadas (entries de item). O ouro ja e
-- preservado por padrao: nao tocamos em character.money.
Prestige.PROTECTED_ITEMS = {
    -- [40752] = true,  -- exemplo: Emblem of Heroism
}

-- Itens da classe Trade Goods que NAO devem ser multiplicados pelo
-- bonus de coleta. Use para materiais de economia sensivel: se algum
-- material vira moeda no seu servidor, coloque aqui antes que o
-- mercado descubra.
Prestige.RESOURCE_BLACKLIST = {
    -- [22572] = true,  -- exemplo: Mote of Air
}

-- Flags at_login do core (AzerothCore, 3.3.5a)
Prestige.AT_LOGIN_RESET_SPELLS  = 0x08
Prestige.AT_LOGIN_RESET_TALENTS = 0x10
