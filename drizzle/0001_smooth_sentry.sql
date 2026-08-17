CREATE INDEX `idx_assistant_requests_created_at` ON `assistant_requests` (`created_at`);--> statement-breakpoint
CREATE INDEX `idx_audit_events_created_at` ON `audit_events` (`created_at`);--> statement-breakpoint
CREATE INDEX `idx_review_items_status_updated_at` ON `review_items` (`status`,`updated_at`);