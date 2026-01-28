using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.AI;
using Azure.Cosmos;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MSDATAIngestion_RAG;

/// <summary>
/// CosmosDB implementation of IngestionChunkWriter for storing vectors
/// Uses Azure Cosmos DB for scalable, production-ready vector storage
/// </summary>
public class CosmosDBChunkWriter : IngestionChunkWriter<string>
{
    private readonly CosmosContainer _container;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;

    public CosmosDBChunkWriter(
        CosmosContainer container,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
    {
        _container = container ?? throw new ArgumentNullException(nameof(container));
        _embeddingGenerator = embeddingGenerator ?? throw new ArgumentNullException(nameof(embeddingGenerator));
    }

    /// <summary>
    /// Writes a chunk to CosmosDB with vector embeddings
    /// </summary>
    public override async Task WriteAsync(IngestionChunk<string> chunk, CancellationToken cancellationToken = default)
    {
        try
        {
            // Generate embedding for the chunk
            var embedding = await _embeddingGenerator.GenerateEmbeddingAsync(chunk.Content, cancellationToken);

            var cosmosChunk = new CosmosChunkDocument
            {
                Id = Guid.NewGuid().ToString(),
                DocumentId = chunk.DocumentId,
                Content = chunk.Content,
                Embedding = embedding.Vector.ToArray(),
                Metadata = chunk.Metadata,
                CreatedAt = DateTime.UtcNow,
                ChunkIndex = chunk.ChunkIndex,
                ContentLength = chunk.Content.Length
            };

            // Create item in CosmosDB with vector embedding as part of the document
            await _container.CreateItemAsync(
                cosmosChunk,
                new PartitionKey(cosmosChunk.DocumentId),
                cancellationToken: cancellationToken);

            Console.WriteLine($"Chunk stored in CosmosDB: ID={cosmosChunk.Id}, DocumentID={chunk.DocumentId}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error storing chunk in CosmosDB: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Searches for similar chunks using vector similarity in CosmosDB
    /// Uses CosmosDB's built-in vector search capabilities
    /// </summary>
    public async Task<List<SearchResult>> SearchSimilarChunksAsync(
        float[] queryEmbedding,
        int topK = 5,
        CancellationToken cancellationToken = default)
    {
        var results = new List<SearchResult>();

        try
        {
            // Query CosmosDB using vector similarity search
            // Note: This requires CosmosDB vector search capabilities (preview/GA)
            var query = @"
                SELECT TOP @topK 
                    c.id,
                    c.documentId,
                    c.content,
                    c.metadata,
                    VectorDistance(c.embedding, @queryEmbedding) as similarity
                FROM c
                ORDER BY VectorDistance(c.embedding, @queryEmbedding)
            ";

            var queryDefinition = new QueryDefinition(query)
                .WithParameter("@topK", topK)
                .WithParameter("@queryEmbedding", queryEmbedding);

            using var iterator = _container.GetItemQueryIterator<dynamic>(queryDefinition);

            while (iterator.HasMoreResults && results.Count < topK)
            {
                var page = await iterator.ReadNextAsync(cancellationToken);

                foreach (var item in page)
                {
                    results.Add(new SearchResult
                    {
                        ChunkId = item["id"],
                        DocumentId = item["documentId"],
                        Content = item["content"],
                        Similarity = 1 - (float)item["similarity"], // Convert distance to similarity
                        Metadata = item["metadata"] ?? new Dictionary<string, object>()
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error searching similar chunks in CosmosDB: {ex.Message}");
            throw;
        }

        return results;
    }

    /// <summary>
    /// Retrieves a chunk by ID
    /// </summary>
    public async Task<CosmosChunkDocument?> GetChunkByIdAsync(
        string chunkId,
        string documentId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _container.ReadItemAsync<CosmosChunkDocument>(
                chunkId,
                new PartitionKey(documentId),
                cancellationToken: cancellationToken);

            return response.Value;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error retrieving chunk from CosmosDB: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Retrieves all chunks for a specific document
    /// </summary>
    public async Task<List<CosmosChunkDocument>> GetChunksByDocumentAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        var chunks = new List<CosmosChunkDocument>();

        try
        {
            var query = "SELECT * FROM c WHERE c.documentId = @documentId ORDER BY c.chunkIndex";
            var queryDefinition = new QueryDefinition(query)
                .WithParameter("@documentId", documentId);

            using var iterator = _container.GetItemQueryIterator<CosmosChunkDocument>(queryDefinition);

            while (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync(cancellationToken);
                chunks.AddRange(page);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error retrieving chunks from CosmosDB: {ex.Message}");
        }

        return chunks;
    }

    /// <summary>
    /// Deletes a chunk from CosmosDB
    /// </summary>
    public async Task<bool> DeleteChunkAsync(
        string chunkId,
        string documentId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _container.DeleteItemAsync<CosmosChunkDocument>(
                chunkId,
                new PartitionKey(documentId),
                cancellationToken: cancellationToken);

            Console.WriteLine($"Chunk deleted from CosmosDB: ID={chunkId}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error deleting chunk from CosmosDB: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Deletes all chunks for a document
    /// </summary>
    public async Task<int> DeleteDocumentChunksAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        int deletedCount = 0;

        try
        {
            var chunks = await GetChunksByDocumentAsync(documentId, cancellationToken);

            foreach (var chunk in chunks)
            {
                if (await DeleteChunkAsync(chunk.Id, documentId, cancellationToken))
                {
                    deletedCount++;
                }
            }

            Console.WriteLine($"Deleted {deletedCount} chunks for document: {documentId}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error deleting document chunks: {ex.Message}");
        }

        return deletedCount;
    }

    public override void Dispose()
    {
        base.Dispose();
    }
}

/// <summary>
/// CosmosDB document model for storing chunks with vector embeddings
/// </summary>
[JsonObject]
public class CosmosChunkDocument
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("documentId")]
    public string DocumentId { get; set; } = string.Empty;

    [JsonProperty("content")]
    public string Content { get; set; } = string.Empty;

    [JsonProperty("embedding")]
    public float[] Embedding { get; set; } = Array.Empty<float>();

    [JsonProperty("metadata")]
    public Dictionary<string, object> Metadata { get; set; } = new();

    [JsonProperty("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonProperty("chunkIndex")]
    public int ChunkIndex { get; set; }

    [JsonProperty("contentLength")]
    public int ContentLength { get; set; }
}

/// <summary>
/// Represents a search result from vector similarity search
/// </summary>
public class SearchResult
{
    public string ChunkId { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public float Similarity { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}
