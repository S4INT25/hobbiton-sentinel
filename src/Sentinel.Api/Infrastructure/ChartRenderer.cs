using System.Net;
using System.Text;
using System.Text.Json;
using QuickChart;

namespace Sentinel.Infrastructure;

/// <summary>
/// Renders chart images via the QuickChart.io API using the QuickChart C# SDK.
/// Accepts a Chart.js configuration (type, labels, datasets) and returns PNG bytes
/// suitable for embedding in emails.
/// </summary>
public class ChartRenderer(ILogger<ChartRenderer> logger)
{
    private const int DefaultWidth = 720;
    private const int DefaultHeight = 400;
    private const string DefaultBackground = "#ffffff";

    /// <summary>
    /// Render a chart from a simplified definition (type + labels + datasets) and return PNG bytes.
    /// </summary>
    public byte[]? Render(EmailChart chart)
    {
        try
        {
            var chartJsConfig = BuildChartJsConfig(chart);

            var qc = new Chart
            {
                Width = chart.Width > 0 ? chart.Width : DefaultWidth,
                Height = chart.Height > 0 ? chart.Height : DefaultHeight,
                BackgroundColor = DefaultBackground,
                Config = chartJsConfig
            };

            var bytes = qc.ToByteArray();
            logger.LogInformation("[ChartRenderer] Rendered {Type} chart: {Title} ({Bytes} bytes)",
                chart.Type, chart.Title, bytes.Length);
            return bytes;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[ChartRenderer] Failed to render chart: {Title}", chart.Title);
            return null;
        }
    }

    public static bool IsBarType(string? type) =>
        type?.ToLowerInvariant() is "bar" or "column" or "horizontalbar" or "horizontal_bar";

    public static string RenderHtml(EmailChart chart)
    {
        var colors = new[] { "#3b1fa8", "#9b6fd4", "#3b82f6", "#10b981", "#f97316", "#8b5cf6" };

        var labels = chart.Labels ?? [];
        var datasets = chart.Datasets ?? [];
        int colCount = labels.Count;
        if (colCount == 0) return "";

        var colTotals = Enumerable.Range(0, colCount)
            .Select(i => datasets.Sum(d => i < (d.Data?.Count ?? 0) ? d.Data![i] : 0))
            .ToList();
        var maxTotal = colTotals.Max();
        if (maxTotal == 0) maxTotal = 1;

        var total = datasets.SelectMany(d => d.Data ?? []).Sum();
        var headline = FormatK(total);

        const int BarAreaPx = 80;

        var sb = new StringBuilder();
        sb.AppendLine("<div style=\"font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif;\">");
        sb.AppendLine(
            $"<p style=\"font-size:26px;font-weight:700;color:#18181b;margin:0 0 2px;line-height:1\">{headline}</p>");
        if (!string.IsNullOrWhiteSpace(chart.Title))
            sb.AppendLine(
                $"<p style=\"font-size:12px;font-weight:600;color:#52525b;margin:0 0 12px\">{WebUtility.HtmlEncode(chart.Title)}</p>");

        var widthPct = colCount > 0 ? (100.0 / colCount).ToString("0.##") : "100";
        sb.AppendLine(
            $"<table width=\"100%\" cellpadding=\"0\" cellspacing=\"2\" border=\"0\" style=\"height:{BarAreaPx}px\">");
        sb.AppendLine("  <tr>");
        for (int i = 0; i < colCount; i++)
        {
            var colTotal = colTotals[i];
            var barHeight = maxTotal > 0 ? (double)(colTotal / maxTotal) * BarAreaPx : 0;

            sb.AppendLine(
                $"    <td width=\"{widthPct}%\" valign=\"bottom\" style=\"vertical-align:bottom;padding:0 1px\">");
            for (int d = 0; d < datasets.Count; d++)
            {
                var val = i < (datasets[d].Data?.Count ?? 0) ? datasets[d].Data![i] : 0;
                var segH = colTotal > 0 ? (double)(val / colTotal) * barHeight : 0;
                if (segH < 1 && val > 0) segH = 1;
                var color = colors[d % colors.Length];
                sb.AppendLine($"      <div style=\"height:{segH:0}px;background:{color};display:block\"></div>");
            }

            sb.AppendLine("    </td>");
        }

        sb.AppendLine("  </tr>");
        sb.AppendLine("</table>");

        sb.AppendLine("<table width=\"100%\" cellpadding=\"0\" cellspacing=\"2\" border=\"0\">");
        sb.AppendLine("  <tr>");
        foreach (var lbl in labels)
            sb.AppendLine(
                $"    <td width=\"{widthPct}%\" style=\"font-size:10px;color:#a1a1aa;text-align:center;padding-top:4px\">{WebUtility.HtmlEncode(lbl)}</td>");
        sb.AppendLine("  </tr>");
        sb.AppendLine("</table>");
        sb.AppendLine("</div>");

        return sb.ToString();
    }

