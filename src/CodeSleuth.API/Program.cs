using CodeSleuth.Core.Services;
using CodeSleuth.Infrastructure.Git;
using CodeSleuth.Infrastructure.AI;
using CodeSleuth.Infrastructure.VectorDatabase;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

var builder = WebApplication.CreateBuilder(args);

// Add user secrets for development
if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets<Program>();
}

// Add services to the container
builder.Services.AddControllers();

// Add CORS for development
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Add Swagger/OpenAPI support
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { 
        Title = "CodeSleuth API", 
        Version = "v1",
        Description = "A RAG-based code analysis API for repository indexing and intelligent querying"
    });
    
    // Include XML comments for API documentation
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }
});

// Configure Semantic Kernel with Azure OpenAI
builder.Services.AddSingleton<Kernel>(provider =>
{
    var configuration = provider.GetRequiredService<IConfiguration>();
    var logger = provider.GetRequiredService<ILogger<Kernel>>();
    
    try
    {
        var apiKey = configuration["OpenAI:ApiKey"];
        var endpoint = configuration["OpenAI:Endpoint"];
        var chatModel = configuration.GetValue<string>("OpenAI:ChatModel") ?? "gpt-4";
        
        // Validate required configuration
        if (string.IsNullOrEmpty(apiKey))
        {
            var message = "OpenAI:ApiKey is required. Please set it using: dotnet user-secrets set \"OpenAI:ApiKey\" \"your-key\"";
            logger.LogError(message);
            throw new InvalidOperationException(message);
        }
        
        if (string.IsNullOrEmpty(endpoint))
        {
            var message = "OpenAI:Endpoint is required. Please set it using: dotnet user-secrets set \"OpenAI:Endpoint\" \"your-endpoint\"";
            logger.LogError(message);
            throw new InvalidOperationException(message);
        }
        
        logger.LogInformation("Configuring Semantic Kernel with endpoint: {Endpoint} and model: {ChatModel}", 
                             endpoint, chatModel);
        
        var kernelBuilder = Kernel.CreateBuilder();
        
        // Use compatible Azure OpenAI configuration
        kernelBuilder.AddAzureOpenAIChatCompletion(
            deploymentName: chatModel,
            endpoint: endpoint,
            apiKey: apiKey);
        
        var kernel = kernelBuilder.Build();
        logger.LogInformation("Semantic Kernel configured successfully");
        
        return kernel;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to configure Semantic Kernel");
        throw;
    }
});

// Extract IChatCompletionService from Kernel
builder.Services.AddSingleton<IChatCompletionService>(provider =>
{
    var logger = provider.GetRequiredService<ILogger<IChatCompletionService>>();
    
    try
    {
        var kernel = provider.GetRequiredService<Kernel>();
        var chatService = kernel.GetRequiredService<IChatCompletionService>();
        logger.LogInformation("IChatCompletionService extracted successfully from Kernel");
        return chatService;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to extract IChatCompletionService from Kernel");
        throw;
    }
});

// Register application services
builder.Services.AddSingleton<GitService>(provider =>
{
    var logger = provider.GetRequiredService<ILogger<GitService>>();
    var reposPath = Path.Combine(".", "repos"); // Use relative path from current directory
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
    
    try
    {
        var endpoint = configuration["OpenAI:Endpoint"];
        var apiKey = configuration["OpenAI:ApiKey"];
        var model = configuration.GetValue<string>("OpenAI:EmbeddingModel") ?? "text-embedding-3-small";
        
        // Validate required configuration
        if (string.IsNullOrEmpty(endpoint))
        {
            var message = "OpenAI:Endpoint is required. Please set it using: dotnet user-secrets set \"OpenAI:Endpoint\" \"your-endpoint\"";
            logger.LogError(message);
            throw new InvalidOperationException(message);
        }
        
        if (string.IsNullOrEmpty(apiKey))
        {
            var message = "OpenAI:ApiKey is required. Please set it using: dotnet user-secrets set \"OpenAI:ApiKey\" \"your-key\"";
            logger.LogError(message);
            throw new InvalidOperationException(message);
        }
        
        logger.LogInformation("Configuring EmbeddingService with endpoint: {Endpoint} and model: {EmbeddingModel}", 
                             endpoint, model);
        
        var embeddingService = new EmbeddingService(endpoint, apiKey, model, logger);
        logger.LogInformation("EmbeddingService configured successfully");
        
        return embeddingService;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to configure EmbeddingService");
        throw;
    }
});

// Register the interface using the same instance
builder.Services.AddSingleton<IEmbeddingService>(provider => provider.GetRequiredService<EmbeddingService>());

// Register HttpClient for Qdrant REST API
builder.Services.AddHttpClient<QdrantRestService>(client =>
{
    var configuration = builder.Configuration;
    var host = configuration.GetValue<string>("Qdrant:Host") ?? "localhost";
    var port = configuration.GetValue<int>("Qdrant:Port", 6333);
    
    client.BaseAddress = new Uri($"http://{host}:{port}");
    client.Timeout = TimeSpan.FromMinutes(5);
    
    // Add default headers if needed
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});

// Register the interface using the REST service
builder.Services.AddSingleton<IQdrantService>(provider => provider.GetRequiredService<QdrantRestService>());

builder.Services.AddScoped<IndexingService>();
builder.Services.AddScoped<QueryService>();

// Add logging
builder.Logging.ClearProviders();
if (builder.Environment.IsDevelopment())
{
    builder.Logging.AddConsole();
    builder.Logging.AddDebug();
    builder.Logging.SetMinimumLevel(LogLevel.Information);
}
else
{
    builder.Logging.AddConsole();
    builder.Logging.SetMinimumLevel(LogLevel.Warning);
}

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "CodeSleuth API v1");
        c.RoutePrefix = "swagger"; // Swagger UI at /swagger
    });
    
    // Use CORS in development
    app.UseCors();
}

app.UseHttpsRedirection();

// Enable routing and controllers
app.UseRouting();
app.MapControllers();

app.Run();
