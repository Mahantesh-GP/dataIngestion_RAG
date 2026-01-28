using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MSDATAIngestion_RAG;

/// <summary>
/// Example usage of the data ingestion pipeline with CosmosDB
/// </summary>
public class Program
{
    public static async Task Main(string[] args)
    {
        // Configure Azure OpenAI and CosmosDB credentials
        var config = new DataIngestionConfig
        {
            // Azure OpenAI Configuration
            AzureOpenAIEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") 
                ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT not set"),
            AzureOpenAIKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY") 
                ?? throw new InvalidOperationException("AZURE_OPENAI_KEY not set"),
            EmbeddingDeploymentName = "text-embedding-3-small",
            TokenizerModel = "gpt-4",

            // Chunking Configuration
            MaxTokensPerChunk = 2000,
            OverlapTokens = 0,
            ChunkingStrategy = ChunkingStrategy.SemanticAware, // Change as needed

            // CosmosDB Configuration
            CosmosDBConnectionString = Environment.GetEnvironmentVariable("COSMOS_DB_CONNECTION_STRING")
                ?? throw new InvalidOperationException("COSMOS_DB_CONNECTION_STRING not set"),
            CosmosDBDatabaseName = "rag-ingestion",
            CosmosDBContainerName = "chunks",
            EmbeddingDimension = 1536
        };

        // Create logger factory
        var loggerFactory = new SimpleLoggerFactory();

        // Initialize the data ingestion pipeline
        using var pipeline = new DataIngestionPipeline(config, loggerFactory);

        Console.WriteLine("========================================");
        Console.WriteLine("Data Ingestion Pipeline with CosmosDB");
        Console.WriteLine("========================================\n");

        Console.WriteLine($"Chunking Strategy: {config.ChunkingStrategy}");
        Console.WriteLine(DataIngestionPipeline.GetChunkingStrategyInfo(config.ChunkingStrategy));
        Console.WriteLine();

        try
        {
            // Example 1: Process documents from a directory
            await ProcessDirectoryExample(pipeline);

            // Example 2: Process a single document
            await ProcessSingleDocumentExample(pipeline);

            // Example 3: Search similar chunks
            await SearchChunksExample(pipeline);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nError: {ex.Message}");
        }

        Console.WriteLine("\nDone!");
    }

    /// <summary>
    /// Example: Process all documents from a directory
    /// </summary>
    private static async Task ProcessDirectoryExample(DataIngestionPipeline pipeline)
    {
        Console.WriteLine("\n--- Processing Documents from Directory ---");

        try
        {
            // Process all markdown files in the current directory
            var results = await pipeline.ProcessDocumentsAsync(
                directoryPath: ".",
                searchPattern: "*.md"
            );

            Console.WriteLine($"\nProcessed {results.Length} documents:");
            foreach (var result in results)
            {
                Console.WriteLine($"  - {result.DocumentId}: {(result.Succeeded ? "✓ Success" : "✗ Failed")}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing directory: {ex.Message}");
        }
    }

    /// <summary>
    /// Example: Process a single document
    /// </summary>
    private static async Task ProcessSingleDocumentExample(DataIngestionPipeline pipeline)
    {
        Console.WriteLine("\n--- Processing Single Document ---");

        try
        {
            // Process a specific markdown file
            var filePath = "./sample-document.md";

            if (!System.IO.File.Exists(filePath))
            {
                Console.WriteLine($"File not found: {filePath}. Skipping this example.");
                return;
            }

            var result = await pipeline.ProcessSingleDocumentAsync(filePath);
            Console.WriteLine($"\nDocument: {result.DocumentId}");
            Console.WriteLine($"Status: {(result.Succeeded ? "✓ Success" : "✗ Failed")}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing document: {ex.Message}");
        }
    }

    /// <summary>
    /// Example: Search for similar chunks using vector embeddings
    /// </summary>
    private static async Task SearchChunksExample(DataIngestionPipeline pipeline)
    {
        Console.WriteLine("\n--- Searching Similar Chunks ---");

        try
        {
            var cosmosDBWriter = await pipeline.GetCosmosDBWriterAsync();

            if (cosmosDBWriter == null)
            {
                Console.WriteLine("CosmosDB writer not available");
                return;
            }

            // Example query text
            var queryText = "machine learning data ingestion retrieval augmented generation";

            Console.WriteLine($"Query: \"{queryText}\"");
            Console.WriteLine("\nSearching for similar chunks in CosmosDB...");

            // In a real scenario, you would generate an embedding for the query text
            // using the same embedding model as used for chunk storage
            // For this example, we'll skip the actual search as it requires
            // the chunks to already exist in CosmosDB

            Console.WriteLine("Note: Vector search requires chunks to be stored in CosmosDB first.");
            Console.WriteLine("Process documents first to populate the vector database.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error searching chunks: {ex.Message}");
        }
    }
}

/// <summary>
/// Example: Interactive chunking strategy selector
/// </summary>
public class ChunkingStrategySelector
{
    public static ChunkingStrategy SelectChunkingStrategy()
    {
        Console.WriteLine("\nSelect Chunking Strategy:");
        Console.WriteLine("1. Header-Based Chunking");
        Console.WriteLine("2. Section-Based Chunking");
        Console.WriteLine("3. Semantic-Aware Chunking (Recommended)");
        Console.Write("\nEnter your choice (1-3): ");

        return Console.ReadLine()?.Trim() switch
        {
            "1" => ChunkingStrategy.HeaderBased,
            "2" => ChunkingStrategy.SectionBased,
            "3" => ChunkingStrategy.SemanticAware,
            _ => ChunkingStrategy.SemanticAware
        };
    }
}
