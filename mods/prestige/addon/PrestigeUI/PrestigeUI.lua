--[[ =====================================================================
     Prestige UI - addon de cliente para WoW 3.3.5a

     Indicador arrastavel que muda de aparencia conforme o nivel de
     prestigio. Nao contem logica de jogo: se o addon nao estiver
     instalado, o jogador continua recebendo o bonus normalmente.
========================================================================]]

local PREFIX = "PRESTIGE"
local DEBUG  = false   -- true imprime toda mensagem de addon recebida

-- =====================================================================
-- Tiers
-- =====================================================================
-- Cada faixa tem icone, nome e cor propria. `min` e o prestigio
-- minimo para entrar na faixa; a tabela e lida de tras para frente,
-- entao basta manter em ordem crescente.
--
-- Os icones sao da mesma familia (INV_Misc_Rune_XX) de proposito:
-- arte consistente, progressao legivel. Troque a vontade -- qualquer
-- caminho valido de Interface\Icons serve.
--
-- A COR e aplicada por SetVertexColor na moldura, o que funciona
-- independente do icone existir. Se um caminho estiver errado, voce
-- perde a arte mas mantem a progressao visual. Redundancia barata.
local TIERS = {
    { min = 1,  name = "Iniciado",  icon = "Interface\\Icons\\INV_Misc_Rune_01",
      r = 0.80, g = 0.52, b = 0.25 },   -- bronze
    { min = 3,  name = "Ascendente", icon = "Interface\\Icons\\INV_Misc_Rune_02",
      r = 0.75, g = 0.75, b = 0.78 },   -- prata
    { min = 5,  name = "Exaltado",  icon = "Interface\\Icons\\INV_Misc_Rune_04",
      r = 1.00, g = 0.82, b = 0.00 },   -- ouro
    { min = 7,  name = "Venerado",  icon = "Interface\\Icons\\INV_Misc_Rune_05",
      r = 0.30, g = 0.60, b = 1.00 },   -- azul
    { min = 10, name = "Eterno",    icon = "Interface\\Icons\\INV_Misc_Rune_06",
      r = 0.70, g = 0.35, b = 1.00 },   -- roxo
}

local FALLBACK_ICON = "Interface\\Icons\\INV_Misc_QuestionMark"

local function GetTier(count)
    local tier = TIERS[1]
    for i = 1, #TIERS do
        if count >= TIERS[i].min then tier = TIERS[i] end
    end
    return tier
end

-- Estado local, alimentado pelo servidor
local prestigeCount = 0
local xpMultiplier  = 1.0
local maxLevelBonus = false
local hasData       = false

-- =====================================================================
-- Frame
-- =====================================================================
local f = CreateFrame("Frame", "PrestigeUIFrame", UIParent)
f:SetWidth(36)
f:SetHeight(36)
f:SetMovable(true)
f:EnableMouse(true)
f:RegisterForDrag("LeftButton")
f:SetClampedToScreen(true)
f:Hide()
f:SetPoint("TOPRIGHT", UIParent, "TOPRIGHT", -30, -240)

local icon = f:CreateTexture(nil, "ARTWORK")
icon:SetAllPoints(f)
icon:SetTexCoord(0.08, 0.92, 0.08, 0.92)   -- recorta a borda transparente

local border = f:CreateTexture(nil, "OVERLAY")
border:SetTexture("Interface\\Buttons\\UI-Quickslot2")
border:SetWidth(58)
border:SetHeight(58)
border:SetPoint("CENTER", f, "CENTER", 0, -1)

-- Brilho, so no tier maximo. Fica atras do icone.
local glow = f:CreateTexture(nil, "BACKGROUND")
glow:SetTexture("Interface\\SpellActivationOverlay\\IconAlert")
glow:SetWidth(70)
glow:SetHeight(70)
glow:SetPoint("CENTER", f, "CENTER", 0, 0)
glow:SetBlendMode("ADD")
glow:Hide()

