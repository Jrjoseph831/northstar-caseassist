import { getChatGPTUser } from "../../chatgpt-auth";
import { jsonOrError, northstarFetch, personaId, personaName } from "../../../lib/azure-bff";

type AuditEvent = {
  eventId: string;
  timestamp: string;
  actorUserId: string;
  action: string;
  requestId: string | null;
  outcome: string;
  reasonCode: string;
};

export async function GET(request: Request) {
  try {
    const signedIn = await getChatGPTUser();
    if (!signedIn && new URL(request.url).hostname !== "localhost")
      return Response.json({ error: "Authentication required." }, { status: 401 });
    const persona = personaId(new URL(request.url).searchParams.get("persona"));
    if (persona !== "priya-shah")
      return Response.json({ error: "Administrator role required." }, { status: 403 });

    const [events, reviews, evaluations] = await Promise.all([
      jsonOrError<AuditEvent[]>(
        await northstarFetch("/api/v1/governance/audit-events?take=100", persona),
      ),
      jsonOrError<Array<{ status: string }>>(
        await northstarFetch("/api/v1/reviews", persona),
      ),
      jsonOrError<Array<{ passed: number; total: number; createdAt: string }>>(
        await northstarFetch("/api/v1/governance/evaluations", persona),
      ),
    ]);
    const requestIds = new Set(events.map((event) => event.requestId).filter(Boolean));
    const requiredIds = new Set(
      events.filter((event) => event.action === "review.trigger").map((event) => event.requestId).filter(Boolean),
    );
    const routed = reviews.length;
    return Response.json({
      requests: requestIds.size,
      reviewRequired: requiredIds.size,
      routed,
      reviewRate: requiredIds.size ? Math.round((routed / requiredIds.size) * 100) : 0,
      pending: reviews.filter((item) => item.status === "Pending").length,
      events: events.map((event) => ({
        id: event.eventId,
        eventType: event.action,
        detail: `${event.outcome} · ${event.reasonCode}`,
        actorName: personaName(event.actorUserId),
        createdAt: event.timestamp,
      })),
      evaluation: evaluations[0] ?? null,
    });
  } catch (error) {
    return Response.json(
      { error: error instanceof Error ? error.message : "Could not load governance data." },
      { status: 400 },
    );
  }
}
