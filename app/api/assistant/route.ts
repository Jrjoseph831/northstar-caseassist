import { getChatGPTUser } from "../../chatgpt-auth";
import {
  jsonOrError,
  northstarFetch,
  personaId,
} from "../../../lib/azure-bff";

const caseIds: Record<string, string> = {
  "NS-1048": "c5e6ef52-1e03-47de-b58e-060e57f8ea31",
};

export async function POST(request: Request) {
  try {
    const signedIn = await getChatGPTUser();
    const local = new URL(request.url).hostname === "localhost";
    if (!signedIn && !local)
      return Response.json(
        { error: "Authentication required." },
        { status: 401 },
      );
    const body = (await request.json()) as {
      caseId?: string;
      azureCaseId?: string;
      question?: string;
      personaId?: string;
    };
    const caseId = body.caseId?.trim() ?? "";
    const question = body.question?.trim() ?? "";
    if (!caseId || !question)
      return Response.json(
        { error: "Case and question are required." },
        { status: 400 },
      );
    const azureCaseId = body.azureCaseId?.trim() || caseIds[caseId];
    if (!azureCaseId)
      return Response.json(
        { error: "This case is not connected to the Azure demo dataset." },
        { status: 404 },
      );
    const persona = personaId(body.personaId);
    const created = await jsonOrError<{
      requestId: string;
      output: string;
      draft: {
        summary: string;
        missingDocuments: Array<{
          title: string;
          detail: string;
          sourceId: string | null;
        }>;
        handlingNote: string | null;
      } | null;
      sources: Array<{
        sourceId: string;
        documentTitle: string;
        sectionLabel: string;
        content: string;
      }>;
      requiresReview: boolean;
      reviewId: string | null;
    }>(
      await northstarFetch("/api/v1/case-assist/requests", persona, {
        method: "POST",
        body: JSON.stringify({ caseId: azureCaseId, question }),
      }),
    );
    const trace = await jsonOrError<{
      requestId: string;
      user: string;
      role: string;
      caseId: string;
      pii: {
        redactedTypes: Array<{
          entityType?: string;
          count?: number;
          EntityType?: string;
          Count?: number;
        }>;
        permittedTypes?: Array<{
          entityType?: string;
          count?: number;
          EntityType?: string;
          Count?: number;
        }>;
        policySource?: string;
        originalValuesExcluded: boolean;
        outputPiiDetected: boolean;
      };
      retrieval: {
        approvedSectionsSearched: number;
        strategy?: string;
        candidateSource?: string;
        queries?: string[];
        candidateCount?: number;
        dense?: { model: string; dimensions: number; isLive: boolean };
        sparse?: { model: string };
        fusion?: { method: string; constant: number };
        rerank?: { model: string; depth: number };
        queryExpansion?: {
          provider: string;
          isLive: boolean;
          generatedQueries: number;
        };
        elapsedMilliseconds?: number;
        ranking?: Array<{
          sourceId: string;
          denseRank: number;
          sparseRank: number;
          fusionRank: number;
          rerankScore: number;
          finalRank: number;
        }>;
        sources: unknown[];
      };
      model: {
        provider: string;
        modelName: string;
        promptVersion: string;
        isLive: boolean;
        tokenUsage: number;
        estimatedCost: number;
      };
      controls: {
        citationsValid: boolean;
        classification: string;
        reasonCodes: string[];
        humanReviewRequired: boolean;
        contentSafety: {
          provider: string;
          isLive: boolean;
          isAllowed: boolean;
        };
      };
    }>(
      await northstarFetch(
        `/api/v1/case-assist/requests/${encodeURIComponent(created.requestId)}/safety-trace`,
        persona,
      ),
    );

    return Response.json(
      {
        id: created.requestId,
        displayId: created.requestId,
        reviewId: created.reviewId,
        answer: created.output,
        draft: created.draft,
        citations: created.sources.map((source) => ({
          id: source.sourceId,
          section: source.sectionLabel,
          title: source.documentTitle,
          content: source.content,
        })),
        requiresReview: created.requiresReview,
        createdAt: new Date().toISOString(),
        trace: {
          requestId: trace.requestId,
          user: trace.user,
          role: trace.role,
          caseId: trace.caseId,
          pii: {
            count: trace.pii.redactedTypes.reduce(
              (total, finding) => total + (finding.count ?? finding.Count ?? 0),
              0,
            ),
            findings: trace.pii.redactedTypes.map((finding) => {
              const entityType = finding.entityType ?? finding.EntityType ?? "PII";
              return {
                type: entityType,
                replacement: `[REDACTED_${entityType.toUpperCase()}]`,
              };
            }),
            permitted: (trace.pii.permittedTypes ?? []).map(
              (finding) => finding.entityType ?? finding.EntityType ?? "PII",
            ),
            policySource: trace.pii.policySource ?? null,
            originalExcluded: trace.pii.originalValuesExcluded,
          },
          retrieval: {
            searched: trace.retrieval.approvedSectionsSearched,
            sections: created.sources.map((source) => source.sourceId),
            strategy: trace.retrieval.strategy ?? null,
            queries: trace.retrieval.queries ?? [],
            candidateCount: trace.retrieval.candidateCount ?? 0,
            denseModel: trace.retrieval.dense?.model ?? null,
            denseDimensions: trace.retrieval.dense?.dimensions ?? 0,
            denseIsLive: trace.retrieval.dense?.isLive ?? false,
            sparseModel: trace.retrieval.sparse?.model ?? null,
            fusionMethod: trace.retrieval.fusion?.method ?? null,
            rerankModel: trace.retrieval.rerank?.model ?? null,
            elapsedMilliseconds: trace.retrieval.elapsedMilliseconds ?? 0,
            ranking: trace.retrieval.ranking ?? [],
          },
          model: {
            name: trace.model.modelName,
            promptVersion: trace.model.promptVersion,
            temperature: 0.2,
            mode: trace.model.isLive ? "live-openai" : "grounded-fixture",
            provider: trace.model.provider,
            tokenUsage: trace.model.tokenUsage,
            estimatedCost: trace.model.estimatedCost,
          },
          controls: {
            citationsVerified: trace.controls.citationsValid,
            outputPiiDetected: trace.pii.outputPiiDetected,
            eligibilityLanguageDetected:
              trace.controls.reasonCodes.includes("ELIGIBILITY_LANGUAGE_DETECTED"),
            humanReviewRequired: trace.controls.humanReviewRequired,
            classification: trace.controls.classification,
            reasonCodes: trace.controls.reasonCodes,
            contentSafety: trace.controls.contentSafety,
          },
        },
      },
      { status: 201 },
    );
  } catch (error) {
    return Response.json(
      { error: error instanceof Error ? error.message : "Request failed." },
      { status: 400 },
    );
  }
}
