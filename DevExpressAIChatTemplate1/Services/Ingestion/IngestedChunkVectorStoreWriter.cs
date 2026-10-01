using DevExpress.XtraPrinting;
using DevExpress.XtraSpellChecker;

namespace DevExpressAIChatTemplate1.Services.Ingestion;

public class IngestedChunkVectorStoreWriter : IngestionChunkWriter<string>
{
    private readonly VectorStoreCollection<Guid, IngestedChunk> _collection;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;

    public IngestedChunkVectorStoreWriter(VectorStoreCollection<Guid, IngestedChunk> collection, IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
    {
        _collection = collection ?? throw new ArgumentNullException(nameof(collection));
        _embeddingGenerator = embeddingGenerator ?? throw new ArgumentNullException(nameof(embeddingGenerator));
    }

    public override async Task WriteAsync(IAsyncEnumerable<IngestionChunk<string>> chunks, CancellationToken cancellationToken = default)
    {
        var recordToUpsert = new List<IngestedChunk>();

        await foreach (var chunk in chunks.WithCancellation(cancellationToken))
        {
            var record = new IngestedChunk
            {
                key = Guid.NewGuid(),

                Text = chunk.Content,

                DocumentId = chunk.Document.Identifier,

                // 2. Pull paragraph layout & bounding-box data directly from chunk.Metadata
                PageNumber = 0
            };
            foreach (var section in chunk.Document.Sections)
            {
                foreach (var element in section.Elements)
                {
                    record.PageNumber = element.Metadata.TryGetValue("PageNumber", out var pageNum) == true ? Convert.ToInt32(pageNum) : 0;
                    record.ContentType = element.Metadata.TryGetValue("ContentType", out var type) == true ? type!.ToString()! : "Text";
                    record.BoundingBoxJson = chunk.Metadata.TryGetValue("BoundingBox", out var bbox) == true ? bbox.ToString()! : string.Empty;
                }
            }

            string generatedContext = $"Document: {record.DocumentId} | Location: {record.PageNumber} | Segment: {record.Text}";
            var embeddings = await _embeddingGenerator.GenerateAsync(new[] { generatedContext }, cancellationToken: cancellationToken);
            record.Context = generatedContext;
            record.Vector = embeddings.First().Vector;

            recordToUpsert.Add(record);
        }

        if (recordToUpsert.Any())
        {
            await _collection.UpsertAsync(recordToUpsert, cancellationToken);
        }
    }
}
