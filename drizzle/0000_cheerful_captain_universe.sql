CREATE TABLE `assistant_requests` (
	`id` text PRIMARY KEY NOT NULL,
	`display_id` text NOT NULL,
	`case_id` text NOT NULL,
	`question` text NOT NULL,
	`redacted_input` text NOT NULL,
	`answer` text NOT NULL,
	`citations` text NOT NULL,
	`trace` text NOT NULL,
	`submitter_id` text NOT NULL,
	`submitter_name` text NOT NULL,
	`submitter_role` text NOT NULL,
	`requires_review` integer NOT NULL,
	`created_at` text NOT NULL
);
--> statement-breakpoint
CREATE UNIQUE INDEX `assistant_requests_display_id_unique` ON `assistant_requests` (`display_id`);--> statement-breakpoint
CREATE TABLE `audit_events` (
	`id` text PRIMARY KEY NOT NULL,
	`request_id` text,
	`actor_id` text NOT NULL,
	`actor_name` text NOT NULL,
	`actor_role` text NOT NULL,
	`event_type` text NOT NULL,
	`detail` text NOT NULL,
	`created_at` text NOT NULL
);
--> statement-breakpoint
CREATE TABLE `cases` (
	`id` text PRIMARY KEY NOT NULL,
	`applicant_name` text NOT NULL,
	`program` text NOT NULL,
	`status` text NOT NULL,
	`assigned_to` text NOT NULL,
	`details` text NOT NULL,
	`created_at` text NOT NULL
);
--> statement-breakpoint
CREATE TABLE `evaluation_runs` (
	`id` text PRIMARY KEY NOT NULL,
	`suite_version` text NOT NULL,
	`passed` integer NOT NULL,
	`total` integer NOT NULL,
	`created_at` text NOT NULL
);
--> statement-breakpoint
CREATE TABLE `policies` (
	`id` text PRIMARY KEY NOT NULL,
	`section` text NOT NULL,
	`title` text NOT NULL,
	`program` text NOT NULL,
	`content` text NOT NULL,
	`approved` integer DEFAULT true NOT NULL
);
--> statement-breakpoint
CREATE UNIQUE INDEX `policies_section_unique` ON `policies` (`section`);--> statement-breakpoint
CREATE TABLE `review_items` (
	`id` text PRIMARY KEY NOT NULL,
	`request_id` text NOT NULL,
	`status` text NOT NULL,
	`submitter_id` text NOT NULL,
	`submitter_name` text NOT NULL,
	`reviewer_id` text,
	`reviewer_name` text,
	`return_note` text,
	`created_at` text NOT NULL,
	`updated_at` text NOT NULL,
	`decided_at` text
);
--> statement-breakpoint
CREATE UNIQUE INDEX `review_items_request_id_unique` ON `review_items` (`request_id`);