    private static string FormatK(decimal val) => val switch
    {
        >= 1_000_000 => $"{val / 1_000_000:0.#}M",
        >= 1_000 => $"{val / 1_000:0.#}k",
        _ => val.ToString("0.#")
    };

    /// <summary>
    /// Build a Chart.js JSON config string from our simplified chart definition.
    /// </summary>
    private static string BuildChartJsConfig(EmailChart chart)
    {
        var type = (chart.Type?.ToLowerInvariant()) switch
        {
            "bar" => "bar",
            "line" => "line",
            "pie" => "pie",
            "doughnut" or "donut" => "doughnut",
            "area" => "line", // area is line with fill
            "radar" => "radar",
            "scatter" => "scatter",
            "horizontalBar" or "horizontal_bar" => "horizontalBar",
            _ => "bar"
        };

        var isArea = chart.Type?.Equals("area", StringComparison.OrdinalIgnoreCase) == true;

        // Build datasets
        var datasets = new List<object>();
        if (chart.Datasets is { Count: > 0 })
        {
            var colorPalette = new[]
            {
                "rgba(59, 130, 246, 0.8)", // blue
                "rgba(16, 185, 129, 0.8)", // emerald
                "rgba(249, 115, 22, 0.8)", // orange
                "rgba(139, 92, 246, 0.8)", // violet
                "rgba(236, 72, 153, 0.8)", // pink
                "rgba(245, 158, 11, 0.8)", // amber
                "rgba(6, 182, 212, 0.8)", // cyan
                "rgba(244, 63, 94, 0.8)", // rose
            };

            var borderPalette = new[]
            {
                "rgba(59, 130, 246, 1)",
                "rgba(16, 185, 129, 1)",
                "rgba(249, 115, 22, 1)",
                "rgba(139, 92, 246, 1)",
                "rgba(236, 72, 153, 1)",
                "rgba(245, 158, 11, 1)",
                "rgba(6, 182, 212, 1)",
                "rgba(244, 63, 94, 1)",
            };

            for (var i = 0; i < chart.Datasets.Count; i++)
            {
                var ds = chart.Datasets[i];
                var color = colorPalette[i % colorPalette.Length];
                var border = borderPalette[i % borderPalette.Length];

                var dataset = new Dictionary<string, object>
                {
                    ["label"] = ds.Label ?? $"Series {i + 1}",
                    ["data"] = ds.Data ?? [],
                };

                if (type is "pie" or "doughnut")
                {
                    // Pie/doughnut: each slice gets its own color
                    dataset["backgroundColor"] = colorPalette.Take(ds.Data?.Count ?? 1).ToArray();
                    dataset["borderColor"] = "#ffffff";
                    dataset["borderWidth"] = 2;
                }
                else
                {
                    dataset["backgroundColor"] = color;
                    dataset["borderColor"] = border;
                    dataset["borderWidth"] = 2;

                    if (isArea || type == "line")
                        dataset["fill"] = isArea;
                }

                datasets.Add(dataset);
            }
        }

        var config = new Dictionary<string, object>
        {
            ["type"] = type,
            ["data"] = new Dictionary<string, object>
            {
                ["labels"] = chart.Labels ?? [],
                ["datasets"] = datasets
            },
            ["options"] = new Dictionary<string, object>
            {
                ["responsive"] = false,
                ["plugins"] = new Dictionary<string, object>
                {
                    ["title"] = new Dictionary<string, object>
                    {
                        ["display"] = !string.IsNullOrWhiteSpace(chart.Title),
                        ["text"] = chart.Title ?? "",
                        ["font"] = new Dictionary<string, object>
                        {
                            ["size"] = 14,
                            ["weight"] = "bold"
                        }
                    },
                    ["legend"] = new Dictionary<string, object>
                    {
                        ["display"] = (chart.Datasets?.Count ?? 0) > 1 || type is "pie" or "doughnut"
                    }
                }
            }
        };

        return JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = false });
    }
}

/// <summary>
/// A chart definition that can be included in an email report.
/// The agent provides this data; ChartRenderer converts it to a Chart.js config for QuickChart.
/// </summary>
public class EmailChart
{
    public string? Type { get; set; }
    public string? Title { get; set; }
    public List<string>? Labels { get; set; }
    public List<EmailChartDataset>? Datasets { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

public class EmailChartDataset
{
    public string? Label { get; set; }
    public List<decimal>? Data { get; set; }
}