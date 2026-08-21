"use client";

import Link from "next/link";
import { useEffect, useMemo, useState, useSyncExternalStore } from "react";

type PolicySection = {
  id: string;
  documentTitle: string;
  documentVersion: string;
  sectionLabel: string;
  programCode: string;
  content: string;
};

type PolicyDocument = {
  title: string;
  version: string;
  sections: PolicySection[];
};

function subscribeToHash(onChange: () => void) {
  window.addEventListener("hashchange", onChange);
  return () => window.removeEventListener("hashchange", onChange);
}

const programLabels: Record<string, string> = {
  ALL: "All programs",
  UTILITY_RELIEF: "Utility Relief",
  HOUSING_STABILITY: "Housing Stability",
  WORKFORCE_TRAINING: "Workforce Training",
};

export default function Policies() {
  const [sections, setSections] = useState<PolicySection[]>([]);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);

  // The highlighted section is named by the URL hash (for example /policies#URP-4.2).
  // Reading it from the browser rather than copying it into state on mount keeps the
  // value derived, and picks up later hash changes — following a citation link while
  // already on this page — for free. The server has no hash, hence the empty snapshot.
  const hash = useSyncExternalStore(
    subscribeToHash,
    () => window.location.hash,
    () => "",
  );
  const active = decodeURIComponent(hash.replace("#", ""));

  useEffect(() => {
    (async () => {
      try {
        const response = await fetch("/api/policies");
        const data = (await response.json()) as {
          sections?: PolicySection[];
          error?: string;
        };
        if (!response.ok) throw new Error(data.error ?? "Could not load policies.");
        setSections(data.sections ?? []);
      } catch (caught) {
        setError(caught instanceof Error ? caught.message : "Could not load policies.");
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  // Bring the named section into view once the corpus has rendered.
  useEffect(() => {
    if (loading || !active) return;
    document.getElementById(active)?.scrollIntoView({ behavior: "smooth", block: "center" });
  }, [loading, active]);

  const documents = useMemo<PolicyDocument[]>(() => {
    const byTitle = new Map<string, PolicyDocument>();
    for (const section of sections) {
      const existing = byTitle.get(section.documentTitle);
      if (existing) existing.sections.push(section);
      else
        byTitle.set(section.documentTitle, {
          title: section.documentTitle,
          version: section.documentVersion,
          sections: [section],
        });
    }
    return [...byTitle.values()];
  }, [sections]);

  return (
    <main className="policy-shell">
      <a className="skip-link" href="#policy-main">
        Skip to main content
      </a>
      <header className="policy-topbar">
        <div>
          <p className="eyebrow">NORTHSTAR PUBLIC SERVICES / POLICY LIBRARY</p>
          <h1>Approved policy &amp; program guides</h1>
        </div>
        <Link className="policy-back" href="/">
          ← Back to workspace
        </Link>
      </header>

      <section id="policy-main" tabIndex={-1} className="policy-body">
        <p className="policy-intro">
          These are the approved program guides, verification standards, and
          privacy/data-handling policies CaseAssist retrieves and cites. The assistant
          may only ground answers in this corpus; every citation links back to the exact
          section below. Fictional demonstration policies.
        </p>

        {loading && <p className="policy-muted">Loading policy corpus…</p>}
        {error && <p className="policy-error">{error}</p>}

        {documents.map((document) => (
          <article key={document.title} className="policy-doc">
            <div className="policy-doc-head">
              <h2>{document.title}</h2>
              <span>Version {document.version}</span>
            </div>
            {document.sections.map((section) => (
              <div
                key={section.id}
                id={section.id}
                className={active === section.id ? "policy-section active" : "policy-section"}
              >
                <div className="policy-section-head">
                  <strong>{section.sectionLabel}</strong>
                  <span className="policy-tags">
                    <span className="policy-id">{section.id}</span>
                    <span className="policy-program">
                      {programLabels[section.programCode] ?? section.programCode}
                    </span>
                  </span>
                </div>
                <p>{section.content}</p>
              </div>
            ))}
          </article>
        ))}
      </section>
    </main>
  );
}
