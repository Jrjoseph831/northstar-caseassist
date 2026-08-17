using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Northstar.Domain;
using Northstar.Infrastructure;

namespace Northstar.Api;

public static partial class SystemOfRecordEndpoints
{
    private static readonly HashSet<string> AllowedPrograms =
        ["UTILITY_RELIEF", "HOUSING_STABILITY", "WORKFORCE_TRAINING"];

    private static readonly HashSet<string> AllowedStatuses =
        ["Open", "PendingDocuments", "InReview", "Approved", "Denied", "Closed"];

    // The four demo cases and the status each should return to on "Reset demo caseload".
    private static readonly (string CaseNumber, string Status)[] SeededCaseStatuses =
    [
        ("NS-1048", "PendingDocuments"),
        ("NS-1061", "InReview"),
        ("NS-1074", "Open"),
        ("NS-1082", "InReview")
    ];

    public static IEndpointRouteBuilder MapSystemOfRecordEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/v1");

        api.MapPost("/applications", CreateApplicationAsync);
        api.MapGet("/applications/{applicationId:guid}", GetApplicationAsync);
        api.MapPost("/applications/{applicationId:guid}/convert-to-case", ConvertToCaseAsync);
        api.MapGet("/cases", GetCasesAsync);
        api.MapGet("/cases/{caseId:guid}", GetCaseAsync);
        api.MapPost("/cases/{caseId:guid}/assignments", AssignCaseAsync);
        api.MapPost("/cases/{caseId:guid}/notes", AddCaseNoteAsync);
        api.MapPatch("/cases/{caseId:guid}/status", UpdateCaseStatusAsync);
        api.MapPost("/cases/{caseId:guid}/decision", DecideCaseAsync);
        api.MapGet("/governance/audit-events", GetAuditEventsAsync);
        api.MapPost("/demo/generate-cases", GenerateCasesAsync);
        api.MapPost("/demo/reset-synthetic-data", ResetSyntheticDataAsync);
        api.MapPost("/demo/restore-caseload", RestoreCaseloadAsync);

