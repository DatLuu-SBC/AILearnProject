using PDFtoImage;
using System.Runtime.Versioning;

namespace DevExpressAIChatTemplate1.Services.Ingestion;

public class PdfRenderService : IPdfRenderService
{
    [SupportedOSPlatform("windows")]
    public async Task<MemoryStream> RenderPageToPng(Stream pdfStream, int pageIndex, int dpi = 300)
    {
        if (pdfStream == null) throw new ArgumentNullException(nameof(pdfStream));

        return await Task.Run(() =>
        {
            var outputStream = new MemoryStream();
            // Setup options optimized for AI OCR models (300 DPI is standard)
            var options = new RenderOptions
            {
                Dpi = dpi,
                WithAspectRatio = true
            };

            try
            {
                if (pdfStream.CanSeek) pdfStream.Position = 0;
                // PDFtoImage handles all native PDFium lifecycle tracking under the hood
                Conversion.SavePng(outputStream, pdfStream, page: pageIndex, options: options);
                outputStream.Position = 0; // Reset position so the AI client can read it immediately
                return outputStream;
            }
            catch (Exception ex)
            {
                outputStream.Dispose();
                throw new InvalidOperationException($"Failed to render PDF page {pageIndex}.", ex);
            }
        });
    }

    [SupportedOSPlatform("windows")]
    public async Task<List<MemoryStream>> RenderAllPagesToPng(Stream pdfStream, int dpi = 300)
    {
        if (pdfStream == null) throw new ArgumentNullException(nameof(pdfStream));
        if (pdfStream.CanSeek) pdfStream.Position = 0;

        var renderedPages = new List<MemoryStream>();

        try
        {
            await foreach (var skImage in Conversion.ToImagesAsync(pdfStream, options: new RenderOptions { Dpi = dpi }))
            {
                var pageStream = new MemoryStream();

                // Encode the native SkiaSharp surface structure out into standard PNG file bytes
                skImage.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100).SaveTo(pageStream);
                pageStream.Position = 0;

                renderedPages.Add(pageStream);
                skImage.Dispose();
            }

            return renderedPages;
        }
        catch (Exception)
        {
            // Clean up any allocated streams if a failure happens mid-document
            foreach (var stream in renderedPages) stream.Dispose();
            throw;
        }
    }
}
