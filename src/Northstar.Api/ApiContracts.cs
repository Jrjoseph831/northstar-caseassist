namespace Northstar.Api;

public sealed record ApplicantInput(
    string SyntheticDisplayName,
    string Email,
    string Phone,
    string Address,
    int HouseholdSize);

public sealed record CreateApplicationRequest(
    string ProgramCode,
    string SubmissionChannel,
    ApplicantInput Applicant,
    string ScenarioCode = "manual-demo-intake");

public sealed record ConvertToCaseRequest(string? AssignedWorkerId, string Priority = "Standard");

public sealed record AssignCaseRequest(string AssignedWorkerId);

public sealed record AddCaseNoteRequest(string NoteType, string Content, string? SourceRequestId);

public sealed record UpdateCaseStatusRequest(string Status);

public sealed record CaseDecisionRequest(string Decision, string? Note);

public sealed record GenerateCasesRequest(int Seed, int Count);

public sealed record ResetSyntheticDataRequest(string Confirmation);

public sealed record CreateCaseAssistRequest(Guid CaseId, string Question);

public sealed record ReviewDecisionRequest(string Decision, string? Note);

public sealed record ErrorEnvelope(string Code, string Message, string CorrelationId);

public sealed record DemoActor(string UserId, string DisplayName, string Role);
