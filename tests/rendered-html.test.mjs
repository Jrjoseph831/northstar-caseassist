import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

// The workspace is prerendered at build time, so the HTML Next.js will serve is on
// disk after `npm run build`. Reading it keeps this a real check of the rendered
// markup without starting a server.
const prerenderedPage = new URL("../.next/server/app/index.html", import.meta.url);

async function renderedHtml() {
  try {
    return await readFile(prerenderedPage, "utf8");
  } catch (error) {
    assert.fail(
      `No prerendered page at .next/server/app/index.html — run "npm run build" before "npm test" (${error.code ?? error.message}).`,
    );
  }
}

test("server-renders the Northstar case workspace", async () => {
  const html = await renderedHtml();
  assert.match(html, /<title>Northstar CaseAssist<\/title>/);
  assert.match(html, /Case workspace/);
  assert.match(html, /Fictional records only/);
  assert.match(html, /Maya Chen/);
  assert.match(html, /Admin · Priya/);
  assert.doesNotMatch(html, /Aisha Bell|47\/48|24 assigned/);
});

test("assistant route validates input and maps the Azure case", async () => {
  const route = await readFile(
    new URL("../app/api/assistant/route.ts", import.meta.url),
    "utf8",
  );
  assert.match(route, /Case and question are required\./);
  assert.match(route, /NS-1048/);
  assert.match(route, /\/api\/v1\/case-assist\/requests/);
  assert.match(route, /safety-trace/);
  assert.match(route, /finding\.entityType \?\? finding\.EntityType/);
  assert.doesNotMatch(route, /processAssistantRequest|OPENAI_API_KEY/);
});

test("Azure BFF credentials stay server-side", async () => {
  const [page, bridge] = await Promise.all([
    readFile(new URL("../app/page.tsx", import.meta.url), "utf8"),
    readFile(new URL("../lib/azure-bff.ts", import.meta.url), "utf8"),
  ]);
  assert.doesNotMatch(page, /NORTHSTAR_BFF_SHARED_SECRET|X-Northstar-Bff-Key/);
  assert.match(bridge, /NORTHSTAR_BFF_SHARED_SECRET/);
  assert.match(bridge, /X-Northstar-Bff-Key/);
  assert.doesNotMatch(bridge, /sk-proj-/);
});

test("role transitions clear caseworker data before routing", async () => {
  const page = await readFile(new URL("../app/page.tsx", import.meta.url), "utf8");
  assert.match(page, /setCases\(\[\]\);[\s\S]*setDocuments\(\[\]\);[\s\S]*setResult\(null\);/);
  assert.match(page, /personas\[next\]\.role === "Senior Reviewer"/);
  assert.match(page, /personas\[next\]\.role === "Administrator"/);
  assert.match(page, /role cannot access the case workspace/);
});
