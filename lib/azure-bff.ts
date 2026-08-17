export type PersonaId = "maya-chen" | "marcus-reed" | "priya-shah";

const azureUsers: Record<PersonaId, string> = {
  "maya-chen": "maya.chen",
  "marcus-reed": "marcus.reed",
  "priya-shah": "priya.shah",
};

const personaNames: Record<string, string> = {
  "maya.chen": "Maya Chen",
  "marcus.reed": "Marcus Reed",
  "priya.shah": "Priya Shah",
};

export function personaId(value: unknown): PersonaId {
  return value === "marcus-reed" || value === "priya-shah"
    ? value
    : "maya-chen";
}

export function personaName(userId: string | null | undefined): string {
  return (userId && personaNames[userId]) || userId || "Unknown synthetic user";
}

export async function northstarFetch(
  path: string,
  persona: PersonaId,
  init: RequestInit = {},
): Promise<Response> {
  const baseUrl = process.env.NORTHSTAR_API_BASE_URL?.trim().replace(/\/$/, "");
  const sharedSecret = process.env.NORTHSTAR_BFF_SHARED_SECRET?.trim();
  if (!baseUrl || !sharedSecret) {
    throw new Error("The Azure backend connection is not configured.");
  }

  const headers = new Headers(init.headers);
  headers.set("X-Northstar-Bff-Key", sharedSecret);
  headers.set("X-Northstar-Demo-User", azureUsers[persona]);
  headers.set("X-Correlation-ID", crypto.randomUUID().replaceAll("-", ""));
  if (init.body && !(init.body instanceof FormData) && !headers.has("content-type")) {
    headers.set("content-type", "application/json");
  }

  return fetch(`${baseUrl}${path}`, { ...init, headers });
}

export async function jsonOrError<T>(response: Response): Promise<T> {
  const data = (await response.json()) as T & {
    error?: string;
    message?: string;
  };
  if (!response.ok) {
    throw new Error(data.message || data.error || `Azure API returned ${response.status}.`);
  }
  return data;
}
