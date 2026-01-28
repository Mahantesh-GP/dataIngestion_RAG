using System;
using System.IO;

namespace MSDATAIngestion_RAG;

/// <summary>
/// Configuration helper for managing environment and settings
/// </summary>
public class ConfigurationHelper
{
    /// <summary>
    /// Loads configuration from environment variables or .env file
    /// </summary>
    public static DataIngestionConfig LoadConfiguration(string? envFilePath = null)
    {
        // Try to load from .env file first if provided
        if (!string.IsNullOrEmpty(envFilePath) && File.Exists(envFilePath))
        {
            LoadEnvFile(envFilePath);
        }

        var config = new DataIngestionConfig
        {
            AzureOpenAIEndpoint = GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT", required: true),
            AzureOpenAIKey = GetEnvironmentVariable("AZURE_OPENAI_KEY", required: true),
            CosmosDBConnectionString = GetEnvironmentVariable("COSMOS_DB_CONNECTION_STRING", required: true),
            EmbeddingDeploymentName = GetEnvironmentVariable("EMBEDDING_DEPLOYMENT_NAME", "text-embedding-3-small"),
            TokenizerModel = GetEnvironmentVariable("TOKENIZER_MODEL", "gpt-4"),
            MaxTokensPerChunk = int.Parse(GetEnvironmentVariable("MAX_TOKENS_PER_CHUNK", "2000")),
            OverlapTokens = int.Parse(GetEnvironmentVariable("OVERLAP_TOKENS", "0")),
            CosmosDBDatabaseName = GetEnvironmentVariable("COSMOS_DB_DATABASE_NAME", "rag-ingestion"),
            CosmosDBContainerName = GetEnvironmentVariable("COSMOS_DB_CONTAINER_NAME", "chunks"),
            EmbeddingDimension = int.Parse(GetEnvironmentVariable("EMBEDDING_DIMENSION", "1536"))
        };

        // Parse chunking strategy
        var strategyStr = GetEnvironmentVariable("CHUNKING_STRATEGY", "SemanticAware");
        if (Enum.TryParse<ChunkingStrategy>(strategyStr, ignoreCase: true, out var strategy))
        {
            config.ChunkingStrategy = strategy;
        }

        return config;
    }

    /// <summary>
    /// Gets environment variable with optional default and required flag
    /// </summary>
    private static string GetEnvironmentVariable(
        string name,
        string? defaultValue = null,
        bool required = false)
    {
        var value = Environment.GetEnvironmentVariable(name);

        if (string.IsNullOrEmpty(value))
        {
            if (required)
                throw new InvalidOperationException($"Environment variable '{name}' is required but not set.");

            return defaultValue ?? string.Empty;
        }

        return value;
    }

    /// <summary>
    /// Loads variables from a .env file
    /// </summary>
    private static void LoadEnvFile(string filePath)
    {
        try
        {
            var lines = File.ReadAllLines(filePath);

            foreach (var line in lines)
            {
                // Skip comments and empty lines
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                    continue;

                var parts = line.Split('=', 2);
                if (parts.Length == 2)
                {
                    var key = parts[0].Trim();
                    var value = parts[1].Trim();

                    // Remove quotes if present
                    if (value.StartsWith("\"") && value.EndsWith("\""))
                        value = value[1..^1];

                    Environment.SetEnvironmentVariable(key, value);
                }
            }

            Console.WriteLine($"Loaded environment variables from: {filePath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Could not load .env file: {ex.Message}");
        }
    }

    /// <summary>
    /// Validates configuration and shows summary
    /// </summary>
    public static void ValidateAndDisplayConfiguration(DataIngestionConfig config)
    {
        Console.WriteLine("\n========================================");
        Console.WriteLine("Configuration Validation");
        Console.WriteLine("========================================\n");

        // Validate Azure OpenAI
        ValidateProperty("Azure OpenAI Endpoint", config.AzureOpenAIEndpoint);
        ValidateProperty("Azure OpenAI Key", config.AzureOpenAIKey, maskSensitive: true);
        ValidateProperty("Embedding Deployment", config.EmbeddingDeploymentName);

        // Validate CosmosDB
        ValidateProperty("CosmosDB Connection", config.CosmosDBConnectionString, maskSensitive: true);
        ValidateProperty("CosmosDB Database", config.CosmosDBDatabaseName);
        ValidateProperty("CosmosDB Container", config.CosmosDBContainerName);

        // Display settings
        Console.WriteLine("\nChunking Configuration:");
        Console.WriteLine($"  Strategy: {config.ChunkingStrategy}");
        Console.WriteLine($"  Max Tokens/Chunk: {config.MaxTokensPerChunk}");
        Console.WriteLine($"  Overlap Tokens: {config.OverlapTokens}");
        Console.WriteLine($"  Tokenizer Model: {config.TokenizerModel}");
        Console.WriteLine($"  Embedding Dimension: {config.EmbeddingDimension}");

        Console.WriteLine("\nStrategyInfo:");
        Console.WriteLine(DataIngestionPipeline.GetChunkingStrategyInfo(config.ChunkingStrategy));
        Console.WriteLine("\n========================================\n");
    }

    /// <summary>
    /// Validates a configuration property
    /// </summary>
    private static void ValidateProperty(
        string propertyName,
        string value,
        bool maskSensitive = false)
    {
        if (string.IsNullOrEmpty(value))
        {
            Console.WriteLine($"  ✗ {propertyName}: NOT SET");
            return;
        }

        var displayValue = maskSensitive
            ? $"{value[..3]}...{value[^3..]}"
            : value;

        Console.WriteLine($"  ✓ {propertyName}: {displayValue}");
    }

