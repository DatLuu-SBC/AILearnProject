using System.Runtime.CompilerServices;

namespace DevExpressAIChatTemplate1.Services;

public class FileIngestionService([FromKeyedServices("ingestion_directory")] DirectoryInfo ingestionDirectory, SemanticSearch search)
{
    public async Task IngestDocumentAsync(string fileName, Stream fileStream)
    {
        var securePath = Path.Combine(ingestionDirectory.FullName, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(securePath)!);

        using (var fs = new FileStream(securePath, FileMode.Create))
        {
            await fileStream.CopyToAsync(fs);
        }

        await search.LoadDocumentsAsync();
    }
}
