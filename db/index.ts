import { env } from "cloudflare:workers";
import { drizzle } from "drizzle-orm/d1";
import * as schema from "./schema";

export function getDb() {
  const d1 = getD1();
  return drizzle(d1, { schema });
}

export function getD1() {
  if (!env.DB) {
    throw new Error(
      "Cloudflare D1 binding `DB` is unavailable. Set the `d1` field in .openai/hosting.json to `DB` or let your control plane inject the real binding values before using the database.",
    );
  }
  return env.DB;
}

export async function ensureDatabaseSchema() {
  const d1 = getD1();
  const statements = [
    "CREATE TABLE IF NOT EXISTS cases (id TEXT PRIMARY KEY NOT NULL, applicant_name TEXT NOT NULL, program TEXT NOT NULL, status TEXT NOT NULL, assigned_to TEXT NOT NULL, details TEXT NOT NULL, created_at TEXT NOT NULL)",
    "CREATE TABLE IF NOT EXISTS policies (id TEXT PRIMARY KEY NOT NULL, section TEXT NOT NULL UNIQUE, title TEXT NOT NULL, program TEXT NOT NULL, content TEXT NOT NULL, approved INTEGER DEFAULT 1 NOT NULL)",
    "CREATE TABLE IF NOT EXISTS assistant_requests (id TEXT PRIMARY KEY NOT NULL, display_id TEXT NOT NULL UNIQUE, case_id TEXT NOT NULL, question TEXT NOT NULL, redacted_input TEXT NOT NULL, answer TEXT NOT NULL, citations TEXT NOT NULL, trace TEXT NOT NULL, submitter_id TEXT NOT NULL, submitter_name TEXT NOT NULL, submitter_role TEXT NOT NULL, requires_review INTEGER NOT NULL, created_at TEXT NOT NULL)",
    "CREATE TABLE IF NOT EXISTS review_items (id TEXT PRIMARY KEY NOT NULL, request_id TEXT NOT NULL UNIQUE, status TEXT NOT NULL, submitter_id TEXT NOT NULL, submitter_name TEXT NOT NULL, reviewer_id TEXT, reviewer_name TEXT, return_note TEXT, created_at TEXT NOT NULL, updated_at TEXT NOT NULL, decided_at TEXT)",
    "CREATE TABLE IF NOT EXISTS audit_events (id TEXT PRIMARY KEY NOT NULL, request_id TEXT, actor_id TEXT NOT NULL, actor_name TEXT NOT NULL, actor_role TEXT NOT NULL, event_type TEXT NOT NULL, detail TEXT NOT NULL, created_at TEXT NOT NULL)",
    "CREATE TABLE IF NOT EXISTS evaluation_runs (id TEXT PRIMARY KEY NOT NULL, suite_version TEXT NOT NULL, passed INTEGER NOT NULL, total INTEGER NOT NULL, created_at TEXT NOT NULL)",
    "CREATE INDEX IF NOT EXISTS idx_assistant_requests_created_at ON assistant_requests(created_at)",
    "CREATE INDEX IF NOT EXISTS idx_audit_events_created_at ON audit_events(created_at)",
    "CREATE INDEX IF NOT EXISTS idx_review_items_status_updated_at ON review_items(status, updated_at)",
  ];
  await d1.batch(statements.map((statement) => d1.prepare(statement)));
}
