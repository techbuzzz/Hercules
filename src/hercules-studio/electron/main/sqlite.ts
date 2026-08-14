import Database from "better-sqlite3";
import { join } from "node:path";
import { existsSync } from "node:fs";

let db: Database.Database | null = null;

const SCHEMA_VERSION = 1;

const MIGRATIONS = [
  // v1 — initial schema
  `
  CREATE TABLE IF NOT EXISTS chat_history (
    id TEXT PRIMARY KEY,
    connection_id TEXT NOT NULL,
    agent_id TEXT NOT NULL,
    role TEXT NOT NULL,
    content TEXT NOT NULL,
    metadata TEXT,
    created_at TEXT NOT NULL
  );
  CREATE INDEX IF NOT EXISTS idx_chat_history_conn ON chat_history(connection_id, created_at);

  CREATE TABLE IF NOT EXISTS skill_drafts (
    id TEXT PRIMARY KEY,
    connection_id TEXT NOT NULL,
    skill_id TEXT,
    name TEXT NOT NULL,
    prompt_md TEXT,
    meta_json TEXT,
    description_md TEXT,
    csharp_files TEXT,
    updated_at TEXT NOT NULL,
    pushed_at TEXT
  );
  CREATE INDEX IF NOT EXISTS idx_skill_drafts_conn ON skill_drafts(connection_id);

  CREATE TABLE IF NOT EXISTS workflows (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    graph_json TEXT NOT NULL,
    template INTEGER DEFAULT 0,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
  );

  CREATE TABLE IF NOT EXISTS scan_cache (
    port INTEGER PRIMARY KEY,
    agent_id TEXT,
    display_name TEXT,
    endpoint TEXT,
    auth_required INTEGER DEFAULT 0,
    found_at TEXT NOT NULL
  );

  CREATE TABLE IF NOT EXISTS schema_version (
    version INTEGER PRIMARY KEY
  );
  INSERT OR IGNORE INTO schema_version (version) VALUES (${SCHEMA_VERSION});
  `,
];

export function initSqlite(userDataPath: string): void {
  const dbPath = join(userDataPath, "studio.db");
  db = new Database(dbPath);
  db.pragma("journal_mode = WAL");
  db.pragma("foreign_keys = ON");

  // Run migrations
  const currentVersion = db.prepare("SELECT version FROM schema_version LIMIT 1").get() as
    | { version: number }
    | undefined;

  const appliedVersion = currentVersion?.version ?? 0;

  for (let v = appliedVersion; v < SCHEMA_VERSION; v++) {
    const migration = MIGRATIONS[v];
    if (migration) {
      db.exec(migration);
    }
  }
}

export function getDb(): Database.Database {
  if (!db) {
    throw new Error("SQLite not initialized. Call initSqlite() first.");
  }
  return db;
}

export function closeDb(): void {
  if (db) {
    db.close();
    db = null;
  }
}