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