-- =====================================================================
--  Sistema de Prestigio - Schema
--  Banco alvo: acore_characters
-- =====================================================================
--  Rode este arquivo UMA vez antes de subir os scripts Lua.
-- =====================================================================

-- ---------------------------------------------------------------------
-- Contador de prestigio. Esta e a FONTE DA VERDADE.
-- Nunca confie em tabela Lua em memoria: em modo multistate cada mapa
-- tem seu proprio estado Lua e o cache de um nao enxerga o do outro.
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `character_prestige` (
    `guid`           INT UNSIGNED     NOT NULL COMMENT 'characters.guid',
    `account`        INT UNSIGNED     NOT NULL DEFAULT 0,
    `prestige_count` TINYINT UNSIGNED NOT NULL DEFAULT 0,
    `last_prestige`  INT UNSIGNED     NOT NULL DEFAULT 0 COMMENT 'unixtime',
    PRIMARY KEY (`guid`),
    KEY `idx_account` (`account`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
  COMMENT='Contador de prestigio por personagem';

-- ---------------------------------------------------------------------
-- Write-ahead log artesanal.
-- Uma linha aqui significa "um prestigio comecou e nao terminou".
-- Se o servidor cair no meio da operacao, o login seguinte encontra
-- essa linha e retoma de onde parou. Sem isso, uma queda no momento
-- errado deixa o personagem sem itens E sem o contador incrementado.
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `character_prestige_pending` (
    `guid`       INT UNSIGNED     NOT NULL,
    `stage`      VARCHAR(24)      NOT NULL COMMENT 'MAILING | WIPING | DONE',
    `old_level`  TINYINT UNSIGNED NOT NULL DEFAULT 0,
    `started_at` INT UNSIGNED     NOT NULL DEFAULT 0,
    PRIMARY KEY (`guid`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
  COMMENT='Prestigios em andamento (crash recovery)';

-- ---------------------------------------------------------------------
-- Snapshot das profissoes.
-- Motivo: a forma idiomatica de zerar talentos/magias no core e a flag
-- at_login, mas AT_LOGIN_RESET_SPELLS tambem apaga as magias de
-- profissao. Salvamos o nivel de cada profissao antes e restauramos
-- depois. Sem isso, "manter profissoes" nao se sustenta.
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `character_prestige_skills` (
    `guid`  INT UNSIGNED   NOT NULL,
    `skill` SMALLINT UNSIGNED NOT NULL,
    `value` SMALLINT UNSIGNED NOT NULL,
    `max`   SMALLINT UNSIGNED NOT NULL,
    PRIMARY KEY (`guid`, `skill`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
  COMMENT='Backup de profissoes durante o prestigio';

-- ---------------------------------------------------------------------
-- Auditoria. Voce VAI precisar disso no primeiro ticket de
-- "sumiu meu item". Nao e opcional em operacao destrutiva.
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `character_prestige_log` (
    `id`         INT UNSIGNED NOT NULL AUTO_INCREMENT,
    `guid`       INT UNSIGNED NOT NULL,
    `at`         INT UNSIGNED NOT NULL,
    `event`      VARCHAR(32)  NOT NULL,
    `detail`     VARCHAR(255) NOT NULL DEFAULT '',
    PRIMARY KEY (`id`),
    KEY `idx_guid` (`guid`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
  COMMENT='Trilha de auditoria do prestigio';
