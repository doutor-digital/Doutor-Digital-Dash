-- ─── Log de envio: a reserva do event_id acontece AQUI, antes da Meta ────────
-- A Kommo reenvia webhook com frequência. Reservar depois do envio significa
-- conversão duplicada, que infla o ROAS e envenena a otimização da campanha.
CREATE TABLE IF NOT EXISTS capi_envios (
  id            bigserial PRIMARY KEY,
  event_id      text NOT NULL UNIQUE,
  event_name    text NOT NULL,
  unidade       text,
  pixel_id      text,
  ctwa_clid     text,
  telefone      text,
  kommo_lead_id text,
  wamid         text,
  valor         numeric(14,2),
  status        text NOT NULL DEFAULT 'reservado',
  http_status   int,
  resposta      jsonb,
  reservado_em  timestamptz NOT NULL DEFAULT now(),
  enviado_em    timestamptz
);
CREATE INDEX IF NOT EXISTS ix_capi_envios_evento ON capi_envios (event_name, reservado_em DESC);
CREATE INDEX IF NOT EXISTS ix_capi_envios_lead   ON capi_envios (kommo_lead_id);
CREATE INDEX IF NOT EXISTS ix_capi_envios_status ON capi_envios (status);

-- ─── Descartados: NÃO é erro, é a medida da cobertura ────────────────────────
-- A razão entre esta tabela e a de cima é a taxa real de atribuição. Lead sem
-- ctwa_clid veio de outra origem; jogar fora sem registrar esconde o tamanho
-- do que o rastreio não alcança.
CREATE TABLE IF NOT EXISTS capi_descartes (
  id            bigserial PRIMARY KEY,
  motivo        text NOT NULL,
  event_name    text,
  kommo_lead_id text,
  telefone      text,
  pipeline_id   text,
  detalhe       jsonb,
  em            timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_capi_descartes_motivo ON capi_descartes (motivo, em DESC);

-- ─── Índice do casamento por telefone ───────────────────────────────────────
-- Sem ele, cada webhook vira varredura da tabela inteira.
CREATE INDEX IF NOT EXISTS ix_ctwa_eventos_digits
  ON ctwa_eventos ((regexp_replace(telefone, '\D', '', 'g')), primeiro_contato_em DESC);

-- ─── Conserto para quem já criou a tabela na versão anterior ────────────────
-- CREATE TABLE IF NOT EXISTS não altera tabela existente: ele não faz nada, em
-- silêncio. Quem rodou o DDL antigo precisa destes ALTER.
ALTER TABLE capi_envios ADD COLUMN IF NOT EXISTS valor numeric(14,2);
ALTER TABLE capi_envios ADD COLUMN IF NOT EXISTS reservado_em timestamptz NOT NULL DEFAULT now();
ALTER TABLE capi_envios ALTER COLUMN enviado_em DROP NOT NULL;
ALTER TABLE capi_envios ALTER COLUMN enviado_em DROP DEFAULT;
ALTER TABLE capi_envios ALTER COLUMN status SET DEFAULT 'reservado';
