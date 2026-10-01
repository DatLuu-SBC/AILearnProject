using System.Runtime.CompilerServices;
using static DevExpress.Utils.Zip.Internal.SecureZipTrace;

namespace DevExpressAIChatTemplate1.Services.Ingestion;

public class IngestedChunkMappingProcessor : IngestionChunkProcessor<string>
{
    public override IAsyncEnumerable<IngestionChunk<string>> ProcessAsync(IAsyncEnumerable<IngestionChunk<string>> chunks, CancellationToken cancellationToken = default)
    {
        // Use async streams to map incoming generic metadata chunks to your custom structure
        var mappedChunks = ProjectChunksAsync(chunks, cancellationToken);
        return mappedChunks;
    }

    private async IAsyncEnumerable<IngestionChunk<string>> ProjectChunksAsync(
        IAsyncEnumerable<IngestionChunk<string>> chunks,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var chunk in chunks.WithCancellation(cancellationToken))
        {
            // 1. Safe-cast or read the raw string data out of the text payload 
            // (SemanticSimilarityChunker stores text in chunk.Data as a string under the hood)
            string rawText = chunk.Content?.ToString() ?? string.Empty;

            // 2. Fetch the metadata properties your DocumentReader added
            string pageInfo = chunk.Metadata.TryGetValue("PageNumber", out var pageNum)
                ? $"Page {pageNum}"
                : "Unknown Page";

            // 3. Construct your custom model matching your exact Qdrant parameters
            //var customChunkModel = new IngestedChunk
            //{
            //    key = Guid.NewGuid(),
            //    DocumentId = chunk.Document.Identifier,
            //    Text = rawText,

            //    // 💡 FIXES EMPTY PAYLOAD: Generates your contextual sentence mapping
            //    Context = $"Document: {chunk.Document.Identifier} | Location: {pageInfo} | Segment: {rawText}",

            //    // Leave null; VectorStoreWriter automatically resolves [VectorStoreVector]
            //    Vector = null
            //};
            var Context = $"Document: {chunk.Document.Identifier} | Location: {pageInfo} | Segment: {rawText}";

            // 4. Return a new IngestionChunk typed to your custom model back into the pipeline flow
            yield return new IngestionChunk<string>(rawText, chunk.Document, Context);
        }
    }
}
