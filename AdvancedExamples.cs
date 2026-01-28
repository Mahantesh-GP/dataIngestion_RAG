using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MSDATAIngestion_RAG;

/// <summary>
/// Advanced usage examples and scenarios
/// </summary>
public class AdvancedExamples
{
    /// <summary>
    /// Example 1: Process documents with different chunking strategies
    /// </summary>
    public static async Task Example_CompareChunkingStrategies()
    {
        Console.WriteLine("\n=== Example 1: Compare Chunking Strategies ===\n");

        var baseConfig = ConfigurationHelper.LoadConfiguration(".env");

        var strategies = new[]
        {
            ChunkingStrategy.HeaderBased,
            ChunkingStrategy.SectionBased,
            ChunkingStrategy.SemanticAware
        };

        foreach (var strategy in strategies)
        {
            Console.WriteLine($"\n--- {strategy} ---");
            Console.WriteLine(DataIngestionPipeline.GetChunkingStrategyInfo(strategy));

            var config = new DataIngestionConfig
            {
                AzureOpenAIEndpoint = baseConfig.AzureOpenAIEndpoint,
                AzureOpenAIKey = baseConfig.AzureOpenAIKey,
                CosmosDBConnectionString = baseConfig.CosmosDBConnectionString,
                ChunkingStrategy = strategy,
                MaxTokensPerChunk = 2000
            };

            // Process with this strategy
            // var results = await new DataIngestionPipeline(config).ProcessDocumentsAsync("./docs");
        }
    }

    /// <summary>
    /// Example 2: Use configuration presets for different use cases
    /// </summary>
    public static async Task Example_ConfigurationPresets()
    {
        Console.WriteLine("\n=== Example 2: Configuration Presets ===\n");

        var baseConfig = ConfigurationHelper.LoadConfiguration(".env");

        // Choose preset based on content type
        var presets = new Dictionary<string, DataIngestionConfig>
        {
            ["Technical Docs"] = ConfigurationPresets.ForTechnicalDocs(baseConfig),
            ["News/Articles"] = ConfigurationPresets.ForNewsAndArticles(baseConfig),
            ["Semantic RAG"] = ConfigurationPresets.ForSemanticRAG(baseConfig),
            ["Fast Retrieval"] = ConfigurationPresets.ForFastRetrieval(baseConfig)
        };

        foreach (var (name, config) in presets)
        {
            Console.WriteLine($"\n--- {name} ---");
            Console.WriteLine($"Strategy: {config.ChunkingStrategy}");
            Console.WriteLine($"Max Tokens: {config.MaxTokensPerChunk}");
            Console.WriteLine($"Overlap: {config.OverlapTokens}");
        }
    }

