using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Azure.Core;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using OpenTelemetry;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Northstar.Application.Pii;
using Northstar.Application.Assistant;
using Northstar.Application.Assistant.Retrieval;
using Northstar.Application.Evaluation;
using Northstar.Application.Documents;
using Northstar.Api;
using Northstar.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor(options => options.SamplingRatio = 0.25f);
}

builder.Services.AddOpenApi();
builder.Services.AddSingleton<IPiiRedactor, DeterministicPiiRedactor>();
builder.Services.AddSingleton<TokenCredential>(_ => new DefaultAzureCredential());
// ---- Retrieval (RAG) ---------------------------------------------------------------
// Multi-query expansion, dense + sparse hybrid search, reciprocal rank fusion and
// late-interaction reranking run over whichever candidate store is configured.
if (builder.Configuration["Search:Provider"] == "AzureAiSearch")
{
    var endpoint = builder.Configuration["Search:Endpoint"]
        ?? throw new InvalidOperationException("Search:Endpoint is required for Azure AI Search.");
    var indexName = builder.Configuration["Search:IndexName"]
        ?? throw new InvalidOperationException("Search:IndexName is required for Azure AI Search.");
    builder.Services.AddHttpClient<AzureSearchPolicyCandidateSource>();
    builder.Services.AddScoped<IPolicyCandidateSource>(services => new AzureSearchPolicyCandidateSource(
        services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(AzureSearchPolicyCandidateSource)),
        services.GetRequiredService<TokenCredential>(),
        new Uri(endpoint),
        indexName));
}
else
{
    builder.Services.AddScoped<IPolicyCandidateSource, DatabasePolicyCandidateSource>();
}

builder.Services.AddSingleton<HashedDenseEmbeddingProvider>();
builder.Services.AddSingleton<PolicyVectorIndex>();
builder.Services.AddSingleton<ILateInteractionReranker, LateInteractionReranker>();
builder.Services.AddSingleton<DeterministicQueryExpander>();
builder.Services.AddSingleton(new HybridRetrievalOptions
{
    QueryVariants = Math.Clamp(builder.Configuration.GetValue<int?>("Retrieval:QueryVariants") ?? 4, 0, 8),
    CandidatePoolSize = Math.Clamp(builder.Configuration.GetValue<int?>("Retrieval:CandidatePoolSize") ?? 24, 1, 200),
    RerankDepth = Math.Clamp(builder.Configuration.GetValue<int?>("Retrieval:RerankDepth") ?? 8, 1, 50),
    FusionConstant = Math.Clamp(builder.Configuration.GetValue<int?>("Retrieval:FusionConstant") ?? 60, 1, 1_000),
    RerankWeight = Math.Clamp(builder.Configuration.GetValue<double?>("Retrieval:RerankWeight") ?? 0.25, 0, 1),
    UseQueryExpansion = builder.Configuration.GetValue<bool?>("Retrieval:UseQueryExpansion") ?? true,
    UseReranking = builder.Configuration.GetValue<bool?>("Retrieval:UseReranking") ?? true
});

if (builder.Configuration["Retrieval:EmbeddingProvider"] == "OpenAI")
{
    var embeddingKey = builder.Configuration["Retrieval:EmbeddingApiKey"]
        ?? builder.Configuration["AI:ApiKey"]
        ?? throw new InvalidOperationException("Retrieval:EmbeddingApiKey or AI:ApiKey is required for hosted embeddings.");
    var embeddingModel = builder.Configuration["Retrieval:EmbeddingModel"] ?? "text-embedding-3-small";
    var embeddingDimensions = Math.Clamp(builder.Configuration.GetValue<int?>("Retrieval:EmbeddingDimensions") ?? 512, 64, 3_072);
    builder.Services.AddHttpClient<OpenAIEmbeddingProvider>();
    builder.Services.AddSingleton<IEmbeddingProvider>(services => new ResilientEmbeddingProvider(
        new OpenAIEmbeddingProvider(
            services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(OpenAIEmbeddingProvider)),
            embeddingKey,
            embeddingModel,
            embeddingDimensions),
        services.GetRequiredService<HashedDenseEmbeddingProvider>(),
        services.GetRequiredService<ILogger<ResilientEmbeddingProvider>>()));
}
else
{
    builder.Services.AddSingleton<IEmbeddingProvider>(services =>
        services.GetRequiredService<HashedDenseEmbeddingProvider>());
}

