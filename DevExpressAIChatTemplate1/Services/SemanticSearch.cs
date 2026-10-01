namespace DevExpressAIChatTemplate1.Services;

public class SemanticSearch(
    //VectorStoreCollection<string, IngestedChunk> vectorCollection, // this work for sqlite
    VectorStoreCollection<Guid, IngestedChunk> vectorCollection,
    [FromKeyedServices("ingestion_directory")] DirectoryInfo ingestionDirectory,
    DataIngestor dataIngestor)
{
    public bool _isIngesting { get; private set; }

    public async Task LoadDocumentsAsync()
    {
        if (_isIngesting)
        {
            return;
        }

        try
        {
            _isIngesting = true;
            await dataIngestor.IngestDataAsync(ingestionDirectory, searchPattern: "*.*");
        }
        finally
        {
            _isIngesting = false;
        }
    }

    public async Task<IReadOnlyList<IngestedChunk>> SearchAsync(string text, string? documentIdFilter, int maxResults)
    {
        // Ensure documents have been loaded before searching
        //await LoadDocumentsAsync(); // this web app has the upload seperated, this should not use here anymore

        var nearest = vectorCollection.SearchAsync(text, maxResults, new VectorSearchOptions<IngestedChunk>
        {
            Filter = documentIdFilter is { Length: > 0 } ? record => record.DocumentId == documentIdFilter : null,
            IncludeVectors = true
        });

        return await nearest.Select(result => result.Record).ToListAsync();
    }
}
