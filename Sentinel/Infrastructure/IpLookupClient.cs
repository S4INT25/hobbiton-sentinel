using System.Text;
using System.Text.Json;

namespace Sentinel.Infrastructure;

/// <summary>
/// Looks up IP geolocation and threat intelligence via ip-api.com (free, no key required).
/// Batch endpoint: POST http://ip-api.com/batch — up to 100 IPs per request, 100 req/min.
/// </summary>
public class IpLookupClient(HttpClient http, ILogger<IpLookupClient> logger)
{
    private const string BatchUrl = "http://ip-api.com/batch?fields=query,country,countryCode,regionName,city,isp,org,as,proxy,hosting,status,message";

    public async Task<string> LookupAsync(IEnumerable<string> ips)
    {
        var ipList = ips
            .Select(ip => ip.Replace("::ffff:", "").Trim()) // normalise IPv4-mapped IPv6
            .Where(ip => !string.IsNullOrWhiteSpace(ip))
            .Distinct()
            .Take(10)
            .ToList();

        if (ipList.Count == 0)
            return "No valid IPs provided.";

        try
        {
            var payload = JsonSerializer.Serialize(ipList.Select(ip => new { query = ip }));
            var response = await http.PostAsync(BatchUrl, new StringContent(payload, Encoding.UTF8, "application/json"));
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("ip-api.com returned {Status}: {Body}", response.StatusCode, content[..Math.Min(200, content.Length)]);
                return $"IP lookup failed ({response.StatusCode}): {content[..Math.Min(200, content.Length)]}";
            }

            // Parse and reformat into a readable summary
            using var doc = JsonDocument.Parse(content);
            var sb = new StringBuilder();

            foreach (var entry in doc.RootElement.EnumerateArray())
            {
                var ip      = entry.TryGetProperty("query",       out var q)   ? q.GetString()   : "?";
                var status  = entry.TryGetProperty("status",      out var st)  ? st.GetString()  : "unknown";

                if (status == "fail")
                {
                    var msg = entry.TryGetProperty("message", out var m) ? m.GetString() : "unknown error";
                    sb.AppendLine($"- {ip}: lookup failed — {msg}");
                    continue;
                }

                var country = entry.TryGetProperty("country",     out var c)   ? c.GetString()   : "?";
                var cc      = entry.TryGetProperty("countryCode", out var ccc) ? ccc.GetString() : "?";
                var region  = entry.TryGetProperty("regionName",  out var r)   ? r.GetString()   : "?";
                var city    = entry.TryGetProperty("city",        out var ci)  ? ci.GetString()  : "?";
                var isp     = entry.TryGetProperty("isp",         out var i)   ? i.GetString()   : "?";
                var org     = entry.TryGetProperty("org",         out var o)   ? o.GetString()   : "?";
                var asn     = entry.TryGetProperty("as",          out var a)   ? a.GetString()   : "?";
                var proxy   = entry.TryGetProperty("proxy",       out var px)  && px.GetBoolean();
                var hosting = entry.TryGetProperty("hosting",     out var h)   && h.GetBoolean();

                var flags = new List<string>();
                if (proxy)   flags.Add("PROXY/VPN");
                if (hosting) flags.Add("DATACENTER/HOSTING");
                if (cc != "ZM") flags.Add($"FOREIGN ({cc})");

                var flagStr = flags.Count > 0 ? $" [{string.Join(", ", flags)}]" : "";

                sb.AppendLine($"- {ip}{flagStr}");
                sb.AppendLine($"  Location : {city}, {region}, {country}");
                sb.AppendLine($"  ISP/Org  : {isp} / {org}");
                sb.AppendLine($"  ASN      : {asn}");
            }

            logger.LogInformation("IP lookup completed for {Count} IPs", ipList.Count);
            return sb.ToString();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "IP lookup failed");
            return $"IP lookup error: {ex.Message}";
        }
    }
}
