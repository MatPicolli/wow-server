--[[ =====================================================================
     Sistema de Prestigio - NPC
     Gossip, validacao e confirmacao
========================================================================]]

local CFG = Prestige.Config

local OPT_INFO     = 1
local OPT_ASK      = 2
local OPT_CONFIRM  = 3
local OPT_CLOSE    = 4

-- =====================================================================
-- Validacao
-- =====================================================================
-- Regra: TUDO e validado antes de qualquer efeito colateral.
-- Depois que a primeira carta sai, nao existe desfazer.
-- Devolve: bool, motivo
function Prestige.CanPrestige(player)
    local level = player:GetLevel()

    if level < CFG.MIN_LEVEL then
        return false, string.format("Voce precisa ser nivel %d ou mais.", CFG.MIN_LEVEL)
    end
    if level > CFG.MAX_LEVEL then
        -- A faixa normal termina em MAX_LEVEL. O nivel maximo do
        -- servidor e a excecao: e la que mora o bonus permanente.
        if not (CFG.ALLOW_MAX_LEVEL and level >= CFG.SERVER_MAX_LEVEL) then
            return false, string.format(
                "Prestigie entre os niveis %d e %d, ou no nivel %d.",
                CFG.MIN_LEVEL, CFG.MAX_LEVEL, CFG.SERVER_MAX_LEVEL)
        end
    end

    local count = Prestige.GetCount(player)
    if CFG.MAX_PRESTIGE > 0 and count >= CFG.MAX_PRESTIGE then
        return false, "Voce ja atingiu o prestigio maximo."
    end

    -- Estados que tornam a operacao insegura. Cada um destes ja causou
    -- perda de item em algum servidor por alguem que nao checou.
    if player:IsInCombat() then
        return false, "Voce nao pode prestigiar em combate."
    end
    if player:GetTrader() then
        return false, "Finalize sua troca antes de prestigiar."
    end
    if player:InBattleground() or player:InArena() then
        return false, "Voce nao pode prestigiar em campo de batalha."
    end
    if player:IsInGroup() and player:GetMap():IsDungeon() then
        return false, "Saia da instancia antes de prestigiar."
    end
    if player:IsDead() then
        return false, "Voce precisa estar vivo."
    end

    -- Correio tem limite duro por jogador. Se a caixa ja esta cheia,
    -- as cartas somem silenciosamente -- que e o pior desfecho possivel.
    local mailCount = player:GetMailCount()
    if mailCount and mailCount > 60 then
        return false, "Sua caixa de correio esta muito cheia. Esvazie-a primeiro."
    end

    -- Conta as pilhas antes de mover qualquer coisa.
    local stacks = Prestige.CountItemStacks(player)
    if stacks > CFG.MAX_ITEM_STACKS then
        return false, string.format(
            "Voce tem %d pilhas de itens (maximo %d). Guarde parte no banco.",
            stacks, CFG.MAX_ITEM_STACKS)
    end

    return true, nil
end

-- =====================================================================
-- Menus
-- =====================================================================

local function SendMainMenu(player, creature)
    local count = Prestige.GetCount(player)
    local mult  = Prestige.GetMultiplier(player)

    player:GossipClearMenu()

    player:GossipMenuAddItem(0, string.format(
        "Prestigio atual: %d   (XP x%.1f)", count, mult), 0, OPT_INFO)

    -- Sinaliza a recompensa extra ANTES da escolha. Um jogador de
    -- nivel 78 precisa saber que esperar dois niveis muda o premio --
    -- descobrir depois seria motivo justo de revolta.
    if CFG.MAXLEVEL_BONUS_ENABLED and not Prestige.HasMaxLevelBonus(player) then
        if player:GetLevel() >= CFG.SERVER_MAX_LEVEL then
            player:GossipMenuAddItem(0,
                "|cff00ff00Ascender agora concede a BenCao do Apice.|r", 0, OPT_INFO)
        else
            player:GossipMenuAddItem(0, string.format(
                "|cffaaaaaaAscender no nivel %d concede a BenCao do Apice.|r",
                CFG.SERVER_MAX_LEVEL), 0, OPT_INFO)
        end
    end

    local ok, reason = Prestige.CanPrestige(player)
    if ok then
        player:GossipMenuAddItem(2, "Quero ascender.", 0, OPT_ASK)
    else
        player:GossipMenuAddItem(0, "|cff888888" .. reason .. "|r", 0, OPT_INFO)
    end

    player:GossipMenuAddItem(0, "Nada por enquanto.", 0, OPT_CLOSE)
    player:GossipSendMenu(CFG.GOSSIP_TEXT_ID, creature, 0)
end

local function SendConfirmMenu(player, creature)
    player:GossipClearMenu()

    -- Confirmacao com popup. O sexto parametro do GossipMenuAddItem e
    -- o texto do popup: forca o jogador a ler e clicar de novo.
    -- Em operacao irreversivel, uma janela de confirmacao nao e
    -- burocracia, e a diferenca entre um ticket e vinte.
    local warn =
        "ATENCAO - ISTO NAO PODE SER DESFEITO.\n\n" ..
        "- Seu nivel voltara para 1\n" ..
        "- Suas quests e conquistas serao apagadas\n" ..
        "- Equipamento e mochila irao para seu correio\n" ..
        "- ENCANTAMENTOS E GEMAS SERAO PERDIDOS\n\n" ..
        "Ouro e profissoes sao preservados.\n\n" ..
        "Confirma?"

    player:GossipMenuAddItem(0, "Sim, eu aceito recomecar.", 0, OPT_CONFIRM, false, warn)
    player:GossipMenuAddItem(0, "Nao, mudei de ideia.", 0, OPT_CLOSE)
    player:GossipSendMenu(CFG.GOSSIP_TEXT_ID, creature, 0)
end

-- =====================================================================
-- Hooks de gossip
-- =====================================================================

local function OnGossipHello(event, player, creature)
    SendMainMenu(player, creature)
    return true
end

local function OnGossipSelect(event, player, creature, sender, intid, code)
    if intid == OPT_ASK then
        -- Revalida. O menu foi montado ha alguns segundos e o jogador
        -- pode ter entrado em combate nesse intervalo. Nunca confie no
        -- estado capturado quando o menu foi desenhado.
        local ok, reason = Prestige.CanPrestige(player)
        if not ok then
            player:SendBroadcastMessage("|cffff0000" .. reason .. "|r")
            player:GossipComplete()
            return true
        end
        SendConfirmMenu(player, creature)

    elseif intid == OPT_CONFIRM then
        local ok, reason = Prestige.CanPrestige(player)   -- terceira e ultima checagem
        if not ok then
            player:SendBroadcastMessage("|cffff0000" .. reason .. "|r")
            player:GossipComplete()
            return true
        end
        player:GossipComplete()
        Prestige.Execute(player)          -- ver 04_prestige_wipe.lua

    else
        player:GossipComplete()
    end

    return true
end

RegisterCreatureGossipEvent(CFG.NPC_ENTRY, 1, OnGossipHello)
RegisterCreatureGossipEvent(CFG.NPC_ENTRY, 2, OnGossipSelect)
