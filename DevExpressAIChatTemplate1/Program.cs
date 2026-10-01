using Azure;
using Azure.AI.OpenAI;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using DevExpressAIChatTemplate1.Components;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Qdrant.Client;
using DevExpress.AIIntegration.Chat;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = options.DefaultPolicy;
});

builder.Services.AddDevExpressBlazor(options =>
{
    options.SizeMode = DevExpress.Blazor.SizeMode.Medium;
});
builder.Services.AddMvc();

var openAiServiceSettings = builder.Configuration.GetSection("AzureOpenAISettings").Get<AzureOpenAIServiceSettings>();
if (openAiServiceSettings == null ||
    string.IsNullOrEmpty(openAiServiceSettings.Endpoint) ||
    string.IsNullOrEmpty(openAiServiceSettings.Key) ||
    string.IsNullOrEmpty(openAiServiceSettings.DeploymentName))
    throw new InvalidOperationException("Specify the Azure OpenAI endpoint, key, and deployment name in the 'appsettings.json' file.");
var azureOpenAi = new AzureOpenAIClient(
     new Uri(openAiServiceSettings.Endpoint),
     new AzureKeyCredential(openAiServiceSettings.Key));

var chatClient = azureOpenAi
    .GetChatClient(openAiServiceSettings.DeploymentName)
    .AsIChatClient();

var embeddingGenerator = azureOpenAi
    .GetEmbeddingClient(openAiServiceSettings.DeploymentEmbeddingName)
    .AsIEmbeddingGenerator();

// after you build `builder` and read openAiServiceSettings:
var formRecognizerSettings = builder.Configuration.GetSection("AzureFormRecognizer").Get<AzureFormRecognizerSettings>();
if (formRecognizerSettings != null &&
    !string.IsNullOrEmpty(formRecognizerSettings.Endpoint) &&
    !string.IsNullOrEmpty(formRecognizerSettings.Key))
{
    var docAnalysisClient = new DocumentAnalysisClient(new Uri(formRecognizerSettings.Endpoint), new AzureKeyCredential(formRecognizerSettings.Key));
    builder.Services.AddSingleton(docAnalysisClient);
    builder.Services.AddSingleton<IVisionService>(sp => new AzureFormVisionService(sp.GetRequiredService<DocumentAnalysisClient>(), formRecognizerSettings.ModelId!));
}
else
{
    // Optionally register a no-op vision service that returns empty results to keep app running.
    builder.Services.AddSingleton<IVisionService>(sp => new NoopVisionService());
}

string qdrantUrl = builder.Configuration.GetConnectionString("QdrantVectorDb") ?? "http://localhost:6334/";
var qdrantClient = new QdrantClient(new Uri(qdrantUrl));

builder.Services.AddQdrantVectorStore(_ => qdrantClient);
//builder.Services.AddQdrantCollection<string, IngestedChunk>(IngestedChunk.CollectionName, _ => qdrantClient);
builder.Services.AddQdrantCollection<Guid, IngestedChunk>(IngestedChunk.CollectionName, _ => qdrantClient);

/*var vectorStorePath = Path.Combine(AppContext.BaseDirectory, "vector-store.db");
var vectorStoreConnectionString = $"Data Source={vectorStorePath}";
using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(vectorStoreConnectionString))
{
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText = "PRAGMA journal_mode=WAL;"; // This only needs to run successfully once!
    command.ExecuteNonQuery();
}
builder.Services.AddSqliteVectorStore(_ => vectorStoreConnectionString);
builder.Services.AddSqliteCollection<string, IngestedChunk>(IngestedChunk.CollectionName, vectorStoreConnectionString);*/

builder.Services.AddScoped<IChatResponseProvider>((sp) =>
{
    var toolEnableClient = chatClient.AsBuilder().UseDXTools().UseFunctionInvocation().UseLogging().Build(sp);

    return toolEnableClient.AsIChatResponseProvider();
});
//builder.Services.AddScoped<IChatResponseProvider>(
//    _ => chatClient.AsIChatResponseProvider());
builder.Services.AddTransient<HybridPdfReader>();
builder.Services.AddTransient<IngestionDocumentReader, HybridPdfReader>();
builder.Services.AddScoped<IPdfRenderService, PdfRenderService>();
builder.Services.AddScoped<DataIngestor>();
builder.Services.AddScoped<SemanticSearch>();
builder.Services.AddScoped<FileIngestionService>();
builder.Services.AddKeyedSingleton("ingestion_directory", new DirectoryInfo(Path.Combine(builder.Environment.WebRootPath, "Data")));
//builder.Services.AddChatClient(chatClient).UseDXTools().UseFunctionInvocation().UseLogging();
builder.Services.AddEmbeddingGenerator(embeddingGenerator).UseLogging();
builder.Services.AddDevExpressAI();

builder.Services.AddCascadingAuthenticationState();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
else
{
    app.UseDeveloperExceptionPage();
}

app.UseHttpsRedirection();

app.UseAntiforgery();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AllowAnonymous();

app.Run();
