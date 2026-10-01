using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;

namespace DevExpressAIChatTemplate1.Services.Ingestion
{
    // Uses Azure Form Recognizer / Document Analysis to extract text + tables from an image stream.
    // Configure and register the DocumentAnalysisClient in DI and then register this as IVisionService.
    internal sealed class AzureFormVisionService : IVisionService
    {
        private readonly DocumentAnalysisClient _client;
        private readonly string _modelId;
        public AzureFormVisionService(DocumentAnalysisClient client, string modelId)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _modelId = string.IsNullOrWhiteSpace(modelId) ? "prebuilt-layout" : modelId;
        }

        public async Task<string> AnalyzeImageAsync(Stream imageStream, CancellationToken cancellationToken = default)
        {
            if (imageStream == null) throw new ArgumentNullException(nameof(imageStream));
            imageStream.Position = 0;

            // Use the prebuilt layout model which extracts pages, lines and tables.
            var operation = await _client.AnalyzeDocumentAsync(WaitUntil.Completed, _modelId, imageStream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var result = operation.Value;

            var sb = new StringBuilder();

            // Append plain lines / paragraphs (preserves reading order reported by the service)
            if (result.Pages != null && result.Pages.Count > 0)
            {
                foreach (var page in result.Pages)
                {
                    if (page.Lines != null)
                    {
                        foreach (var line in page.Lines)
                        {
                            if (!string.IsNullOrWhiteSpace(line.Content))
                            {
                                sb.AppendLine(line.Content.Trim());
                            }
                        }
                    }

                    sb.AppendLine();
                }
            }

            // Convert detected tables into Markdown (if any)
            if (result.Tables != null && result.Tables.Count > 0)
            {
                foreach (var table in result.Tables)
                {
                    // Try to build a 2D cell matrix for ordered output.
                    int rows = table.RowCount > 0 ? table.RowCount : 0;
                    int cols = table.ColumnCount > 0 ? table.ColumnCount : 0;
                    if (rows == 0 || cols == 0)
                    {
                        // fallback: dump cells in reading order
                        sb.AppendLine("Detected table (cells):");
                        foreach (var cell in table.Cells)
                        {
                            sb.AppendLine($"- [{cell.RowIndex},{cell.ColumnIndex}] {cell.Content}");
                        }
                        sb.AppendLine();
                        continue;
                    }

                    // Build matrix and populate text
                    var matrix = Enumerable.Range(0, rows).Select(_ => Enumerable.Repeat(string.Empty, cols).ToArray()).ToArray();
                    foreach (var cell in table.Cells)
                    {
                        var r = cell.RowIndex;
                        var c = cell.ColumnIndex;
                        if (r >= 0 && r < rows && c >= 0 && c < cols)
                        {
                            matrix[r][c] = (cell.Content ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
                        }
                    }

                    // Markdown table output
                    sb.AppendLine("Detected table:");
                    // Header: try to treat first row as header when reasonable
                    for (int r = 0; r < rows; r++)
                    {
                        sb.Append("| ");
                        for (int c = 0; c < cols; c++)
                        {
                            sb.Append(string.IsNullOrEmpty(matrix[r][c]) ? " " : matrix[r][c]);
                            sb.Append(" | ");
                        }
                        sb.AppendLine();

                        if (r == 0)
                        {
                            sb.Append("| ");
                            for (int c = 0; c < cols; c++)
                            {
                                sb.Append("--- | ");
                            }
                            sb.AppendLine();
                        }
                    }

                    sb.AppendLine();
                }
            }

            // If nothing useful was returned by layout model, return empty.
            var output = sb.ToString().Trim();
            return string.IsNullOrWhiteSpace(output) ? string.Empty : output;
        }
    }

    internal sealed class NoopVisionService : IVisionService
    {
        public Task<string> AnalyzeImageAsync(Stream imageStream, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
    }
}