using Northstar.Domain;

namespace Northstar.Infrastructure;

public static class PolicyCatalog
{
    public static IReadOnlyList<PolicySection> Sections { get; } =
    [
        Section("URP-4.2", "Utility Relief Program Manual", "1.0", "Section 4.2 Required documents", "UTILITY_RELIEF", "A caseworker must verify a current utility statement, household income evidence, and any available disconnection notice. A missing notice must be recorded as a document gap, not treated as an automatic denial."),
        Section("URP-6.1", "Utility Relief Program Manual", "1.0", "Section 6.1 Decision authority", "UTILITY_RELIEF", "Only an authorized program decision-maker may determine eligibility or approve a benefit amount. CaseAssist may summarize evidence but must not make or communicate the determination."),
        Section("HSP-3.3", "Housing Stability Program Guide", "1.0", "Section 3.3 Completeness review", "HOUSING_STABILITY", "Completeness review includes identity documentation, housing obligation evidence, household composition, and program-specific hardship documentation."),
        Section("HSP-7.2", "Housing Stability Program Guide", "1.0", "Section 7.2 Applicant communication", "HOUSING_STABILITY", "Communication drafts must distinguish verified facts from missing information and must not promise approval, payment, or a decision date."),
        Section("WTG-2.4", "Workforce Training Grant Manual", "1.0", "Section 2.4 Training-plan evidence", "WORKFORCE_TRAINING", "The case record should contain the proposed training provider, program dates, expected credential, and an itemized cost estimate."),
        Section("DVS-2.1", "Document Verification Standard", "1.0", "Section 2.1 Conflicting evidence", "ALL", "When two documents contain conflicting material values, the discrepancy must be recorded and routed for human resolution before the information is relied upon."),
        Section("CCS-5.1", "Caseworker Communication Standard", "1.0", "Section 5.1 Drafting boundaries", "ALL", "Caseworkers may use drafting assistance for neutral summaries and requests for missing information. Drafts require caseworker review before use."),
        Section("AIW-3.2", "AI-Assisted Work Policy", "1.0", "Section 3.2 Approved uses", "ALL", "Approved uses include policy search, case summarization, document completeness checks, and communication drafts based on approved sources."),
        Section("AIW-3.3", "AI-Assisted Work Policy", "1.0", "Section 3.3 Prohibited uses", "ALL", "The assistant must not determine eligibility, authorize payment, close a case, or autonomously contact an applicant."),
        Section("HRD-2.2", "Human Review and Decision Authority Standard", "1.0", "Section 2.2 Separation of duties", "ALL", "A person who submits an AI-assisted output for high-impact review must not approve the same review item. Approval requires a separately assigned reviewer."),
        Section("DHR-4.1", "Data Handling and Retention Standard", "1.0", "Section 4.1 Minimum necessary data", "ALL", "Only the minimum case information necessary for the task may be sent to an AI service. Direct identifiers must be removed when they are not necessary."),
        Section("DHR-4.2", "Data Handling and Retention Standard", "1.0", "Section 4.2 Identifiers permitted in AI processing", "ALL", "The following identifiers may be included in AI-assistant processing when relevant to the task: email address, telephone number, and case reference number. The following identifiers must never be transmitted to an AI service and must be removed before processing: Social Security number, financial account number, residential street address, and the applicant's full name."),
        Section("DHR-4.3", "Data Handling and Retention Standard", "1.0", "Section 4.3 Audit safety", "ALL", "Audit records may contain identifiers, control outcomes, and counts, but must not contain credentials, full SSNs, or unredacted prompts.")
    ];

    private static PolicySection Section(string id, string title, string version, string label, string program, string content) =>
        new()
        {
            Id = id,
            DocumentTitle = title,
            DocumentVersion = version,
            SectionLabel = label,
            ProgramCode = program,
            Content = content
        };
}
