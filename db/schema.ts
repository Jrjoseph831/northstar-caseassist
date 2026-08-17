import { index, integer, sqliteTable, text } from "drizzle-orm/sqlite-core";

export const cases = sqliteTable("cases", {
  id: text("id").primaryKey(),
  applicantName: text("applicant_name").notNull(),
  program: text("program").notNull(),
  status: text("status").notNull(),
  assignedTo: text("assigned_to").notNull(),
  details: text("details").notNull(),
  createdAt: text("created_at").notNull(),
});

export const policies = sqliteTable("policies", {
  id: text("id").primaryKey(),
  section: text("section").notNull().unique(),
  title: text("title").notNull(),
  program: text("program").notNull(),
  content: text("content").notNull(),
  approved: integer("approved", { mode: "boolean" }).notNull().default(true),
});

export const assistantRequests = sqliteTable(
  "assistant_requests",
  {
    id: text("id").primaryKey(),
    displayId: text("display_id").notNull().unique(),
    caseId: text("case_id").notNull(),
    question: text("question").notNull(),
    redactedInput: text("redacted_input").notNull(),
    answer: text("answer").notNull(),
    citations: text("citations").notNull(),
    trace: text("trace").notNull(),
    submitterId: text("submitter_id").notNull(),
    submitterName: text("submitter_name").notNull(),
    submitterRole: text("submitter_role").notNull(),
    requiresReview: integer("requires_review", { mode: "boolean" }).notNull(),
    createdAt: text("created_at").notNull(),
  },
  (table) => [index("idx_assistant_requests_created_at").on(table.createdAt)],
);

export const reviewItems = sqliteTable(
  "review_items",
  {
    id: text("id").primaryKey(),
    requestId: text("request_id").notNull().unique(),
    status: text("status").notNull(),
    submitterId: text("submitter_id").notNull(),
    submitterName: text("submitter_name").notNull(),
    reviewerId: text("reviewer_id"),
    reviewerName: text("reviewer_name"),
    returnNote: text("return_note"),
    createdAt: text("created_at").notNull(),
    updatedAt: text("updated_at").notNull(),
    decidedAt: text("decided_at"),
  },
  (table) => [
    index("idx_review_items_status_updated_at").on(
      table.status,
      table.updatedAt,
    ),
  ],
);

export const auditEvents = sqliteTable(
  "audit_events",
  {
    id: text("id").primaryKey(),
    requestId: text("request_id"),
    actorId: text("actor_id").notNull(),
    actorName: text("actor_name").notNull(),
    actorRole: text("actor_role").notNull(),
    eventType: text("event_type").notNull(),
    detail: text("detail").notNull(),
    createdAt: text("created_at").notNull(),
  },
  (table) => [index("idx_audit_events_created_at").on(table.createdAt)],
);

export const evaluationRuns = sqliteTable("evaluation_runs", {
  id: text("id").primaryKey(),
  suiteVersion: text("suite_version").notNull(),
  passed: integer("passed").notNull(),
  total: integer("total").notNull(),
  createdAt: text("created_at").notNull(),
});
