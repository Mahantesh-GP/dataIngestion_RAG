using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DataIngestion.MarkItDown;
using Microsoft.Extensions.AI;
using Microsoft.ML.Tokenizers;
using Azure.AI.OpenAI;
using Azure;
using Azure.Cosmos;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MSDATAIngestion_RAG;

/// <summary>
/// Enumeration for different chunking strategies
/// </summary>
public enum ChunkingStrategy
{
    /// <summary>
    /// Split documents on header boundaries
    /// </summary>
    HeaderBased,

    /// <summary>
    /// Split documents on section boundaries (e.g., pages)
    /// </summary>
    SectionBased,

    /// <summary>
    /// Preserve complete thoughts using semantic-aware chunking
    /// </summary>
    SemanticAware
}

/// <summary>
/// Configuration options for the data ingestion pipeline
/// </summary>
public class DataIngestionConfig
{
    /// <summary>
    /// Azure OpenAI endpoint URL
    /// </summary>
    public string AzureOpenAIEndpoint { get; set; } = 
        Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? "";

    /// <summary>
    /// Azure OpenAI API key
    /// </summary>
    public string AzureOpenAIKey { get; set; } = 
        Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY") ?? "";

    /// <summary>
    /// Deployment name for the embedding model
    /// </summary>
    public string EmbeddingDeploymentName { get; set; } = "text-embedding-3-small";

    /// <summary>
    /// Model name for tokenization
    /// </summary>
    public string TokenizerModel { get; set; } = "gpt-4";

    /// <summary>
    /// Maximum tokens per chunk
    /// </summary>
    public int MaxTokensPerChunk { get; set; } = 2000;

    /// <summary>
    /// Overlap tokens between chunks
    /// </summary>
    public int OverlapTokens { get; set; } = 0;

    /// <summary>
    /// Selected chunking strategy
    /// </summary>
    public ChunkingStrategy ChunkingStrategy { get; set; } = ChunkingStrategy.SemanticAware;

    /// <summary>
    /// Dimension count for embeddings (text-embedding-3-small = 1536)
    /// </summary>
    public int EmbeddingDimension { get; set; } = 1536;

    /// <summary>
    /// CosmosDB connection string
    /// </summary>
    public string CosmosDBConnectionString { get; set; } = 
        Environment.GetEnvironmentVariable("COSMOS_DB_CONNECTION_STRING") ?? "";

    /// <summary>
    /// CosmosDB database name
    /// </summary>
    public string CosmosDBDatabaseName { get; set; } = "rag-ingestion";

    /// <summary>
    /// CosmosDB container name
    /// </summary>
    public string CosmosDBContainerName { get; set; } = "chunks";
}

/// <summary>
/// Main data ingestion pipeline orchestrator
/// </summary>
public class DataIngestionPipeline
{
    private readonly DataIngestionConfig _config;
    private readonly ILoggerFactory _loggerFactory;
    private readonly AzureOpenAIClient _azureOpenAIClient;
    private readonly ILogger<DataIngestionPipeline> _logger;
    private CosmosClient? _cosmosClient;
    private CosmosDBChunkWriter? _cosmosDBWriter;

    public DataIngestionPipeline(DataIngestionConfig config, ILoggerFactory? loggerFactory = null)
    {
        _config = config;
        _loggerFactory = loggerFactory ?? new SimpleLoggerFactory();
        _logger = _loggerFactory.CreateLogger<DataIngestionPipeline>();

        // Initialize Azure OpenAI client
        _azureOpenAIClient = new AzureOpenAIClient(
            new Uri(_config.AzureOpenAIEndpoint),
            new AzureKeyCredential(_config.AzureOpenAIKey)
        );
    }

