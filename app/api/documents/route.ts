import { getChatGPTUser } from "../../chatgpt-auth";
import { jsonOrError, northstarFetch, personaId } from "../../../lib/azure-bff";

async function authorize(request: Request) {
  return Boolean(await getChatGPTUser()) || new URL(request.url).hostname === "localhost";
}

export async function GET(request: Request) {
  try {
    if (!(await authorize(request)))
      return Response.json({ error: "Authentication required." }, { status: 401 });
    const url = new URL(request.url);
    const caseId = url.searchParams.get("caseId");
    if (!caseId) return Response.json({ error: "Case is required." }, { status: 400 });
    const documents = await jsonOrError<unknown[]>(
      await northstarFetch(
        `/api/v1/cases/${encodeURIComponent(caseId)}/documents`,
        personaId(url.searchParams.get("persona")),
      ),
    );
    return Response.json({ documents });
  } catch (error) {
    return Response.json(
      { error: error instanceof Error ? error.message : "Could not load documents." },
      { status: 400 },
    );
  }
}

export async function POST(request: Request) {
  try {
    if (!(await authorize(request)))
      return Response.json({ error: "Authentication required." }, { status: 401 });
    const incoming = await request.formData();
    const caseId = String(incoming.get("caseId") ?? "");
    const persona = personaId(incoming.get("personaId"));
    const file = incoming.get("file");
    if (!caseId || !(file instanceof File))
      return Response.json({ error: "Case and document are required." }, { status: 400 });
    const outbound = new FormData();
    outbound.set("file", file, file.name);
    const document = await jsonOrError<unknown>(
      await northstarFetch(
        `/api/v1/cases/${encodeURIComponent(caseId)}/documents`,
        persona,
        { method: "POST", body: outbound },
      ),
    );
    return Response.json({ document }, { status: 201 });
  } catch (error) {
    return Response.json(
      { error: error instanceof Error ? error.message : "Upload failed." },
      { status: 400 },
    );
  }
}