// Model-written query variants are opt-in: the rule-based expander is free, adds no latency
// and keeps a retrieval trace reproducible, which matters more here than fluent rewrites.
if (builder.Configuration["Retrieval:QueryExpansionProvider"] == "OpenAI")
{
    var expansionKey = builder.Configuration["AI:ApiKey"]
        ?? throw new InvalidOperationException("AI:ApiKey is required for model-generated query expansion.");
    var expansionModel = builder.Configuration["Retrieval:QueryExpansionModel"]
        ?? builder.Configuration["AI:Model"]
        ?? "gpt-5-mini";
    builder.Services.AddHttpClient<OpenAIQueryExpander>();
    builder.Services.AddSingleton<IQueryExpander>(services => new OpenAIQueryExpander(
        services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(OpenAIQueryExpander)),
        expansionKey,
        expansionModel,
        services.GetRequiredService<DeterministicQueryExpander>(),
        services.GetRequiredService<ILogger<OpenAIQueryExpander>>()));
}
else
{
    builder.Services.AddSingleton<IQueryExpander>(services =>
        services.GetRequiredService<DeterministicQueryExpander>());
}

builder.Services.AddScoped<IPolicyRetriever>(services => new HybridPolicyRetriever(
    services.GetRequiredService<IPolicyCandidateSource>(),
    services.GetRequiredService<IEmbeddingProvider>(),
    services.GetRequiredService<IQueryExpander>(),
    services.GetRequiredService<ILateInteractionReranker>(),
    services.GetRequiredService<PolicyVectorIndex>(),
    services.GetRequiredService<HybridRetrievalOptions>()));

if (builder.Configuration["Storage:Provider"] == "AzureBlob")
{
    var accountName = builder.Configuration["Storage:AccountName"]
        ?? throw new InvalidOperationException("Storage:AccountName is required for Azure Blob storage.");
    builder.Services.AddSingleton<ICaseDocumentStore>(services => new AzureBlobCaseDocumentStore(
        new Uri($"https://{accountName}.blob.core.windows.net/case-documents"),
        services.GetRequiredService<TokenCredential>()));
}
else
{
    var root = builder.Configuration["Storage:LocalRoot"] ?? "work/documents";
    builder.Services.AddSingleton<ICaseDocumentStore>(_ => new LocalCaseDocumentStore(root));
}
if (builder.Configuration["AI:Provider"] == "OpenAI")
{
    var apiKey = builder.Configuration["AI:ApiKey"]
        ?? throw new InvalidOperationException("AI:ApiKey is required for the OpenAI provider.");
    var model = builder.Configuration["AI:Model"] ?? "gpt-5-mini";
    var inputPrice = builder.Configuration.GetValue<decimal?>("AI:InputPricePerMillion") ?? 0.25m;
    var outputPrice = builder.Configuration.GetValue<decimal?>("AI:OutputPricePerMillion") ?? 2m;
    builder.Services.AddHttpClient<OpenAIResponsesModelProvider>();
    builder.Services.AddScoped<IAssistantModelProvider>(services => new OpenAIResponsesModelProvider(
        services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(OpenAIResponsesModelProvider)),
        apiKey,
        model,
        inputPrice,
        outputPrice));
}
else
{
    builder.Services.AddSingleton<IAssistantModelProvider, OfflineFixtureModelProvider>();
}
if (builder.Configuration["ContentSafety:Provider"] == "AzureAIContentSafety")
{
    var endpoint = builder.Configuration["ContentSafety:Endpoint"]
        ?? throw new InvalidOperationException("ContentSafety:Endpoint is required for Azure AI Content Safety.");
    builder.Services.AddHttpClient<AzureContentSafetyScanner>();
    builder.Services.AddScoped<IContentSafetyScanner>(services => new AzureContentSafetyScanner(
        services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(AzureContentSafetyScanner)),
        services.GetRequiredService<TokenCredential>(),
        new Uri(endpoint)));
}
else
{
    builder.Services.AddSingleton<IContentSafetyScanner, DeterministicContentSafetyScanner>();
}
builder.Services.AddSingleton<PromptInjectionDetector>();
builder.Services.AddSingleton<CitationValidator>();
builder.Services.AddSingleton<RiskClassifier>();
builder.Services.AddSingleton<ControlEvaluationRunner>();
// Retrieval is measured against the configured retriever, but the answers it is measured on
// are generated by the offline fixture: an evaluation run must be reproducible and must not
// spend the live-model budget every time an administrator presses the button.
builder.Services.AddScoped(services => new RagEvaluationRunner(
    services.GetRequiredService<IPolicyRetriever>(),
    new OfflineFixtureModelProvider(),
    services.GetRequiredService<CitationValidator>(),
    services.GetRequiredService<RiskClassifier>(),
    services.GetRequiredService<IPiiRedactor>()));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<DemoIdentityService>();