    /// <summary>
    /// Creates a sample .env file template
    /// </summary>
    public static void CreateEnvTemplate(string filePath = ".env.template")
    {
        var template = @"# Azure OpenAI Configuration
AZURE_OPENAI_ENDPOINT=https://<your-resource>.openai.azure.com/
AZURE_OPENAI_KEY=<your-api-key>
EMBEDDING_DEPLOYMENT_NAME=text-embedding-3-small
TOKENIZER_MODEL=gpt-4

# CosmosDB Configuration
COSMOS_DB_CONNECTION_STRING=AccountEndpoint=https://<account>.documents.azure.com:443/;AccountKey=<key>;
COSMOS_DB_DATABASE_NAME=rag-ingestion
COSMOS_DB_CONTAINER_NAME=chunks

# Chunking Configuration
CHUNKING_STRATEGY=SemanticAware
MAX_TOKENS_PER_CHUNK=2000
OVERLAP_TOKENS=0

# Embedding Configuration
EMBEDDING_DIMENSION=1536
";

        File.WriteAllText(filePath, template);
        Console.WriteLine($"Created template file: {filePath}");
    }

    /// <summary>
    /// Creates a default configuration for testing
    /// </summary>
    public static DataIngestionConfig CreateTestConfiguration()
    {
        // Load from environment or use defaults for testing
        return new DataIngestionConfig
        {
            AzureOpenAIEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") 
                ?? "https://test.openai.azure.com/",
            AzureOpenAIKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY") 
                ?? "test-key",
            CosmosDBConnectionString = Environment.GetEnvironmentVariable("COSMOS_DB_CONNECTION_STRING")
                ?? "AccountEndpoint=https://test.documents.azure.com:443/;AccountKey=test-key;",
            ChunkingStrategy = ChunkingStrategy.SemanticAware,
            MaxTokensPerChunk = 2000,
            OverlapTokens = 0,
            EmbeddingDimension = 1536
        };
    }
}

/// <summary>
/// Configuration preset for common scenarios
/// </summary>
public static class ConfigurationPresets
{
    /// <summary>
    /// Configuration optimized for technical documentation
    /// </summary>
    public static DataIngestionConfig ForTechnicalDocs(DataIngestionConfig baseConfig)
    {
        return new DataIngestionConfig
        {
            AzureOpenAIEndpoint = baseConfig.AzureOpenAIEndpoint,
            AzureOpenAIKey = baseConfig.AzureOpenAIKey,
            CosmosDBConnectionString = baseConfig.CosmosDBConnectionString,
            ChunkingStrategy = ChunkingStrategy.HeaderBased,  // Use header-based for tech docs
            MaxTokensPerChunk = 2500,  // Larger chunks for context
            OverlapTokens = 100,  // Overlap for continuity
            EmbeddingDeploymentName = baseConfig.EmbeddingDeploymentName,
            EmbeddingDimension = baseConfig.EmbeddingDimension
        };
    }

    /// <summary>
    /// Configuration optimized for news articles and blogs
    /// </summary>
    public static DataIngestionConfig ForNewsAndArticles(DataIngestionConfig baseConfig)
    {
        return new DataIngestionConfig
        {
            AzureOpenAIEndpoint = baseConfig.AzureOpenAIEndpoint,
            AzureOpenAIKey = baseConfig.AzureOpenAIKey,
            CosmosDBConnectionString = baseConfig.CosmosDBConnectionString,
            ChunkingStrategy = ChunkingStrategy.SectionBased,  // Section-based for articles
            MaxTokensPerChunk = 1500,  // Smaller chunks for varied content
            OverlapTokens = 50,  // Minimal overlap
            EmbeddingDeploymentName = baseConfig.EmbeddingDeploymentName,
            EmbeddingDimension = baseConfig.EmbeddingDimension
        };
    }

    /// <summary>
    /// Configuration optimized for semantic search and RAG
    /// </summary>
    public static DataIngestionConfig ForSemanticRAG(DataIngestionConfig baseConfig)
    {
        return new DataIngestionConfig
        {
            AzureOpenAIEndpoint = baseConfig.AzureOpenAIEndpoint,
            AzureOpenAIKey = baseConfig.AzureOpenAIKey,
            CosmosDBConnectionString = baseConfig.CosmosDBConnectionString,
            ChunkingStrategy = ChunkingStrategy.SemanticAware,  // Best for meaning preservation
            MaxTokensPerChunk = 2000,  // Balanced chunk size
            OverlapTokens = 0,  // No overlap for deduplication
            EmbeddingDeploymentName = baseConfig.EmbeddingDeploymentName,
            EmbeddingDimension = baseConfig.EmbeddingDimension
        };
    }

    /// <summary>
    /// Configuration optimized for low-latency retrieval
    /// </summary>
    public static DataIngestionConfig ForFastRetrieval(DataIngestionConfig baseConfig)
    {
        return new DataIngestionConfig
        {
            AzureOpenAIEndpoint = baseConfig.AzureOpenAIEndpoint,
            AzureOpenAIKey = baseConfig.AzureOpenAIKey,
            CosmosDBConnectionString = baseConfig.CosmosDBConnectionString,
            ChunkingStrategy = ChunkingStrategy.SectionBased,  // Fast to compute
            MaxTokensPerChunk = 1000,  // Small chunks for fast retrieval
            OverlapTokens = 0,  // No overlap
            EmbeddingDeploymentName = baseConfig.EmbeddingDeploymentName,
            EmbeddingDimension = baseConfig.EmbeddingDimension
        };
    }
}
