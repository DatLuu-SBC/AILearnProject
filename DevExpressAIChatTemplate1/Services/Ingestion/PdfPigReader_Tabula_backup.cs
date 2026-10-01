using System.Text;
using Tabula;
using Tabula.Extractors;
using UglyToad.PdfPig.DocumentLayoutAnalysis;
using UglyToad.PdfPig.DocumentLayoutAnalysis.ReadingOrderDetector;
using UglyToad.PdfPig.Geometry;

namespace DevExpressAIChatTemplate1.Services.Ingestion;

internal sealed class PdfPigReader_Tabula : IngestionDocumentReader
{
    public override Task<IngestionDocument> ReadAsync(Stream source, string identifier, string mediaType, CancellationToken cancellationToken = default)
    {
        using var pdf = PdfDocument.Open(source);

        var document = new IngestionDocument(identifier);
        foreach (var page in pdf.GetPages())
        {
            document.Sections.Add(GetPageSection(page, ObjectExtractor.Extract(pdf, page.Number)));
        }
        return Task.FromResult(document);
    }

    private static IngestionDocumentSection GetPageSection(Page pdfPage, PageArea fullTabulaPage)
    {
        var section = new IngestionDocumentSection
        {
            PageNumber = pdfPage.Number,
        };

        var letters = pdfPage.Letters;
        var words = NearestNeighbourWordExtractor.Instance.GetWords(letters);
        var blocks = DocstrumBoundingBoxes.Instance.GetBlocks(words);

        foreach (var block in UnsupervisedReadingOrderDetector.Instance.Get(blocks))
        {
            string blockText = block.Text;
            if (string.IsNullOrWhiteSpace(blockText)) continue;

            if (IsBlockATable(pdfPage, block, fullTabulaPage))
            {
                var rect = new UglyToad.PdfPig.Core.PdfRectangle(
                    (float)block.BoundingBox.Top,
                    (float)block.BoundingBox.Left,
                    (float)block.BoundingBox.Bottom,
                    (float)block.BoundingBox.Right
                );

                PageArea croppedArea = fullTabulaPage.GetArea(rect);

                var tables = ExtractTabulaTable(croppedArea);

                if (tables.Count > 0)
                {
                    foreach (var table in tables)
                    {
                        var markdownTable = ConvertTableToMarkdown(table);
                        if (!string.IsNullOrEmpty(markdownTable))
                        {
                            section.Elements.Add(new IngestionDocumentParagraph(markdownTable)
                            {
                                Text = markdownTable,
                                PageNumber = pdfPage.Number
                            });
                        }
                    }
                    continue; // Successfully handled as a table, move to next block
                }
            }

            section.Elements.Add(new IngestionDocumentParagraph(block.Text)
            {
                Text = block.Text,
                PageNumber = pdfPage.Number
            });
        }

        return section;
    }


    private static bool IsBlockATable(Page pdfPigPage, TextBlock block, PageArea tabulaPage)
    {
        // Heuristic 1: Check if there are graphical vector lines intersecting this block's area (Lattice tables)
        var intersectingLines = pdfPigPage.Paths
            .Where(path => path.IsFilled || path.IsStroked)
            .Select(path => path.GetBoundingRectangle())
            .Any(rect => rect.HasValue && block.BoundingBox.IntersectsWith(rect.Value));

        if (intersectingLines) return true;

        // Heuristic 2: Check for multiple consecutive spaces inside individual text lines (Stream tables)
        // If lines look like: "Item A         $20.00" -> it's a table row
        int multiSpaceLinesCount = 0;
        foreach (var line in block.TextLines)
        {
            if (line.Text.Contains("   ") || line.Text.Contains("\t"))
            {
                multiSpaceLinesCount++;
            }
        }

        // If more than 30% of the block's lines exhibit multi-space gaps, flag it
        if (block.TextLines.Count > 0 && ((double)multiSpaceLinesCount / block.TextLines.Count) > 0.3)
        {
            return true;
        }

        return false;
    }

    private static IReadOnlyList<Table> ExtractTabulaTable(PageArea area)
    {
        // Try Spreadsheet (Lattice) algorithm first for bounded tables
        IExtractionAlgorithm algorithm = new SpreadsheetExtractionAlgorithm();
        var tables = algorithm.Extract(area);

        // Fallback to Basic (Stream) if no strict visual grid lines are found
        if (tables.Count == 0)
        {
            algorithm = new BasicExtractionAlgorithm();
            tables = algorithm.Extract(area);
        }

        return tables;
    }


    private static string ConvertTableToMarkdown(Table table)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < table.Rows.Count; i++)
        {
            var row = table.Rows[i];
            sb.Append("| ");
            foreach (var cell in row)
            {
                string text = cell.GetText().Replace("\r", " ").Replace("\n", " ").Trim();
                sb.Append($"{text} | ");
            }
            sb.AppendLine();

            // Insert markdown header separator row
            if (i == 0)
            {
                sb.Append("| ");
                foreach (var _ in row) sb.Append("--- | ");
                sb.AppendLine();
            }
        }
        return sb.ToString();
    }
    /*private static IngestionDocumentSection GetPageSection(Page pdfPage)
    {
        var section = new IngestionDocumentSection
        {
            PageNumber = pdfPage.Number,
        };

        var letters = pdfPage.Letters;
        var words = NearestNeighbourWordExtractor.Instance.GetWords(letters);

        foreach (var textBlock in DocstrumBoundingBoxes.Instance.GetBlocks(words))
        {
            section.Elements.Add(new IngestionDocumentParagraph(textBlock.Text)
            {
                Text = textBlock.Text
            });
        }

        return section;
    }*/
}
