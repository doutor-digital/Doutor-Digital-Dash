-- ============================================================================
--  Limpeza dos DADOS da Cloudia
--  Doutor Digital Dash — gerado em 2026-05-29
--
--  ⚠️  DESTRUTIVO E IRREVERSÍVEL. Faça backup antes:
--        pg_dump "$DATABASE_URL" > backup_antes_cloudia_$(date +%F).sql
--
--  Rode tudo dentro da transação. Confira as contagens do SELECT antes de
--  remover o "-- " do COMMIT (deixe o ROLLBACK se só quiser conferir).
-- ============================================================================

BEGIN;

-- 1) Quanto será afetado (rode primeiro só pra conferir) -------------------
SELECT 'webhook_envelopes (cloudia)' AS alvo,
       count(*) AS linhas
FROM   webhook_envelopes
WHERE  provider = 'cloudia'
UNION ALL
SELECT 'leads (Source=Cloudia)', count(*)
FROM   leads
WHERE  "Source" = 'Cloudia'
UNION ALL
SELECT 'contacts (origem webhook_cloudia)', count(*)
FROM   contacts
WHERE  "Origem" = 'webhook_cloudia';

-- 2) Apagar a fila de webhooks da Cloudia ---------------------------------
DELETE FROM webhook_envelopes
WHERE  provider = 'cloudia';

-- 3) Apagar contatos importados pelo webhook da Cloudia -------------------
DELETE FROM contacts
WHERE  "Origem" = 'webhook_cloudia';

-- 4) Apagar leads originados da Cloudia E seus dependentes ----------------
--    (recovery_attempts, payments, consultations, treatments, etc. têm FK
--     para leads; aqui removemos os filhos antes para não violar a FK.)
--    Ajuste/retire blocos conforme o que você quer realmente zerar.
WITH cloudia_leads AS (
    SELECT "Id" FROM leads WHERE "Source" = 'Cloudia'
)
DELETE FROM recovery_attempts
WHERE  lead_id IN (SELECT "Id" FROM cloudia_leads);

WITH cloudia_leads AS (
    SELECT "Id" FROM leads WHERE "Source" = 'Cloudia'
)
DELETE FROM "LeadPaymentReceipts"
WHERE  "LeadId" IN (SELECT "Id" FROM cloudia_leads);

WITH cloudia_leads AS (
    SELECT "Id" FROM leads WHERE "Source" = 'Cloudia'
)
DELETE FROM treatment_installments
WHERE  "TreatmentId" IN (
    SELECT "Id" FROM treatments
    WHERE  "LeadId" IN (SELECT "Id" FROM cloudia_leads)
);

WITH cloudia_leads AS (
    SELECT "Id" FROM leads WHERE "Source" = 'Cloudia'
)
DELETE FROM treatments
WHERE  "LeadId" IN (SELECT "Id" FROM cloudia_leads);

WITH cloudia_leads AS (
    SELECT "Id" FROM leads WHERE "Source" = 'Cloudia'
)
DELETE FROM consultations
WHERE  "LeadId" IN (SELECT "Id" FROM cloudia_leads);

WITH cloudia_leads AS (
    SELECT "Id" FROM leads WHERE "Source" = 'Cloudia'
)
DELETE FROM payment_splits
WHERE  "PaymentId" IN (
    SELECT "Id" FROM payments
    WHERE  "LeadId" IN (SELECT "Id" FROM cloudia_leads)
);

WITH cloudia_leads AS (
    SELECT "Id" FROM leads WHERE "Source" = 'Cloudia'
)
DELETE FROM payments
WHERE  "LeadId" IN (SELECT "Id" FROM cloudia_leads);

WITH cloudia_leads AS (
    SELECT "Id" FROM leads WHERE "Source" = 'Cloudia'
)
DELETE FROM lead_assignments
WHERE  "LeadId" IN (SELECT "Id" FROM cloudia_leads);

WITH cloudia_leads AS (
    SELECT "Id" FROM leads WHERE "Source" = 'Cloudia'
)
DELETE FROM lead_stage_histories
WHERE  "LeadId" IN (SELECT "Id" FROM cloudia_leads);

WITH cloudia_leads AS (
    SELECT "Id" FROM leads WHERE "Source" = 'Cloudia'
)
DELETE FROM lead_conversations
WHERE  "LeadId" IN (SELECT "Id" FROM cloudia_leads);

-- Por fim, os próprios leads.
DELETE FROM leads
WHERE  "Source" = 'Cloudia';

-- 5) Confirme ou desfaça --------------------------------------------------
--    Deixe ROLLBACK para um "dry run". Troque por COMMIT quando tiver certeza.
ROLLBACK;
-- COMMIT;
