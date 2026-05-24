using System.Text;
using System.Text.Json;
using ZiggyCreatures.Caching.Fusion;

namespace Sentinel.Infrastructure;

/// <summary>
/// Looks up IP geolocation and threat intelligence via ip-api.com (free, no key required).
/// Results are cached via FusionCache — IPs rarely change ISP/geo, so TTL is 30 days.
/// </summary>
public class IpLookupClient(HttpClient http, IFusionCache cache, ILogger<IpLookupClient> logger)
{
    private const string BatchUrl = "http://ip-api.com/batch?fields=query,country,countryCode,regionName,city,isp,org,as,proxy,hosting,status,message";

    public async Task<string> LookupAsync(IEnumerable<string> ips)
    {
        var ipList = ips
            .Select(ip => ip.Replace("::ffff:", "").Trim())
            .Where(ip => !string.IsNullOrWhiteSpace(ip))
            .Distinct()
            .Take(10)
            .ToList();

        if (ipList.Count == 0)
            return "No valid IPs provided.";

        // Check cache per-IP, collect misses for batch lookup
        var results = new Dictionary<string, string>();
        var misses = new List<string>();

        foreach (var ip in ipList)
        {
            var cached = await cache.TryGetAsync<string>($"sentinel:ip:{ip}");
            if (cached.HasValue)
                results[ip] = cached.Value;
            else
                misses.Add(ip);
        }

        if (misses.Count > 0)
        {
            var fresh = await FetchAsync(misses);
            foreach (var (ip, summary) in fresh)
            {
                results[ip] = summary;
                await cache.SetAsync($"sentinel:ip:{ip}", summary,
                    options => options.SetDuration(TimeSpan.FromDays(30)));
            }
        }

        var sb = new StringBuilder();
        foreach (var ip in ipList)
            if (results.TryGetValue(ip, out var line)) sb.Append(line);

        logger.LogInformation("IP lookup: {Total} IPs ({Cached} cached, {Fresh} fetched)",
            ipList.Count, ipList.Count - misses.Count, misses.Count);

        return sb.ToString();
    }

    private async Task<Dictionary<string, string>> FetchAsync(List<string> ips)
    {
        var output = new Dictionary<string, string>();
        try
        {
            var payload = JsonSerializer.Serialize(ips.Select(ip => new { query = ip }));
            var response = await http.PostAsync(BatchUrl, new StringContent(payload, Encoding.UTF8, "application/json"));
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("ip-api.com returned {Status}: {Body}", response.StatusCode, content[..Math.Min(200, content.Length)]);
                foreach (var ip in ips)
                    output[ip] = $"- {ip}: lookup failed ({response.StatusCode})\n";
                return output;
            }

            using var doc = JsonDocument.Parse(content);
            foreach (var entry in doc.RootElement.EnumerateArray())
            {
                var ip     = entry.TryGetProperty("query",       out var q)   ? q.GetString()  ?? "?" : "?";
                var status = entry.TryGetProperty("status",      out var st)  ? st.GetString() ?? ""  : "";

                if (status == "fail")
                {
                    var msg = entry.TryGetProperty("message", out var m) ? m.GetString() : "unknown";
                    output[ip] = $"- {ip}: lookup failed — {msg}\n";
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
                var sb = new StringBuilder();
                sb.AppendLine($"- {ip}{flagStr}");
                sb.AppendLine($"  Location : {city}, {region}, {country}");
                sb.AppendLine($"  ISP/Org  : {isp} / {org}");
                sb.AppendLine($"  ASN      : {asn}");
                output[ip] = sb.ToString();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "IP lookup failed");
            foreach (var ip in ips)
                output[ip] = $"- {ip}: lookup error — {ex.Message}\n";
        }
        return output;
    }
}
