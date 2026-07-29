--[[ =====================================================================
     Sistema de Prestigio - Bonus de Nivel Maximo

     Concedido de forma permanente a quem prestigia estando no nivel
     maximo. Quatro multiplicadores:

       1. Reputacao          hook 15  -> limpo, o core aceita o retorno
       2. Profissao          hook 61  -> limpo, o core aceita o retorno
       3. Recursos coletados hook 32  -> aceitavel, adicionamos copias
       4. Honra              SEM HOOK -> gambiarra por polling, leia abaixo

     O bonus e um estado booleano por personagem, nao acumulativo:
     prestigiar duas vezes no nivel maximo nao da 6x.
========================================================================]]

local CFG = Prestige.Config

-- =====================================================================
-- Cache do flag
-- =====================================================================
-- Cache proprio, separado do cache de contador do nucleo. Mesma regra:
-- o banco e a verdade, isto aqui e so para nao consultar em hook quente.
local bonusCache = {}

function Prestige.HasMaxLevelBonus(player)
    local guid = player:GetGUIDLow()
    local v = bonusCache[guid]
    if v == nil then
        local q = CharDBQuery(
            "SELECT max_level_bonus FROM character_prestige WHERE guid = " .. guid)
        v = (q and q:GetUInt32(0) == 1) or false
        bonusCache[guid] = v
    end
    return v
end

--- Concede o bonus. Chamado pelo fluxo de prestigio.
function Prestige.GrantMaxLevelBonus(guid)
    CharDBExecute(
        "UPDATE character_prestige SET max_level_bonus = 1 WHERE guid = " .. guid)
    bonusCache[guid] = true
    Prestige.Log(guid, "MAXLEVEL_BONUS", "concedido")
end

-- =====================================================================
-- 1. REPUTACAO - hook 15
-- =====================================================================
-- Assinatura: (event, player, factionId, standing, incremental)
-- Pode retornar novo standing.
--
-- ARMADILHA: retornar -1 faz o core BLOQUEAR o ganho inteiro. Se algum
-- calculo aqui der -1 por acidente, o jogador para de ganhar reputacao
-- e voce vai demorar para descobrir por que.
local function OnReputationChange(event, player, factionId, standing, incremental)
    if not Prestige.HasMaxLevelBonus(player) then return end

    -- So multiplicamos ganho incremental. Quando incremental e falso,
    -- standing e o valor absoluto sendo definido -- multiplicar isso
    -- catapultaria o jogador para exaltado de uma vez.
    if not incremental then return end
    if standing <= 0 then return end   -- perda de reputacao fica intacta

    local newStanding = math.floor(standing * CFG.MAXLEVEL_REP_MULT)
    if newStanding < 1 then newStanding = 1 end   -- nunca devolver 0 ou -1

    return newStanding
end

-- =====================================================================
-- 2. PROFISSAO - hook 61
-- =====================================================================
-- Assinatura: (event, player, skill_id, value, max, step)
-- Pode retornar novo valor.
--
-- O core sobe skill de 1 em 1 (step). Multiplicamos o passo, nao o
-- valor total -- multiplicar o total levaria um mineiro de 150 para
-- 450 num unico minerio.
local function OnBeforeUpdateSkill(event, player, skill_id, value, max, step)
    if not Prestige.PROFESSION_SKILLS[skill_id] then return end
    if not Prestige.HasMaxLevelBonus(player) then return end

    local bonusStep = math.floor((step or 1) * CFG.MAXLEVEL_SKILL_MULT)
    local newValue  = value + (bonusStep - (step or 1))

    if newValue > max then newValue = max end
    if newValue < value then newValue = value end

    if CFG.DEBUG then
        PrintInfo(string.format(
            "[Prestige] skill %d: %d -> %d (step %d, max %d)",
            skill_id, value, newValue, step or 1, max))
    end

    return newValue
end

