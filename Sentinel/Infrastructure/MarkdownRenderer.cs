using Markdig;
using Microsoft.AspNetCore.Components;

namespace Sentinel.Infrastructure;

/// <summary>
/// Renders markdown to sanitized HTML for use in Blazor components via MarkupString.
/// </summary>
public static class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    /// <summary>
    /// Converts markdown text to a MarkupString safe for rendering in Blazor.
    /// </summary>
    public static MarkupString ToHtml(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return new MarkupString(string.Empty);

        var html = Markdown.ToHtml(markdown, Pipeline);
        return new MarkupString(html);
    }
}
