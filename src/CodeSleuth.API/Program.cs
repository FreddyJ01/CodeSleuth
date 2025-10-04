using CodeSleuth.Core.Services;
using CodeSleuth.Infrastructure.Git;
using CodeSleuth.Infrastructure.AI;
using CodeSleuth.Infrastructure.VectorDatabase;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Add Swagger/OpenAPI support
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "CodeSleuth API", Version = "v1" });
    
    // Include XML comments for API documentation
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }
});

// Register application services
// Note: In a production environment, these configuration values should come from appsettings.json or environment variables
builder.Services.AddSingleton<GitService>(provider =>
{
    var logger = provider.GetRequiredService<ILogger<GitService>>();
    var reposPath = Path.Combine(Path.GetTempPath(), "CodeSleuth", "repos");
    return new GitService(logger, reposPath);
});

builder.Services.AddSingleton<CodeParsingService>(provider =>
{
    var logger = provider.GetRequiredService<ILogger<CodeParsingService>>();
    return new CodeParsingService(logger);
});

builder.Services.AddSingleton<EmbeddingService>(provider =>
{
    var logger = provider.GetRequiredService<ILogger<EmbeddingService>>();
    var configuration = provider.GetRequiredService<IConfiguration>();
    
    // TODO: These should be configured in appsettings.json
    var endpoint = configuration.GetValue<string>("AzureOpenAI:Endpoint") ?? "https://your-openai-endpoint.openai.azure.com";
    var apiKey = configuration.GetValue<string>("AzureOpenAI:ApiKey") ?? "your-api-key";
    var model = configuration.GetValue<string>("AzureOpenAI:EmbeddingModel") ?? "text-embedding-3-small";
    
    return new EmbeddingService(endpoint, apiKey, model, logger);
});

builder.Services.AddSingleton<QdrantService>(provider =>
{
    var logger = provider.GetRequiredService<ILogger<QdrantService>>();
    var configuration = provider.GetRequiredService<IConfiguration>();
    
    // TODO: This should be configured in appsettings.json
    var endpoint = configuration.GetValue<string>("Qdrant:Endpoint") ?? "localhost:6333";
    
    return new QdrantService(logger, endpoint);
});

builder.Services.AddSingleton<IndexingService>();

// Add logging
builder.Logging.AddConsole();
builder.Logging.AddDebug();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "CodeSleuth API v1");
        c.RoutePrefix = string.Empty; // Set Swagger UI at the app's root
    });
}

app.UseHttpsRedirection();

// Enable routing and controllers
app.UseRouting();
app.MapControllers();

app.Run();
