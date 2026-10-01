using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis;
using UglyToad.PdfPig.DocumentLayoutAnalysis.ReadingOrderDetector;
using UglyToad.PdfPig.Content;
using System.Text.Json;

namespace DevExpressAIChatTemplate1.Services.Ingestion;

// Minimal vision/analyzer abstraction for sending page images to a vision model.
public interface IVisionService
{
    // Returns textual or structured OCR/caption result for the provided image stream.
    Task<string> AnalyzeImageAsync(Stream imageStream, CancellationToken cancellationToken = default);
}

// Hybrid reader: uses PdfPig for normal text pages, but routes complex pages (tables/diagrams/images)
// to a vision model (via IVisionService). Optional IPdfRenderer is used to rasterize pages.
internal sealed class HybridPdfReader : IngestionDocumentReader
{
    private readonly IVisionService _vision;
    private readonly IPdfRenderService? _renderer;

    public HybridPdfReader(IVisionService visionService, IPdfRenderService? pdfRenderer = null)
    {
        _vision = visionService ?? throw new ArgumentNullException(nameof(visionService));
        _renderer = pdfRenderer;
    }

    public override async Task<IngestionDocument> ReadAsync(Stream source, string identifier, string mediaType, CancellationToken cancellationToken = default)
    {
        var document = new IngestionDocument(identifier);
        try
        {
            // Buffer PDF so we can both open it with PdfPig and hand it to a renderer/vision service.
            using var pdfBuffer = new MemoryStream();
            await source.CopyToAsync(pdfBuffer, cancellationToken).ConfigureAwait(false);
            pdfBuffer.Position = 0;

            using var pdf = PdfDocument.Open(pdfBuffer);

            foreach (var page in pdf.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var section = new IngestionDocumentSection { PageNumber = page.Number };
                var paragraph = new IngestionDocumentParagraph("temporary markdown text");
                // Decide if page is "complex" (has embedded images, vector paths/diagrams, or table-like layout).
                var isComplex = IsComplexPage(page);

                if (!isComplex)
                {
                    // Normal text extraction path (existing PdfPig flow).
                    var letters = page.Letters;
                    var words = NearestNeighbourWordExtractor.Instance.GetWords(letters);

                    foreach (var textBlock in DocstrumBoundingBoxes.Instance.GetBlocks(words))
                    {
                        if (string.IsNullOrWhiteSpace(textBlock.Text)) continue;
                        //section.Elements.Add(new IngestionDocumentParagraph(textBlock.Text)
                        //{
                        //    Text = textBlock.Text,
                        //    PageNumber = page.Number
                        //});

                        paragraph = new IngestionDocumentParagraph(textBlock.Text);
                        paragraph.PageNumber = page.Number;
                        paragraph.Metadata["PageNumber"] = page.Number.ToString();
                    }
                }
                else
                {
                    // Complex page -> prefer renderer; otherwise try embedded images; then call vision service.
                    MemoryStream? imageStream = null;

                    if (_renderer is not null)
                    {
                        // Ensure renderer sees a fresh PDF stream starting at 0.
                        var pdfForRender = new MemoryStream(pdfBuffer.ToArray());
                        imageStream = await _renderer.RenderPageToPng(pdfForRender, page.Number - 1);
                        pdfForRender.Dispose(); // renderer should have completed read or created its own copy.
                    }
                    else
                    {
                        // Fallback: if there are embedded images, use the first one.
                        var imgs = page.GetImages()?.ToList();
                        if (imgs is { Count: > 0 })
                        {
                            imageStream = new MemoryStream(imgs[0].RawBytes.ToArray());
                        }
                    }

                    if (imageStream is not null)
                    {
                        using (imageStream)
                        {
                            var analysisText = await _vision.AnalyzeImageAsync(imageStream, cancellationToken).ConfigureAwait(false);
                            if (!string.IsNullOrWhiteSpace(analysisText))
                            {
                                // Add vision result as one paragraph. You can extend to structured elements as needed.
                                paragraph = new IngestionDocumentParagraph(analysisText);
                                paragraph.PageNumber = page.Number;
                                paragraph.Metadata["ContentType"] = "Table";
                                paragraph.Metadata["PageNumber"] = page.Number.ToString();
                                //paragraph.Metadata["BoundingBox"] = JsonSerializer.Serialize(table.BoundingRegions);
                                //section.Elements.Add(new IngestionDocumentParagraph(analysisText));
                            }
                        }
                    }
                    else
                    {
                        // Last-resort fallback to text extraction if no image available.
                        var letters = page.Letters;
                        var words = NearestNeighbourWordExtractor.Instance.GetWords(letters);
                        foreach (var textBlock in DocstrumBoundingBoxes.Instance.GetBlocks(words))
                        {
                            if (string.IsNullOrWhiteSpace(textBlock.Text)) continue;
                            //section.Elements.Add(new IngestionDocumentParagraph(textBlock.Text)
                            //{
                            //    Text = textBlock.Text,
                            //    PageNumber = page.Number
                            //});
                            paragraph = new IngestionDocumentParagraph(textBlock.Text);
                            paragraph.PageNumber = page.Number;
                            paragraph.Metadata["PageNumber"] = page.Number.ToString();
                        }
                    }
                }
                section.Elements.Add(paragraph);
                document.Sections.Add(section);
            }
        }
        catch (Exception ex)
        {

        }

        return document;
    }

    private static bool IsComplexPage(Page page)
    {
        // Embedded images
        var hasImages = page.GetImages() is { } imgs && imgs.Any();
        if (hasImages) return true;

        // Vector graphics / drawn paths (likely diagrams)
        var hasPaths = page.Paths.Any(p => p.IsFilled || p.IsStroked);
        if (hasPaths) return true;

        // Heuristic: block-level detection of tables / columned layout
        var letters = page.Letters;
        var words = NearestNeighbourWordExtractor.Instance.GetWords(letters);
        var blocks = DocstrumBoundingBoxes.Instance.GetBlocks(words);
        foreach (var block in blocks)
        {
            if (IsBlockLikelyTable(block)) return true;
        }

        return false;
    }

    private static bool IsBlockLikelyTable(TextBlock block)
    {
        // Heuristic 1: many lines with large gaps (stream tables)
        int multiSpaceLines = 0;
        foreach (var line in block.TextLines)
        {
            if (line.Text.Contains("   ") || line.Text.Contains("\t")) multiSpaceLines++;
        }

        if (block.TextLines.Count > 0 && ((double)multiSpaceLines / block.TextLines.Count) > 0.3) return true;

        // Heuristic 2: width of block is wide and text Word density low (possible table region)
        var bbox = block.BoundingBox;
        var width = bbox.Right - bbox.Left;
        var height = bbox.Top - bbox.Bottom;
        if (width > 200 && block.Text.Length < (width / 5)) return true;

        return false;
    }
}