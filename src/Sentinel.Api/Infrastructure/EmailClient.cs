using System.Globalization;
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
                                            <div style="border-bottom:1px solid #e7e5e4;padding-bottom:10px;margin-bottom:20px">
                                              <div style="font-size:11px;font-weight:700;letter-spacing:.1em;text-transform:uppercase;color:#666">{{KICKER}}</div>
                                              <h1 style="font-size:18px;margin:6px 0 4px">{{SUBJECT}}</h1>
                                              {{HEADLINE}}
                                              <p style="font-size:12px;color:#999">{{TIMESTAMP}} &nbsp;·&nbsp; {{SEVERITY}}</p>
                                            </div>
                                            {{METRICS}}
                                            {{BODY}}
                                            {{DASHBOARD_CTA}}
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
        string? senderName = null,
        string? subjectPrefix = null,
        string? template = null,
        string? headline = null,
        IReadOnlyList<ReportMetric>? metrics = null)
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

            var htmlBody = BuildHtml(subject, body, severity, wide, template, headline, metrics);
            var fullSubject = string.IsNullOrEmpty(prefix) ? subject : $"{prefix} {subject}";

            var builder = new BodyBuilder { TextBody = body, HtmlBody = htmlBody };

            var spec = ReportTemplate.Resolve(template, severity);
            var message = new MimeMessage
            {
                Subject = fullSubject,
                Body = builder.ToMessageBody(),
                Sender = new MailboxAddress(fromName, from),
                From = { new MailboxAddress(fromName, from) }
            };

            // Only genuine incidents should shove their way to the top of an inbox — a scheduled
            // digest marked "urgent" trains people to ignore the flag when it actually matters.
            if (spec.HighPriority || severity is "critical")
            {
                message.Importance = MessageImportance.High;
                message.Priority = MessagePriority.Urgent;
                message.Headers.Add("X-Priority", "1");
            }

            var toRecipients = recipients is { Count: > 0 } ? recipients : [defaultRecipient];
            foreach (var recipient in toRecipients)
                message.To.Add(MailboxAddress.Parse(recipient));

            using var smtp = new SmtpClient();
            await smtp.ConnectAsync(host, port, SecureSocketOptions.StartTls);
            await smtp.AuthenticateAsync(user, pass);
            await smtp.SendAsync(message);
            await smtp.DisconnectAsync(true);

            logger.LogInformation("Report sent [{Template}/{Severity}]: {Subject}",
                spec.Key, severity, fullSubject);
            return $"Report sent to {string.Join(", ", message.To)}";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send report email");
            return $"Email failed: {ex.Message}";
        }
    }

    private string BuildHtml(
        string subject,
        string markdownBody,
        string severity,
        bool wide,
        string? templateKey,
        string? headline,
        IReadOnlyList<ReportMetric>? metrics)
    {
        var spec = ReportTemplate.Resolve(templateKey, severity);

        // Severity drives the accent for anything alarming; otherwise the template's own identity
        // colour wins, so an executive digest never looks like a red alert.
        var color = severity switch
        {
            "critical" => "#b91c1c",
            "warning" => "#d97706",
            _ => spec.Accent
        };

        var zambiaTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ZambiaZone);
        var timestamp = zambiaTime.ToString("dddd, dd MMMM yyyy 'at' HH:mm") + " CAT";

        var template = File.Exists(TemplatePath)
            ? File.ReadAllText(TemplatePath)
            : FallbackTemplate;

        var dashboardUrl = config["Email:DashboardUrl"];

        return template
            .Replace("{{COLOR}}", color)
            .Replace("{{WIDTH}}", wide ? "860px" : "640px")
            .Replace("{{SUBJECT}}", Encode(subject))
            .Replace("{{KICKER}}", Encode(spec.Kicker))
            .Replace("{{PILL_CLASS}}", $"badge badge-{severity.ToLowerInvariant()}")
            .Replace("{{SEVERITY}}", Encode(severity))
            .Replace("{{TIMESTAMP}}", timestamp)
            .Replace("{{HEADLINE}}", BuildHeadline(headline))
            .Replace("{{METRICS}}", BuildMetricStrip(metrics))
            .Replace("{{BODY}}", MarkdownToHtml(markdownBody))
            .Replace("{{DASHBOARD_CTA}}", BuildDashboardCta(dashboardUrl, spec));
    }

    private static string BuildHeadline(string? headline) =>
        string.IsNullOrWhiteSpace(headline)
            ? ""
            : $"<p class=\"headline\">{InlineFormat(Encode(headline.Trim()))}</p>";

    /// <summary>
    /// Renders 2-4 headline figures as a table-based strip. Tables, not flexbox — Outlook
    /// ignores modern layout entirely and would otherwise stack these into a ragged column.
    /// </summary>
    private static string BuildMetricStrip(IReadOnlyList<ReportMetric>? metrics)
    {
        if (metrics is not { Count: > 0 }) return "";

        var shown = metrics.Take(4).ToList();
        var width = (100 / shown.Count).ToString(CultureInfo.InvariantCulture);

        var sb = new StringBuilder();
        sb.AppendLine("<table class=\"metrics\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" role=\"presentation\">");
        sb.AppendLine("  <tr>");
        foreach (var m in shown)
        {
            var deltaClass = m.Direction switch
            {
                "up" => "delta delta-up",
                "down" => "delta delta-down",
                _ => "delta"
            };
            var arrow = m.Direction switch { "up" => "&#8593; ", "down" => "&#8595; ", _ => "" };

            sb.AppendLine($"    <td class=\"metric\" width=\"{width}%\" valign=\"top\">");
            sb.AppendLine($"      <p class=\"metric-label\">{Encode(m.Label)}</p>");
            sb.AppendLine($"      <p class=\"metric-value\">{Encode(m.Value)}</p>");
            if (!string.IsNullOrWhiteSpace(m.Change))
                sb.AppendLine($"      <p class=\"{deltaClass}\">{arrow}{Encode(m.Change)}</p>");
            sb.AppendLine("    </td>");
        }
        sb.AppendLine("  </tr>");
        sb.AppendLine("</table>");
        return sb.ToString();
    }

    private static string BuildDashboardCta(string? dashboardUrl, ReportTemplate spec)
    {
        if (string.IsNullOrWhiteSpace(dashboardUrl)) return "";
        var url = Encode(dashboardUrl.TrimEnd('/'));
        return $"""
                <table class="cta" width="100%" cellpadding="0" cellspacing="0" role="presentation">
                  <tr><td>
                    <a class="cta-link" href="{url}">{Encode(spec.CtaLabel)} &rarr;</a>
                    <p class="cta-hint">Charts, filters and full history live on the dashboard.</p>
                  </td></tr>
                </table>
                """;
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

/// <summary>A single headline figure rendered in the metric strip at the top of a report.</summary>
public record ReportMetric(string Label, string Value, string? Change = null, string? Direction = null);

/// <summary>
/// Per-report-type presentation: identity colour, header kicker, dashboard CTA wording and
/// whether the mail deserves inbox priority. One shared HTML shell renders all of them, so a
/// styling fix lands everywhere at once instead of drifting across near-identical template files.
/// </summary>
public sealed record ReportTemplate(
    string Key,
    string Kicker,
    string Accent,
    string CtaLabel,
    bool HighPriority)
{
    public static readonly ReportTemplate Executive =
        new("executive", "Executive Summary", "#0f766e", "Open the dashboard", false);

    public static readonly ReportTemplate Operational =
        new("operational", "Operational Report", "#2563eb", "View operational metrics", false);

    public static readonly ReportTemplate Incident =
        new("incident", "Incident Report", "#b91c1c", "Investigate in Sentinel", true);

    public static readonly ReportTemplate Activity =
        new("activity", "Platform Activity", "#7c3aed", "See full activity", false);

    public static readonly ReportTemplate Custom =
        new("custom", "Sentinel Analytics", "#16a34a", "Open the dashboard", false);

    public static ReportTemplate Resolve(string? key, string severity) =>
        (key ?? "").Trim().ToLowerInvariant() switch
        {
            "executive" => Executive,
            "operational" => Operational,
            "incident" => Incident,
            "activity" => Activity,
            // Legacy keys from before templates were split out by report purpose.
            "fraud_alert" => Incident,
            "insights" => Executive,
            // An unlabelled critical report is an incident whether or not it said so.
            _ => severity == "critical" ? Incident : Custom
        };
}
