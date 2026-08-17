using Microsoft.EntityFrameworkCore;
using Northstar.Domain;
using System.Text.Json;

namespace Northstar.Infrastructure;

public static class SyntheticDataSeeder
{
    public static async Task SeedAsync(NorthstarDbContext database, CancellationToken cancellationToken = default)
    {
        await database.Database.EnsureCreatedAsync(cancellationToken);
        if (!await database.Users.AnyAsync(cancellationToken))
        {
            database.Users.AddRange(
                new UserRecord { Id = "maya.chen", DisplayName = "Maya Chen" },
                new UserRecord { Id = "marcus.reed", DisplayName = "Marcus Reed" },
                new UserRecord { Id = "priya.shah", DisplayName = "Priya Shah" });
            database.RoleAssignments.AddRange(
                new RoleAssignment { UserId = "maya.chen", Role = "Caseworker" },
                new RoleAssignment { UserId = "marcus.reed", Role = "Reviewer" },
                new RoleAssignment { UserId = "priya.shah", Role = "Administrator" });
        }


        // Reconcile the approved policy corpus by section id so newly published sections
        // (for example the data-handling permitted-identifiers rule) appear without a reset.
        var existingPolicyIds = (await database.PolicySections.AsNoTracking()
            .Select(item => item.Id)
            .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var section in PolicyCatalog.Sections)
        {
            if (!existingPolicyIds.Contains(section.Id))
            {
                database.PolicySections.Add(section);
            }
        }

        if (!await database.AISystemRegistry.AnyAsync(cancellationToken))
        {
            database.AISystemRegistry.Add(new AISystemRegistry
            {
                SystemName = "Northstar CaseAssist",
                SystemOwner = "Northstar Responsible AI Office (fictional)",
                ApprovedPurposeJson = JsonSerializer.Serialize(new[] { "case summaries", "document completeness checks", "policy search", "communication drafts" }),
                ProhibitedUsesJson = JsonSerializer.Serialize(new[] { "eligibility decisions", "payment authorization", "case closure", "autonomous applicant contact" }),
                DataClassification = "Synthetic confidential case data",
                RiskTier = "High-impact support system",
                ActiveModelRelease = "offline-fixture-v1",
                ActivePromptVersion = "northstar-prompt-v1",
                ApprovalStatus = "Approved for synthetic portfolio demonstration",
                NextReviewDate = new DateTimeOffset(2026, 11, 16, 0, 0, 0, TimeSpan.Zero),
                MappedControlsJson = JsonSerializer.Serialize(new[] { "PII-01", "AUTH-01", "RAG-01", "HITL-01", "AUDIT-01" })
            });
        }

        // Reconcile the demo caseload by case number so the seeder is idempotent: the
        // original NS-1048 (referenced by tests and the assistant route) is preserved, and
        // any of the richer mockup cases that are missing are added on startup.
        var baseTime = new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
        var specs = new[]
        {
            new CaseSpec(
                Guid.Parse("c5e6ef52-1e03-47de-b58e-060e57f8ea31"),
                Guid.Parse("47e583d8-6036-4205-9956-e90a6ec720e8"),
                Guid.Parse("2fdb9dc6-23f0-4a71-a505-5acdf8172c77"),
                "NS-1048", "APP-2026-1048", "UTILITY_RELIEF", "complete-utility-relief",
                "Elena Brooks", "elena.brooks@example.test", "202-555-0142",
                "1200 Example Avenue, Northstar, VA 00000", 3,
                "PendingDocuments", "High", baseTime.AddMinutes(-12),
                "Applicant Elena Brooks called the assistance line and said her work hours were cut about six weeks ago, so she has fallen behind on her electric bill and a disconnection notice is scheduled in about eight days. She is asking for help covering the past-due balance so her household of three does not lose power. Intake was taken over the phone. Contact number 202-555-0142 and email elena.brooks@example.test. She has not uploaded any supporting documents yet. Case file NS-1048.",
                []),
            new CaseSpec(
                Guid.Parse("b1a7f0d2-4c33-4a1e-9b64-2d6f5c8e1a10"),
                Guid.Parse("a2b8e1c3-5d44-4b2f-8c75-3e7f6d9f2b21"),
                Guid.Parse("c3c9d2e4-6e55-4c3a-9d86-4f8a7eaf3c32"),
                "NS-1061", "APP-2026-1061", "HOUSING_STABILITY", "housing-stability",
                "Darius Moore", "darius.moore@example.test", "202-555-0161",
                "48 Sample Street, Northstar, VA 00000", 4,
                "InReview", "Standard", baseTime.AddMinutes(-38),
                "Applicant Darius Moore submitted a housing stability request through the online portal. He explained that his lease renewal raises his rent by about two hundred dollars a month, which he cannot cover after a recent cut in hours, and he needs a decision before the renewal deadline. He provided his Social Security number 512-84-6390 and uploaded a completed income and lease review. Best contact number is 202-555-0161. Case file NS-1061.",
                ["Housing obligation evidence"]),
            new CaseSpec(
                Guid.Parse("d4dae3f5-7f66-4d4b-9e97-5a9b8fbf4d43"),
                Guid.Parse("e5ebf406-8a77-4e5c-8fa8-6bac9caf5e54"),
                Guid.Parse("f6fca517-9b88-4f6d-9fb9-7cbdadbf6f65"),
                "NS-1074", "APP-2026-1074", "WORKFORCE_TRAINING", "workforce-training-grant",
                "Amina Patel", "amina.patel@example.test", "202-555-0174",
                "9 Fiction Lane, Northstar, VA 00000", 2,
                "Open", "Standard", baseTime.AddMinutes(-63),
                "Applicant Amina Patel applied online for a workforce training grant to enroll in a licensed practical nursing program. She wrote that she has a confirmed seat starting in the fall and needs help with tuition and books. She uploaded a training provider letter and the program dates. She can be reached at amina.patel@example.test or 202-555-0174. Case file NS-1074.",
                ["Training provider details", "Program dates"]),
            new CaseSpec(
                Guid.Parse("07adb628-ac99-4a7e-8aca-8dcebeaf7a76"),
                Guid.Parse("18bec739-bdaa-4b8f-9bdb-9edfcfbf8b87"),
                Guid.Parse("29cfd84a-cebb-4c9a-8cec-af1adacf9c98"),
                "NS-1082", "APP-2026-1082", "UTILITY_RELIEF", "incomplete-utility-documents",
                "Jordan Taylor", "jordan.taylor@example.test", "202-555-0182",
                "310 Demo Court, Northstar, VA 00000", 5,
                "InReview", "High", baseTime.AddDays(-1),
                "Applicant Jordan Taylor came in through employee-assisted intake and explained that an unexpected medical bill left him unable to pay a past-due electric balance, and he is worried about a shutoff. He submitted his most recent utility statement and a written hardship explanation. His Social Security number 447-22-1908 is on file and his phone number is 202-555-0182. Case file NS-1082.",
                ["Current utility statement"]),
        };

        var existingNumbers = await database.Cases.AsNoTracking()
            .Select(item => item.CaseNumber)
            .ToListAsync(cancellationToken);
        var existing = existingNumbers.ToHashSet(StringComparer.Ordinal);

        foreach (var spec in specs)
        {
            if (existing.Contains(spec.CaseNumber)) continue;
            var applicant = new ApplicantRecord
            {
                Id = spec.ApplicantId,
                SyntheticDisplayName = spec.DisplayName,
                Email = spec.Email,
                Phone = spec.Phone,
                Address = spec.Address,
                HouseholdSize = spec.HouseholdSize
            };
            var application = new ApplicationRecord
            {
                Id = spec.ApplicationId,
                ApplicationNumber = spec.ApplicationNumber,
                ProgramCode = spec.ProgramCode,
                ScenarioCode = spec.ScenarioCode,
                ApplicantId = applicant.Id,
                Applicant = applicant,
                SubmissionChannel = "CitizenDemo",
                SubmittedAt = spec.SubmittedAt,
                Status = "Converted",
                CreatedByUserId = "maya.chen",
                CreatedAt = spec.SubmittedAt,
                UpdatedAt = spec.SubmittedAt
            };
            database.Cases.Add(new CaseRecord
            {
                Id = spec.CaseId,
                CaseNumber = spec.CaseNumber,
                ApplicationId = application.Id,
                Application = application,
                ProgramCode = spec.ProgramCode,
                Status = spec.Status,
                Priority = spec.Priority,
                AssignedWorkerId = "maya.chen",
                SubmittedAt = spec.SubmittedAt
            });
        }

        // Ensure each case has a synthetic, non-identifying background note. This is fed to
        // the assistant as case context so answers are grounded in this specific case rather
        // than generic policy boilerplate. Runs for pre-existing cases too.
        var casesWithBackground = await database.CaseNotes.AsNoTracking()
            .Where(item => item.NoteType == "CaseBackground")
            .Select(item => item.CaseId)
            .ToListAsync(cancellationToken);
        var backgroundSet = casesWithBackground.ToHashSet();
        foreach (var spec in specs)
        {
            if (backgroundSet.Contains(spec.CaseId)) continue;
            database.CaseNotes.Add(new CaseNote
            {
                CaseId = spec.CaseId,
                AuthorUserId = "maya.chen",
                NoteType = "CaseBackground",
                Content = spec.Background,
                CreatedAt = spec.SubmittedAt
            });
        }

        // Seed the documents each applicant actually submitted, so the assistant's
        // document-gap analysis compares a real (partial) set against policy requirements.
        // The document type is carried in the file name. Runs only for cases with no docs.
        var casesWithDocuments = await database.CaseDocuments.AsNoTracking()
            .Select(item => item.CaseId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var documentSet = casesWithDocuments.ToHashSet();
        foreach (var spec in specs)
        {
            if (spec.Documents.Length == 0 || documentSet.Contains(spec.CaseId)) continue;
            foreach (var label in spec.Documents)
            {
                database.CaseDocuments.Add(new CaseDocument
                {
                    CaseId = spec.CaseId,
                    OriginalFileName = $"{label}.txt",
                    StorageKey = $"{spec.CaseId:N}/{Guid.NewGuid():N}.txt",
                    ContentType = "text/plain",
                    SizeBytes = 1024,
                    Sha256 = "0000000000000000000000000000000000000000000000000000000000000000",
                    ScanStatus = "Passed",
                    ScanReasonCodesJson = "[]",
                    UploadedByUserId = "maya.chen",
                    CreatedAt = spec.SubmittedAt
                });
            }
        }

        await database.SaveChangesAsync(cancellationToken);
    }

    private sealed record CaseSpec(
        Guid CaseId,
        Guid ApplicationId,
        Guid ApplicantId,
        string CaseNumber,
        string ApplicationNumber,
        string ProgramCode,
        string ScenarioCode,
        string DisplayName,
        string Email,
        string Phone,
        string Address,
        int HouseholdSize,
        string Status,
        string Priority,
        DateTimeOffset SubmittedAt,
        string Background,
        string[] Documents);
}
