using System.Diagnostics;
using Northstar.Application.Assistant;
using Northstar.Application.Assistant.Retrieval;
using Northstar.Application.Pii;

namespace Northstar.Application.Evaluation;

/// <summary>
/// Measures the retrieval-augmented pipeline itself against a labeled question set, in the
/// four places it can fail independently:
///
///   Retrieval quality  — did the right approved sections come back, and how high?
///   Generation quality — is the answer grounded in what came back, or beside it?
///   End to end         — does the caseworker get a usable, in-bounds answer?
///   User experience    — is it fast enough, and does it always show its sources?
///
/// A high retrieval score with a poor generation score is a prompt problem; the reverse is a
/// retrieval problem. Reporting one blended number hides which one you have, so each metric
/// is reported separately with the threshold it is judged against.
///
/// Every metric is a measurement; the threshold is the minimum bar a run has to clear, not a
/// claim about the expected value. The measured value is recorded as evidence either way.
/// </summary>
public sealed class RagEvaluationRunner(
    IPolicyRetriever retriever,
    IAssistantModelProvider modelProvider,
    CitationValidator citationValidator,
    RiskClassifier riskClassifier,
    IPiiRedactor redactor)
{
    private const int CutOff = 3;

    public async Task<EvaluationSummary> RunAsync(CancellationToken cancellationToken = default)
    {
        var outcomes = new List<CaseOutcome>();
        foreach (var goldenCase in RagGoldenDataset.Cases)
        {
            outcomes.Add(await EvaluateAsync(goldenCase, cancellationToken));
        }

        var checks = new List<EvaluationCheck>();
        checks.AddRange(RetrievalChecks(outcomes));
        checks.AddRange(GenerationChecks(outcomes));
        checks.AddRange(EndToEndChecks(outcomes));
        checks.AddRange(ExperienceChecks(outcomes));
        return new EvaluationSummary(checks);
    }

    private async Task<CaseOutcome> EvaluateAsync(RagGoldenCase goldenCase, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var retrieval = await retriever.SearchAsync(goldenCase.ProgramCode, goldenCase.Question, CutOff, cancellationToken);
        stopwatch.Stop();

        var hits = retrieval.Hits;
        var retrievedIds = hits.Select(item => item.SourceId).ToArray();
        var relevant = goldenCase.RelevantSourceIds.ToHashSet(StringComparer.Ordinal);
        var found = retrievedIds.Where(relevant.Contains).ToArray();

        var firstRelevantRank = 0;
        for (var index = 0; index < retrievedIds.Length; index++)
        {
            if (!relevant.Contains(retrievedIds[index])) continue;
            firstRelevantRank = index + 1;
            break;
        }

        var caseContext = $"Case number: EVAL-{goldenCase.Id}; program: {goldenCase.ProgramCode.Replace('_', ' ').ToLowerInvariant()}; "
            + "synthetic evaluation fixture; documents on file: none recorded.";
        var generation = await modelProvider.GenerateAsync(
            new ModelRequest(
                goldenCase.Id,
                goldenCase.Question,
                goldenCase.ProgramCode,
                caseContext,
                hits,
                "northstar-prompt-v1",
                450),
            cancellationToken);

        var validation = citationValidator.Validate(generation, hits);
        var citedIds = generation.CitationSourceIds.Distinct(StringComparer.Ordinal).ToArray();
        var citationPrecision = citedIds.Length == 0
            ? 0
            : (double)citedIds.Count(id => retrievedIds.Contains(id, StringComparer.Ordinal)) / citedIds.Length;

        var outputPii = redactor.Redact(generation.Text, DataHandlingPolicy.ProhibitedFromAi).TotalRedactions > 0;
        var risk = riskClassifier.Classify(goldenCase.Question, generation.Text, validation.IsValid, outputPii, []);

        // An answer built from a section that was never retrieved must fail validation. Running
        // the negative case through the same validator on every run is what keeps the
        // faithfulness number meaningful instead of self-congratulatory.
        var ungrounded = new ModelGeneration(
            "The applicant meets every requirement in the manual. [NOT-RETRIEVED-1]",
            ["NOT-RETRIEVED-1"],
            10,
            0,
            generation.Provider,
            generation.ModelName,
            generation.IsLive);
        var ungroundedRejected = !citationValidator.Validate(ungrounded, hits).IsValid;

        return new CaseOutcome(
            goldenCase,
            retrieval.Diagnostics,
            retrievedIds,
            Recall: relevant.Count == 0 ? 1 : (double)found.Distinct(StringComparer.Ordinal).Count() / relevant.Count,
            Precision: retrievedIds.Length == 0 ? 0 : (double)found.Length / retrievedIds.Length,
            ReciprocalRank: firstRelevantRank == 0 ? 0 : 1.0 / firstRelevantRank,
            NormalizedDiscountedGain: NormalizedDiscountedGain(retrievedIds, relevant),
            Faithful: validation.IsValid,
            CitationPrecision: citationPrecision,
            HasCitations: citedIds.Length > 0,
            UngroundedRejected: ungroundedRejected,
            AnswerRelevance: Overlap(goldenCase.Question, generation.Text),
            FactCoverage: FactCoverage(goldenCase.ExpectedTerms, generation.Text),
            OutputContainsPii: outputPii,
            RoutedForReview: risk.RequiresReview,
            ElapsedMilliseconds: stopwatch.ElapsedMilliseconds);
    }

    private static IEnumerable<EvaluationCheck> RetrievalChecks(IReadOnlyList<CaseOutcome> outcomes)
    {
        yield return Metric("Retrieval quality", "rag.retrieval.recall-at-3",
            outcomes.Average(item => item.Recall), 0.75,
            $"{outcomes.Count(item => item.Recall >= 1)}/{outcomes.Count} questions retrieved every labeled section");
        yield return Metric("Retrieval quality", "rag.retrieval.precision-at-3",
            outcomes.Average(item => item.Precision), 0.30,
            "labeled sections as a share of the three returned");
        yield return Metric("Retrieval quality", "rag.retrieval.mrr",
            outcomes.Average(item => item.ReciprocalRank), 0.70,
            "mean reciprocal rank of the first labeled section");
        yield return Metric("Retrieval quality", "rag.retrieval.ndcg-at-3",
            outcomes.Average(item => item.NormalizedDiscountedGain), 0.70,
            "rank-weighted retrieval gain");
        yield return Metric("Retrieval quality", "rag.retrieval.hit-rate-at-3",
            Rate(outcomes, item => item.ReciprocalRank > 0), 0.80,
            "questions with at least one labeled section in the top three");

        var unapproved = outcomes
            .SelectMany(item => item.RetrievedSourceIds)
            .Distinct(StringComparer.Ordinal)
            .Where(id => !ApprovedSourceIds.Contains(id))
            .ToArray();
        yield return new EvaluationCheck("Retrieval quality", "rag.retrieval.approved-sources-only",
            unapproved.Length == 0,
            unapproved.Length == 0 ? "no source outside the approved corpus was returned" : string.Join(',', unapproved));

        var diagnostics = outcomes[0].Diagnostics;
        var pipelineComplete = outcomes.All(item => item.Diagnostics.Queries.Count > 1)
            && !string.IsNullOrWhiteSpace(diagnostics.DenseModel)
            && !string.IsNullOrWhiteSpace(diagnostics.SparseModel)
            && diagnostics.RerankModel != "disabled";
        yield return new EvaluationCheck("Retrieval quality", "rag.retrieval.pipeline-stages-active",
            pipelineComplete,
            $"queries={diagnostics.Queries.Count}, dense={diagnostics.DenseModel}, sparse={diagnostics.SparseModel}, fusion=rrf@{diagnostics.FusionConstant}, rerank={diagnostics.RerankModel}");
    }

    private static IEnumerable<EvaluationCheck> GenerationChecks(IReadOnlyList<CaseOutcome> outcomes)
    {
        yield return Metric("Generation quality", "rag.generation.faithfulness",
            Rate(outcomes, item => item.Faithful), 1.0,
            "answers whose every citation quotes a retrieved section");
        yield return Metric("Generation quality", "rag.generation.citation-precision",
            outcomes.Average(item => item.CitationPrecision), 0.95,
            "cited sections that were actually retrieved");
        yield return Metric("Generation quality", "rag.generation.citation-coverage",
            Rate(outcomes, item => item.HasCitations), 1.0,
            "answers carrying at least one citation");
        yield return Metric("Generation quality", "rag.generation.ungrounded-answer-detected",
            Rate(outcomes, item => item.UngroundedRejected), 1.0,
            "negative control: an answer citing a section that was never retrieved is rejected");
        yield return Metric("Generation quality", "rag.generation.answer-relevance",
            outcomes.Average(item => item.AnswerRelevance), 0.30,
            "question terms addressed by the answer");
    }

    private static IEnumerable<EvaluationCheck> EndToEndChecks(IReadOnlyList<CaseOutcome> outcomes)
    {
        yield return Metric("End-to-end", "rag.endtoend.expected-fact-coverage",
            outcomes.Average(item => item.FactCoverage), 0.60,
            "labeled facts the answer actually states");
        yield return Metric("End-to-end", "rag.endtoend.grounded-answer-rate",
            Rate(outcomes, item => item.Faithful && item.HasCitations), 1.0,
            "answers that both cite and survive citation validation");

        var decisionCases = outcomes.Where(item => item.Case.RequiresHumanDecision).ToArray();
        yield return Metric("End-to-end", "rag.endtoend.decision-boundary-held",
            decisionCases.Length == 0 ? 1 : Rate(decisionCases, item => item.RoutedForReview), 1.0,
            $"{decisionCases.Length} question(s) that must reach a person");

        var neutralCases = outcomes.Where(item => !item.Case.RequiresHumanDecision).ToArray();
        yield return Metric("End-to-end", "rag.endtoend.neutral-questions-not-routed",
            neutralCases.Length == 0 ? 1 : Rate(neutralCases, item => !item.RoutedForReview), 0.80,
            "routine questions answered without a review detour");

        yield return Metric("End-to-end", "rag.endtoend.no-output-pii",
            Rate(outcomes, item => !item.OutputContainsPii), 1.0,
            "answers free of prohibited identifiers");
    }

    private static IEnumerable<EvaluationCheck> ExperienceChecks(IReadOnlyList<CaseOutcome> outcomes)
    {
        yield return Metric("User experience", "rag.ux.sources-shown-rate",
            Rate(outcomes, item => item.RetrievedSourceIds.Count > 0), 1.0,
            "answers a caseworker can open the underlying policy from");

        var latencies = outcomes.Select(item => item.ElapsedMilliseconds).OrderBy(value => value).ToArray();
        var median = Percentile(latencies, 0.50);
        var ninetyFifth = Percentile(latencies, 0.95);
        yield return new EvaluationCheck("User experience", "rag.ux.retrieval-latency-p50",
            median <= 1_500,
            $"p50={median}ms (threshold 1500ms)");
        yield return new EvaluationCheck("User experience", "rag.ux.retrieval-latency-p95",
            ninetyFifth <= 5_000,
            $"p95={ninetyFifth}ms (threshold 5000ms)");
    }

    private static EvaluationCheck Metric(string category, string name, double value, double threshold, string evidence) =>
        new(category, name, value + 1e-9 >= threshold, $"{value:0.###} (threshold {threshold:0.###}) — {evidence}");

    private static double Rate<T>(IReadOnlyCollection<T> items, Func<T, bool> predicate) =>
        items.Count == 0 ? 1 : (double)items.Count(predicate) / items.Count;

    private static long Percentile(IReadOnlyList<long> sorted, double percentile)
    {
        if (sorted.Count == 0) return 0;
        var index = (int)Math.Ceiling(percentile * sorted.Count) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
    }

    private static double NormalizedDiscountedGain(IReadOnlyList<string> retrieved, IReadOnlySet<string> relevant)
    {
        if (relevant.Count == 0) return 1;
        double gain = 0;
        for (var index = 0; index < retrieved.Count; index++)
        {
            if (relevant.Contains(retrieved[index])) gain += 1.0 / Math.Log2(index + 2);
        }
        double ideal = 0;
        for (var index = 0; index < Math.Min(relevant.Count, Math.Max(retrieved.Count, 1)); index++)
        {
            ideal += 1.0 / Math.Log2(index + 2);
        }
        return ideal <= 0 ? 0 : gain / ideal;
    }

    private static double Overlap(string question, string answer)
    {
        var questionTerms = TextAnalysis.ContentTerms(question).Distinct(StringComparer.Ordinal).ToArray();
        if (questionTerms.Length == 0) return 1;
        var answerTerms = TextAnalysis.ContentTerms(answer).ToHashSet(StringComparer.Ordinal);
        return (double)questionTerms.Count(answerTerms.Contains) / questionTerms.Length;
    }

    private static double FactCoverage(IReadOnlyList<string> expectedTerms, string answer)
    {
        if (expectedTerms.Count == 0) return 1;
        var present = expectedTerms.Count(term => answer.Contains(term, StringComparison.OrdinalIgnoreCase));
        return (double)present / expectedTerms.Count;
    }

    private static readonly HashSet<string> ApprovedSourceIds = RagGoldenDataset.Cases
        .SelectMany(item => item.RelevantSourceIds)
        .Concat(["URP-4.2", "URP-6.1", "HSP-3.3", "HSP-7.2", "WTG-2.4", "DVS-2.1", "CCS-5.1", "AIW-3.2", "AIW-3.3", "HRD-2.2", "DHR-4.1", "DHR-4.2", "DHR-4.3"])
        .ToHashSet(StringComparer.Ordinal);

    private sealed record CaseOutcome(
        RagGoldenCase Case,
        RetrievalDiagnostics Diagnostics,
        IReadOnlyList<string> RetrievedSourceIds,
        double Recall,
        double Precision,
        double ReciprocalRank,
        double NormalizedDiscountedGain,
        bool Faithful,
        double CitationPrecision,
        bool HasCitations,
        bool UngroundedRejected,
        double AnswerRelevance,
        double FactCoverage,
        bool OutputContainsPii,
        bool RoutedForReview,
        long ElapsedMilliseconds);
}
