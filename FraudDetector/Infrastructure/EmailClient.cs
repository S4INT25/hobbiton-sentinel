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
    /// Handles: headings, tables (with proper thead/tbody), ordered/unordered lists,
    /// code blocks, paragraphs, bold, italic, inline code.
    /// </summary>
    private static string MarkdownToHtml(string markdown)
    {
        // Normalise line endings
        var lines = markdown.Replace("\r\n", "\n").Split('\n');

        // Collect table blocks first — parse them as a unit
        var sb = new StringBuilder();
        int i = 0;

        while (i < lines.Length)
        {
            var line = lines[i];

            // --- Code block ---
            if (line.TrimStart().StartsWith("```"))
            {
                sb.AppendLine("<pre>");
                i++;
                while (i < lines.Length && !lines[i].TrimStart().StartsWith("```"))
                {
                    sb.AppendLine(System.Net.WebUtility.HtmlEncode(lines[i]));
                    i++;
                }

                sb.AppendLine("</pre>");
                i++; // skip closing ```
                continue;
            }

            // --- Table block: collect all consecutive | lines ---
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

            // --- Headings ---
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

            // --- Horizontal rule ---
            if (Regex.IsMatch(line.Trim(), @"^-{3,}$"))
            {
                sb.AppendLine("<hr/>");
                i++;
                continue;
            }

            // --- Unordered list block ---
            if (line.StartsWith("- ") || line.StartsWith("* "))
            {
                sb.AppendLine("<ul>");
                while (i < lines.Length && (lines[i].StartsWith("- ") || lines[i].StartsWith("* ")))
                {
                    var text = lines[i].StartsWith("- ") ? lines[i][2..] : lines[i][2..];
                    sb.AppendLine($"  <li>{InlineFormat(Encode(text))}</li>");
                    i++;
                }

                sb.AppendLine("</ul>");
                continue;
            }

            // --- Ordered list block ---
            if (Regex.IsMatch(line, @"^\d+\. "))
            {
                sb.AppendLine("<ol>");
                while (i < lines.Length && Regex.IsMatch(lines[i], @"^\d+\. "))
                {
                    var text = Regex.Replace(lines[i], @"^\d+\. ", "");
                    sb.AppendLine($"  <li>{InlineFormat(Encode(text))}</li>");
                    i++;
                }

                sb.AppendLine("</ol>");
                continue;
            }

            // --- Blank line (skip) ---
            if (string.IsNullOrWhiteSpace(line))
            {
                i++;
                continue;
            }

            // --- Paragraph ---
            sb.AppendLine($"<p>{InlineFormat(Encode(line))}</p>");
            i++;
        }

        return sb.ToString();
    }

    private static string RenderTable(List<string> tableLines)
    {
        if (tableLines.Count == 0) return "";

        // Split a row into cells, trimming pipes and whitespace
        string[] SplitRow(string row) =>
            row.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var sb = new StringBuilder();
        sb.AppendLine("<table>");

        // First row = header
        sb.AppendLine("  <thead><tr>");
        foreach (var cell in SplitRow(tableLines[0]))
            sb.AppendLine($"    <th>{InlineFormat(Encode(cell))}</th>");
        sb.AppendLine("  </tr></thead>");

        // Skip separator row (line 1 is header, line 2 is ---)
        int dataStart = tableLines.Count > 1 && tableLines[1].Contains("---") ? 2 : 1;

        if (dataStart < tableLines.Count)
        {
            sb.AppendLine("  <tbody>");
            for (int r = dataStart; r < tableLines.Count; r++)
            {
                if (tableLines[r].Contains("---")) continue; // safety skip
                sb.AppendLine("    <tr>");
                foreach (var cell in SplitRow(tableLines[r]))
                    sb.AppendLine($"      <td>{InlineFormat(Encode(cell))}</td>");
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