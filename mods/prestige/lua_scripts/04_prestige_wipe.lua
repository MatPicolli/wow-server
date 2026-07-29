--[[ =====================================================================
     Sistema de Prestigio - Execucao (parte destrutiva)

     Ordem sagrada das operacoes:
       1. valida            (feito no NPC)
       2. grava WAL         <- ponto sem volta registrado no banco
       3. snapshot profissoes
       4. envia itens por correio
       5. remove itens do inventario
       6. reseta nivel / quests / conquistas
       7. restaura profissoes
       8. incrementa contador, limpa WAL
       9. forca relogin

     Se cair entre 2 e 8, o login seguinte encontra o WAL e retoma.
========================================================================]]

local CFG = Prestige.Config

-- Slots do 3.3.5a. Bag 255 = "inventario base" (equipado + mochila).
local EQUIP_START, EQUIP_END       = 0, 18    -- 19 slots equipados
local BACKPACK_START, BACKPACK_END = 23, 38   -- 16 slots da mochila
local BAG_START, BAG_END           = 19, 22   -- 4 slots de bolsa
local MAX_BAG_SLOTS                = 36

-- =====================================================================
-- Varredura de inventario
-- =====================================================================
--- Percorre tudo que sera evacuado e chama fn(item, bag, slot).
--- Nao modifica nada -- e usada tanto pela contagem quanto pelo dry run
--- quanto pela execucao real. Uma varredura so, tres usos.
local function ForEachItem(player, fn)
    for slot = EQUIP_START, EQUIP_END do
        local it = player:GetItemByPos(255, slot)
        if it then fn(it, 255, slot) end
    end

    for slot = BACKPACK_START, BACKPACK_END do
        local it = player:GetItemByPos(255, slot)
        if it then fn(it, 255, slot) end
    end

    -- Conteudo das bolsas primeiro; as bolsas em si sao itens tambem e
    -- entram na varredura de 255 acima. Esvaziar o conteudo antes de
    -- remover o continente evita perder o que estava dentro.
    for bag = BAG_START, BAG_END do
        for slot = 0, MAX_BAG_SLOTS - 1 do
            local it = player:GetItemByPos(bag, slot)
            if it then fn(it, bag, slot) end
        end
    end
end

--- Conta pilhas que serao enviadas (ignora as protegidas).
function Prestige.CountItemStacks(player)
    local n = 0
    ForEachItem(player, function(item)
        if not Prestige.PROTECTED_ITEMS[item:GetEntry()] then n = n + 1 end
    end)
    return n
end

--- Modo seguro: so LOGA o que seria enviado. Use antes de confiar.
function Prestige.DryRun(player)
    local n = 0
    ForEachItem(player, function(item, bag, slot)
        local entry = item:GetEntry()
        if Prestige.PROTECTED_ITEMS[entry] then
            PrintInfo(string.format("  [PROTEGIDO] entry=%u", entry))
        else
            n = n + 1
            PrintInfo(string.format("  carta %d: entry=%u count=%u (bag=%d slot=%d)",
                n, entry, item:GetCount(), bag, slot))
        end
    end)
    PrintInfo(string.format("[Prestige] DRY RUN: %d cartas seriam enviadas.", n))
    return n
end

-- =====================================================================
-- Etapa: correio
-- =====================================================================
--- Envia tudo por correio e remove do inventario.
--- Ordem dentro do loop: manda a carta PRIMEIRO, so entao remove.
--- Se a carta falhar, o item continua com o jogador. O inverso
--- perderia o item.
local function MailAndStrip(player)
    local guid = player:GetGUIDLow()
    local sent, failed = 0, 0

    local pending = {}
    ForEachItem(player, function(item)
        local entry = item:GetEntry()
        if not Prestige.PROTECTED_ITEMS[entry] then
            table.insert(pending, { entry = entry, count = item:GetCount() })
        end
    end)

    for _, e in ipairs(pending) do
        -- SendMail(subject, text, receiver, sender, stationary, delay,
        --          money, cod, entry, amount)
        -- Um item por carta: a API so aceita um par entry/amount.
        local ok = pcall(function()
            SendMail(CFG.MAIL_SUBJECT, CFG.MAIL_BODY, guid, guid,
                     0, CFG.MAIL_DELAY, 0, 0, e.entry, e.count)
        end)

        if ok then
            sent = sent + 1
            -- So remove depois que a carta existe.
            player:RemoveItem(e.entry, e.count)
        else
            failed = failed + 1
            PrintError(string.format(
                "[Prestige] GUID %u: falha ao enviar entry=%u", guid, e.entry))
        end
    end

    Prestige.Log(guid, "MAILED", string.format("enviadas=%d falhas=%d", sent, failed))
    return sent, failed
end

-- =====================================================================
-- Etapa: snapshot e restauracao de profissoes
-- =====================================================================
local function SnapshotProfessions(player)
    local guid = player:GetGUIDLow()
    CharDBExecute("DELETE FROM character_prestige_skills WHERE guid = " .. guid)

    for skillId, _ in pairs(Prestige.PROFESSION_SKILLS) do
        if player:HasSkill(skillId) then
            CharDBExecute(string.format(
                "INSERT INTO character_prestige_skills (guid, skill, value, max) VALUES (%u, %u, %u, %u)",
                guid, skillId,
                player:GetSkillValue(skillId),
                player:GetMaxSkillValue(skillId)))
        end
    end
