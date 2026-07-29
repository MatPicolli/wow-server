--[[ =====================================================================
     Sistema de Prestigio - Ponte para o addon de cliente

     Envia o contador e o multiplicador para o addon PrestigeUI.
     Se o jogador nao tiver o addon, estas mensagens sao simplesmente
     ignoradas pelo cliente. Nenhum efeito colateral.
========================================================================]]

local CFG = Prestige.Config

local ADDON_PREFIX = "PRESTIGE"

-- Canal da mensagem de addon. 6 = whisper na enum ChatMsg do core.
-- Se o addon nao receber nada (ative DEBUG nele para ver), teste
-- outros valores: 0 (say) e 7 costumam ser os proximos candidatos.
-- Este e o unico numero deste mod que eu nao consegui confirmar na
-- documentacao -- trate-o como suspeito numero um se algo falhar.
local ADDON_CHANNEL = 6

-- Atraso antes do primeiro envio. O addon precisa ter terminado de
-- carregar e registrado CHAT_MSG_ADDON. Mandar no instante do login
-- e mandar para o vazio.
local FIRST_SEND_DELAY = 5000

--- Envia o estado atual para o cliente.
function Prestige.SendUI(player)
    if not player then return end

    local count = Prestige.GetCount(player)
    local mult  = Prestige.GetMultiplier(player)

    -- Formato: P:<contador>:<multiplicador>:<bonus>
    -- Mantido curto de proposito. Mensagem de addon tem limite de
    -- tamanho e nao vale gastar bytes com JSON para tres numeros.
    -- O campo de bonus e opcional no parser do addon, entao versoes
    -- antigas do cliente continuam funcionando.
    local bonus = 0
    if Prestige.HasMaxLevelBonus and Prestige.HasMaxLevelBonus(player) then
        bonus = 1
    end

    local payload = string.format("P:%d:%.1f:%d", count, mult, bonus)

    -- pcall porque a assinatura de SendAddonMessage pode variar entre
    -- builds do ALE, e uma falha aqui nao pode derrubar o login.
    local ok, err = pcall(function()
        player:SendAddonMessage(ADDON_PREFIX, payload, ADDON_CHANNEL, player)
    end)

    if not ok and CFG.DEBUG then
        PrintError("[Prestige] SendAddonMessage falhou: " .. tostring(err))
    end
end

-- Envio no login, com atraso.
local function OnLoginSendUI(event, player)
    local guid = player:GetGUIDLow()

    CreateLuaEvent(function()
        -- Reobtem o jogador: em 5 segundos ele pode ter deslogado, e
        -- guardar a referencia antiga seria um ponteiro para o nada.
        local p = GetPlayerByGUID(guid)
        if p then Prestige.SendUI(p) end
    end, FIRST_SEND_DELAY, 1)
end

RegisterPlayerEvent(3, OnLoginSendUI)

if CFG.DEBUG then PrintInfo("[Prestige] ponte de addon carregada") end
