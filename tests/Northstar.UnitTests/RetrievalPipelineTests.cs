using Northstar.Application.Assistant;
using Northstar.Application.Assistant.Retrieval;
using Northstar.Infrastructure;
using Xunit;

namespace Northstar.UnitTests;

/// <summary>In-memory candidate pool over the approved policy catalog.</summary>
internal sealed class CatalogCandidateSource : IPolicyCandidateSource
{
    public string Name => "test-approved-policy-catalog";

    public Task<IReadOnlyList<PolicyCandidate>> FetchAsync(
        string programCode,
        IReadOnlyList<string> queries,
        int take,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PolicyCandidate> candidates = PolicyCatalog.Sections
            .Where(section => section.ProgramCode == programCode || section.ProgramCode == "ALL")
            .Select(section => new PolicyCandidate(
                section.Id,
                section.DocumentTitle,
                section.DocumentVersion,
                section.SectionLabel,
                section.Content,
                0))
            .Take(take)
            .ToArray();
        return Task.FromResult(candidates);
    }
}

public sealed class RetrievalPipelineTests
{
    private static HybridPolicyRetriever BuildRetriever(HybridRetrievalOptions? options = null) =>
        new(
            new CatalogCandidateSource(),
            new HashedDenseEmbeddingProvider(),
            new DeterministicQueryExpander(),
            new LateInteractionReranker(),
            new PolicyVectorIndex(),
            options ?? new HybridRetrievalOptions());

    [Fact]
    public void ReciprocalRankFusion_PrefersTheSourceSeveralSearchesAgreeOn()
    {
        var dense = new[] { "A", "B", "C" };
        var sparse = new[] { "B", "A", "C" };
        var expanded = new[] { "B", "C", "A" };

        var fused = ReciprocalRankFusion.Fuse([dense, sparse, expanded]);

        Assert.Equal("B", fused[0].SourceId);
        Assert.Equal(1, fused[0].Rank);
        Assert.True(fused[0].Score > fused[1].Score);
    }

    [Fact]
    public void SparseEncoder_RanksTheSectionThatUsesTheExactTerms()
    {
        var candidates = PolicyCatalog.Sections
            .Select(section => new PolicyCandidate(section.Id, section.DocumentTitle, section.DocumentVersion, section.SectionLabel, section.Content, 0))
            .ToArray();

        var ranked = new Bm25SparseEncoder().Build(candidates).Rank("disconnection notice and household income evidence");

        Assert.Equal("URP-4.2", ranked[0].SourceId);
        Assert.True(ranked[0].Score > 0);
    }

    [Fact]
    public void LateInteractionReranker_PromotesTheSectionCarryingTheQuestionTerms()
    {
        var candidates = new[]
        {
            new PolicyCandidate("A-1", "Unrelated Policy", "1.0", "Section 1", "Office opening hours and mail handling.", 0),
            new PolicyCandidate("B-1", "Audit Policy", "1.0", "Section 2", "Audit records may contain identifiers, control outcomes, and counts.", 0)
        };

        var reranked = new LateInteractionReranker().Rerank("what can the audit record contain", candidates);

        Assert.Equal("B-1", reranked[0].Candidate.SourceId);
        Assert.True(reranked[0].Score > reranked[1].Score);
    }

    [Fact]
    public void QueryExpander_ProducesDeterministicPolicyWordedVariants()
    {
        const string question = "What documents are still missing on this case?";

        var first = DeterministicQueryExpander.Expand("UTILITY_RELIEF", question, 4);
        var second = DeterministicQueryExpander.Expand("UTILITY_RELIEF", question, 4);

        Assert.Equal(first, second);
        Assert.Equal(4, first.Count);
        Assert.Contains(first, variant => variant.Contains("disconnection notice", StringComparison.Ordinal));
        Assert.DoesNotContain(first, variant => variant.Equals(question, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task HybridRetriever_SearchesEveryQueryAndReportsEachStage()
    {
        var result = await BuildRetriever().SearchAsync(
            "UTILITY_RELIEF",
            "What paperwork do I still need before this utility case can move forward?",
            3);

        Assert.Equal(3, result.Hits.Count);
        Assert.Contains(result.Hits, hit => hit.SourceId == "URP-4.2");

        var diagnostics = result.Diagnostics;
        Assert.Equal(5, diagnostics.Queries.Count);
        Assert.Equal("northstar-hashed-dense-v1", diagnostics.DenseModel);
        Assert.Equal(Bm25SparseEncoder.ModelName, diagnostics.SparseModel);
        Assert.Equal("northstar-late-interaction-maxsim-v1", diagnostics.RerankModel);
        Assert.Equal(ReciprocalRankFusion.DefaultConstant, diagnostics.FusionConstant);
        Assert.All(diagnostics.Ranking, score => Assert.True(score.FusionRank > 0));
        Assert.Contains(diagnostics.Ranking, score => score.FinalRank == 1);
    }

    [Fact]
    public async Task HybridRetriever_NeverReturnsSectionsFromAnotherProgram()
    {
        var result = await BuildRetriever().SearchAsync("WORKFORCE_TRAINING", "What training evidence is required?", 3);

        Assert.NotEmpty(result.Hits);
        Assert.All(result.Hits, hit => Assert.False(hit.SourceId.StartsWith("URP", StringComparison.Ordinal)));
        Assert.All(result.Hits, hit => Assert.False(hit.SourceId.StartsWith("HSP", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task HybridRetriever_ReusesEmbeddedSectionsAcrossRequests()
    {
        var index = new PolicyVectorIndex();
        var retriever = new HybridPolicyRetriever(
            new CatalogCandidateSource(),
            new HashedDenseEmbeddingProvider(),
            new DeterministicQueryExpander(),
            new LateInteractionReranker(),
            index,
            new HybridRetrievalOptions());

        await retriever.SearchAsync("UTILITY_RELIEF", "What documents are required?", 3);
        var afterFirst = index.Count;
        await retriever.SearchAsync("UTILITY_RELIEF", "Who decides eligibility?", 3);

        Assert.True(afterFirst > 0);
        Assert.Equal(afterFirst, index.Count);
    }
}
