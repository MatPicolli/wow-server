-- =====================================================================
--  Sistema de Prestigio - upgrade: bonus de nivel maximo
--  Banco alvo: acore_characters
-- =====================================================================
--  Pode rodar quantas vezes quiser.
--
--  [ajuste local] A versao original era um ALTER TABLE ... ADD COLUMN
--  puro, com o comentario "rode UMA vez". Na segunda vez ele falha com
--  "Duplicate column name 'max_level_bonus'", e quem instala pela
--  interface nao tem como saber se ja rodou antes - a instalacao
--  passaria a depender de o usuario lembrar. O MySQL 8 nao tem
--  ADD COLUMN IF NOT EXISTS, entao a checagem vai no information_schema,
--  o mesmo padrao do resto do SQL deste servidor.
-- =====================================================================

DROP PROCEDURE IF EXISTS prestige_add_maxlevel_column;

DELIMITER //
CREATE PROCEDURE prestige_add_maxlevel_column()
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
         WHERE TABLE_SCHEMA = DATABASE()
           AND TABLE_NAME   = 'character_prestige'
           AND COLUMN_NAME  = 'max_level_bonus'
    ) THEN
        ALTER TABLE `character_prestige`
            ADD COLUMN `max_level_bonus` TINYINT UNSIGNED NOT NULL DEFAULT 0
            COMMENT '1 = prestigiou no nivel maximo ao menos uma vez';
    END IF;
END //
DELIMITER ;

CALL prestige_add_maxlevel_column();
DROP PROCEDURE prestige_add_maxlevel_column;

-- Conferencia:
-- SELECT guid, prestige_count, max_level_bonus FROM character_prestige;
