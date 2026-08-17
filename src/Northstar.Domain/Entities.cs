namespace Northstar.Domain;

public sealed class ApplicantRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string SyntheticDisplayName { get; set; }
    public required string Email { get; set; }
    public required string Phone { get; set; }
    public required string Address { get; set; }
    public int HouseholdSize { get; set; }
    public bool IsSynthetic { get; init; } = true;
}

public sealed class ApplicationRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string ApplicationNumber { get; set; }
    public required string ProgramCode { get; set; }
    public required string ScenarioCode { get; set; }
    public Guid ApplicantId { get; set; }
    public ApplicantRecord? Applicant { get; set; }
    public required string SubmissionChannel { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
    public required string Status { get; set; }
    public required string CreatedByUserId { get; set; }
    public string? IdempotencyKey { get; set; }
    public bool IsSynthetic { get; init; } = true;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class CaseRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string CaseNumber { get; set; }
    public Guid ApplicationId { get; set; }
    public ApplicationRecord? Application { get; set; }
    public required string ProgramCode { get; set; }
    public required string Status { get; set; }
    public required string Priority { get; set; }
    public required string AssignedWorkerId { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
    public bool IsSynthetic { get; init; } = true;
    public uint Version { get; set; } = 1;
}

public sealed class CaseNote
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid CaseId { get; set; }
    public required string AuthorUserId { get; set; }
    public required string NoteType { get; set; }
    public required string Content { get; set; }
    public string? SourceRequestId { get; set; }
    public bool IsSynthetic { get; init; } = true;
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class CaseDocument
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid CaseId { get; init; }
    public required string OriginalFileName { get; init; }
    public required string StorageKey { get; init; }
    public required string ContentType { get; init; }
    public long SizeBytes { get; init; }
    public required string Sha256 { get; init; }
    public required string ScanStatus { get; init; }
    public required string ScanReasonCodesJson { get; init; }
    public required string UploadedByUserId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public bool IsSynthetic { get; init; } = true;
}

public sealed class UserRecord
{
    public required string Id { get; init; }
    public required string DisplayName { get; set; }
    public bool IsSynthetic { get; init; } = true;
}

public sealed class RoleAssignment
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string UserId { get; set; }
    public UserRecord? User { get; set; }
    public required string Role { get; set; }
    public bool IsSynthetic { get; init; } = true;
}

public sealed class AuditEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string EventId { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public required string ActorUserId { get; init; }
    public required string ActorRole { get; init; }
    public required string Action { get; init; }
    public required string ResourceType { get; init; }
    public required string ResourceId { get; init; }
    public Guid? CaseId { get; init; }
    public string? RequestId { get; init; }
    public required string Outcome { get; init; }
    public required string ReasonCode { get; init; }
    public required string CorrelationId { get; init; }
    public required string MetadataJson { get; init; }
    public bool IsSynthetic { get; init; } = true;
}

public sealed class PolicySection
{
    public required string Id { get; init; }
    public required string DocumentTitle { get; init; }
    public required string DocumentVersion { get; init; }
    public required string SectionLabel { get; init; }
    public required string ProgramCode { get; init; }
    public required string Content { get; init; }
    public bool IsApproved { get; init; } = true;
    public bool IsSynthetic { get; init; } = true;
}

public sealed class AIRequest
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string RequestId { get; init; }
    public Guid CaseId { get; init; }
    public required string RequestedByUserId { get; init; }
    public required string RedactedQuestion { get; init; }
    public required string PromptVersion { get; init; }
    public required string ModelProvider { get; init; }
    public required string ModelName { get; init; }
    public required string RedactionSummaryJson { get; init; }
    public required string RetrievedSourceIdsJson { get; init; }
    public required string RiskClassification { get; set; }
    public required string Status { get; set; }
    public int TokenUsage { get; init; }
    public decimal EstimatedCost { get; init; }
    public required string AssistantOutput { get; init; }
    public required string SafetyTraceJson { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public bool IsSynthetic { get; init; } = true;
}

public sealed class ReviewItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid AIRequestId { get; init; }
    public Guid CaseId { get; init; }
    public required string SubmittedByUserId { get; init; }
    public required string AssignedReviewerId { get; init; }
    public required string ReasonCodesJson { get; init; }
    public required string Status { get; set; }
    public required string AssistantOutput { get; init; }
    public string? ReviewerNote { get; set; }
    public string? Decision { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public bool IsSynthetic { get; init; } = true;
}

public sealed class AISystemRegistry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string SystemName { get; init; }
    public required string SystemOwner { get; init; }
    public required string ApprovedPurposeJson { get; init; }
    public required string ProhibitedUsesJson { get; init; }
    public required string DataClassification { get; init; }
    public required string RiskTier { get; init; }
    public required string ActiveModelRelease { get; init; }
    public required string ActivePromptVersion { get; init; }
    public required string ApprovalStatus { get; init; }
    public DateTimeOffset NextReviewDate { get; init; }
    public required string MappedControlsJson { get; init; }
    public bool IsSynthetic { get; init; } = true;
}

public sealed class EvaluationRun
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string RunId { get; init; }
    public required string EvaluationVersion { get; init; }
    public required string DatasetVersion { get; init; }
    public required string PromptVersion { get; init; }
    public required string ModelVersion { get; init; }
    public int Total { get; init; }
    public int Passed { get; init; }
    public int Failed { get; init; }
    public required string ResultsJson { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public bool IsSynthetic { get; init; } = true;
}