        return endpoints;
    }

    private static async Task<IResult> CreateApplicationAsync(
        CreateApplicationRequest request,
        HttpContext context,
        NorthstarDbContext database,
        DemoIdentityService identities,
        AuditWriter audit,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var actor = await identities.ResolveAsync(context, database, cancellationToken);
        if (request.SubmissionChannel.Equals("EmployeeAssisted", StringComparison.OrdinalIgnoreCase)
            && actor?.Role is not ("Caseworker" or "Administrator"))
        {
            await audit.WriteAsync(database, context, actor, "application.submit", "Application", "new", "Denied", "ROLE_REQUIRED", cancellationToken: cancellationToken);
            return Forbidden(context, "Employee-assisted intake requires an authorized employee role.");
        }

        var validationError = ValidateApplication(request);
        if (validationError is not null)
        {
            return BadRequest(context, "VALIDATION_FAILED", validationError);
        }

        var idempotencyKey = context.Request.Headers["Idempotency-Key"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var existing = await database.Applications.AsNoTracking()
                .SingleOrDefaultAsync(item => item.IdempotencyKey == idempotencyKey, cancellationToken);
            if (existing is not null)
            {
                return Results.Ok(ToApplicationSummary(existing));
            }
        }

        var now = timeProvider.GetUtcNow();
        var applicant = new ApplicantRecord
        {
            SyntheticDisplayName = request.Applicant.SyntheticDisplayName.Trim(),
            Email = request.Applicant.Email.Trim(),
            Phone = request.Applicant.Phone.Trim(),
            Address = request.Applicant.Address.Trim(),
            HouseholdSize = request.Applicant.HouseholdSize
        };
        var sequence = await database.Applications.CountAsync(cancellationToken) + 1;
        var application = new ApplicationRecord
        {
            ApplicationNumber = $"APP-{now.Year}-{sequence:0000}",
            ProgramCode = request.ProgramCode,
            ScenarioCode = request.ScenarioCode,
            ApplicantId = applicant.Id,
            Applicant = applicant,
            SubmissionChannel = request.SubmissionChannel,
            SubmittedAt = now,
            Status = "Submitted",
            CreatedByUserId = actor?.UserId ?? "citizen.demo",
            IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey,
            CreatedAt = now,
            UpdatedAt = now
        };

        database.Applications.Add(application);
        await database.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(database, context, actor, "application.submit", "Application", application.Id.ToString(), "Succeeded", "SYNTHETIC_APPLICATION_CREATED", metadata: new Dictionary<string, object?>
        {
            ["programCode"] = application.ProgramCode,
            ["scenarioCode"] = application.ScenarioCode,
            ["submissionChannel"] = application.SubmissionChannel,
            ["isSynthetic"] = true
        }, cancellationToken: cancellationToken);

        return Results.Created($"/api/v1/applications/{application.Id}", ToApplicationSummary(application));
    }

    private static async Task<IResult> GetApplicationAsync(
        Guid applicationId,
        HttpContext context,
        NorthstarDbContext database,
        DemoIdentityService identities,
        AuditWriter audit,
        CancellationToken cancellationToken)
    {
        var actor = await identities.ResolveAsync(context, database, cancellationToken);
        if (actor is null)
        {
            await audit.WriteAsync(database, context, null, "application.read", "Application", applicationId.ToString(), "Denied", "AUTHENTICATION_REQUIRED", cancellationToken: cancellationToken);
            return Unauthorized(context);
        }

        var application = await database.Applications.AsNoTracking()
            .Include(item => item.Applicant)
            .SingleOrDefaultAsync(item => item.Id == applicationId, cancellationToken);
        if (application is null)
        {
            return Results.NotFound(Error(context, "NOT_FOUND", "Application not found."));
        }

        if (actor.Role != "Administrator" && application.CreatedByUserId != actor.UserId)
        {
            await audit.WriteAsync(database, context, actor, "application.read", "Application", applicationId.ToString(), "Denied", "APPLICATION_ACCESS_DENIED", cancellationToken: cancellationToken);
            return Forbidden(context, "You are not authorized to access this application.");
        }

        return Results.Ok(new
        {
            application.Id,
            application.ApplicationNumber,
            application.ProgramCode,
            application.ScenarioCode,
            application.SubmissionChannel,
            application.SubmittedAt,
            application.Status,
            application.IsSynthetic,
            applicant = application.Applicant
        });
    }

    private static async Task<IResult> ConvertToCaseAsync(
        Guid applicationId,
        ConvertToCaseRequest request,
        HttpContext context,
        NorthstarDbContext database,
        DemoIdentityService identities,
        AuditWriter audit,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var actor = await identities.ResolveAsync(context, database, cancellationToken);
        if (actor?.Role is not ("Caseworker" or "Administrator"))
        {
            await audit.WriteAsync(database, context, actor, "case.create", "Application", applicationId.ToString(), "Denied", "ROLE_REQUIRED", cancellationToken: cancellationToken);
            return actor is null ? Unauthorized(context) : Forbidden(context, "Case conversion requires a caseworker or administrator.");
        }

        var existing = await database.Cases.AsNoTracking().SingleOrDefaultAsync(item => item.ApplicationId == applicationId, cancellationToken);
        if (existing is not null)
        {
            return Results.Ok(ToCaseSummary(existing));
        }

        var application = await database.Applications.SingleOrDefaultAsync(item => item.Id == applicationId, cancellationToken);
        if (application is null)
        {
            return Results.NotFound(Error(context, "NOT_FOUND", "Application not found."));
        }

        var assignedWorkerId = request.AssignedWorkerId ?? (actor.Role == "Caseworker" ? actor.UserId : null);
        if (string.IsNullOrWhiteSpace(assignedWorkerId)
            || !await HasRoleAsync(database, assignedWorkerId, "Caseworker", cancellationToken))
        {
            return BadRequest(context, "INVALID_ASSIGNMENT", "A valid synthetic caseworker assignment is required.");
        }

        var sequence = await database.Cases.CountAsync(cancellationToken) + 1048;
        var caseRecord = new CaseRecord
        {
            CaseNumber = $"NS-{sequence}",
            ApplicationId = application.Id,
            ProgramCode = application.ProgramCode,
            Status = "Open",
            Priority = request.Priority,
            AssignedWorkerId = assignedWorkerId,
            SubmittedAt = timeProvider.GetUtcNow()
        };
        application.Status = "Converted";
        application.UpdatedAt = timeProvider.GetUtcNow();
        database.Cases.Add(caseRecord);
        await database.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(database, context, actor, "case.create", "Case", caseRecord.Id.ToString(), "Succeeded", "APPLICATION_CONVERTED", caseRecord.Id, cancellationToken: cancellationToken);

        return Results.Created($"/api/v1/cases/{caseRecord.Id}", ToCaseSummary(caseRecord));
    }

    private static async Task<IResult> GetCasesAsync(
        HttpContext context,
        NorthstarDbContext database,
        DemoIdentityService identities,
        AuditWriter audit,
        CancellationToken cancellationToken)
    {
        var actor = await identities.ResolveAsync(context, database, cancellationToken);
        if (actor is null)
        {
            await audit.WriteAsync(database, context, null, "case.list", "Case", "collection", "Denied", "AUTHENTICATION_REQUIRED", cancellationToken: cancellationToken);
            return Unauthorized(context);
        }

        var query = database.Cases.AsNoTracking().AsQueryable();
        if (actor.Role == "Caseworker")
        {
            query = query.Where(item => item.AssignedWorkerId == actor.UserId);
        }
        else if (actor.Role != "Administrator")
        {
            await audit.WriteAsync(database, context, actor, "case.list", "Case", "collection", "Denied", "ROLE_REQUIRED", cancellationToken: cancellationToken);
            return Forbidden(context, "Your role cannot list cases.");
        }

        return Results.Ok(await query.OrderBy(item => item.CaseNumber).Select(item => ToCaseSummary(item)).ToListAsync(cancellationToken));
    }

    private static async Task<IResult> GetCaseAsync(
        Guid caseId,
        HttpContext context,
        NorthstarDbContext database,
        DemoIdentityService identities,
        AuditWriter audit,
        CancellationToken cancellationToken)
    {
        var actor = await identities.ResolveAsync(context, database, cancellationToken);
        if (actor is null)
        {
            await audit.WriteAsync(database, context, null, "case.read", "Case", caseId.ToString(), "Denied", "AUTHENTICATION_REQUIRED", caseId, cancellationToken: cancellationToken);
            return Unauthorized(context);
        }

        var caseRecord = await database.Cases.AsNoTracking()
            .Include(item => item.Application).ThenInclude(item => item!.Applicant)
            .SingleOrDefaultAsync(item => item.Id == caseId, cancellationToken);
        if (caseRecord is null)
        {
            return Results.NotFound(Error(context, "NOT_FOUND", "Case not found."));
        }

        if (!CanAccessCase(actor, caseRecord))
        {
            await audit.WriteAsync(database, context, actor, "case.read", "Case", caseId.ToString(), "Denied", "CASE_ACCESS_DENIED", caseId, cancellationToken: cancellationToken);
            return Forbidden(context, "You are not authorized to access this case.");
        }

        var background = await database.CaseNotes.AsNoTracking()
            .Where(item => item.CaseId == caseId && item.NoteType == "CaseBackground")
            .Select(item => item.Content)
            .FirstOrDefaultAsync(cancellationToken);

        await audit.WriteAsync(database, context, actor, "case.read", "Case", caseId.ToString(), "Succeeded", "CASE_ACCESS_ALLOWED", caseId, cancellationToken: cancellationToken);
        return Results.Ok(new
        {
            summary = ToCaseSummary(caseRecord),
            application = new
            {
                caseRecord.Application!.ApplicationNumber,
                caseRecord.Application.SubmissionChannel,
                applicant = caseRecord.Application.Applicant
            },
            background
        });
    }

    private static async Task<IResult> AssignCaseAsync(
        Guid caseId,
        AssignCaseRequest request,
        HttpContext context,
        NorthstarDbContext database,
        DemoIdentityService identities,
        AuditWriter audit,
        CancellationToken cancellationToken)
    {
        var actor = await identities.ResolveAsync(context, database, cancellationToken);
        if (actor?.Role != "Administrator")
        {
            await audit.WriteAsync(database, context, actor, "case.assign", "Case", caseId.ToString(), "Denied", "ADMIN_REQUIRED", caseId, cancellationToken: cancellationToken);
            return actor is null ? Unauthorized(context) : Forbidden(context, "Case assignment requires an administrator.");
        }

        if (!await HasRoleAsync(database, request.AssignedWorkerId, "Caseworker", cancellationToken))
        {
            return BadRequest(context, "INVALID_ASSIGNMENT", "The assignee must have the Caseworker role.");
        }

        var caseRecord = await database.Cases.SingleOrDefaultAsync(item => item.Id == caseId, cancellationToken);
        if (caseRecord is null)
        {
            return Results.NotFound(Error(context, "NOT_FOUND", "Case not found."));
        }

        caseRecord.AssignedWorkerId = request.AssignedWorkerId;
        caseRecord.Version++;
        await database.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(database, context, actor, "case.assign", "Case", caseId.ToString(), "Succeeded", "CASE_ASSIGNED", caseId, metadata: new Dictionary<string, object?> { ["assignedWorkerId"] = request.AssignedWorkerId }, cancellationToken: cancellationToken);
        return Results.Ok(ToCaseSummary(caseRecord));
    }

    private static async Task<IResult> AddCaseNoteAsync(
        Guid caseId,
        AddCaseNoteRequest request,
        HttpContext context,
        NorthstarDbContext database,
        DemoIdentityService identities,
        AuditWriter audit,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var actor = await identities.ResolveAsync(context, database, cancellationToken);
        if (actor is null)
        {
            await audit.WriteAsync(database, context, null, "case.note.create", "Case", caseId.ToString(), "Denied", "AUTHENTICATION_REQUIRED", caseId, cancellationToken: cancellationToken);
            return Unauthorized(context);
        }

        var caseRecord = await database.Cases.SingleOrDefaultAsync(item => item.Id == caseId, cancellationToken);
        if (caseRecord is null)
        {
            return Results.NotFound(Error(context, "NOT_FOUND", "Case not found."));
        }

        if (!CanAccessCase(actor, caseRecord))
        {
            await audit.WriteAsync(database, context, actor, "case.note.create", "Case", caseId.ToString(), "Denied", "CASE_ACCESS_DENIED", caseId, cancellationToken: cancellationToken);
            return Forbidden(context, "You are not authorized to add a note to this case.");
        }

        if (string.IsNullOrWhiteSpace(request.Content) || request.Content.Length > 4_000)
        {
            return BadRequest(context, "VALIDATION_FAILED", "Note content must contain 1 to 4,000 characters.");
        }

        var note = new CaseNote
        {
            CaseId = caseId,
            AuthorUserId = actor.UserId,
            NoteType = request.NoteType,
            Content = request.Content.Trim(),
            SourceRequestId = request.SourceRequestId,
            CreatedAt = timeProvider.GetUtcNow()
        };
        database.CaseNotes.Add(note);
        await database.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(database, context, actor, "case.note.create", "CaseNote", note.Id.ToString(), "Succeeded", "CASE_NOTE_CREATED", caseId, cancellationToken: cancellationToken);
        return Results.Created($"/api/v1/cases/{caseId}/notes/{note.Id}", note);
    }

    private static async Task<IResult> UpdateCaseStatusAsync(
        Guid caseId,
        UpdateCaseStatusRequest request,
        HttpContext context,
        NorthstarDbContext database,
        DemoIdentityService identities,
        AuditWriter audit,
        CancellationToken cancellationToken)
    {
        var actor = await identities.ResolveAsync(context, database, cancellationToken);
        if (actor is null)
        {
            await audit.WriteAsync(database, context, null, "case.status.update", "Case", caseId.ToString(), "Denied", "AUTHENTICATION_REQUIRED", caseId, cancellationToken: cancellationToken);
            return Unauthorized(context);
        }

        var caseRecord = await database.Cases.SingleOrDefaultAsync(item => item.Id == caseId, cancellationToken);
        if (caseRecord is null)
        {
            return Results.NotFound(Error(context, "NOT_FOUND", "Case not found."));
        }

        if (!CanAccessCase(actor, caseRecord))
        {
            await audit.WriteAsync(database, context, actor, "case.status.update", "Case", caseId.ToString(), "Denied", "CASE_ACCESS_DENIED", caseId, cancellationToken: cancellationToken);
            return Forbidden(context, "You are not authorized to update this case.");
        }

        if (!AllowedStatuses.Contains(request.Status))
        {
            return BadRequest(context, "VALIDATION_FAILED", "Unsupported case status.");
        }

        var ifMatch = context.Request.Headers.IfMatch.FirstOrDefault()?.Trim('"');
        if (!uint.TryParse(ifMatch, out var expectedVersion) || expectedVersion != caseRecord.Version)
        {
            return Results.Json(Error(context, "CONCURRENCY_CONFLICT", "The case changed; refresh and retry."), statusCode: StatusCodes.Status412PreconditionFailed);
        }

        caseRecord.Status = request.Status;
        caseRecord.Version++;
        await database.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(database, context, actor, "case.status.update", "Case", caseId.ToString(), "Succeeded", "CASE_STATUS_UPDATED", caseId, metadata: new Dictionary<string, object?> { ["status"] = request.Status, ["version"] = caseRecord.Version }, cancellationToken: cancellationToken);
        return Results.Ok(ToCaseSummary(caseRecord));
    }

    private static async Task<IResult> DecideCaseAsync(
        Guid caseId,
        CaseDecisionRequest request,
        HttpContext context,
        NorthstarDbContext database,
        DemoIdentityService identities,
        AuditWriter audit,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var actor = await identities.ResolveAsync(context, database, cancellationToken);
        if (actor is null)
        {
            await audit.WriteAsync(database, context, null, "case.decision", "Case", caseId.ToString(), "Denied", "AUTHENTICATION_REQUIRED", caseId, cancellationToken: cancellationToken);
            return Unauthorized(context);
        }

        var caseRecord = await database.Cases.SingleOrDefaultAsync(item => item.Id == caseId, cancellationToken);
        if (caseRecord is null)
        {
            return Results.NotFound(Error(context, "NOT_FOUND", "Case not found."));
        }

        // The eligibility determination is a human control. Only the assigned caseworker (or an
        // administrator) may record it; CaseAssist has no path to this endpoint.
        if (!CanAccessCase(actor, caseRecord))
        {
            await audit.WriteAsync(database, context, actor, "case.decision", "Case", caseId.ToString(), "Denied", "CASE_ACCESS_DENIED", caseId, cancellationToken: cancellationToken);
            return Forbidden(context, "You are not authorized to decide this case.");
        }

        var decision = request.Decision.Trim();
        if (decision is not ("Approve" or "Deny"))
        {
            return BadRequest(context, "VALIDATION_FAILED", "Decision must be Approve or Deny.");
        }

        var outcome = decision == "Approve" ? "Approved" : "Denied";
        caseRecord.Status = outcome;
        caseRecord.Version++;
        database.CaseNotes.Add(new CaseNote
        {
            CaseId = caseId,
            AuthorUserId = actor.UserId,
            NoteType = "EligibilityDecision",
            Content = $"Eligibility determination: {outcome}, recorded by {actor.DisplayName}."
                + (string.IsNullOrWhiteSpace(request.Note) ? string.Empty : $" Note: {request.Note.Trim()}"),
            CreatedAt = timeProvider.GetUtcNow()
        });
        await database.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(database, context, actor, "case.decision", "Case", caseId.ToString(), "Succeeded", decision == "Approve" ? "ELIGIBILITY_APPROVED" : "ELIGIBILITY_DENIED", caseId, metadata: new Dictionary<string, object?>
        {
            ["outcome"] = outcome,
            ["decidedByUserId"] = actor.UserId,
            ["decidedByRole"] = actor.Role,
            ["noteProvided"] = !string.IsNullOrWhiteSpace(request.Note)
        }, cancellationToken: cancellationToken);
        return Results.Ok(ToCaseSummary(caseRecord));
    }

    private static async Task<IResult> RestoreCaseloadAsync(
        HttpContext context,
        NorthstarDbContext database,
        DemoIdentityService identities,
        AuditWriter audit,
        IWebHostEnvironment environment,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var actor = await identities.ResolveAsync(context, database, cancellationToken);
        if (actor is null) return Unauthorized(context);
        if (actor.Role is not ("Caseworker" or "Administrator"))
            return Forbidden(context, "Restoring the demo caseload requires a caseworker or administrator.");
        if (!IsDemoMode(environment, configuration))
            return Forbidden(context, "Restore is disabled outside demo or development mode.");

        var numbers = SeededCaseStatuses.Select(item => item.CaseNumber).ToArray();
        var restored = 0;
        foreach (var (number, status) in SeededCaseStatuses)
        {
            var caseRecord = await database.Cases.SingleOrDefaultAsync(item => item.CaseNumber == number, cancellationToken);
            if (caseRecord is null) continue;
            caseRecord.Status = status;
            caseRecord.Version++;
            restored++;
        }
        await database.SaveChangesAsync(cancellationToken);

        var caseIds = await database.Cases.AsNoTracking()
            .Where(item => numbers.Contains(item.CaseNumber))
            .Select(item => item.Id)
            .ToListAsync(cancellationToken);
        await database.CaseNotes
            .Where(item => caseIds.Contains(item.CaseId) && item.NoteType == "EligibilityDecision")
            .ExecuteDeleteAsync(cancellationToken);

        await audit.WriteAsync(database, context, actor, "demo.restore-caseload", "SyntheticData", "caseload", "Succeeded", "CASELOAD_RESTORED", metadata: new Dictionary<string, object?> { ["restored"] = restored }, cancellationToken: cancellationToken);
        return Results.Ok(new { restored });
    }

    private static async Task<IResult> GetAuditEventsAsync(
        int? take,
        HttpContext context,
        NorthstarDbContext database,
        DemoIdentityService identities,
        AuditWriter audit,
        CancellationToken cancellationToken)
    {
        var actor = await identities.ResolveAsync(context, database, cancellationToken);
        if (actor is null)
        {
            await audit.WriteAsync(database, context, null, "audit.list", "AuditEvent", "collection", "Denied", "AUTHENTICATION_REQUIRED", cancellationToken: cancellationToken);
            return Unauthorized(context);
        }
        if (actor.Role != "Administrator")
        {
            await audit.WriteAsync(database, context, actor, "audit.list", "AuditEvent", "collection", "Denied", "ADMIN_REQUIRED", cancellationToken: cancellationToken);
            return Forbidden(context, "Audit events require the Administrator role.");
        }

        var limit = Math.Clamp(take ?? 50, 1, 100);
        var events = await database.AuditEvents.AsNoTracking().ToListAsync(cancellationToken);
        return Results.Ok(events.OrderByDescending(item => item.Timestamp).Take(limit));
    }

    private static async Task<IResult> GenerateCasesAsync(
        GenerateCasesRequest request,
        HttpContext context,
        NorthstarDbContext database,
        DemoIdentityService identities,
        AuditWriter audit,
        TimeProvider timeProvider,
        IWebHostEnvironment environment,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var actor = await identities.ResolveAsync(context, database, cancellationToken);
        if (actor?.Role != "Administrator")
        {
            await audit.WriteAsync(database, context, actor, "synthetic.generate", "SyntheticData", "batch", "Denied", "ADMIN_REQUIRED", cancellationToken: cancellationToken);
            return actor is null ? Unauthorized(context) : Forbidden(context, "Synthetic generation requires the Administrator role.");
        }

        if (!IsDemoMode(environment, configuration))
        {
            return Forbidden(context, "Synthetic generation is disabled outside demo or development mode.");
        }

        if (request.Count is < 1 or > 25)
        {
            return BadRequest(context, "VALIDATION_FAILED", "Count must be between 1 and 25.");
        }

        var random = new Random(request.Seed);
        var generated = new List<object>(request.Count);
        for (var index = 0; index < request.Count; index++)
        {
            var scenario = GoldenScenarios[random.Next(GoldenScenarios.Length)];
            var ordinal = index + 1;
            var idempotencyKey = $"synthetic-{request.Seed}-{ordinal}";
            context.Request.Headers["Idempotency-Key"] = idempotencyKey;
            var phoneSuffix = Math.Abs(request.Seed * 31 + ordinal) % 100;
            var applicationRequest = new CreateApplicationRequest(
                scenario.ProgramCode,
                "EmployeeAssisted",
                new ApplicantInput(
                    $"{scenario.DisplayName} {ordinal}",
                    $"case-{request.Seed}-{ordinal}@example.test",
                    $"202-555-01{phoneSuffix:00}",
                    $"{100 + ordinal} Fictional Avenue, Northstar, VA 00000",
                    1 + random.Next(5)),
                scenario.Code);

            await CreateApplicationAsync(applicationRequest, context, database, identities, audit, timeProvider, cancellationToken);
            var application = await database.Applications.AsNoTracking()
                .SingleAsync(item => item.IdempotencyKey == idempotencyKey, cancellationToken);
            await ConvertToCaseAsync(
                application.Id,
                new ConvertToCaseRequest("maya.chen", scenario.Priority),
                context,
                database,
                identities,
                audit,
                timeProvider,
                cancellationToken);
            var caseRecord = await database.Cases.AsNoTracking()
                .SingleAsync(item => item.ApplicationId == application.Id, cancellationToken);
            generated.Add(new
            {
                scenario = scenario.Code,
                applicationId = application.Id,
                application.ApplicationNumber,
                caseId = caseRecord.Id,
                caseRecord.CaseNumber,
                application.IsSynthetic
            });
        }

        context.Request.Headers.Remove("Idempotency-Key");
        await audit.WriteAsync(database, context, actor, "synthetic.generate", "SyntheticData", $"seed:{request.Seed}", "Succeeded", "SYNTHETIC_BATCH_GENERATED", metadata: new Dictionary<string, object?>
        {
            ["seed"] = request.Seed,
            ["count"] = request.Count,
            ["datasetVersion"] = "golden-v1"
        }, cancellationToken: cancellationToken);
        return Results.Ok(new { request.Seed, request.Count, datasetVersion = "golden-v1", cases = generated });
    }

    private static async Task<IResult> ResetSyntheticDataAsync(
        ResetSyntheticDataRequest request,
        HttpContext context,
        NorthstarDbContext database,
        DemoIdentityService identities,
        AuditWriter audit,
        IWebHostEnvironment environment,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var actor = await identities.ResolveAsync(context, database, cancellationToken);
        if (actor?.Role != "Administrator")
        {
            await audit.WriteAsync(database, context, actor, "synthetic.reset", "SyntheticData", "all", "Denied", "ADMIN_REQUIRED", cancellationToken: cancellationToken);
            return actor is null ? Unauthorized(context) : Forbidden(context, "Synthetic reset requires the Administrator role.");
        }

        if (!IsDemoMode(environment, configuration))
        {
            return Forbidden(context, "Synthetic reset is disabled outside demo or development mode.");
        }

        if (!string.Equals(request.Confirmation, "DELETE SYNTHETIC DATA", StringComparison.Ordinal))
        {
            return BadRequest(context, "CONFIRMATION_REQUIRED", "Exact synthetic reset confirmation is required.");
        }

        var deletedReviews = await database.ReviewItems.Where(item => item.IsSynthetic).ExecuteDeleteAsync(cancellationToken);
        var deletedAiRequests = await database.AIRequests.Where(item => item.IsSynthetic).ExecuteDeleteAsync(cancellationToken);
        var deletedEvaluations = await database.EvaluationRuns.Where(item => item.IsSynthetic).ExecuteDeleteAsync(cancellationToken);
        var deletedDocuments = await database.CaseDocuments.Where(item => item.IsSynthetic).ExecuteDeleteAsync(cancellationToken);
        var deletedNotes = await database.CaseNotes.Where(item => item.IsSynthetic).ExecuteDeleteAsync(cancellationToken);
        var deletedCases = await database.Cases.Where(item => item.IsSynthetic).ExecuteDeleteAsync(cancellationToken);
        var deletedApplications = await database.Applications.Where(item => item.IsSynthetic).ExecuteDeleteAsync(cancellationToken);
        var deletedApplicants = await database.Applicants.Where(item => item.IsSynthetic).ExecuteDeleteAsync(cancellationToken);
        var deletedAuditEvents = await database.AuditEvents.Where(item => item.IsSynthetic).ExecuteDeleteAsync(cancellationToken);

        await audit.WriteAsync(database, context, actor, "synthetic.reset", "SyntheticData", "all", "Succeeded", "SYNTHETIC_DATA_RESET", metadata: new Dictionary<string, object?>
        {
            ["notes"] = deletedNotes,
            ["documents"] = deletedDocuments,
            ["reviews"] = deletedReviews,
            ["aiRequests"] = deletedAiRequests,
            ["evaluations"] = deletedEvaluations,
            ["cases"] = deletedCases,
            ["applications"] = deletedApplications,
            ["applicants"] = deletedApplicants,
            ["priorAuditEvents"] = deletedAuditEvents
        }, cancellationToken: cancellationToken);
        return Results.Ok(new { deletedReviews, deletedAiRequests, deletedEvaluations, deletedDocuments, deletedNotes, deletedCases, deletedApplications, deletedApplicants, deletedAuditEvents });
    }

    private static string? ValidateApplication(CreateApplicationRequest request)
    {
        if (!AllowedPrograms.Contains(request.ProgramCode)) return "Unsupported program code.";
        if (request.SubmissionChannel is not ("CitizenDemo" or "EmployeeAssisted")) return "Unsupported submission channel.";
        if (string.IsNullOrWhiteSpace(request.Applicant.SyntheticDisplayName)) return "Synthetic display name is required.";
        if (!EmailPattern().IsMatch(request.Applicant.Email)) return "A valid reserved .test email is required.";
        if (request.Applicant.HouseholdSize is < 1 or > 20) return "Household size must be between 1 and 20.";
        return null;
    }

    private static bool CanAccessCase(DemoActor actor, CaseRecord caseRecord) =>
        actor.Role == "Administrator"
        || actor.Role == "Caseworker" && caseRecord.AssignedWorkerId == actor.UserId;

    private static Task<bool> HasRoleAsync(NorthstarDbContext database, string userId, string role, CancellationToken cancellationToken) =>
        database.RoleAssignments.AnyAsync(item => item.UserId == userId && item.Role == role, cancellationToken);

    private static object ToApplicationSummary(ApplicationRecord item) => new
    {
        item.Id,
        item.ApplicationNumber,
        item.ProgramCode,
        item.ScenarioCode,
        item.SubmissionChannel,
        item.SubmittedAt,
        item.Status,
        item.IsSynthetic
    };

    private static object ToCaseSummary(CaseRecord item) => new
    {
        item.Id,
        item.CaseNumber,
        item.ProgramCode,
        item.Status,
        item.Priority,
        item.AssignedWorkerId,
        item.SubmittedAt,
        item.IsSynthetic,
        item.Version
    };

    private static ErrorEnvelope Error(HttpContext context, string code, string message) =>
        new(code, message, RequestContext.CorrelationId(context));

    private static IResult Unauthorized(HttpContext context) =>
        Results.Json(Error(context, "AUTHENTICATION_REQUIRED", "A recognized synthetic demo identity is required."), statusCode: StatusCodes.Status401Unauthorized);

    private static IResult Forbidden(HttpContext context, string message) =>
        Results.Json(Error(context, "ACCESS_DENIED", message), statusCode: StatusCodes.Status403Forbidden);

    private static IResult BadRequest(HttpContext context, string code, string message) =>
        Results.BadRequest(Error(context, code, message));

    private static bool IsDemoMode(IWebHostEnvironment environment, IConfiguration configuration) =>
        environment.IsDevelopment()
        || environment.IsEnvironment("Testing")
        || configuration.GetValue<bool>("Northstar:DemoMode");

    private static readonly GoldenScenario[] GoldenScenarios =
    [
        new("complete-utility-relief", "UTILITY_RELIEF", "Elena Example", "Standard"),
        new("incomplete-utility-documents", "UTILITY_RELIEF", "Jordan Sample", "High"),
        new("housing-stability", "HOUSING_STABILITY", "Morgan Example", "Standard"),
        new("workforce-training-grant", "WORKFORCE_TRAINING", "Riley Sample", "Standard"),
        new("duplicate-application", "UTILITY_RELIEF", "Cameron Fiction", "Standard"),
        new("conflicting-document-values", "HOUSING_STABILITY", "Taylor Example", "High"),
        new("unauthorized-case-access", "UTILITY_RELIEF", "Quinn Sample", "Standard"),
        new("pii-redaction-test", "UTILITY_RELIEF", "Parker Fiction", "Standard"),
        new("document-prompt-injection", "WORKFORCE_TRAINING", "Drew Example", "High"),
        new("harmless-drafting-request", "HOUSING_STABILITY", "Skyler Sample", "Standard"),
        new("protected-decision-request", "UTILITY_RELIEF", "Casey Fiction", "High"),
        new("unsupported-citation", "WORKFORCE_TRAINING", "Reese Example", "High")
    ];

    private sealed record GoldenScenario(string Code, string ProgramCode, string DisplayName, string Priority);

    [GeneratedRegex(@"^[A-Z0-9._%+-]+@[A-Z0-9.-]+\.test$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();
}
