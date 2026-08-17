using Northstar.Application.Pii;
using Xunit;

namespace Northstar.UnitTests;

public sealed class DeterministicPiiRedactorTests
{
    [Fact]
    public void Redact_RemovesSupportedSyntheticIdentifiers()
    {
        var redactor = new DeterministicPiiRedactor();
        const string input = "Email elena@example.test, SSN 123-45-6789, phone 202-555-0142.";

        var result = redactor.Redact(input);

        Assert.Equal(
            "Email [REDACTED_EMAIL], SSN [REDACTED_SSN], phone [REDACTED_PHONE].",
            result.RedactedText);
        Assert.Equal(3, result.TotalRedactions);
        Assert.DoesNotContain("123-45-6789", result.RedactedText, StringComparison.Ordinal);
    }

    [Fact]
    public void Redact_ReturnsNoMetadataContainingOriginalValues()
    {
        var redactor = new DeterministicPiiRedactor();

        var result = redactor.Redact("Contact demo.person@example.test.");

        var serializedSummary = string.Join('|', result.Summary.Select(item => $"{item.EntityType}:{item.Count}"));
        Assert.Equal("EMAIL:1", serializedSummary);
        Assert.DoesNotContain("demo.person", serializedSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void Redact_RemovesCaseAddressAndLabeledSyntheticPerson()
    {
        var redactor = new DeterministicPiiRedactor();

        var result = redactor.Redact("Applicant Elena Brooks lives at 1200 Example Avenue. Case NS-1048.");

        Assert.Contains("[REDACTED_PERSON]", result.RedactedText, StringComparison.Ordinal);
        Assert.Contains("[REDACTED_ADDRESS]", result.RedactedText, StringComparison.Ordinal);
        Assert.Contains("[REDACTED_CASE_ID]", result.RedactedText, StringComparison.Ordinal);
        Assert.DoesNotContain("Elena Brooks", result.RedactedText, StringComparison.Ordinal);
        Assert.DoesNotContain("1200 Example Avenue", result.RedactedText, StringComparison.Ordinal);
        Assert.DoesNotContain("NS-1048", result.RedactedText, StringComparison.Ordinal);
    }
}
