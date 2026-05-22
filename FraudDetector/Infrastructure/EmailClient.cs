using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using System.Text;
using System.Text.RegularExpressions;

namespace FraudDetector.Infrastructure;

public class EmailClient(IConfiguration config, ILogger<EmailClient> logger)
{
    private static readonly string TemplatePath = Path.Combine(
        AppContext.BaseDirectory, "Templates", "alert-email.html");

    private static readonly TimeZoneInfo ZambiaZone =
        TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "South Africa Standard Time" : "Africa/Harare");

    public async Task<string> SendAsync(string subject, string body, string severity = "watching")
    {
        try
        {
            var from = config["Email:From"]!;
            var toEmail = config["Email:To"] ?? "security@hobbiton.co.zm";
            var prefix = config["Email:SubjectPrefix"] ?? "[FRAUD DETECTOR]";
            var host = config["Email:Smtp:Host"] ?? "smtp.gmail.com";
            var port = config.GetValue("Email:Smtp:Port", 587);
            var user = config["Email:Smtp:User"]!;
            var pass = config["Email:Smtp:Password"]!;

            var htmlBody = BuildHtml(subject, body, severity);
            var fullSubject = $"{prefix} {subject}";

            var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse(from));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = fullSubject;
            message.Body = new BodyBuilder { HtmlBody = htmlBody, TextBody = body }.ToMessageBody();


            using var smtp = new SmtpClient();
            await smtp.ConnectAsync(host, port, SecureSocketOptions.StartTls);
            await smtp.AuthenticateAsync(user, pass);
            await smtp.SendAsync(message);
            await smtp.DisconnectAsync(true);

            logger.LogInformation("Alert sent [{Severity}]: {Subject}", severity, fullSubject);
            return $"Alert sent to {string.Join(", ", message.To)}";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send alert email");
            return $"Email failed: {ex.Message}";
        }
    }

    private static string BuildHtml(string subject, string markdownBody, string severity)
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
            .Replace("{{SUBJECT}}", System.Net.WebUtility.HtmlEncode(subject))
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
                i++; continue;
            }
            if (line.StartsWith("## "))
            {
                sb.AppendLine($"<h2>{InlineFormat(Encode(line[3..]))}</h2>");
                i++; continue;
            }
            if (line.StartsWith("# "))
            {
                sb.AppendLine($"<h2>{InlineFormat(Encode(line[2..]))}</h2>");
                i++; continue;
            }

            // ── Horizontal rule ────────────────────────────────────────────
            if (Regex.IsMatch(line.Trim(), @"^[-*]{3,}$"))
            {
                sb.AppendLine("<hr class=\"rule\">");
                i++; continue;
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
                i++; continue;
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
        return sb.ToString();
    }

    private static string Encode(string s) => System.Net.WebUtility.HtmlEncode(s);

    private static string InlineFormat(string text)
    {
        text = Regex.Replace(text, @"\*\*(.+?)\*\*", "<strong>$1</strong>");
        text = Regex.Replace(text, @"\*(.+?)\*", "<em>$1</em>");
        text = Regex.Replace(text, @"`(.+?)`", "<code>$1</code>");
        return text;
    }

    private const string FallbackTemplate = """
                                            <html><body style="font-family:sans-serif;max-width:680px;margin:0 auto;padding:24px;color:#111">
                                            <div style="border-bottom:2px solid {{COLOR}};padding-bottom:10px;margin-bottom:20px">
                                              <div style="font-size:11px;font-weight:700;letter-spacing:.1em;text-transform:uppercase;color:#666">Lipila Payment Gateway</div>
                                              <h1 style="font-size:18px;margin:6px 0 4px">{{SUBJECT}}</h1>
                                              <p style="font-size:12px;color:#999">{{TIMESTAMP}} &nbsp;·&nbsp; {{SEVERITY}}</p>
                                            </div>
                                            {{BODY}}
                                            <p style="font-size:11px;color:#bbb;margin-top:24px;border-top:1px solid #eee;padding-top:10px">Fraud Detector · Automated report · Do not reply</p>
                                            </body></html>
                                            """;
}