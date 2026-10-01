using System.Runtime.CompilerServices;

namespace DevExpressAIChatTemplate1.Services.Ingestion;

public class IngestedChunkSemanticSimilarityChunker(SemanticSimilarityChunker _internalChunker,
                                                    IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator) : IngestionChunker<IngestedChunk>
{
    public override IAsyncEnumerable<IngestionChunk<IngestedChunk>> ProcessAsync(IngestionDocument document, CancellationToken cancellationToken = default)
    {
        // 1. Run the native Microsoft string-based semantic chunker
        var stringChunks = _internalChunker.ProcessAsync(document, cancellationToken);
        return ProjectChunksAsync(stringChunks, cancellationToken);
    }

    private async IAsyncEnumerable<IngestionChunk<IngestedChunk>> ProjectChunksAsync(
        IAsyncEnumerable<IngestionChunk<string>> stringChunks,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var stringChunk in stringChunks.WithCancellation(cancellationToken))
        {
            // 2. Safely capture your metadata properties from the document reader
            int pageInfo = stringChunk.Metadata.TryGetValue("PageNumber", out var pageNum)
                ? Convert.ToInt32(pageNum)
                : 0;
            string generatedContext = $"Document: {stringChunk.Document.Identifier} | Location: {pageInfo} | Segment: {stringChunk.Content}";

            var embeddings = await _embeddingGenerator.GenerateAsync(new[] { generatedContext }, cancellationToken: cancellationToken);
            ReadOnlyMemory<float> vectorCoordinates = embeddings.First().Vector;

            // 3. Construct your custom model with your Qdrant configuration mapping parameters
            var customChunkModel = new IngestedChunk
            {
                key = Guid.NewGuid(),
                DocumentId = stringChunk.Document.Identifier,
                Text = stringChunk.Content,
                PageNumber = pageInfo,
                // 💡 FIXES EMPTY PAYLOAD: Generates your contextual statement
                Context = generatedContext,

                // Leave null; VectorStoreWriter automatically resolves [VectorStoreVector]
                Vector = vectorCoordinates
            };

            // 4. Return the chunk as a strongly-typed IngestedChunk to the pipeline
            yield return new IngestionChunk<IngestedChunk>(customChunkModel, stringChunk.Document, customChunkModel.Context);
        }
    }
}
