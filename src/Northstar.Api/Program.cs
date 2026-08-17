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
if (builder.Configuration["Search:Provider"] == "AzureAiSearch")
{
    var endpoint = builder.Configuration["Search:Endpoint"]
        ?? throw new InvalidOperationException("Search:Endpoint is required for Azure AI Search.");
    var indexName = builder.Configuration["Search:IndexName"]
        ?? throw new InvalidOperationException("Search:IndexName is required for Azure AI Search.");
    builder.Services.AddHttpClient<AzureSearchPolicyRetriever>();
    builder.Services.AddScoped<IPolicyRetriever>(services => new AzureSearchPolicyRetriever(
        services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(AzureSearchPolicyRetriever)),
        services.GetRequiredService<TokenCredential>(),
        new Uri(endpoint),
        indexName));
}
else
{
    builder.Services.AddScoped<IPolicyRetriever, DatabasePolicyRetriever>();
}
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
