using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using ContentDisposition = MimeKit.ContentDisposition;

namespace Sentinel.Infrastructure;

public class EmailClient(IConfiguration config, ILogger<EmailClient> logger)
{
    private const string FallbackTemplate = """
                                            <html><body style="font-family:sans-serif;max-width:{{WIDTH}};margin:0 auto;padding:24px;color:#111">
                                            <div style="border-bottom:2px solid {{COLOR}};padding-bottom:10px;margin-bottom:20px">
                                              <div style="font-size:11px;font-weight:700;letter-spacing:.1em;text-transform:uppercase;color:#666">Sentinel Analytics</div>
                                              <h1 style="font-size:18px;margin:6px 0 4px">{{SUBJECT}}</h1>
                                              <p style="font-size:12px;color:#999">{{TIMESTAMP}} &nbsp;·&nbsp; {{SEVERITY}}</p>
                                            </div>
                                            {{BODY}}
                                            <p style="font-size:11px;color:#bbb;margin-top:24px;border-top:1px solid #eee;padding-top:10px">Sentinel · Automated report · Do not reply</p>
                                            </body></html>
                                            """;

    private static readonly string TemplatePath = Path.Combine(
        AppContext.BaseDirectory, "Templates", "alert-email.html");

    private static readonly TimeZoneInfo ZambiaZone =
        TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "South Africa Standard Time" : "Africa/Harare");

    public async Task<string> SendAsync(
        string subject,
        string body,
        string severity = "watching",
        IReadOnlyList<string>? recipients = null,
        bool wide = false,
        IReadOnlyList<EmbeddedChartImage>? chartImages = null,
        string? senderName = null,
        string? subjectPrefix = null)
    {
        try
        {
            var from = config["Email:From"]!;
            var fromName = senderName ?? config["Email:FromName"] ?? "Sentinel";
            var defaultRecipient = config["Email:To"] ?? "security@hobbiton.co.zm";
            var prefix = subjectPrefix ?? config["Email:SubjectPrefix"] ?? "";
            var host = config["Email:Smtp:Host"] ?? "smtp.gmail.com";
            var port = config.GetValue("Email:Smtp:Port", 587);
            var user = config["Email:Smtp:User"]!;
            var pass = config["Email:Smtp:Password"]!;

            var htmlBody = BuildHtml(subject, body, severity, wide);

            // Append chart images section to the HTML if present
            if (chartImages is { Count: > 0 })
            {
                var chartHtml = BuildChartSection(chartImages);
                // Insert before the footer
                var footerMarker = "<table class=\"footer-rule\"";
                var footerIdx = htmlBody.IndexOf(footerMarker, StringComparison.Ordinal);
                if (footerIdx > 0)
                    htmlBody = htmlBody.Insert(footerIdx, chartHtml);
                else
                    htmlBody = htmlBody.Replace("</body>", chartHtml + "</body>");
            }

            var fullSubject = string.IsNullOrEmpty(prefix) ? subject : $"{prefix} {subject}";

            var builder = new BodyBuilder { TextBody = body };

            // Embed chart images as CID-linked resources so email clients render them inline
            // Skip images that use inline HTML (bar charts rendered without QuickChart)
            if (chartImages is { Count: > 0 })
            {
                foreach (var ci in chartImages.Where(c => c.InlineHtml == null))
                {
                    var resource = builder.LinkedResources.Add(ci.ContentId + ".png", ci.PngBytes,
                        new ContentType("image", "png"));
                    resource.ContentId = ci.ContentId;
                    resource.ContentDisposition = new ContentDisposition(ContentDisposition.Inline);
                }
            }

            builder.HtmlBody = htmlBody;

            var message = new MimeMessage
            {
                Subject = fullSubject,
                Body = builder.ToMessageBody(),
                Sender = new MailboxAddress(fromName, from),
                From = { new MailboxAddress(fromName, from) },
                Importance = MessageImportance.High,
                Priority = MessagePriority.Urgent
            };
            message.Headers.Add("X-Priority", "1");

            var toRecipients = recipients is { Count: > 0 }
                ? recipients
                : [defaultRecipient];

            foreach (var recipient in toRecipients)
            {
                message.To.Add(MailboxAddress.Parse(recipient));
            }

            using var smtp = new SmtpClient();
            await smtp.ConnectAsync(host, port, SecureSocketOptions.StartTls);
            await smtp.AuthenticateAsync(user, pass);
            await smtp.SendAsync(message);
            await smtp.DisconnectAsync(true);

            logger.LogInformation("Alert sent [{Severity}]: {Subject} (charts: {ChartCount})",
                severity, fullSubject, chartImages?.Count ?? 0);
            return $"Alert sent to {string.Join(", ", message.To)}" +
                   (chartImages is { Count: > 0 } ? $" with {chartImages.Count} chart(s)" : "");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send alert email");
            return $"Email failed: {ex.Message}";
        }
    }

    private static string BuildChartSection(IReadOnlyList<EmbeddedChartImage> charts)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < charts.Count; i += 2)
        {
            var left = charts[i];
            var right = i + 1 < charts.Count ? charts[i + 1] : null;

            sb.AppendLine(
                "<table width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"margin-bottom:16px\">");
            sb.AppendLine("  <tr>");
            var width = right != null ? "50%" : "100%";
            var padRight = right != null ? "12px" : "0";
            sb.AppendLine($"    <td width=\"{width}\" valign=\"top\" style=\"padding-right:{padRight}\">");
            sb.AppendLine(ChartCell(left));
            sb.AppendLine("    </td>");
            if (right != null)
            {
                sb.AppendLine("    <td width=\"50%\" valign=\"top\" style=\"padding-left:12px\">");
                sb.AppendLine(ChartCell(right));
                sb.AppendLine("    </td>");
            }

            sb.AppendLine("  </tr>");
            sb.AppendLine("</table>");
        }

        return sb.ToString();
    }

    private static string ChartCell(EmbeddedChartImage ci)
    {
        if (ci.InlineHtml != null) return ci.InlineHtml;
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(ci.Title))
            sb.AppendLine(
                $"<p style=\"font-size:12px;font-weight:600;color:#52525b;margin:0 0 4px\">{Encode(ci.Title)}</p>");
        sb.AppendLine($"<img src=\"cid:{ci.ContentId}\" alt=\"{Encode(ci.Title ?? "Chart")}\" " +
                      "style=\"max-width:100%;height:auto;border:1px solid #e4e4e7;border-radius:6px\" />");
        return sb.ToString();
    }

    private static string BuildHtml(string subject, string markdownBody, string severity, bool wide = false)
    {
        var color = severity switch
        {
            "critical" => "#b91c1c",
            "warning" => "#d97706",
            "watching" => "#2563eb",
            _ => "#16a34a"
        };

        var zambiaTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ZambiaZone);
        var timestamp = zambiaTime.ToString("dddd, dd MMMM yyyy 'at' HH:mm") + " CAT";

        var template = File.Exists(TemplatePath)
            ? File.ReadAllText(TemplatePath)
            : FallbackTemplate;

        var pillClass = $"badge badge-{severity.ToLower()}";

        return template
            .Replace("{{COLOR}}", color)
            .Replace("{{WIDTH}}", wide ? "860px" : "620px")
            .Replace("{{SUBJECT}}", WebUtility.HtmlEncode(subject))
            .Replace("{{PILL_CLASS}}", pillClass)
            .Replace("{{SEVERITY}}", severity)
            .Replace("{{TIMESTAMP}}", timestamp)
            .Replace("{{BODY}}", MarkdownToHtml(markdownBody));
    }

    /// <summary>
    /// Converts a markdown string to HTML suitable for email.
    /// Handles: h1/h2/h3, tables (class="data", thead/tbody), blockquotes,
    /// ordered/unordered lists, code blocks, inline bold/italic/code, paragraphs.
    /// </summary>
    private static string MarkdownToHtml(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder();
        int i = 0;

        while (i < lines.Length)
        {
            var line = lines[i];

            // ── Fenced code block ──────────────────────────────────────────
            if (line.TrimStart().StartsWith("```"))
            {
                sb.AppendLine("<pre>");
                i++;
                while (i < lines.Length && !lines[i].TrimStart().StartsWith("```"))
                {
                    sb.AppendLine(Encode(lines[i]));
                    i++;
                }

                sb.AppendLine("</pre>");
                i++; // skip closing ```
                continue;
            }

            // ── Markdown table ─────────────────────────────────────────────
            if (line.TrimStart().StartsWith("|"))
            {
                var tableLines = new List<string>();
                while (i < lines.Length && lines[i].TrimStart().StartsWith("|"))
                {
                    tableLines.Add(lines[i]);
                    i++;
                }

                sb.Append(RenderTable(tableLines));
                continue;
            }

            // ── Blockquote ─────────────────────────────────────────────────
            if (line.TrimStart().StartsWith("> "))
            {
                sb.AppendLine("<blockquote>");
                while (i < lines.Length && lines[i].TrimStart().StartsWith("> "))
                {
                    var content = Regex.Replace(lines[i].TrimStart(), @"^>\s?", "");
                    sb.AppendLine($"  <p>{InlineFormat(Encode(content))}</p>");
                    i++;
                }

                sb.AppendLine("</blockquote>");
                continue;
            }

            // ── Headings ───────────────────────────────────────────────────
            if (line.StartsWith("### "))
            {
                sb.AppendLine($"<h3>{InlineFormat(Encode(line[4..]))}</h3>");
                i++;
                continue;
            }

            if (line.StartsWith("## "))
            {
                sb.AppendLine($"<h2>{InlineFormat(Encode(line[3..]))}</h2>");
                i++;
                continue;
            }

            if (line.StartsWith("# "))
            {
                sb.AppendLine($"<h2>{InlineFormat(Encode(line[2..]))}</h2>");
                i++;
                continue;
            }

            // ── Horizontal rule ────────────────────────────────────────────
            if (Regex.IsMatch(line.Trim(), @"^[-*]{3,}$"))
            {
                sb.AppendLine("<hr class=\"rule\">");
                i++;
                continue;
            }

            // ── Unordered list ─────────────────────────────────────────────
            if (Regex.IsMatch(line.TrimStart(), @"^[-*] "))
            {
                sb.AppendLine("<ul>");
                while (i < lines.Length && Regex.IsMatch(lines[i].TrimStart(), @"^[-*] "))
                {
                    var text = Regex.Replace(lines[i].TrimStart(), @"^[-*] ", "");
                    sb.AppendLine($"  <li>{InlineFormat(Encode(text))}</li>");
                    i++;
                }

                sb.AppendLine("</ul>");
                continue;
            }

            // ── Ordered list ───────────────────────────────────────────────
            if (Regex.IsMatch(line.TrimStart(), @"^\d+[\.\)] "))
            {
                sb.AppendLine("<ol>");
                while (i < lines.Length && Regex.IsMatch(lines[i].TrimStart(), @"^\d+[\.\)] "))
                {
                    var text = Regex.Replace(lines[i].TrimStart(), @"^\d+[\.\)] ", "");
                    sb.AppendLine($"  <li>{InlineFormat(Encode(text))}</li>");
                    i++;
                }

                sb.AppendLine("</ol>");
                continue;
            }

            // ── Blank line ─────────────────────────────────────────────────
            if (string.IsNullOrWhiteSpace(line))
            {
                i++;
                continue;
            }

            // ── Paragraph — group consecutive plain lines ──────────────────
            var para = new StringBuilder();
            while (i < lines.Length
                   && !string.IsNullOrWhiteSpace(lines[i])
                   && !lines[i].TrimStart().StartsWith("|")
                   && !lines[i].TrimStart().StartsWith("```")
                   && !lines[i].TrimStart().StartsWith("> ")
                   && !lines[i].StartsWith("# ")
                   && !lines[i].StartsWith("## ")
                   && !lines[i].StartsWith("### ")
                   && !Regex.IsMatch(lines[i].TrimStart(), @"^[-*] ")
                   && !Regex.IsMatch(lines[i].TrimStart(), @"^\d+[\.\)] ")
                   && !Regex.IsMatch(lines[i].Trim(), @"^[-*]{3,}$"))
            {
                if (para.Length > 0) para.Append(' ');
                para.Append(lines[i].Trim());
                i++;
            }

            if (para.Length > 0)
                sb.AppendLine($"<p>{InlineFormat(Encode(para.ToString()))}</p>");
        }

        return sb.ToString();
    }

    private static string RenderTable(List<string> tableLines)
    {
        if (tableLines.Count == 0) return "";

        // Split a row into trimmed cells, stripping leading/trailing pipes
        string[] SplitRow(string row)
        {
            var trimmed = row.Trim().Trim('|');
            return trimmed.Split('|').Select(c => c.Trim()).ToArray();
        }

        bool IsSeparator(string row) =>
            row.Replace("|", "").Replace("-", "").Replace(":", "").Replace(" ", "").Length == 0;

        var sb = new StringBuilder();
        sb.AppendLine("<div class=\"table-wrap\">");
        sb.AppendLine("<table class=\"data\">");

        // Header row
        var headerCells = SplitRow(tableLines[0]);
        sb.AppendLine("  <thead>");
        sb.AppendLine("    <tr>");
        foreach (var cell in headerCells)
            sb.AppendLine($"      <th>{InlineFormat(Encode(cell))}</th>");
        sb.AppendLine("    </tr>");
        sb.AppendLine("  </thead>");

        // Body rows — skip separator lines
        var bodyRows = tableLines.Skip(1).Where(r => !IsSeparator(r)).ToList();
        if (bodyRows.Count > 0)
        {
            sb.AppendLine("  <tbody>");
            foreach (var row in bodyRows)
            {
                var cells = SplitRow(row);
                sb.AppendLine("    <tr>");
                for (int c = 0; c < cells.Length; c++)
                {
                    // Pad missing cells, trim extra cells to match header width
                    var content = c < cells.Length ? cells[c] : "";
                    sb.AppendLine($"      <td>{InlineFormat(Encode(content))}</td>");
                }

                sb.AppendLine("    </tr>");
            }

            sb.AppendLine("  </tbody>");
        }

        sb.AppendLine("</table>");
        sb.AppendLine("</div>");
        return sb.ToString();
    }

    private static string Encode(string s) => WebUtility.HtmlEncode(s);

    private static string InlineFormat(string text)
    {
        text = Regex.Replace(text, @"\*\*(.+?)\*\*", "<strong>$1</strong>");
        text = Regex.Replace(text, @"\*(.+?)\*", "<em>$1</em>");
        text = Regex.Replace(text, @"`(.+?)`", "<code>$1</code>");
        return text;
    }
}

/// <summary>
/// A rendered chart image ready for CID embedding in an email.
/// </summary>
public class EmbeddedChartImage
{
    public required string ContentId { get; init; }
    public required byte[] PngBytes { get; init; }
    public string? Title { get; init; }
    public string? InlineHtml { get; init; }
}