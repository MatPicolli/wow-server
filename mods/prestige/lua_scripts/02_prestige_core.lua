--[[ =====================================================================
     Sistema de Prestigio - Nucleo
     Camada de dados + cache + multiplicador de XP + aura
========================================================================]]

local CFG = Prestige.Config

-- =====================================================================
-- Cache
-- =====================================================================
-- Por que cache: o hook de XP dispara a cada mob morto. Consultar o
-- banco de forma sincrona ali dentro travaria a thread do mapa.
--
-- Por que o cache NAO e a fonte da verdade: em multistate cada mapa
-- roda seu proprio estado Lua. O cache do mapa A nao existe no mapa B.
-- Por isso ele e populado no login e descartado no logout, e toda
-- escrita vai para o banco imediatamente.
local cache = {}

local function debug(msg)
    if CFG.DEBUG then PrintInfo("[Prestige] " .. tostring(msg)) end
end

local function log(guid, event, detail)
    CharDBExecute(string.format(
        "INSERT INTO character_prestige_log (guid, at, event, detail) VALUES (%u, %u, '%s', '%s')",
        guid, os.time(), event, (detail or ""):gsub("'", "")
    ))
end
Prestige.Log = log

-- =====================================================================
-- Acesso a dados
-- =====================================================================

--- Le o contador do banco. Use apenas fora de caminhos quentes.
function Prestige.LoadCount(guid)
    local q = CharDBQuery(
        "SELECT prestige_count FROM character_prestige WHERE guid = " .. guid)
    -- Consulta sem resultado devolve nil, nao uma query vazia.
    -- Esquecer essa checagem e o erro numero um aqui.
    if not q then return 0 end
    return q:GetUInt32(0)
end

--- Grava o contador (upsert).
function Prestige.SaveCount(guid, accountId, count)
    CharDBExecute(string.format([[
        INSERT INTO character_prestige (guid, account, prestige_count, last_prestige)
        VALUES (%u, %u, %u, %u)
        ON DUPLICATE KEY UPDATE prestige_count = %u, last_prestige = %u
    ]], guid, accountId or 0, count, os.time(), count, os.time()))
    cache[guid] = count
end

--- Leitura barata, para hooks quentes.
function Prestige.GetCount(player)
    local guid = player:GetGUIDLow()
    local c = cache[guid]
    if c == nil then
        c = Prestige.LoadCount(guid)
        cache[guid] = c
    end
    return c
end

--- Multiplicador efetivo de XP.
function Prestige.GetMultiplier(player)
    local count = Prestige.GetCount(player)
    if count <= 0 then return 1.0 end
    local mult = 1.0 + (count * CFG.XP_STEP)
    if CFG.XP_CAP > 0 and mult > CFG.XP_CAP then mult = CFG.XP_CAP end
    return mult
end

-- =====================================================================
-- Aura visual
-- =====================================================================
-- Auras aplicadas por script nao sobrevivem a relog de forma confiavel,
-- entao reaplicamos no login. Ideia central: a aura e puramente
-- cosmetica. O bonus real vem do hook de XP, nunca da aura. Se a aura
-- falhar, o jogador perde o icone -- nao o beneficio.
function Prestige.ApplyAura(player)
    if CFG.AURA_SPELL_ID == 0 then return end
    if Prestige.GetCount(player) <= 0 then return end

    -- pcall porque AddAura pode variar entre builds do ALE e uma
    -- falha aqui nao pode derrubar o login do jogador.
    local ok, err = pcall(function()
        local aura = player:AddAura(CFG.AURA_SPELL_ID, player)
        if aura then
            aura:SetMaxDuration(-1)
            aura:SetDuration(-1)
        end
    end)
    if not ok then
        PrintError("[Prestige] Falha ao aplicar aura: " .. tostring(err))
    end
end

-- =====================================================================
-- HOOK 12 - PLAYER_EVENT_ON_GIVE_XP
-- =====================================================================
-- Este hook E o multiplicador. Nada de DBC, nada de patch de cliente.
-- Ele pode retornar um novo valor de XP e o core usa o retorno.
--
-- Retornar nil = "nao mexi", o core segue com o valor original.
local function OnGiveXP(event, player, amount, victim, source)
    local mult = Prestige.GetMultiplier(player)
    if mult <= 1.0 then return end

    local newAmount = math.floor(amount * mult)

    -- Guarda contra estouro: XP e uint32 no core. Um multiplicador
    -- alto sobre um valor ja grande vira comportamento indefinido.
    if newAmount > 4000000000 then newAmount = 4000000000 end

    return newAmount
end

-- =====================================================================
-- HOOK 3 - PLAYER_EVENT_ON_LOGIN
-- =====================================================================
local function OnLogin(event, player)
    local guid = player:GetGUIDLow()
    cache[guid] = Prestige.LoadCount(guid)

    -- Retomada de crash: existe prestigio pendente?
    local pend = CharDBQuery(
        "SELECT stage, old_level FROM character_prestige_pending WHERE guid = " .. guid)
    if pend then
        local stage = pend:GetString(0)
        PrintError(string.format(
            "[Prestige] GUID %u logou com prestigio pendente no estagio '%s'. Retomando.",
            guid, stage))
        log(guid, "RESUME", stage)
        -- Prestige.Resume vive em 04_prestige_wipe.lua
        if Prestige.Resume then Prestige.Resume(player, stage, pend:GetUInt32(1)) end
        return
    end

    Prestige.ApplyAura(player)

    local c = cache[guid]
    if c > 0 then
        player:SendBroadcastMessage(string.format(
            "|cff00ccffPrestigio %d|r - XP recebido x%.1f",
            c, Prestige.GetMultiplier(player)))
    end
end

-- =====================================================================
-- HOOK 4 - PLAYER_EVENT_ON_LOGOUT
-- =====================================================================
-- Higiene de memoria. Sem isso, o cache cresce indefinidamente ao
-- longo de dias de uptime.
local function OnLogout(event, player)
    cache[player:GetGUIDLow()] = nil
end

RegisterPlayerEvent(12, OnGiveXP)
RegisterPlayerEvent(3,  OnLogin)
RegisterPlayerEvent(4,  OnLogout)

debug("nucleo carregado")
