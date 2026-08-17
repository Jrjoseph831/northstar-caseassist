using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Northstar.Domain;
using Northstar.Infrastructure;

namespace Northstar.Api;

public static class RequestContext
{
    public const string CorrelationIdItem = "NorthstarCorrelationId";

    public static string CorrelationId(HttpContext context) =>
        context.Items[CorrelationIdItem]?.ToString() ?? "unknown";
}

public sealed class DemoIdentityService
{
    public async Task<DemoActor?> ResolveAsync(
        HttpContext context,
        NorthstarDbContext database,
        CancellationToken cancellationToken)
    {
        var userId = context.Request.Headers["X-Northstar-Demo-User"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        return await database.RoleAssignments
            .AsNoTracking()
            .Where(item => item.UserId == userId)
            .Select(item => new DemoActor(item.UserId, item.User!.DisplayName, item.Role))
            .SingleOrDefaultAsync(cancellationToken);
    }
}

public sealed class AuditWriter(TimeProvider timeProvider)
{
    public async Task<string> WriteAsync(
        NorthstarDbContext database,
        HttpContext context,
        DemoActor? actor,
        string action,
        string resourceType,
        string resourceId,
        string outcome,
        string reasonCode,
        Guid? caseId = null,
        string? requestId = null,
        IReadOnlyDictionary<string, object?>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        var eventId = $"EVT-{Guid.NewGuid():N}";
        database.AuditEvents.Add(new AuditEvent
        {
            EventId = eventId,
            Timestamp = timeProvider.GetUtcNow(),
            ActorUserId = actor?.UserId ?? "anonymous",
            ActorRole = actor?.Role ?? "Anonymous",
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            CaseId = caseId,
            RequestId = requestId,
            Outcome = outcome,
            ReasonCode = reasonCode,
            CorrelationId = RequestContext.CorrelationId(context),
            MetadataJson = JsonSerializer.Serialize(metadata ?? new Dictionary<string, object?>())
        });
        await database.SaveChangesAsync(cancellationToken);
        return eventId;
    }
}