end

--- Chamada no login seguinte, quando as flags at_login ja rodaram.
local function RestoreProfessions(player)
    local guid = player:GetGUIDLow()
    local q = CharDBQuery(
        "SELECT skill, value, max FROM character_prestige_skills WHERE guid = " .. guid)
    if not q then return end

    repeat
        local skill, value, maxv = q:GetUInt32(0), q:GetUInt32(1), q:GetUInt32(2)
        pcall(function() player:SetSkill(skill, 0, value, maxv) end)
    until not q:NextRow()

    CharDBExecute("DELETE FROM character_prestige_skills WHERE guid = " .. guid)
    Prestige.Log(guid, "PROF_RESTORED", "")
end

-- =====================================================================
-- Etapa: reset
-- =====================================================================
local function WipeProgress(player)
    local guid = player:GetGUIDLow()

    -- Quests. Percorremos os slots do log ativo e a tabela de
    -- completadas via SQL. RemoveQuest so alcanca o log ativo.
    for slot = 0, 24 do
        local questId = player:GetQuestSlotQuestId(slot)
        if questId and questId > 0 then
            pcall(function() player:RemoveQuest(questId) end)
        end
    end
    -- As quests ja entregues vivem em character_queststatus_rewarded.
    -- Sem apagar isso, o jogador nao pode refazer nada.
    CharDBExecute("DELETE FROM character_queststatus          WHERE guid = " .. guid)
    CharDBExecute("DELETE FROM character_queststatus_rewarded WHERE guid = " .. guid)

    if CFG.RESET_ACHIEVEMENTS then
        pcall(function() player:ResetAchievements() end)
    end

    if CFG.RESET_TALENTS then
        pcall(function() player:ResetTalents(true) end)
    end

    -- Nivel por ultimo: mexer no nivel invalida caches de stat.
    player:SetLevel(1)

    -- [ajuste local] Skills que escalam com o nivel.
    --
    -- Tem que vir DEPOIS do SetLevel(1). O SetLevel do ALE chama
    -- Player::GiveLevel, que chama UpdateSkillsForLevel e ja acerta o
    -- MAXIMO para o nivel novo -- entao aqui o GetMaxSkillValue devolve 5,
    -- e nao 400. Antes do SetLevel a skill sairia com maximo de nivel 80.
    --
    -- E nao da para deixar isso para o core: quando o nivel muda ele
    -- chama UpdateSkillsForLevel, que faz MAKE_SKILL_VALUE(val, maxSkill)
    -- -- baixa o maximo e MANTEM o valor. Um nivel 1 ficaria com Fogo em
    -- 400 e maximo 5.
    if CFG.RESET_LEVEL_SKILLS then
        local zeradas = 0

        for skillId, _ in pairs(Prestige.LEVEL_SKILLS) do
            -- HasSkill primeiro: SetSkill numa skill que o personagem nao
            -- tem a ENSINARIA, e um mago sairia sabendo Espadas.
            if player:HasSkill(skillId) then
                -- Nao usar Prestige.PROFESSION_SKILLS como guarda aqui: as
                -- duas tabelas nao se cruzam, e conferir daria a impressao
                -- errada de que poderiam.
                local maximo = player:GetMaxSkillValue(skillId)
                local ok = pcall(function() player:SetSkill(skillId, 0, 1, maximo) end)
                if ok then zeradas = zeradas + 1 end
            end
        end

        Prestige.Log(guid, "SKILLS_RESET", string.format("zeradas=%d", zeradas))
        if CFG.DEBUG then
            PrintInfo(string.format("[Prestige] %d skill(s) de nivel zeradas para %s",
                                    zeradas, player:GetName()))
        end
    end

    -- Flags at_login: a forma idiomatica do core de fazer limpeza
    -- pesada. Ele executa no proximo carregamento do personagem,
    -- fora de qualquer caminho quente.
    local flags = 0
    if CFG.RESET_SPELLS  then flags = flags + Prestige.AT_LOGIN_RESET_SPELLS  end
    if CFG.RESET_TALENTS then flags = flags + Prestige.AT_LOGIN_RESET_TALENTS end
    if flags > 0 then
        pcall(function() player:SetAtLoginFlag(flags) end)
    end

    Prestige.Log(guid, "WIPED", "")
end

