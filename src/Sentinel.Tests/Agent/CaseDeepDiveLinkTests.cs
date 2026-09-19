using Sentinel.Agent;
using Sentinel.Infrastructure;

namespace Sentinel.Tests.Agent;

/// <summary>
/// The deep-dive link is built by rewriting case IDs in the alert body, then rendered by the
/// email markdown pass. Both halves are regex, and a second rewrite pass over an inserted URL
/// would corrupt the link silently — the email still sends, it just goes nowhere useful.
/// </summary>
public class CaseDeepDiveLinkTests
{
    private const string Base = "https://sentinel.example.com";

    [Fact]
    public void Known_case_ids_become_links_backticked_or_bare()
    {
        var body = FraudAgent.LinkifyCaseIds("Case ID `A1B2C3D4` — see also 9F8E7D6C.", Base,
            ["A1B2C3D4", "9F8E7D6C"]);

        Assert.Contains("[A1B2C3D4](https://sentinel.example.com/chat?q=Deep%20dive%20on%20fraud%20case%20A1B2C3D4)", body);
        Assert.Contains("[9F8E7D6C](", body);
        Assert.DoesNotContain("`A1B2C3D4`", body);
    }

    [Fact]
    public void Unknown_ids_are_left_alone_and_urls_are_not_rewritten_twice()
    {
        var body = FraudAgent.LinkifyCaseIds("Case `A1B2C3D4` and ref `DEADBEEF`.", Base, ["A1B2C3D4"]);

        Assert.Contains("`DEADBEEF`", body);
        Assert.Single(body.Split("/chat?q=").Skip(1));
        Assert.DoesNotContain("[A1B2C3D4](https://sentinel.example.com/chat?q=Deep%20dive%20on%20fraud%20case%20[A1B2C3D4]", body);
    }

    [Fact]
    public void Markdown_links_render_as_anchors_including_inside_tables()
    {
        var html = EmailClient.MarkdownToHtml(
            "See [A1B2C3D4](https://x.test/chat?q=a%20b).\n\n| Case | Status |\n|---|---|\n| [A1B2C3D4](https://x.test/c) | open |");

        Assert.Contains("<a href=\"https://x.test/chat?q=a%20b\">A1B2C3D4</a>", html);
        Assert.Contains("<td><a href=\"https://x.test/c\">A1B2C3D4</a></td>", html);
    }
}