-- =====================================================================
-- 3. RECURSOS COLETADOS - hook 32
-- =====================================================================
-- Assinatura: (event, player, item, count)
-- NAO aceita retorno. Entao nao "multiplicamos" o loot: adicionamos
-- copias extras com AddItem.
--
-- Isso e seguro contra loop infinito porque AddItem dispara
-- ON_STORE_NEW_ITEM (53), nunca ON_LOOT_ITEM (32). Se um dia voce
-- registrar algo no 53, revise isto.
local ITEM_CLASS_TRADE_GOODS = 7

local function OnLootItem(event, player, item, count)
    if not Prestige.HasMaxLevelBonus(player) then return end
    if not item then return end

    -- Filtro por classe do item. Trade Goods cobre minerio, ervas,
    -- couro, pano, po de encantamento e peixe de materia-prima.
    local ok, itemClass = pcall(function() return item:GetClass() end)
    if not ok or itemClass ~= ITEM_CLASS_TRADE_GOODS then return end

    local entry = item:GetEntry()
    if Prestige.RESOURCE_BLACKLIST[entry] then return end

    local extra = math.floor((count or 1) * CFG.MAXLEVEL_LOOT_MULT) - (count or 1)
    if extra <= 0 then return end

    -- Se a mochila estiver cheia, AddItem falha silenciosamente. Nao
    -- tratamos: perder o bonus e aceitavel, travar o loot nao e.
    pcall(function() player:AddItem(entry, extra) end)

    if CFG.DEBUG then
        PrintInfo(string.format("[Prestige] loot bonus: entry=%u +%d", entry, extra))
    end
end

-- =====================================================================
-- 4. HONRA - sem hook, polling de delta
-- =====================================================================
-- NAO EXISTE hook de ganho de honra no ALE. As alternativas eram:
--
--   a) hook de matar jogador (6) e estimar a honra -- perde honra de
--      objetivo de BG, de quest e de bonus de fim de partida;
--   b) comparar o total de honra periodicamente e creditar a diferenca.
--
-- Escolhi (b) porque pega TODAS as fontes. O preco: o jogador ve a
-- honra chegar em duas etapas, o valor base e o bonus alguns segundos
-- depois. E aparente, mas correto.
--
-- Se um dia o ALE ganhar um hook de honra, jogue isto fora sem dó.
local honorBaseline = {}

local function PollHonor()
    local players = GetPlayersInWorld()
    if not players then return end

    for _, player in pairs(players) do
        if Prestige.HasMaxLevelBonus(player) then
            local guid    = player:GetGUIDLow()
            local current = player:GetHonorPoints()
            local base    = honorBaseline[guid]

            if base == nil then
                honorBaseline[guid] = current      -- primeira leitura
            elseif current > base then
                local gained = current - base
                local extra  = math.floor(gained * CFG.MAXLEVEL_HONOR_MULT) - gained
                if extra > 0 then
                    player:ModifyHonorPoints(extra)
                end
                -- Atualiza a linha de base para o total DEPOIS do bonus.
                -- Sem isto, o proximo ciclo veria nosso proprio bonus
                -- como ganho novo e multiplicaria de novo, para sempre.
                honorBaseline[guid] = player:GetHonorPoints()
            elseif current < base then
                honorBaseline[guid] = current      -- gastou honra
            end
        end
    end
end

local function OnLoginHonor(event, player)
    honorBaseline[player:GetGUIDLow()] = player:GetHonorPoints()
end

local function OnLogoutClean(event, player)
    local guid = player:GetGUIDLow()
    honorBaseline[guid] = nil
    bonusCache[guid]    = nil
end

-- =====================================================================
-- Registro
-- =====================================================================
if CFG.MAXLEVEL_BONUS_ENABLED then
    RegisterPlayerEvent(15, OnReputationChange)
    RegisterPlayerEvent(61, OnBeforeUpdateSkill)
    RegisterPlayerEvent(32, OnLootItem)
    RegisterPlayerEvent(3,  OnLoginHonor)
    RegisterPlayerEvent(4,  OnLogoutClean)

    CreateLuaEvent(PollHonor, CFG.MAXLEVEL_HONOR_POLL_MS, 0)  -- 0 = repete sempre

    if CFG.DEBUG then PrintInfo("[Prestige] bonus de nivel maximo ativo") end
end
