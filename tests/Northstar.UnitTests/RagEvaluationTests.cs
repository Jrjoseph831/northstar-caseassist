using Northstar.Application.Assistant;
using Northstar.Application.Assistant.Retrieval;
using Northstar.Application.Evaluation;
using Northstar.Application.Pii;
using Northstar.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace Northstar.UnitTests;

public sealed class RagEvaluationTests(ITestOutputHelper output)
{
    private static RagEvaluationRunner BuildRunner() =>
        new(
            new HybridPolicyRetriever(
                new CatalogCandidateSource(),
                new HashedDenseEmbeddingProvider(),
                new DeterministicQueryExpander(),
                new LateInteractionReranker(),
                new PolicyVectorIndex(),
                new HybridRetrievalOptions()),
            new OfflineFixtureModelProvider(),
            new CitationValidator(),
            new RiskClassifier(),
            new DeterministicPiiRedactor());

    [Fact]
    public async Task RagEvaluation_MeasuresRetrievalGenerationEndToEndAndExperience()
    {
        var summary = await BuildRunner().RunAsync();

        foreach (var check in summary.Checks)
        {
            output.WriteLine($"[{(check.Passed ? "PASS" : "FAIL")}] {check.Category} · {check.Name}: {check.Evidence}");
        }

        Assert.Equal(
            ["End-to-end", "Generation quality", "Retrieval quality", "User experience"],
            summary.Checks.Select(check => check.Category).Distinct().Order(StringComparer.Ordinal).ToArray());
        Assert.All(summary.Checks, check => Assert.True(check.Passed, $"{check.Name}: {check.Evidence}"));
        Assert.Equal(0, summary.Failed);
    }

    [Fact]
    public async Task RagEvaluation_IsReproducibleAcrossRuns()
    {
        var first = await BuildRunner().RunAsync();
        var second = await BuildRunner().RunAsync();

        // Latency naturally varies; every quality metric must not.
        var firstQuality = first.Checks.Where(check => check.Category != "User experience").ToArray();
        var secondQuality = second.Checks.Where(check => check.Category != "User experience").ToArray();
        Assert.Equal(
            firstQuality.Select(check => $"{check.Name}={check.Evidence}"),
            secondQuality.Select(check => $"{check.Name}={check.Evidence}"));
    }
}