    /// <summary>
    /// Initializes CosmosDB connection and returns the chunk writer
    /// </summary>
    private async Task InitializeCosmosDBAsync()
    {
        if (_cosmosDBWriter != null)
            return;

        try
        {
            _logger.LogInformation("Initializing CosmosDB connection");

            _cosmosClient = new CosmosClient(_config.CosmosDBConnectionString);

            // Create database if it doesn't exist
            var database = await _cosmosClient.CreateDatabaseIfNotExistsAsync(_config.CosmosDBDatabaseName);

            // Create container if it doesn't exist with vector embedding policy
            var containerProperties = new ContainerProperties
            {
                Id = _config.CosmosDBContainerName,
                PartitionKeyPath = "/documentId",
                // Enable vector search for embeddings
                VectorEmbeddingPolicy = new VectorEmbeddingPolicy
                {
                    VectorEmbeddings = new Collection<Embedding>
                    {
                        new Embedding
                        {
                            Name = "embedding",
                            DataType = VectorDataType.Float32,
                            Dimensions = _config.EmbeddingDimension,
                            DistanceFunction = DistanceFunction.Cosine
                        }
                    }
                },
                // Add vector index
                VectorIndexes = new Collection<VectorIndex>
                {
                    new VectorIndex
                    {
                        Name = "embeddingIndex",
                        Path = "/embedding"
                    }
                }
            };

            var container = await database.Database.CreateContainerIfNotExistsAsync(containerProperties);

            var embeddingGenerator = CreateEmbeddingGenerator();
            _cosmosDBWriter = new CosmosDBChunkWriter(container.Container, embeddingGenerator);

            _logger.LogInformation("CosmosDB initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error initializing CosmosDB: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Creates an IngestionDocumentReader for loading markdown files
    /// </summary>
    private IngestionDocumentReader CreateDocumentReader()
    {
        _logger.LogInformation("Initializing document reader");
        return new MarkItDownDocumentReader();
    }

    /// <summary>
    /// Creates the appropriate chunker based on the configured strategy
    /// </summary>
    private IngestionChunker<string> CreateChunker()
    {
        _logger.LogInformation($"Creating chunker with strategy: {_config.ChunkingStrategy}");

        // Create tokenizer for token-based chunking
        var tokenizer = TiktokenTokenizer.CreateForModel(_config.TokenizerModel);

        var options = new IngestionChunkerOptions(tokenizer)
        {
            MaxTokensPerChunk = _config.MaxTokensPerChunk,
            OverlapTokens = _config.OverlapTokens
        };

        return _config.ChunkingStrategy switch
        {
            ChunkingStrategy.HeaderBased => 
                new HeaderChunker(options),

            ChunkingStrategy.SectionBased => 
                new SectionChunker(options),

            ChunkingStrategy.SemanticAware => 
                new SemanticChunker(options),

            _ => throw new InvalidOperationException(
                $"Unknown chunking strategy: {_config.ChunkingStrategy}")
        };
    }

    /// <summary>
    /// Creates document processors for enriching content
    /// </summary>
    private List<IngestionDocumentProcessor> CreateDocumentProcessors()
    {
        _logger.LogInformation("Initializing document processors");

        var processors = new List<IngestionDocumentProcessor>();

        try
        {
            // Create an embedding generator for image alternative text
            var embeddingGenerator = CreateEmbeddingGenerator();
            var chatClient = _azureOpenAIClient.GetChatClient(_config.EmbeddingDeploymentName);

            // Add image alternative text enricher
            var imageAltTextEnricher = new ImageAlternativeTextEnricher(chatClient.AsChatClientWithTokenLimit(4096));
            processors.Add(imageAltTextEnricher);

            _logger.LogInformation("Document processors initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Could not initialize some document processors: {ex.Message}");
        }

        return processors;
    }

    /// <summary>
    /// Creates chunk processors for enriching chunk content
    /// </summary>
    private List<IngestionChunkProcessor<string>> CreateChunkProcessors()
    {
        _logger.LogInformation("Initializing chunk processors");

        var processors = new List<IngestionChunkProcessor<string>>();

        try
        {
            var chatClient = _azureOpenAIClient.GetChatClient(_config.EmbeddingDeploymentName);
            var limitedChatClient = chatClient.AsChatClientWithTokenLimit(4096);

            // Add summary enricher
            var summaryEnricher = new SummaryEnricher(limitedChatClient);
            processors.Add(summaryEnricher);

            // Add sentiment enricher
            var sentimentEnricher = new SentimentEnricher(limitedChatClient);
            processors.Add(sentimentEnricher);

            // Add keyword enricher
            var keywordEnricher = new KeywordEnricher(limitedChatClient);
            processors.Add(keywordEnricher);

            _logger.LogInformation("Chunk processors initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Could not initialize some chunk processors: {ex.Message}");
        }

        return processors;
    }

    /// <summary>
    /// Creates an embedding generator using Azure OpenAI
    /// </summary>
    private IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator()
    {
        _logger.LogInformation($"Creating embedding generator for model: {_config.EmbeddingDeploymentName}");

        var embeddingClient = _azureOpenAIClient
            .GetEmbeddingClient(_config.EmbeddingDeploymentName);

        return embeddingClient.AsIEmbeddingGenerator();
    }

    /// <summary>
    /// Creates a chunk writer for storing chunks in CosmosDB
    /// </summary>
    private async Task<IngestionChunkWriter<string>> CreateChunkWriterAsync()
    {
        _logger.LogInformation("Creating CosmosDB chunk writer");

        await InitializeCosmosDBAsync();

        if (_cosmosDBWriter == null)
            throw new InvalidOperationException("CosmosDB writer failed to initialize");

        return _cosmosDBWriter;
    }

    /// <summary>
    /// Processes documents from a directory using the configured pipeline
    /// </summary>
    public async Task<IngestionResult[]> ProcessDocumentsAsync(
        string directoryPath,
        string searchPattern = "*.md",
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation($"Starting document processing from: {directoryPath}");

        var reader = CreateDocumentReader();
        var chunker = CreateChunker();
        var writer = await CreateChunkWriterAsync();

        var documentProcessors = CreateDocumentProcessors();
        var chunkProcessors = CreateChunkProcessors();

        var results = new List<IngestionResult>();

        try
        {
            using var pipeline = new IngestionPipeline<string>(
                reader,
                chunker,
                writer,
                loggerFactory: _loggerFactory)
            {
                DocumentProcessors = documentProcessors,
                ChunkProcessors = chunkProcessors
            };

            await foreach (var result in pipeline.ProcessAsync(
                new DirectoryInfo(directoryPath),
                searchPattern: searchPattern,
                cancellationToken: cancellationToken))
            {
                results.Add(result);
                _logger.LogInformation(
                    $"Completed processing '{result.DocumentId}'. Succeeded: '{result.Succeeded}'.");

                if (!result.Succeeded)
                {
                    _logger.LogError($"Document processing failed: {result.DocumentId}");
                }
            }

            _logger.LogInformation($"Pipeline processing completed. Total documents: {results.Count}");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error during pipeline processing: {ex.Message}");
            throw;
        }

        return results.ToArray();
    }

    /// <summary>
    /// Processes a single document file
    /// </summary>
    public async Task<IngestionResult> ProcessSingleDocumentAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation($"Processing single document: {filePath}");

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Document not found: {filePath}");
        }

        var reader = CreateDocumentReader();
        var chunker = CreateChunker();
        var writer = await CreateChunkWriterAsync();

        try
        {
            using var fileStream = File.OpenRead(filePath);
            var document = await reader.ReadAsync(fileStream, Path.GetFileName(filePath), cancellationToken);

            // Process chunks
            var chunks = chunker.Chunk(document);
            _logger.LogInformation($"Document split into {chunks.Count} chunks");

            // Store chunks
            foreach (var chunk in chunks)
            {
                await writer.WriteAsync(chunk, cancellationToken);
            }

            return new IngestionResult
            {
                DocumentId = Path.GetFileName(filePath),
                Succeeded = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error processing document: {ex.Message}");
            return new IngestionResult
            {
                DocumentId = Path.GetFileName(filePath),
                Succeeded = false
            };
        }
    }

    /// <summary>
    /// Gets the CosmosDB chunk writer for custom operations
    /// </summary>
    public async Task<CosmosDBChunkWriter?> GetCosmosDBWriterAsync()
    {
        await InitializeCosmosDBAsync();
        return _cosmosDBWriter;
    }

    /// <summary>
    /// Gets chunking strategy details and recommendations
    /// </summary>
    public static string GetChunkingStrategyInfo(ChunkingStrategy strategy)
    {
        return strategy switch
        {
            ChunkingStrategy.HeaderBased =>
                "Header-Based Chunking: Splits documents on markdown headers (H1, H2, H3, etc.). " +
                "Best for: Documents with clear hierarchical structure, technical documentation. " +
                "Pros: Preserves logical document structure. " +
                "Cons: May create very large chunks if headers are sparse.",

            ChunkingStrategy.SectionBased =>
                "Section-Based Chunking: Splits documents on section boundaries (e.g., pages, paragraphs). " +
                "Best for: Long documents with natural section breaks, multi-page documents. " +
                "Pros: Balanced chunk sizes, maintains readability. " +
                "Cons: May split related content if boundaries don't align with meaning.",

            ChunkingStrategy.SemanticAware =>
                "Semantic-Aware Chunking: Intelligently splits documents while preserving complete thoughts. " +
                "Best for: Complex documents, academic papers, articles with deep semantic meaning. " +
                "Pros: Maintains context and meaning, optimal for semantic search. " +
                "Cons: More computationally intensive, may create variable-sized chunks.",

            _ => "Unknown chunking strategy"
        };
    }

    /// <summary>
    /// Cleans up resources
    /// </summary>
    public void Dispose()
    {
        _cosmosDBWriter?.Dispose();
        _cosmosClient?.Dispose();
    }
}

/// <summary>
/// Simple logger factory implementation for demonstration
/// </summary>
public class SimpleLoggerFactory : ILoggerFactory
{
    public ILogger CreateLogger(string categoryName) => new SimpleLogger(categoryName);
    public void AddProvider(ILoggerProvider provider) { }
    public void Dispose() { }
}

/// <summary>
/// Simple logger implementation for console output
/// </summary>
public class SimpleLogger : ILogger
{
    private readonly string _categoryName;

    public SimpleLogger(string categoryName) => _categoryName = categoryName;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{logLevel}] [{_categoryName}] {message}");
    }
}

/// <summary>
/// Result of document ingestion processing
/// </summary>
public class IngestionResult
{
    public string DocumentId { get; set; } = string.Empty;
    public bool Succeeded { get; set; }
}
