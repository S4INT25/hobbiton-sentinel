using Microsoft.Extensions.Logging.Abstractions;
using Sentinel.Infrastructure;
using ZiggyCreatures.Caching.Fusion;

namespace Sentinel.Tests.Infrastructure;

/// <summary>
/// Integration tests for IpLookupClient — makes real calls to ip-api.com.
/// Requires internet access. Validates parsing, flag detection, and edge cases.
/// </summary>
public class IpLookupClientTests
{
    private static IpLookupClient CreateClient()
    {
        var http = new HttpClient();
        var cache = new FusionCache(new FusionCacheOptions());
        var logger = NullLogger<IpLookupClient>.Instance;
        return new IpLookupClient(http, cache, logger);
    }

    // ── Known IPs used in tests ──────────────────────────────────────────────
    // 8.8.8.8        → Google DNS, US, hosting flag expected
    // 193.9.36.24    → Czech IP (Datacamp), seen in Lipila fraud — foreign + hosting
    // 197.213.0.1    → Zambian IP (Airtel) — should be ZM, not flagged as foreign
    // 10.0.0.1       → Private/RFC1918 — ip-api returns fail for private ranges

    [Fact]
    public async Task LookupAsync_GoogleDns_ReturnsUsResult()
    {
        var client = CreateClient();
        var result = await client.LookupAsync(["8.8.8.8"]);

        Assert.Contains("8.8.8.8", result);
        Assert.Contains("United States", result);
        Assert.Contains("Google", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LookupAsync_KnownFraudIp_FlaggedAsForeignAndDatacenter()
    {
        // 193.9.36.24 — Czech Datacamp IP seen in confirmed Lipila fraud (March 2026)
        var client = CreateClient();
        var result = await client.LookupAsync(["193.9.36.24"]);

        Assert.Contains("193.9.36.24", result);
        Assert.Contains("FOREIGN", result);
        Assert.Contains("DATACENTER", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LookupAsync_ZambianIp_NotFlaggedAsForeign()
    {
        // Airtel Zambia IP — should be ZM, no FOREIGN flag
        var client = CreateClient();
        var result = await client.LookupAsync(["197.213.0.1"]);

        Assert.Contains("197.213.0.1", result);
        Assert.DoesNotContain("FOREIGN", result);
    }

    [Fact]
    public async Task LookupAsync_PrivateIp_ReturnsFailGracefully()
    {
        var client = CreateClient();
        var result = await client.LookupAsync(["10.0.0.1"]);

        // ip-api returns status:fail for private ranges — should not throw
        Assert.Contains("10.0.0.1", result);
        Assert.Contains("fail", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LookupAsync_Ipv4MappedIpv6_NormalisedAndResolved()
    {
        // IPv4-mapped IPv6 addresses (e.g. from ClickHouse logs) should be normalised
        var client = CreateClient();
        var result = await client.LookupAsync(["::ffff:8.8.8.8"]);

        Assert.Contains("8.8.8.8", result);
        Assert.Contains("United States", result);
    }

    [Fact]
    public async Task LookupAsync_MultipleMixedIps_AllReturned()
    {
        var client = CreateClient();
        var result = await client.LookupAsync(["8.8.8.8", "193.9.36.24", "197.213.0.1"]);

        Assert.Contains("8.8.8.8", result);
        Assert.Contains("193.9.36.24", result);
        Assert.Contains("197.213.0.1", result);
    }

    [Fact]
    public async Task LookupAsync_EmptyList_ReturnsMessage()
    {
        var client = CreateClient();
        var result = await client.LookupAsync([]);

        Assert.Contains("No valid IPs", result);
    }

    [Fact]
    public async Task LookupAsync_DuplicateIps_DeduplicatedBeforeSending()
    {
        var client = CreateClient();
        // Passing the same IP 5 times should only result in one lookup entry
        var result = await client.LookupAsync(["8.8.8.8", "8.8.8.8", "8.8.8.8"]);

        var count = result.Split("8.8.8.8", StringSplitOptions.None).Length - 1;
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task LookupAsync_SevenRealWorldIps_AllReturned()
    {
        var client = CreateClient();
        var ips = new[]
        {
            "45.215.236.72", "102.150.205.113", "178.62.84.81",
            "142.93.47.134", "41.72.118.210", "41.223.116.246", "197.239.7.13"
        };
        var result = await client.LookupAsync(ips);

        Assert.DoesNotContain("invalid query", result, StringComparison.OrdinalIgnoreCase);
        foreach (var ip in ips)
            Assert.Contains(ip, result);
    }
}
