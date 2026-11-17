PRAGMA journal_mode = WAL;

BEGIN IMMEDIATE;

CREATE TABLE IF NOT EXISTS clientes (
    id INTEGER PRIMARY KEY,
    nome TEXT NOT NULL,
    email TEXT NOT NULL COLLATE NOCASE UNIQUE,
    telefone TEXT
) STRICT;

CREATE TABLE IF NOT EXISTS agendamentos (
    id INTEGER PRIMARY KEY,
    cliente_id INTEGER NOT NULL REFERENCES clientes(id),
    inicio INTEGER NOT NULL,
    fim INTEGER NOT NULL CHECK (fim > inicio),
    cancelado_em INTEGER
) STRICT;

CREATE INDEX IF NOT EXISTS ix_agenda_ativa
ON agendamentos (inicio, fim) WHERE cancelado_em IS NULL;

CREATE TRIGGER IF NOT EXISTS impedir_sobreposicao_ao_inserir
BEFORE INSERT ON agendamentos
WHEN NEW.cancelado_em IS NULL AND EXISTS (
    SELECT 1 FROM agendamentos AS a
    WHERE a.cancelado_em IS NULL
      AND a.inicio < NEW.fim AND a.fim > NEW.inicio
)
BEGIN
    SELECT RAISE(ABORT, 'agenda_conflito');
END;

CREATE TRIGGER IF NOT EXISTS impedir_sobreposicao_ao_atualizar
BEFORE UPDATE OF inicio, fim, cancelado_em ON agendamentos
WHEN NEW.cancelado_em IS NULL AND EXISTS (
    SELECT 1 FROM agendamentos AS a
    WHERE a.id <> OLD.id AND a.cancelado_em IS NULL
      AND a.inicio < NEW.fim AND a.fim > NEW.inicio
)
BEGIN
    SELECT RAISE(ABORT, 'agenda_conflito');
END;

COMMIT;
