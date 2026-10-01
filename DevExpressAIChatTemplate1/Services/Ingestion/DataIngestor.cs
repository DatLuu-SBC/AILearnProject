using CommunityToolkit.VectorData.Qdrant;
using System.IO.Pipes;

namespace DevExpressAIChatTemplate1.Services.Ingestion;

public class DataIngestor(
    ILogger<DataIngestor> logger,
    ILoggerFactory loggerFactory,
    VectorStore vectorStore,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    IngestionDocumentReader pdfReader,
    IConfiguration configuration)
{
    public async Task IngestDataAsync(DirectoryInfo directory, string searchPattern)
    {
        //using var writer = new VectorStoreWriter<string>(vectorStore, dimensionCount: IngestedChunk.VectorDimensions, new()
        //{
        //    CollectionName = IngestedChunk.CollectionName,
        //    DistanceFunction = IngestedChunk.VectorDistanceFunction,
        //    IncrementalIngestion = false
        //});
        VectorStoreCollection<Guid, IngestedChunk> collection = vectorStore.GetCollection<Guid, IngestedChunk>(IngestedChunk.CollectionName);
        using var writer = new IngestedChunkVectorStoreWriter(collection, embeddingGenerator);

        using var pipeline = new IngestionPipeline<string>(
            reader: new DocumentReader(directory, pdfReader),
            chunker: new SemanticSimilarityChunker(embeddingGenerator, new(TiktokenTokenizer.CreateForModel(configuration.GetValue<string>("DeploymentEmbeddingName") ?? "text-embedding-3-small"))),
            writer: writer,
            loggerFactory: loggerFactory);

        //pipeline.ChunkProcessors.Add(new IngestedChunkMappingProcessor());

        logger.LogInformation($"pipeline is ready, file: {directory.FullName}");
        await foreach (var result in pipeline.ProcessAsync(directory, searchPattern))
        {
            logger.LogInformation("Completed processing '{id}'. Succeeded: '{succeeded}'.", result.DocumentId, result.Succeeded);
        }
    }
}
