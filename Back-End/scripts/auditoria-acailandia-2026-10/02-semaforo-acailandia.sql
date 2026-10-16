-- ============================================================================
--  Conserto de configuração · KPI "Semáforo" da Açailândia
--  Auditoria do dashboard (01–16/10/2026)
--
--  O QUE MUDA NA CONFIG DO KPI (kpi_configurations, KpiKey = 'semaforo')
--   1. Sai a etapa 106810359 — ela não existe na conta da Açailândia.
--   2. O 142 passa a valer SÓ no funil TRATAMENTO (14304315) = ALTA.
--      Hoje ele soma também o 142 do COMERCIAL (13795975) = GANHO / CONCLUIDO.
--      Escolha baseada no glossário do João: o Semáforo é o desfecho do TRATAMENTO
--      proposto, contado em COMPARECEU, NEGOCIAÇÃO, EM TRATAMENTO e ALTA.
--      Se a intenção for o contrário (GANHO), troque 14304315 por 13795975 abaixo.
--
--  PRÉ-REQUISITO: o back novo no ar. O campo "funilDaEtapa" só é lido por ele;
--  no back antigo ele é ignorado (o 142 continuaria valendo nos dois funis).
--
--  COMO RODAR
--   • PASSO 1 só lê. Rode e confira.
--   • PASSO 2 roda numa transação que termina em ROLLBACK (não grava). Confira o
--     SELECT final; se estiver certo, troque o ROLLBACK por COMMIT e rode de novo.
--  Nada aqui toca em lead, histórico, Kommo ou franquia — só nesta linha de config.
-- ============================================================================

-- ── PASSO 1 · CONFERIR (só leitura) ────────────────────────────────────────

-- 1a. A config atual. Esperado: 1 linha, com 106810359 e 142 em "stageIds".
SELECT u."Id" AS unidade, k."Id" AS kpi_id, k."KpiKey", k."ConfigJson"
FROM   kpi_configurations k
JOIN   units u ON u."Id" = k."UnitId"
WHERE  u."Slug" = 'doutor-hernia-acailandia'
AND    k."KpiKey" = 'semaforo';

-- 1b. Quantas entradas do histórico apontam para a etapa inexistente (deixam de contar).
SELECT count(*) AS entradas_na_106810359
FROM   lead_stage_histories h
JOIN   leads l ON l."Id" = h."LeadId"
JOIN   units u ON u."Id" = l."UnitId"
WHERE  u."Slug" = 'doutor-hernia-acailandia'
AND    h."StageId" = 106810359;

-- 1c. Entradas no 142 por funil, desde 01/10. Linha com PipelineId 13795975 = GANHO do
--     COMERCIAL (sai da conta); 14304315 = ALTA (fica); vazio = sem funil gravado (sai).
SELECT h."PipelineId", count(*) AS entradas
FROM   lead_stage_histories h
JOIN   leads l ON l."Id" = h."LeadId"
JOIN   units u ON u."Id" = l."UnitId"
WHERE  u."Slug" = 'doutor-hernia-acailandia'
AND    h."StageId" = 142
AND    h."ChangedAt" >= '2026-10-01 03:00:00+00'
GROUP  BY h."PipelineId"
ORDER  BY 2 DESC;

-- ── PASSO 2 · GRAVAR (transação; termina em ROLLBACK) ──────────────────────
BEGIN;

UPDATE kpi_configurations k
SET    "ConfigJson" = jsonb_set(
           jsonb_set(
               k."ConfigJson",
               '{stageIds}',
               (SELECT coalesce(jsonb_agg(e), '[]'::jsonb)
                FROM   jsonb_array_elements(k."ConfigJson" -> 'stageIds') e
                WHERE  e #>> '{}' <> '106810359')),
           '{funilDaEtapa}',
           coalesce(k."ConfigJson" -> 'funilDaEtapa', '{}'::jsonb) || '{"142": 14304315}'::jsonb),
       "UpdatedAt" = now()
FROM   units u
WHERE  u."Id" = k."UnitId"
AND    u."Slug" = 'doutor-hernia-acailandia'
AND    k."KpiKey" = 'semaforo'
AND    EXISTS (SELECT 1 FROM jsonb_array_elements(k."ConfigJson" -> 'stageIds') e
               WHERE e #>> '{}' = '142');
-- Esperado: UPDATE 1. Se vier 0, a config não é a que a auditoria viu — pare e confira.

-- Como ficou (dentro da transação):
SELECT k."ConfigJson"
FROM   kpi_configurations k
JOIN   units u ON u."Id" = k."UnitId"
WHERE  u."Slug" = 'doutor-hernia-acailandia'
AND    k."KpiKey" = 'semaforo';

ROLLBACK;   -- ← troque por COMMIT depois de conferir

-- ── OPCIONAL · nome do KPI "Leads qualificados" ────────────────────────────
-- O card do painel passou a se chamar "Leads quentes" (o número conta só Quente).
-- O nome que aparece nas Configurações Técnicas vem do banco (DisplayName). Para
-- alinhar, confira e, se quiser, rode o UPDATE (vale para todas as unidades):
--
-- SELECT "UnitId", "DisplayName" FROM kpi_configurations WHERE "KpiKey" = 'leads_qualificados';
-- BEGIN;
-- UPDATE kpi_configurations SET "DisplayName" = 'Leads quentes', "UpdatedAt" = now()
-- WHERE  "KpiKey" = 'leads_qualificados' AND "DisplayName" = 'Leads qualificados';
-- ROLLBACK;  -- ← troque por COMMIT depois de conferir