builder.Services.AddScoped<AuditWriter>();
builder.Services.AddDbContext<NorthstarDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("Northstar")
        ?? throw new InvalidOperationException("ConnectionStrings:Northstar is required.");
    if (builder.Configuration["Database:Provider"] == "SqlServer")
    {
        options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(3));
    }
    else
    {
        options.UseSqlite(connectionString);
    }
});
var allowedOrigins = builder.Configuration.GetSection("Security:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddPolicy("SitesBff", policy =>
    policy.WithOrigins(allowedOrigins).WithMethods("GET", "POST", "PATCH").WithHeaders("Content-Type", "X-Correlation-ID", "X-Northstar-Demo-User", "Idempotency-Key", "If-Match")));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Request.Headers["X-Northstar-Demo-User"].FirstOrDefault() ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

var app = builder.Build();

app.UseCors("SitesBff");
app.UseRateLimiter();

app.Use(async (context, next) =>
{
    var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault()
        ?? Guid.NewGuid().ToString("n");
    context.Items[RequestContext.CorrelationIdItem] = correlationId;
    context.Response.Headers["X-Correlation-ID"] = correlationId;
    using (app.Logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
    {
        await next(context);
    }
});

app.Use(async (context, next) =>
{
    if (!app.Environment.IsDevelopment()
        && !app.Environment.IsEnvironment("Testing")
        && context.Request.Path.StartsWithSegments("/api/v1"))
    {
        var configuredSecret = app.Configuration["Security:BffSharedSecret"];
        var suppliedSecret = context.Request.Headers["X-Northstar-Bff-Key"].FirstOrDefault();
        var configuredBytes = Encoding.UTF8.GetBytes(configuredSecret ?? string.Empty);
        var suppliedBytes = Encoding.UTF8.GetBytes(suppliedSecret ?? string.Empty);
        if (configuredBytes.Length == 0
            || configuredBytes.Length != suppliedBytes.Length
            || !CryptographicOperations.FixedTimeEquals(configuredBytes, suppliedBytes))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new
            {
                code = "BFF_AUTHENTICATION_REQUIRED",
                message = "The API accepts portfolio traffic only through the authorized backend-for-frontend.",
                correlationId = RequestContext.CorrelationId(context)
            });
            return;
        }
    }

    await next(context);
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapPost("/api/v1/demo/redact", (RedactionRequest request, IPiiRedactor redactor) =>
        Results.Ok(redactor.Redact(request.Text)));
}

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    service = "Northstar Case Management API",
    syntheticDataOnly = true
}));

app.MapSystemOfRecordEndpoints();
app.MapCaseAssistEndpoints();
app.MapCaseDocumentEndpoints();

if (app.Configuration["Database:Provider"] != "SqlServer")
{
    var sqliteConnection = new SqliteConnectionStringBuilder(app.Configuration.GetConnectionString("Northstar"));
    var databaseDirectory = Path.GetDirectoryName(Path.GetFullPath(sqliteConnection.DataSource));
    if (!string.IsNullOrWhiteSpace(databaseDirectory)) Directory.CreateDirectory(databaseDirectory);
}
await using (var scope = app.Services.CreateAsyncScope())
{
    await SyntheticDataSeeder.SeedAsync(scope.ServiceProvider.GetRequiredService<NorthstarDbContext>());
}

await app.RunAsync();

public sealed record RedactionRequest(string Text);

public partial class Program;
