// Portfolio identity layer.
// Production mapping: replace this with Azure Static Web Apps managed auth
// (X-MS-CLIENT-PRINCIPAL header) or Microsoft Entra ID app roles via MSAL.
// In this portfolio deployment the persona is selected via the UI dropdown;
// the BFF enforces role rules server-side using the X-Northstar-Demo-User header.

export type ChatGPTUser = {
  userId: string;
  displayName: string;
  email: string;
  fullName: string | null;
};

const PORTFOLIO_USER: ChatGPTUser = {
  userId: "portfolio-demo",
  displayName: "Demo User",
  email: "demo@northstar.test",
  fullName: "Demo User",
};

export async function getChatGPTUser(): Promise<ChatGPTUser | null> {
  return PORTFOLIO_USER;
}

export async function requireChatGPTUser(): Promise<ChatGPTUser> {
  return PORTFOLIO_USER;
}
