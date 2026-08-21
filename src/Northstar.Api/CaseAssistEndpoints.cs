using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Northstar.Application.Assistant;
using Northstar.Application.Pii;
using Northstar.Application.Evaluation;
using Northstar.Domain;
using Northstar.Infrastructure;

namespace Northstar.Api;

public static class CaseAssistEndpoints
{
    private static readonly JsonSerializerOptions TraceJsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapCaseAssistEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/v1");
        api.MapPost("/case-assist/requests", CreateRequestAsync);
        api.MapGet("/case-assist/requests/{requestId}/safety-trace", GetSafetyTraceAsync);
        api.MapGet("/policies", GetPoliciesAsync);
        api.MapGet("/reviews", GetReviewsAsync);
        api.MapGet("/reviews/{reviewId:guid}", GetReviewAsync);
        api.MapPost("/reviews/{reviewId:guid}/decision", DecideReviewAsync);
        api.MapGet("/governance/system-registry", GetSystemRegistryAsync);
        api.MapGet("/governance/evaluations", GetEvaluationsAsync);
        api.MapPost("/governance/evaluations/run", RunEvaluationAsync);
        return endpoints;
    }

    private static async Task<IResult> CreateRequestAsync(
        CreateCaseAssistRequest request,
        HttpContext context,
        NorthstarDbContext database,
        DemoIdentityService identities,
        AuditWriter audit,
        IPiiRedactor redactor,
        IPolicyRetriever retriever,
        IAssistantModelProvider modelProvider,
        IContentSafetyScanner contentSafetyScanner,
        PromptInjectionDetector injectionDetector,
        CitationValidator citationValidator,
        RiskClassifier riskClassifier,
        IConfiguration configuration,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var actor = await identities.ResolveAsync(context, database, cancellationToken);
        if (actor is null)
        {
            await audit.WriteAsync(database, context, null, "caseassist.request", "Case", request.CaseId.ToString(), "Denied", "AUTHENTICATION_REQUIRED", request.CaseId, cancellationToken: cancellationToken);
            return Unauthorized(context);
        }

        var caseRecord = await database.Cases.AsNoTracking().SingleOrDefaultAsync(item => item.Id == request.CaseId, cancellationToken);
        if (caseRecord is null)
        {
            return Results.NotFound(Error(context, "NOT_FOUND", "Case not found."));
        }

        if (actor.Role != "Caseworker" || caseRecord.AssignedWorkerId != actor.UserId)
        {
            await audit.WriteAsync(database, context, actor, "caseassist.request", "Case", request.CaseId.ToString(), "Denied", "CASE_ACCESS_DENIED", request.CaseId, cancellationToken: cancellationToken);
            return Forbidden(context, "Only the assigned caseworker may create a CaseAssist request.");
        }

        if (string.IsNullOrWhiteSpace(request.Question) || request.Question.Length > 2_000)
        {
            return Results.BadRequest(Error(context, "VALIDATION_FAILED", "Question must contain 1 to 2,000 characters."));
        }

        var now = timeProvider.GetUtcNow();
        var dayStart = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero);
        var dailyRequestLimit = Math.Clamp(configuration.GetValue<int?>("AI:DailyRequestLimit") ?? 40, 1, 500);
        var dailyCostLimit = Math.Clamp(configuration.GetValue<decimal?>("AI:DailyCostLimitUsd") ?? 0.25m, 0.01m, 25m);
        var actorUsage = await database.AIRequests.AsNoTracking()
            .Where(item => item.RequestedByUserId == actor.UserId)
            .Select(item => new { item.CreatedAt, item.EstimatedCost })
            .ToListAsync(cancellationToken);
        var usageToday = actorUsage.Where(item => item.CreatedAt >= dayStart).ToList();
        var dailyRequestCount = usageToday.Count;
        var dailyEstimatedCost = usageToday.Sum(item => item.EstimatedCost);
        if (dailyRequestCount >= dailyRequestLimit || dailyEstimatedCost >= dailyCostLimit)
        {
            await audit.WriteAsync(database, context, actor, "caseassist.request", "Case", request.CaseId.ToString(), "Denied", "DAILY_AI_BUDGET_LIMIT", request.CaseId, metadata: new Dictionary<string, object?>
            {
                ["dailyRequestCount"] = dailyRequestCount,
                ["dailyEstimatedCost"] = dailyEstimatedCost,
                ["dailyRequestLimit"] = dailyRequestLimit,
                ["dailyCostLimitUsd"] = dailyCostLimit
            }, cancellationToken: cancellationToken);
            return Results.Json(
                Error(context, "DAILY_AI_BUDGET_LIMIT", "This demo identity has reached its daily live-AI allowance."),
                statusCode: StatusCodes.Status429TooManyRequests);
        }

        var requestId = $"REQ-{now:yyyyMMdd}-{Guid.NewGuid():N}";
        var auditEventIds = new List<string>();
        auditEventIds.Add(await audit.WriteAsync(database, context, actor, "case.access", "Case", request.CaseId.ToString(), "Succeeded", "CASE_ACCESS_ALLOWED", request.CaseId, requestId, cancellationToken: cancellationToken));

        var documentRecords = await database.CaseDocuments.AsNoTracking()
            .Where(item => item.CaseId == caseRecord.Id)
            .Select(item => new { item.OriginalFileName, item.ScanStatus })
            .ToListAsync(cancellationToken);
        var documentStatuses = documentRecords
            .GroupBy(item => item.ScanStatus)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToList();
        // The document type is carried in the file name (for example "Household income
        // evidence.txt"). Only documents that passed the safety scan are treated as usable
        // evidence on file; the model compares this list against the policy requirements.
        var presentDocumentTypes = documentRecords
            .Where(item => item.ScanStatus == "Passed")
            .Select(item => Path.GetFileNameWithoutExtension(item.OriginalFileName))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var householdSize = await database.Applications.AsNoTracking()
            .Where(item => item.Id == caseRecord.ApplicationId)
            .Select(item => (int?)item.Applicant!.HouseholdSize)
            .FirstOrDefaultAsync(cancellationToken);
        var background = await database.CaseNotes.AsNoTracking()
            .Where(item => item.CaseId == caseRecord.Id && item.NoteType == "CaseBackground")
            .Select(item => item.Content)
            .FirstOrDefaultAsync(cancellationToken);

        // Data-handling policy (DHR-4.2) governs which identifiers may reach the AI. Every
        // free-text input the model could see — the question, the case background, and
        // document-type labels — is redacted of PROHIBITED categories only (SSN, address,
        // name); PERMITTED categories (email, phone, case reference) are retained so the
        // assistant can use them when the task calls for it.
        var prohibited = DataHandlingPolicy.ProhibitedFromAi;
        var redaction = redactor.Redact(request.Question, prohibited);
        var backgroundSafe = string.IsNullOrWhiteSpace(background) ? null : redactor.Redact(background, prohibited);
        var safeBackground = backgroundSafe?.RedactedText;
        var safeDocumentTypes = presentDocumentTypes
            .Select(item => redactor.Redact(item, prohibited).RedactedText)
            .ToList();

        // Full detection (all categories) across the same inputs, so the safety trace can
        // report both what was removed and what was retained under policy.
        var detectionInputs = new List<string> { request.Question };
        if (!string.IsNullOrWhiteSpace(background)) detectionInputs.Add(background!);
        detectionInputs.AddRange(presentDocumentTypes);
        var detectedByCategory = detectionInputs
            .SelectMany(text => redactor.Redact(text).Summary)
            .GroupBy(item => item.EntityType, StringComparer.Ordinal)
            .Select(group => new RedactionCount(group.Key, group.Sum(item => item.Count)))
            .ToArray();
        var redactedTypes = detectedByCategory
            .Where(item => DataHandlingPolicy.ProhibitedFromAi.Contains(item.EntityType))
            .OrderBy(item => item.EntityType, StringComparer.Ordinal)
            .ToArray();
        var permittedTypes = detectedByCategory
            .Where(item => DataHandlingPolicy.PermittedForAi.Contains(item.EntityType))
            .OrderBy(item => item.EntityType, StringComparer.Ordinal)
            .ToArray();
        var redactedCount = redactedTypes.Sum(item => item.Count);

        auditEventIds.Add(await audit.WriteAsync(database, context, actor, "pii.redact", "AIRequest", requestId, "Succeeded", redactedCount > 0 ? "PII_REDACTED" : "NO_PII_DETECTED", request.CaseId, requestId, metadata: new Dictionary<string, object?>
        {
            ["prohibitedRedacted"] = redactedTypes.Select(item => item.EntityType).ToArray(),
            ["permittedRetained"] = permittedTypes.Select(item => item.EntityType).ToArray(),
            ["count"] = redactedCount,
            ["policySource"] = DataHandlingPolicy.SourceId
        }, cancellationToken: cancellationToken));

        var injectionReasons = injectionDetector.Detect(redaction.RedactedText);
        var programLabel = caseRecord.ProgramCode.Replace('_', ' ').ToLowerInvariant();
        var caseContext = $"Case number: {caseRecord.CaseNumber}; program: {programLabel}; "
            + $"case status: {caseRecord.Status}; priority: {caseRecord.Priority}; "
            + $"submitted: {caseRecord.SubmittedAt:yyyy-MM-dd}; "
            + $"household size: {(householdSize is > 0 ? householdSize.ToString() : "not recorded")}; "
            + $"uploaded document count: {documentStatuses.Sum(item => item.Count)}; "
            + $"document scan summary: {(documentStatuses.Count > 0 ? string.Join(", ", documentStatuses.Select(item => $"{item.Status}={item.Count}")) : "none")}. "
            + $"Documents on file that passed the safety scan (these are the document types provided for this case): {(safeDocumentTypes.Count > 0 ? string.Join("; ", safeDocumentTypes) : "none")}. "
            + $"Case background (synthetic, non-identifying): {(string.IsNullOrWhiteSpace(safeBackground) ? "none recorded" : safeBackground)}. "
            + "The list of documents on file is authoritative for what has been provided; the contents of each document are not supplied, so do not assert what a document says, only whether that document type is present.";
        // Retrieval runs the full pipeline: the question is expanded into similar queries, each
        // query is searched with both dense vectors and BM25 sparse vectors, the ranked lists are
        // fused with reciprocal rank fusion, and the shortlist is reranked with late-interaction
        // scoring. Every stage is recorded so the safety trace can show why a section was used.
        var retrieval = await retriever.SearchAsync(caseRecord.ProgramCode, redaction.RedactedText, 3, cancellationToken);
        var sources = retrieval.Hits;
        var retrievalTrace = retrieval.Diagnostics;
        var searchedCount = await database.PolicySections.CountAsync(item => item.IsApproved && (item.ProgramCode == caseRecord.ProgramCode || item.ProgramCode == "ALL"), cancellationToken);
        auditEventIds.Add(await audit.WriteAsync(database, context, actor, "policy.retrieve", "AIRequest", requestId, sources.Count > 0 ? "Succeeded" : "Failed", sources.Count > 0 ? "APPROVED_SOURCES_RETRIEVED" : "NO_APPROVED_SOURCE", request.CaseId, requestId, metadata: new Dictionary<string, object?>
        {
            ["searchedCount"] = searchedCount,
            ["retrievedSourceIds"] = sources.Select(item => item.SourceId).ToArray(),
            ["strategy"] = retrievalTrace.Strategy,
            ["candidateSource"] = retrievalTrace.CandidateSource,
            ["queryCount"] = retrievalTrace.Queries.Count,
            ["candidateCount"] = retrievalTrace.CandidateCount,
            ["denseModel"] = retrievalTrace.DenseModel,
            ["sparseModel"] = retrievalTrace.SparseModel,
            ["rerankModel"] = retrievalTrace.RerankModel,
            ["elapsedMilliseconds"] = retrievalTrace.ElapsedMilliseconds
        }, cancellationToken: cancellationToken));

        var generation = await modelProvider.GenerateAsync(new ModelRequest(
            requestId,
            redaction.RedactedText,
            caseRecord.ProgramCode,
            caseContext,
            sources,
            "northstar-prompt-v1",
            450), cancellationToken);
        auditEventIds.Add(await audit.WriteAsync(database, context, actor, "model.generate", "AIRequest", requestId, "Succeeded", generation.IsLive ? "LIVE_MODEL_COMPLETED" : "OFFLINE_FIXTURE_COMPLETED", request.CaseId, requestId, metadata: new Dictionary<string, object?>
        {
            ["provider"] = generation.Provider,
            ["model"] = generation.ModelName,
            ["tokenUsage"] = generation.TokenUsage,
            ["estimatedCost"] = generation.EstimatedCost,
            ["isLive"] = generation.IsLive
        }, cancellationToken: cancellationToken));

        // The safety trace is an audit artifact, and DHR-4.3 keeps unredacted prompts out of
        // audit records. Retrieval itself runs on the policy-permitted redaction (phone, email
        // and case reference are allowed to reach the AI); the copies stored in the trace have
        // every identifier category removed, including the permitted ones.
        var traceQueries = retrievalTrace.Queries
            .Select(item => redactor.Redact(item).RedactedText)
            .ToArray();

        var citationValidation = citationValidator.Validate(generation, sources);
        auditEventIds.Add(await audit.WriteAsync(database, context, actor, "citation.validate", "AIRequest", requestId, citationValidation.IsValid ? "Succeeded" : "Failed", citationValidation.IsValid ? "CITATIONS_VALID" : "CITATIONS_INVALID", request.CaseId, requestId, metadata: new Dictionary<string, object?>
        {
            ["reasonCodes"] = citationValidation.ReasonCodes
        }, cancellationToken: cancellationToken));

        var outputRedaction = redactor.Redact(generation.Text, prohibited);
        var contentSafety = await contentSafetyScanner.AnalyzeAsync(generation.Text, cancellationToken);
        auditEventIds.Add(await audit.WriteAsync(database, context, actor, "content-safety.analyze", "AIRequest", requestId, contentSafety.IsAllowed ? "Succeeded" : "Failed", contentSafety.IsAllowed ? "CONTENT_SAFETY_PASSED" : "CONTENT_SAFETY_REVIEW_REQUIRED", request.CaseId, requestId, metadata: new Dictionary<string, object?>
        {
            ["provider"] = contentSafety.Provider,
            ["isLive"] = contentSafety.IsLive,
            ["categories"] = contentSafety.Categories,
            ["reasonCodes"] = contentSafety.ReasonCodes
        }, cancellationToken: cancellationToken));
        var baseRisk = riskClassifier.Classify(request.Question, generation.Text, citationValidation.IsValid, outputRedaction.TotalRedactions > 0, injectionReasons);
        var combinedReasons = baseRisk.ReasonCodes.Concat(contentSafety.ReasonCodes).Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal).ToArray();
        var risk = new RiskAssessment(
            combinedReasons.Length == 0 ? baseRisk.Classification : "High",
            combinedReasons.Length > 0,
            combinedReasons);
        var status = risk.RequiresReview ? "PendingReview" : "DraftReady";
        var trace = new
        {
            requestId,
            correlationId = RequestContext.CorrelationId(context),
            user = actor.DisplayName,
            actor.Role,
            caseId = caseRecord.CaseNumber,
            caseAccess = "Allowed",
            pii = new
            {
                redactedTypes = redactedTypes,
                permittedTypes = permittedTypes,
                policySource = DataHandlingPolicy.SourceId,
                originalValuesExcluded = true,
                outputPiiDetected = outputRedaction.TotalRedactions > 0
            },
            retrieval = new
            {
                approvedSectionsSearched = searchedCount,
                strategy = retrievalTrace.Strategy,
                candidateSource = retrievalTrace.CandidateSource,
                queries = traceQueries,
                candidateCount = retrievalTrace.CandidateCount,
                dense = new
                {
                    model = retrievalTrace.DenseModel,
                    dimensions = retrievalTrace.DenseDimensions,
                    isLive = retrievalTrace.DenseIsLive
                },
                sparse = new { model = retrievalTrace.SparseModel },
                fusion = new { method = "reciprocal-rank-fusion", constant = retrievalTrace.FusionConstant },
                rerank = new { model = retrievalTrace.RerankModel, depth = retrievalTrace.RerankDepth },
                queryExpansion = new
                {
                    provider = retrievalTrace.QueryExpansionProvider,
                    isLive = retrievalTrace.QueryExpansionIsLive,
                    generatedQueries = retrievalTrace.Queries.Count
                },
                elapsedMilliseconds = retrievalTrace.ElapsedMilliseconds,
                ranking = retrievalTrace.Ranking,
                sources = sources.Select(item => new { item.SourceId, item.DocumentTitle, item.DocumentVersion, item.SectionLabel, item.Content, item.Score })
            },
            model = new
            {
                generation.Provider,
                generation.ModelName,
                promptVersion = "northstar-prompt-v1",
                generation.IsLive,
                generation.TokenUsage,
                generation.EstimatedCost
            },
            controls = new
            {
                citationsValid = citationValidation.IsValid,
                citationReasons = citationValidation.ReasonCodes,
                injectionDetected = injectionReasons.Count > 0,
                injectionReasons,
                contentSafety = new
                {
                    contentSafety.Provider,
                    contentSafety.IsLive,
                    contentSafety.IsAllowed,
                    contentSafety.Categories,
                    contentSafety.ReasonCodes
                },
                risk.Classification,
                risk.ReasonCodes,
                humanReviewRequired = risk.RequiresReview
            },
            auditEventIds
        };
        var aiRequest = new AIRequest
        {
            RequestId = requestId,
            CaseId = request.CaseId,
            RequestedByUserId = actor.UserId,
            RedactedQuestion = redaction.RedactedText,
            PromptVersion = "northstar-prompt-v1",
            ModelProvider = generation.Provider,
            ModelName = generation.ModelName,
            RedactionSummaryJson = JsonSerializer.Serialize(redactedTypes),
            RetrievedSourceIdsJson = JsonSerializer.Serialize(sources.Select(item => item.SourceId)),
            RiskClassification = risk.Classification,
            Status = status,
            TokenUsage = generation.TokenUsage,
            EstimatedCost = generation.EstimatedCost,
            AssistantOutput = generation.Text,
            SafetyTraceJson = JsonSerializer.Serialize(trace, TraceJsonOptions),
            CreatedAt = now
        };
        database.AIRequests.Add(aiRequest);

        ReviewItem? review = null;
        if (risk.RequiresReview)
        {
            review = new ReviewItem
            {
                AIRequestId = aiRequest.Id,
                CaseId = request.CaseId,
                SubmittedByUserId = actor.UserId,
                AssignedReviewerId = "marcus.reed",
                ReasonCodesJson = JsonSerializer.Serialize(risk.ReasonCodes),
                Status = "Pending",
                AssistantOutput = generation.Text
            };
            database.ReviewItems.Add(review);
        }
        await database.SaveChangesAsync(cancellationToken);

        if (review is not null)
        {
            auditEventIds.Add(await audit.WriteAsync(database, context, actor, "review.trigger", "ReviewItem", review.Id.ToString(), "Succeeded", "HUMAN_REVIEW_REQUIRED", request.CaseId, requestId, metadata: new Dictionary<string, object?>
            {
                ["reasonCodes"] = risk.ReasonCodes,
                ["assignedReviewerId"] = review.AssignedReviewerId
            }, cancellationToken: cancellationToken));
            aiRequest.SafetyTraceJson = JsonSerializer.Serialize(trace with { }, TraceJsonOptions);
            await database.SaveChangesAsync(cancellationToken);
        }

        return Results.Created($"/api/v1/case-assist/requests/{requestId}/safety-trace", new
        {
            requestId,
            output = generation.Text,
            draft = generation.Draft,
            sources,
            risk = risk.Classification,
            reasonCodes = risk.ReasonCodes,
            requiresReview = risk.RequiresReview,
            reviewId = review?.Id,
            mode = generation.IsLive ? "Live" : "OfflineFixture"
        });
    }

    private static async Task<IResult> GetPoliciesAsync(
        HttpContext context,
        NorthstarDbContext database,
        DemoIdentityService identities,
        CancellationToken cancellationToken)
    {
        var actor = await identities.ResolveAsync(context, database, cancellationToken);
        if (actor is null) return Unauthorized(context);

        var sections = await database.PolicySections.AsNoTracking()
            .Where(item => item.IsApproved)
            .ToListAsync(cancellationToken);
        var ordered = sections
            .OrderBy(item => item.DocumentTitle, StringComparer.Ordinal)
            .ThenBy(item => item.SectionLabel, StringComparer.Ordinal)
            .Select(item => new
            {
                item.Id,
                item.DocumentTitle,
                item.DocumentVersion,
                item.SectionLabel,
                item.ProgramCode,
                item.Content
            });
        return Results.Ok(ordered);
    }

    private static async Task<IResult> GetSafetyTraceAsync(
        string requestId,
        HttpContext context,
        NorthstarDbContext database,
        DemoIdentityService identities,
        AuditWriter audit,
        CancellationToken cancellationToken)
    {
        var actor = await identities.ResolveAsync(context, database, cancellationToken);
        var request = await database.AIRequests.AsNoTracking().SingleOrDefaultAsync(item => item.RequestId == requestId, cancellationToken);
        if (request is null) return Results.NotFound(Error(context, "NOT_FOUND", "AI request not found."));
        var review = await database.ReviewItems.AsNoTracking().SingleOrDefaultAsync(item => item.AIRequestId == request.Id, cancellationToken);
        var allowed = actor is not null && (actor.Role == "Administrator" || actor.UserId == request.RequestedByUserId || review?.AssignedReviewerId == actor.UserId);
        if (!allowed)
        {
            await audit.WriteAsync(database, context, actor, "safetytrace.read", "AIRequest", requestId, "Denied", actor is null ? "AUTHENTICATION_REQUIRED" : "ACCESS_DENIED", request.CaseId, requestId, cancellationToken: cancellationToken);
            return actor is null ? Unauthorized(context) : Forbidden(context, "You are not authorized to view this safety trace.");
        }

        return Results.Content(request.SafetyTraceJson, "application/json");
    }

    private static async Task<IResult> GetReviewsAsync(
        HttpContext context,
        NorthstarDbContext database,
        DemoIdentityService identities,
        CancellationToken cancellationToken)
    {
        var actor = await identities.ResolveAsync(context, database, cancellationToken);
        if (actor is null) return Unauthorized(context);
        var query = database.ReviewItems.AsNoTracking().AsQueryable();
        if (actor.Role == "Reviewer") query = query.Where(item => item.AssignedReviewerId == actor.UserId);
        else if (actor.Role == "Caseworker") query = query.Where(item => item.SubmittedByUserId == actor.UserId);
        else if (actor.Role != "Administrator") return Forbidden(context, "Your role cannot access review items.");
        return Results.Ok(await query.OrderBy(item => item.Status).ThenBy(item => item.Id).ToListAsync(cancellationToken));
    }

    private static async Task<IResult> GetReviewAsync(
        Guid reviewId,
        HttpContext context,
        NorthstarDbContext database,
        DemoIdentityService identities,
        CancellationToken cancellationToken)
    {
        var actor = await identities.ResolveAsync(context, database, cancellationToken);
        if (actor is null) return Unauthorized(context);
        var review = await database.ReviewItems.AsNoTracking().SingleOrDefaultAsync(item => item.Id == reviewId, cancellationToken);
        if (review is null) return Results.NotFound(Error(context, "NOT_FOUND", "Review item not found."));
        var allowed = actor.Role == "Administrator" || actor.UserId == review.AssignedReviewerId || actor.UserId == review.SubmittedByUserId;
        return allowed ? Results.Ok(review) : Forbidden(context, "You are not authorized to view this review item.");
    }

    private static async Task<IResult> DecideReviewAsync(
        Guid reviewId,
        ReviewDecisionRequest request,
        HttpContext context,
        NorthstarDbContext database,
        DemoIdentityService identities,
        AuditWriter audit,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var actor = await identities.ResolveAsync(context, database, cancellationToken);
        if (actor is null) return Unauthorized(context);
        var review = await database.ReviewItems.SingleOrDefaultAsync(item => item.Id == reviewId, cancellationToken);
        if (review is null) return Results.NotFound(Error(context, "NOT_FOUND", "Review item not found."));
        if (actor.Role != "Reviewer" || review.AssignedReviewerId != actor.UserId)
        {
            await audit.WriteAsync(database, context, actor, "review.decide", "ReviewItem", reviewId.ToString(), "Denied", "ASSIGNED_REVIEWER_REQUIRED", review.CaseId, cancellationToken: cancellationToken);
            return Forbidden(context, "Only the assigned reviewer may decide this item.");
        }
        if (review.SubmittedByUserId == actor.UserId)
        {
            await audit.WriteAsync(database, context, actor, "review.decide", "ReviewItem", reviewId.ToString(), "Denied", "SELF_APPROVAL_PROHIBITED", review.CaseId, cancellationToken: cancellationToken);
            return Forbidden(context, "A submitter cannot decide their own review item.");
        }

        var decision = request.Decision.Trim();
        if (decision is not ("Approve" or "Return" or "Reject"))
            return Results.BadRequest(Error(context, "VALIDATION_FAILED", "Decision must be Approve, Return, or Reject."));
        if (decision is "Return" or "Reject" && string.IsNullOrWhiteSpace(request.Note))
            return Results.BadRequest(Error(context, "NOTE_REQUIRED", "A reviewer note is required for return or rejection."));
        if (review.Status != "Pending")
            return Results.Conflict(Error(context, "ALREADY_DECIDED", "This review item has already been decided."));

        review.Decision = decision;
        review.Status = decision == "Return" ? "Returned" : decision == "Approve" ? "Approved" : "Rejected";
        review.ReviewerNote = request.Note?.Trim();
        review.DecidedAt = timeProvider.GetUtcNow();
        if (decision == "Approve")
        {
            database.CaseNotes.Add(new CaseNote
            {
                CaseId = review.CaseId,
                AuthorUserId = actor.UserId,
                NoteType = "AIApprovedDraft",
                Content = review.AssistantOutput,
                SourceRequestId = review.AIRequestId.ToString(),
                CreatedAt = timeProvider.GetUtcNow()
            });
        }
        await database.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(database, context, actor, "review.decide", "ReviewItem", reviewId.ToString(), "Succeeded", $"REVIEW_{decision.ToUpperInvariant()}", review.CaseId, metadata: new Dictionary<string, object?> { ["decision"] = decision, ["noteProvided"] = !string.IsNullOrWhiteSpace(request.Note) }, cancellationToken: cancellationToken);
        return Results.Ok(review);
    }

    private static async Task<IResult> GetSystemRegistryAsync(HttpContext context, NorthstarDbContext database, DemoIdentityService identities, CancellationToken cancellationToken)
    {
        var actor = await identities.ResolveAsync(context, database, cancellationToken);
        if (actor?.Role != "Administrator") return actor is null ? Unauthorized(context) : Forbidden(context, "System registry requires the Administrator role.");
        return Results.Ok(await database.AISystemRegistry.AsNoTracking().SingleAsync(cancellationToken));
    }

    private static async Task<IResult> GetEvaluationsAsync(HttpContext context, NorthstarDbContext database, DemoIdentityService identities, CancellationToken cancellationToken)
    {
        var actor = await identities.ResolveAsync(context, database, cancellationToken);
        if (actor?.Role != "Administrator") return actor is null ? Unauthorized(context) : Forbidden(context, "Evaluation evidence requires the Administrator role.");
        var runs = await database.EvaluationRuns.AsNoTracking().ToListAsync(cancellationToken);
        return Results.Ok(runs.OrderByDescending(item => item.CreatedAt));
    }

    private static async Task<IResult> RunEvaluationAsync(
        HttpContext context,
        NorthstarDbContext database,
        DemoIdentityService identities,
        AuditWriter audit,
        ControlEvaluationRunner runner,
        RagEvaluationRunner ragRunner,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var actor = await identities.ResolveAsync(context, database, cancellationToken);
        if (actor?.Role != "Administrator")
        {
            await audit.WriteAsync(database, context, actor, "evaluation.run", "EvaluationRun", "new", "Denied", "ADMIN_REQUIRED", cancellationToken: cancellationToken);
            return actor is null ? Unauthorized(context) : Forbidden(context, "Evaluations require the Administrator role.");
        }

        // Two dataset families in one run: the control checks prove the safety pipeline behaves,
        // and the RAG checks measure whether retrieval and grounding are any good. Both have to
        // pass for the system to be doing its job.
        var controlSummary = runner.Run();
        var ragSummary = await ragRunner.RunAsync(cancellationToken);
        var summary = new EvaluationSummary([.. controlSummary.Checks, .. ragSummary.Checks]);
        var evaluation = new EvaluationRun
        {
            RunId = $"EVAL-{timeProvider.GetUtcNow():yyyyMMdd}-{Guid.NewGuid():N}",
            EvaluationVersion = "controls-and-rag-v3",
            DatasetVersion = $"golden-v2+{RagGoldenDataset.Version}",
            PromptVersion = "northstar-prompt-v1",
            ModelVersion = "offline-fixture-v1",
            Total = summary.Total,
            Passed = summary.Passed,
            Failed = summary.Failed,
            ResultsJson = JsonSerializer.Serialize(summary.Checks),
            CreatedAt = timeProvider.GetUtcNow()
        };
        database.EvaluationRuns.Add(evaluation);
        await database.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(database, context, actor, "evaluation.run", "EvaluationRun", evaluation.Id.ToString(), summary.Failed == 0 ? "Succeeded" : "Failed", summary.Failed == 0 ? "ALL_CHECKS_PASSED" : "CHECKS_FAILED", metadata: new Dictionary<string, object?>
        {
            ["total"] = summary.Total,
            ["passed"] = summary.Passed,
            ["failed"] = summary.Failed
        }, cancellationToken: cancellationToken);
        return Results.Created($"/api/v1/governance/evaluations/{evaluation.Id}", evaluation);
    }

    private static ErrorEnvelope Error(HttpContext context, string code, string message) => new(code, message, RequestContext.CorrelationId(context));
    private static IResult Unauthorized(HttpContext context) => Results.Json(Error(context, "AUTHENTICATION_REQUIRED", "A recognized synthetic demo identity is required."), statusCode: StatusCodes.Status401Unauthorized);
    private static IResult Forbidden(HttpContext context, string message) => Results.Json(Error(context, "ACCESS_DENIED", message), statusCode: StatusCodes.Status403Forbidden);
}
