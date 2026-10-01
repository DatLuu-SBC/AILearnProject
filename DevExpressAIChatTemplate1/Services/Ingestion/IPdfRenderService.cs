namespace DevExpressAIChatTemplate1.Services.Ingestion;

public interface IPdfRenderService
{
    public Task<MemoryStream> RenderPageToPng(Stream pdfStream, int pageIndex, int dpi = 300);
    public Task<List<MemoryStream>> RenderAllPagesToPng(Stream pdfStream, int dpi = 300);
}