-- =====================================================================
-- Orquestrador
-- =====================================================================
function Prestige.Execute(player)
    local guid      = player:GetGUIDLow()
    local accountId = player:GetAccountId()
    local oldLevel  = player:GetLevel()

    -- --- WAL: a partir daqui existe registro de que comecamos ---
    CharDBExecute(string.format([[
        INSERT INTO character_prestige_pending (guid, stage, old_level, started_at)
        VALUES (%u, 'MAILING', %u, %u)
        ON DUPLICATE KEY UPDATE stage = 'MAILING', started_at = %u
    ]], guid, oldLevel, os.time(), os.time()))
    Prestige.Log(guid, "START", "level=" .. oldLevel)

    -- Trava o jogador: nada de andar, atacar ou trocar durante a
    -- operacao. Um segundo de movimento aqui pode significar um item
    -- em transito entre dois estados.
    pcall(function() player:SetPlayerLock(true) end)
    player:SendBroadcastMessage("|cff00ccffAscendendo... nao saia do jogo.|r")

    -- 1. profissoes
    SnapshotProfessions(player)

    -- 2. itens
    local sent, failed = MailAndStrip(player)
    if failed > 0 then
        -- Aborto parcial. Nao seguimos para o wipe: melhor um
        -- personagem intacto com itens no correio do que um
        -- personagem zerado que ainda esta segurando equipamento.
        pcall(function() player:SetPlayerLock(false) end)
        player:SendBroadcastMessage(
            "|cffff0000Falha ao enviar alguns itens. O prestigio foi cancelado. Contate um GM.|r")
        Prestige.Log(guid, "ABORT", "falhas no correio=" .. failed)
        return false
    end

    CharDBExecute("UPDATE character_prestige_pending SET stage = 'WIPING' WHERE guid = " .. guid)

    -- 3. reset
    WipeProgress(player)

    -- 4. contador
    local newCount = Prestige.GetCount(player) + 1
    Prestige.SaveCount(guid, accountId, newCount)

    -- 4b. bonus de nivel maximo.
    -- Usamos oldLevel, capturado no inicio: neste ponto o personagem
    -- ja foi rebaixado para 1 e GetLevel() devolveria o valor errado.
    local gotBonus = false
    if CFG.MAXLEVEL_BONUS_ENABLED
       and oldLevel >= CFG.SERVER_MAX_LEVEL
       and Prestige.GrantMaxLevelBonus then
        Prestige.GrantMaxLevelBonus(guid)
        gotBonus = true
    end

    -- 5. persiste antes de qualquer coisa que possa derrubar a sessao
    player:SaveToDB()

    -- WAL vai para DONE, nao e apagado ainda: o login seguinte ainda
    -- precisa restaurar profissoes depois que as flags at_login rodarem.
    CharDBExecute("UPDATE character_prestige_pending SET stage = 'DONE' WHERE guid = " .. guid)
    Prestige.Log(guid, "SUCCESS", "prestigio=" .. newCount)

    local mult = math.min(1.0 + (newCount * CFG.XP_STEP), CFG.XP_CAP)
    player:SendBroadcastMessage(string.format(
        "|cff00ff00Prestigio %d alcancado! XP x%.1f. Voce sera desconectado.|r",
        newCount, mult))

    if gotBonus then
        player:SendBroadcastMessage(
            "|cff00ff00Voce recebeu a BenCao do Apice: x3 em reputacao, honra, profissoes e coleta.|r")
    end

    -- 6. relogin forcado.
    -- Obrigatorio: as flags at_login so tem efeito no proximo
    -- carregamento do personagem, e o cliente precisa recarregar
    -- o cache de conquistas e talentos.
    CreateLuaEvent(function()
        local p = GetPlayerByGUID(guid)
        if p then p:LogoutPlayer(true) end
    end, 3000, 1)

    return true
end

-- =====================================================================
-- Retomada apos crash
-- =====================================================================
function Prestige.Resume(player, stage, oldLevel)
    local guid = player:GetGUIDLow()

    if stage == "MAILING" then
        -- Caiu antes de terminar as cartas. O estado e ambiguo: parte
        -- dos itens pode ter ido. Nao adivinhamos -- congelamos e
        -- avisamos, porque agir no escuro aqui pode duplicar itens.
        player:SendBroadcastMessage(
            "|cffff0000Seu prestigio anterior foi interrompido. Um GM precisa revisar sua conta.|r")
        Prestige.Log(guid, "STUCK", "interrompido em MAILING")
        return

    elseif stage == "WIPING" then
        -- Cartas ja sairam. Seguro terminar o reset.
        WipeProgress(player)
        local newCount = Prestige.LoadCount(guid) + 1
        Prestige.SaveCount(guid, player:GetAccountId(), newCount)
        player:SaveToDB()
        CharDBExecute("UPDATE character_prestige_pending SET stage = 'DONE' WHERE guid = " .. guid)
        player:SendBroadcastMessage("|cff00ff00Prestigio concluido.|r")
        return

    elseif stage == "DONE" then
        -- Caminho normal: flags at_login ja rodaram neste login.
        RestoreProfessions(player)
        CharDBExecute("DELETE FROM character_prestige_pending WHERE guid = " .. guid)
        Prestige.ApplyAura(player)
        player:SendBroadcastMessage("|cff00ccffSuas profissoes foram preservadas.|r")
        return
    end
end

-- Comando de teste seguro. Rode com um personagem descartavel antes
-- de liberar qualquer coisa: ele lista o que seria enviado sem tocar
-- em nada.
--   .reload eluna  e depois use este hook via um item ou GM command.
function Prestige.TestDryRun(playerName)
    local p = GetPlayerByName(playerName)
    if p then Prestige.DryRun(p) end
end
