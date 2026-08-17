using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Northstar.Application.Assistant;
using Northstar.Application.Documents;
using Northstar.Domain;
using Northstar.Infrastructure;

namespace Northstar.Api;

public static class CaseDocumentEndpoints
{
    private const long MaximumDocumentBytes = 1_048_576;
    private static readonly HashSet<string> AllowedExtensions = [".md", ".txt"];

    public static IEndpointRouteBuilder MapCaseDocumentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/v1");
        api.MapGet("/cases/{caseId:guid}/documents", ListAsync);
        api.MapPost("/cases/{caseId:guid}/documents", UploadAsync).DisableAntiforgery();
        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        Guid caseId,
        HttpContext context,
        NorthstarDbContext database,
        DemoIdentityService identities,
        AuditWriter audit,
        CancellationToken cancellationToken)
    {
        var authorization = await AuthorizeCaseAsync(caseId, context, database, identities, audit, "case.document.list", cancellationToken);
        if (authorization.Error is not null) return authorization.Error;

        var storedDocuments = await database.CaseDocuments.AsNoTracking()
            .Where(item => item.CaseId == caseId)
            .ToListAsync(cancellationToken);
        var documents = storedDocuments
            .OrderByDescending(item => item.CreatedAt)
            .Select(ToResponse)
            .ToList();
        await audit.WriteAsync(database, context, authorization.Actor, "case.document.list", "Case", caseId.ToString(), "Succeeded", "CASE_DOCUMENTS_LISTED", caseId, metadata: new Dictionary<string, object?> { ["count"] = documents.Count }, cancellationToken: cancellationToken);
        return Results.Ok(documents);
    }

    private static async Task<IResult> UploadAsync(
        Guid caseId,
        IFormFile file,
        HttpContext context,
        NorthstarDbContext database,
        DemoIdentityService identities,
        AuditWriter audit,
        ICaseDocumentStore store,
        PromptInjectionDetector injectionDetector,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var authorization = await AuthorizeCaseAsync(caseId, context, database, identities, audit, "case.document.upload", cancellationToken);
        if (authorization.Error is not null) return authorization.Error;

        var safeFileName = Path.GetFileName(file.FileName);
        var extension = Path.GetExtension(safeFileName).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(safeFileName)
            || !string.Equals(safeFileName, file.FileName, StringComparison.Ordinal)
            || !AllowedExtensions.Contains(extension)
            || file.Length is <= 0 or > MaximumDocumentBytes)
        {
            await audit.WriteAsync(database, context, authorization.Actor, "case.document.upload", "Case", caseId.ToString(), "Denied", "DOCUMENT_VALIDATION_FAILED", caseId, metadata: new Dictionary<string, object?> { ["fileName"] = safeFileName, ["sizeBytes"] = file.Length }, cancellationToken: cancellationToken);
            return Results.BadRequest(Error(context, "DOCUMENT_VALIDATION_FAILED", "Upload a non-empty .txt or .md file no larger than 1 MiB."));
        }

        byte[] bytes;
        await using (var input = file.OpenReadStream())
        {
            using var buffer = new MemoryStream((int)file.Length);
            await input.CopyToAsync(buffer, cancellationToken);
            bytes = buffer.ToArray();
        }

        string text;
        try
        {
            text = new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Results.BadRequest(Error(context, "DOCUMENT_ENCODING_INVALID", "Document text must use valid UTF-8 encoding."));
        }

        var reasons = injectionDetector.Detect(text);
        var documentId = Guid.NewGuid();
        var storageKey = $"{caseId:N}/{documentId:N}{extension}";
        await using (var content = new MemoryStream(bytes, writable: false))
        {
            await store.StoreAsync(storageKey, content, string.IsNullOrWhiteSpace(file.ContentType) ? "text/plain" : file.ContentType, cancellationToken);
        }

        var document = new CaseDocument
        {
            Id = documentId,
            CaseId = caseId,
            OriginalFileName = safeFileName,
            StorageKey = storageKey,
            ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "text/plain" : file.ContentType,
            SizeBytes = bytes.LongLength,
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            ScanStatus = reasons.Count == 0 ? "Passed" : "Quarantined",
            ScanReasonCodesJson = JsonSerializer.Serialize(reasons),
            UploadedByUserId = authorization.Actor!.UserId,
            CreatedAt = timeProvider.GetUtcNow()
        };
        database.CaseDocuments.Add(document);
        await database.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(database, context, authorization.Actor, "case.document.upload", "CaseDocument", document.Id.ToString(), "Succeeded", reasons.Count == 0 ? "DOCUMENT_SCAN_PASSED" : "DOCUMENT_QUARANTINED", caseId, metadata: new Dictionary<string, object?>
        {
            ["fileName"] = safeFileName,
            ["sizeBytes"] = document.SizeBytes,
            ["sha256"] = document.Sha256,
            ["scanStatus"] = document.ScanStatus,
            ["reasonCodes"] = reasons
        }, cancellationToken: cancellationToken);
        return Results.Created($"/api/v1/cases/{caseId}/documents/{document.Id}", ToResponse(document));
    }

    private static async Task<(DemoActor? Actor, IResult? Error)> AuthorizeCaseAsync(
        Guid caseId,
        HttpContext context,
        NorthstarDbContext database,
        DemoIdentityService identities,
        AuditWriter audit,
        string action,
        CancellationToken cancellationToken)
    {
        var actor = await identities.ResolveAsync(context, database, cancellationToken);
        if (actor is null)
        {
            await audit.WriteAsync(database, context, null, action, "Case", caseId.ToString(), "Denied", "AUTHENTICATION_REQUIRED", caseId, cancellationToken: cancellationToken);
            return (null, Results.Json(Error(context, "AUTHENTICATION_REQUIRED", "A recognized synthetic demo identity is required."), statusCode: StatusCodes.Status401Unauthorized));
        }

        var caseRecord = await database.Cases.AsNoTracking().SingleOrDefaultAsync(item => item.Id == caseId, cancellationToken);
        if (caseRecord is null)
        {
            return (actor, Results.NotFound(Error(context, "NOT_FOUND", "Case not found.")));
        }

        if (actor.Role != "Administrator" && (actor.Role != "Caseworker" || caseRecord.AssignedWorkerId != actor.UserId))
        {
            await audit.WriteAsync(database, context, actor, action, "Case", caseId.ToString(), "Denied", "CASE_ACCESS_DENIED", caseId, cancellationToken: cancellationToken);
            return (actor, Results.Json(Error(context, "ACCESS_DENIED", "You are not authorized to access documents for this case."), statusCode: StatusCodes.Status403Forbidden));
        }

        return (actor, null);
    }

    private static object ToResponse(CaseDocument document) => new
    {
        document.Id,
        document.CaseId,
        document.OriginalFileName,
        document.ContentType,
        document.SizeBytes,
        document.Sha256,
        document.ScanStatus,
        reasonCodes = JsonSerializer.Deserialize<string[]>(document.ScanReasonCodesJson) ?? [],
        document.UploadedByUserId,
        document.CreatedAt,
        document.IsSynthetic
    };

    private static ErrorEnvelope Error(HttpContext context, string code, string message) =>
        new(code, message, RequestContext.CorrelationId(context));
}
