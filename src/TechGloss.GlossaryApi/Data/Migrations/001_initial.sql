-- Research §5.6.3 — 작성자/승인자 컬럼 없음
CREATE TABLE IF NOT EXISTS glossary_category (
    id   TEXT PRIMARY KEY,          -- UUID 문자열
    name TEXT NOT NULL UNIQUE       -- 영문 카테고리명 (예: General, Cloud, DevOps)
);

CREATE TABLE IF NOT EXISTS glossary_entry (
    id                  TEXT PRIMARY KEY,
    term_ko             TEXT NOT NULL,
    term_en             TEXT NOT NULL,
    term_ko_normalized  TEXT NOT NULL,
    term_en_normalized  TEXT NOT NULL,
    definition_ko       TEXT NOT NULL DEFAULT '',
    category_id         TEXT REFERENCES glossary_category(id) ON DELETE SET NULL,
    notes               TEXT,
    case_sensitive      INTEGER NOT NULL DEFAULT 0,
    is_preferred        INTEGER NOT NULL DEFAULT 1,
    status              TEXT NOT NULL DEFAULT 'draft'
                            CHECK(status IN ('draft','published','deprecated')),
    created_at          TEXT NOT NULL,
    updated_at          TEXT NOT NULL
);

-- EN 방향 UNIQUE: 동일 카테고리 내 동일 영문 정규형 중복 방지
CREATE UNIQUE INDEX IF NOT EXISTS uq_entry_en_cat
    ON glossary_entry(term_en_normalized, category_id);

-- KO 방향 UNIQUE: 동일 카테고리 내 동일 한글 정규형 중복 방지 (EN 방향과 대칭)
CREATE UNIQUE INDEX IF NOT EXISTS uq_entry_ko_cat
    ON glossary_entry(term_ko_normalized, category_id);

CREATE INDEX IF NOT EXISTS idx_entry_status_cat
    ON glossary_entry(status, category_id);

-- SQLite FTS5 (LIKE '%q%' 성능 보조)
CREATE VIRTUAL TABLE IF NOT EXISTS glossary_fts USING fts5(
    id UNINDEXED,
    term_ko,
    term_en,
    definition_ko,
    content='glossary_entry',
    content_rowid='rowid'
);

-- ON DELETE CASCADE: entry 삭제 시 임베딩 상태도 자동 삭제 → 고아 레코드 방지
CREATE TABLE IF NOT EXISTS glossary_embedding_state (
    entry_id            TEXT PRIMARY KEY REFERENCES glossary_entry(id) ON DELETE CASCADE,
    embed_model         TEXT NOT NULL,
    embed_dimension     INTEGER NOT NULL,
    embed_text_hash     TEXT NOT NULL,
    -- Phase D 전까지는 'none'; Qdrant 도입 후 'qdrant'로 업데이트
    vector_store        TEXT NOT NULL DEFAULT 'none',
    vector_point_id     TEXT NOT NULL DEFAULT '',
    last_embedded_at    TEXT,
    last_error          TEXT
);

-- ── FTS5 동기화 트리거 ───────────────────────────────────────────────────
-- content='glossary_entry' 모드는 트리거 없이는 FTS 인덱스가 갱신되지 않음
-- INSERT/UPDATE/DELETE 세 트리거 모두 필수 — 누락 시 FTS 검색이 항상 빈 결과

CREATE TRIGGER IF NOT EXISTS glossary_fts_insert
AFTER INSERT ON glossary_entry BEGIN
    INSERT INTO glossary_fts(rowid, id, term_ko, term_en, definition_ko)
    VALUES (new.rowid, new.id, new.term_ko, new.term_en, new.definition_ko);
END;

-- FTS5는 UPDATE를 직접 지원하지 않으므로 DELETE + INSERT 패턴 사용
CREATE TRIGGER IF NOT EXISTS glossary_fts_update
AFTER UPDATE ON glossary_entry BEGIN
    DELETE FROM glossary_fts WHERE rowid = old.rowid;
    INSERT INTO glossary_fts(rowid, id, term_ko, term_en, definition_ko)
    VALUES (new.rowid, new.id, new.term_ko, new.term_en, new.definition_ko);
END;

CREATE TRIGGER IF NOT EXISTS glossary_fts_delete
AFTER DELETE ON glossary_entry BEGIN
    DELETE FROM glossary_fts WHERE rowid = old.rowid;
END;