local label = f:CreateFontString(nil, "OVERLAY", "NumberFontNormal")
label:SetPoint("BOTTOMRIGHT", f, "BOTTOMRIGHT", 2, 0)

-- Marca da Bencao do Apice: estrela no canto superior esquerdo.
-- Fica fora do icone principal de proposito -- o tier e o bonus sao
-- conquistas independentes e nao devem competir pelo mesmo espaco.
local blessing = f:CreateTexture(nil, "OVERLAY")
blessing:SetTexture("Interface\\Common\\ReputationStar")
blessing:SetWidth(16)
blessing:SetHeight(16)
blessing:SetPoint("TOPLEFT", f, "TOPLEFT", -4, 4)
blessing:SetVertexColor(0.3, 1, 0.3)
blessing:Hide()

-- =====================================================================
-- Pulso do tier maximo
-- =====================================================================
-- Alpha oscilando com seno. Sem AnimationSystem porque o 3.3.5a
-- ainda nao tem -- OnUpdate e o caminho da epoca.
local elapsedTotal = 0
local function GlowPulse(self, elapsed)
    elapsedTotal = elapsedTotal + (elapsed or arg1 or 0)
    glow:SetAlpha(0.35 + 0.25 * math.sin(elapsedTotal * 2))
end

-- =====================================================================
-- Atualizacao
-- =====================================================================
local function Refresh()
    if not hasData or prestigeCount <= 0 then
        f:Hide()
        f:SetScript("OnUpdate", nil)
        return
    end

    local tier = GetTier(prestigeCount)

    icon:SetTexture(tier.icon)
    -- Se o caminho nao existir, GetTexture volta nil e caimos no
    -- fallback. Melhor um ponto de interrogacao que um quadrado vazio.
    if not icon:GetTexture() then icon:SetTexture(FALLBACK_ICON) end

    border:SetVertexColor(tier.r, tier.g, tier.b)
    label:SetText(prestigeCount)
    label:SetTextColor(tier.r, tier.g, tier.b)

    if maxLevelBonus then blessing:Show() else blessing:Hide() end

    -- Brilho pulsante reservado ao tier final: se tudo brilha, nada
    -- se destaca.
    if prestigeCount >= TIERS[#TIERS].min then
        glow:SetVertexColor(tier.r, tier.g, tier.b)
        glow:Show()
        f:SetScript("OnUpdate", GlowPulse)
    else
        glow:Hide()
        f:SetScript("OnUpdate", nil)
    end

    f:Show()
end

local function RestorePosition()
    if PrestigeUIDB.point then
        f:ClearAllPoints()
        f:SetPoint(PrestigeUIDB.point, UIParent, PrestigeUIDB.relPoint,
                   PrestigeUIDB.x, PrestigeUIDB.y)
    end
end

-- =====================================================================
-- Arrastar
-- =====================================================================
f:SetScript("OnDragStart", function()
    if not PrestigeUIDB.locked then f:StartMoving() end
end)

f:SetScript("OnDragStop", function()
    f:StopMovingOrSizing()
    local point, _, relPoint, x, y = f:GetPoint()
    PrestigeUIDB.point, PrestigeUIDB.relPoint = point, relPoint
    PrestigeUIDB.x, PrestigeUIDB.y = x, y
end)

-- =====================================================================
-- Tooltip
-- =====================================================================
f:SetScript("OnEnter", function()
    local tier = GetTier(prestigeCount)
    GameTooltip:SetOwner(f, "ANCHOR_LEFT")
    GameTooltip:AddLine(tier.name, tier.r, tier.g, tier.b)
    GameTooltip:AddLine("Prestigio " .. prestigeCount, 1, 1, 1)
    GameTooltip:AddLine("Experiencia recebida: x"
        .. string.format("%.1f", xpMultiplier), 0.3, 1, 0.3)

    if maxLevelBonus then
        GameTooltip:AddLine(" ")
        GameTooltip:AddLine("Bencao do Apice", 0.3, 1, 0.3)
        GameTooltip:AddLine("x3 reputacao", 1, 1, 1)
        GameTooltip:AddLine("x3 honra de PvP", 1, 1, 1)
        GameTooltip:AddLine("x3 pontos de profissao", 1, 1, 1)
        GameTooltip:AddLine("x3 recursos coletados", 1, 1, 1)
    end

    GameTooltip:AddLine(" ")
    GameTooltip:AddLine("Arraste para mover.", 0.6, 0.6, 0.6)
    GameTooltip:AddLine("/prestige lock  para travar.", 0.6, 0.6, 0.6)
    GameTooltip:Show()
end)

f:SetScript("OnLeave", function() GameTooltip:Hide() end)

-- =====================================================================
-- Eventos
-- =====================================================================
f:RegisterEvent("ADDON_LOADED")
f:RegisterEvent("CHAT_MSG_ADDON")

f:SetScript("OnEvent", function(self, event, a1, a2)
    -- No 3.3.5a alguns contextos entregam os argumentos como globais
    -- arg1..argN. Normalizamos para funcionar dos dois jeitos.
    event = event or _G.event
    a1    = a1    or _G.arg1
    a2    = a2    or _G.arg2

    if event == "ADDON_LOADED" then
        if a1 ~= "PrestigeUI" then return end
        PrestigeUIDB = PrestigeUIDB or {}
        RestorePosition()
        return
    end

    if event == "CHAT_MSG_ADDON" then
        if DEBUG then
            DEFAULT_CHAT_FRAME:AddMessage("|cff888888[PrestigeUI] "
                .. tostring(a1) .. " / " .. tostring(a2) .. "|r")
        end
        if a1 ~= PREFIX then return end

        -- O campo de bonus e opcional: se o servidor for de uma versao
        -- anterior, o addon ainda funciona sem ele.
        local count, mult, bonus = string.match(a2 or "", "^P:(%d+):([%d%.]+):?(%d*)$")
        if not count then return end

        prestigeCount = tonumber(count) or 0
        xpMultiplier  = tonumber(mult)  or 1.0
        maxLevelBonus = (bonus == "1")
        hasData       = true
        Refresh()
    end
end)

-- =====================================================================
-- Comandos
-- =====================================================================
SLASH_PRESTIGE1 = "/prestige"
SlashCmdList["PRESTIGE"] = function(msg)
    msg = string.lower(msg or "")
    local cmd, arg = string.match(msg, "^(%a*)%s*(%d*)$")

    if cmd == "lock" then
        PrestigeUIDB.locked = true
        DEFAULT_CHAT_FRAME:AddMessage("|cff00ccffPrestigio:|r icone travado.")

    elseif cmd == "unlock" then
        PrestigeUIDB.locked = false
        DEFAULT_CHAT_FRAME:AddMessage("|cff00ccffPrestigio:|r icone destravado.")

    elseif cmd == "reset" then
        PrestigeUIDB.point = nil
        f:ClearAllPoints()
        f:SetPoint("TOPRIGHT", UIParent, "TOPRIGHT", -30, -240)
        DEFAULT_CHAT_FRAME:AddMessage("|cff00ccffPrestigio:|r posicao restaurada.")

    elseif cmd == "test" then
        -- /prestige test 7  mostra o tier 7 sem envolver o servidor.
        -- Use para conferir os cinco visuais antes de liberar.
        local n = tonumber(arg) or 3
        prestigeCount = n
        xpMultiplier  = math.min(1 + n * 0.4, 5.0)
        maxLevelBonus = not maxLevelBonus   -- alterna a cada teste
        hasData       = true
        Refresh()
        DEFAULT_CHAT_FRAME:AddMessage("|cff00ccffPrestigio:|r teste nivel "
            .. n .. " (" .. GetTier(n).name .. ")")

    else
        DEFAULT_CHAT_FRAME:AddMessage("|cff00ccffPrestigio:|r /prestige lock | unlock | reset | test <n>")
    end
end
