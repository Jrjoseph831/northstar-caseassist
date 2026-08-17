import { getChatGPTUser } from "../../chatgpt-auth";
import { jsonOrError, northstarFetch, personaId } from "../../../lib/azure-bff";

type PolicySection = {
  id: string;
  documentTitle: string;
  documentVersion: string;
  sectionLabel: string;
  programCode: string;
  content: string;
};

export async function GET(request: Request) {
  try {
    const signedIn = await getChatGPTUser();
    if (!signedIn && new URL(request.url).hostname !== "localhost")
      return Response.json({ error: "Authentication required." }, { status: 401 });
    const sections = await jsonOrError<PolicySection[]>(
      await northstarFetch(
        "/api/v1/policies",
        personaId(new URL(request.url).searchParams.get("persona")),
      ),
    );
    return Response.json({ sections });
  } catch (error) {
    return Response.json(
      { error: error instanceof Error ? error.message : "Could not load policies." },
      { status: 400 },
    );
  }
}