    /// <summary>
    /// Example 3: Process documents and retrieve chunks
    /// </summary>
    public static async Task Example_ProcessAndRetrieve()
    {
        Console.WriteLine("\n=== Example 3: Process and Retrieve Chunks ===\n");

        var config = ConfigurationHelper.LoadConfiguration(".env");
        ConfigurationHelper.ValidateAndDisplayConfiguration(config);

        using var pipeline = new DataIngestionPipeline(config);

        try
        {
            // Process documents
            Console.WriteLine("Processing documents...");
            var results = await pipeline.ProcessDocumentsAsync("./documents", "*.md");

            Console.WriteLine($"\nProcessed {results.Length} documents:");
            foreach (var result in results)
            {
                Console.WriteLine($"  - {result.DocumentId}: {(result.Succeeded ? "✓" : "✗")}");
            }

            // Get CosmosDB writer for custom operations
            var cosmosDBWriter = await pipeline.GetCosmosDBWriterAsync();
            if (cosmosDBWriter != null)
            {
                // Retrieve all chunks for a document
                var documentId = results[0].DocumentId;
                var chunks = await cosmosDBWriter.GetChunksByDocumentAsync(documentId);

                Console.WriteLine($"\nRetrieved {chunks.Count} chunks for document: {documentId}");
                for (int i = 0; i < Math.Min(3, chunks.Count); i++)
                {
                    Console.WriteLine($"\nChunk {i + 1}:");
                    Console.WriteLine($"  ID: {chunks[i].Id}");
                    Console.WriteLine($"  Length: {chunks[i].ContentLength} chars");
                    Console.WriteLine($"  Preview: {chunks[i].Content[..Math.Min(100, chunks[i].Content.Length)]}...");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Example 4: Batch process multiple documents
    /// </summary>
    public static async Task Example_BatchProcessing()
    {
        Console.WriteLine("\n=== Example 4: Batch Processing ===\n");

        var config = ConfigurationHelper.LoadConfiguration(".env");
        var pipeline = new DataIngestionPipeline(config);

        var documentPaths = new[]
        {
            "./documents/doc1.md",
            "./documents/doc2.md",
            "./documents/doc3.md"
        };

        var results = new List<(string Path, IngestionResult Result)>();

        foreach (var docPath in documentPaths)
        {
            try
            {
                Console.WriteLine($"Processing: {docPath}");
                var result = await pipeline.ProcessSingleDocumentAsync(docPath);
                results.Add((docPath, result));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Error: {ex.Message}");
            }
        }

        // Summary
        Console.WriteLine("\nBatch Processing Summary:");
        Console.WriteLine($"  Total: {results.Count}");
        Console.WriteLine($"  Successful: {results.Count(r => r.Result.Succeeded)}");
        Console.WriteLine($"  Failed: {results.Count(r => !r.Result.Succeeded)}");
    }

    /// <summary>
    /// Example 5: Search and retrieve similar chunks
    /// </summary>
    public static async Task Example_VectorSearch()
    {
        Console.WriteLine("\n=== Example 5: Vector Search ===\n");

        var config = ConfigurationHelper.LoadConfiguration(".env");
        using var pipeline = new DataIngestionPipeline(config);

        var cosmosDBWriter = await pipeline.GetCosmosDBWriterAsync();
        if (cosmosDBWriter == null)
        {
            Console.WriteLine("CosmosDB writer not available");
            return;
        }

        // Note: In production, you would:
        // 1. Generate embedding for your query using the same model
        // 2. Use that embedding to search similar chunks

        var mockQueryEmbedding = new float[1536];
        for (int i = 0; i < mockQueryEmbedding.Length; i++)
        {
            mockQueryEmbedding[i] = (float)Random.Shared.NextDouble();
        }

        try
        {
            Console.WriteLine("Searching for similar chunks...");
            var results = await cosmosDBWriter.SearchSimilarChunksAsync(
                queryEmbedding: mockQueryEmbedding,
                topK: 5
            );

            Console.WriteLine($"\nFound {results.Count} similar chunks:");
            foreach (var result in results)
            {
                Console.WriteLine($"\n  - Chunk: {result.ChunkId}");
                Console.WriteLine($"    Document: {result.DocumentId}");
                Console.WriteLine($"    Similarity: {result.Similarity:P}");
                Console.WriteLine($"    Preview: {result.Content[..Math.Min(80, result.Content.Length)]}...");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error searching: {ex.Message}");
        }
    }

    /// <summary>
    /// Example 6: Delete and cleanup
    /// </summary>
    public static async Task Example_DeleteChunks()
    {
        Console.WriteLine("\n=== Example 6: Delete Chunks ===\n");

        var config = ConfigurationHelper.LoadConfiguration(".env");
        using var pipeline = new DataIngestionPipeline(config);

        var cosmosDBWriter = await pipeline.GetCosmosDBWriterAsync();
        if (cosmosDBWriter == null)
        {
            Console.WriteLine("CosmosDB writer not available");
            return;
        }

        var documentId = "sample-document.md";

        try
        {
            // Get chunks for the document
            var chunks = await cosmosDBWriter.GetChunksByDocumentAsync(documentId);
            Console.WriteLine($"Found {chunks.Count} chunks for document: {documentId}");

            // Delete all chunks for the document
            if (chunks.Count > 0)
            {
                Console.WriteLine("Deleting chunks...");
                int deletedCount = await cosmosDBWriter.DeleteDocumentChunksAsync(documentId);
                Console.WriteLine($"Deleted {deletedCount} chunks");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error deleting chunks: {ex.Message}");
        }
    }

    /// <summary>
    /// Example 7: Custom configuration with validation
    /// </summary>
    public static async Task Example_CustomConfiguration()
    {
        Console.WriteLine("\n=== Example 7: Custom Configuration ===\n");

        // Create custom configuration
        var config = new DataIngestionConfig
        {
            AzureOpenAIEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? "",
            AzureOpenAIKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY") ?? "",
            CosmosDBConnectionString = Environment.GetEnvironmentVariable("COSMOS_DB_CONNECTION_STRING") ?? "",
            
            // Custom settings
            ChunkingStrategy = ChunkingStrategy.SemanticAware,
            MaxTokensPerChunk = 1500,  // Smaller than default
            OverlapTokens = 50,  // Add overlap
            TokenizerModel = "gpt-3.5-turbo",
            EmbeddingDeploymentName = "text-embedding-3-large",  // Larger model
            EmbeddingDimension = 3072,  // 3072 for large model
            CosmosDBDatabaseName = "custom-rag-db",
            CosmosDBContainerName = "custom-chunks"
        };

        // Validate configuration
        ConfigurationHelper.ValidateAndDisplayConfiguration(config);
    }

    /// <summary>
    /// Example 8: Error handling and resilience
    /// </summary>
    public static async Task Example_ErrorHandling()
    {
        Console.WriteLine("\n=== Example 8: Error Handling ===\n");

        var config = ConfigurationHelper.LoadConfiguration(".env");
        using var pipeline = new DataIngestionPipeline(config);

        try
        {
            // This will fail gracefully
            var result = await pipeline.ProcessSingleDocumentAsync("./non-existent.md");
            Console.WriteLine($"Processing result: {(result.Succeeded ? "Success" : "Failed")}");
        }
        catch (FileNotFoundException ex)
        {
            Console.WriteLine($"Caught exception: {ex.GetType().Name}");
            Console.WriteLine($"Message: {ex.Message}");
        }

        // Batch processing with partial failures
        var documentPaths = new[] { "./doc1.md", "./doc2.md", "./non-existent.md" };
        int successCount = 0;
        int failureCount = 0;

        foreach (var docPath in documentPaths)
        {
            try
            {
                var result = await pipeline.ProcessSingleDocumentAsync(docPath);
                if (result.Succeeded)
                    successCount++;
                else
                    failureCount++;
            }
            catch (Exception ex)
            {
                failureCount++;
                Console.WriteLine($"Failed to process {docPath}: {ex.Message}");
            }
        }

        Console.WriteLine($"\nResults: {successCount} succeeded, {failureCount} failed");
    }

    /// <summary>
    /// Run all examples
    /// </summary>
    public static async Task RunAllExamples()
    {
        Console.WriteLine("\n╔════════════════════════════════════════╗");
        Console.WriteLine("║   Advanced Examples - Data Ingestion    ║");
        Console.WriteLine("╚════════════════════════════════════════╝");

        try
        {
            await Example_CompareChunkingStrategies();
            await Example_ConfigurationPresets();
            // await Example_ProcessAndRetrieve();  // Uncomment to run
            // await Example_BatchProcessing();     // Uncomment to run
            // await Example_VectorSearch();        // Uncomment to run
            // await Example_DeleteChunks();        // Uncomment to run
            await Example_CustomConfiguration();
            await Example_ErrorHandling();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nUnhandled error: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }

        Console.WriteLine("\nDone!");
    }
}
