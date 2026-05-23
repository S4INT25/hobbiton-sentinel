namespace Sentinel.Agent;

public enum PatternCategory
{
    TransactionAnomaly,
    VelocityAbuse,
    IdentityAndAccess,
    AccountCompromise,
    NetworkAnomaly,
    DataIntegrity,
    MerchantOnboarding,
}

public record FraudPattern(
    int Id,
    string Name,
    string Description,
    PatternCategory Category,
    bool EnabledByDefault = true
);

public static class FraudPatternRegistry
{
    private static readonly List<FraudPattern> All =
    [
        new(1, "Crafted reference IDs",
            """
            Legitimate gateway generates proper UUIDs. Fraud: hand-crafted prefixes with a shared
            suffix across multiple transactions, e.g. "pay001-e6e5-4527-971e-f50bcc1c2a11",
            "pay002-e6e5-4527-971e-f50bcc1c2a11".
            """,
            PatternCategory.DataIntegrity),

        new(2, "Unverified merchant disbursing",
            """
            Merchants in awaiting_verification or suspended status should never disburse.
            If they are, API controls may be bypassed.
            """,
            PatternCategory.MerchantOnboarding),

        new(3, "Velocity abuse",
            """
            More than 5 disbursements in 1 hour from the same merchant or wallet — but ONLY flag this
            if the rate meaningfully exceeds that merchant's historical baseline.

            REQUIRED: Before flagging, query the merchant's transaction history over the past 30 days
            to establish their normal hourly disbursement rate. Compare current rate against the baseline.

            Context: Some merchants are betting companies (sports betting, online gaming). These
            legitimately disburse winnings at high frequency — 50–200+ disbursements per hour is normal
            for them. A betting merchant running at 80 disbursements/hour is not suspicious if their
            30-day average is 70/hour. Only flag if current rate is significantly above their own baseline
            (e.g. >3x their normal peak), or if the pattern is unusual in other ways (new recipients,
            round amounts, sequential account numbers).
            """,
            PatternCategory.VelocityAbuse),

        new(4, "Round amount sweeping",
            """
            Exact round amounts (10000, 5000, 50000) sent to multiple different recipients.
            Classic mobile money cash-out pattern.
            """,
            PatternCategory.TransactionAnomaly),

        new(5, "Known fraud recipients",
            """
            Zambian mobile money numbers linked to a confirmed March–May 2026 fraud ring:
            260961441191, 260961678290, 260962406188, 260963580399, 260964104396,
            260964586604, 260965002533, 260966064891, 260966461615, 260966836104,
            260967095991, 260968327963, 260969099370, 260969261748, 260970137943,
            260970413460, 260971084084, 260971378460, 260972089326, 260972209994,
            260973119302, 260973404970, 260974096574, 260975061461, 260975528553,
            260976236827, 260977182288.
            Any disbursement to these numbers should be treated as high priority regardless of amount.
            """,
            PatternCategory.TransactionAnomaly),

        new(6, "Compromised portal access",
            """
            Admin or portal logins from VPS or datacenter IPs. Legitimate staff use Zambian ISP IPs.
            Suspicious ranges: 79.135.x.x (Datacamp FR), 185.220.x.x (Tor exit nodes),
            any DigitalOcean / AWS / Azure IP not matching known app servers.
            """,
            PatternCategory.AccountCompromise),

        new(7, "Bare narrations",
            """
            Fraud transactions use generic narrations: "payment", "pay", "refund", "transfer".
            Legitimate disbursements are specific: "Policy refund P/01/...", "Commission for [Name]".
            Flag disbursements with single-word or suspiciously vague narrations.
            """,
            PatternCategory.DataIntegrity),

        new(8, "API key allowlist tampering",
            """
            Activity logs showing unrecognised IPs added to a merchant API key's allowed_ips list.
            This pre-stages future fraud access and should be treated as a precursor event.
            """,
            PatternCategory.AccountCompromise),

        new(9, "New merchant immediate large disbursement",
            """
            Merchant created less than 7 days ago disbursing large amounts.
            Legitimate merchants take time to onboard; immediate large disbursements are a red flag.
            """,
            PatternCategory.MerchantOnboarding),

        new(10, "Wallet draining",
            """
            Wallet balance dropping significantly in a short window.
            Compare current balance to the sum of recent disbursements to detect rapid draining.
            """,
            PatternCategory.VelocityAbuse),

        new(11, "Off-hours transactions",
            """
            Large disbursements between midnight and 5am Zambia time (UTC+2) when no staff are
            monitoring. Flag by converting created_at to Africa/Harare timezone before comparing.
            """,
            PatternCategory.TransactionAnomaly),

        new(12, "Paired disbursement fraud",
            """
            Two disbursements to different networks (Airtel + MTN) for the same amount within
            seconds of each other to the same recipient pair, repeated across multiple rounds.
            Indicates coordinated cross-network cash-out.
            """,
            PatternCategory.TransactionAnomaly),

        new(13, "Account number recycling",
            """
            Same destination account number receiving funds from multiple different merchants or
            wallets in a short window. Legitimate recipients rarely appear across unrelated
            merchants simultaneously.
            """,
            PatternCategory.TransactionAnomaly),

        new(14, "IP address sharing across merchants",
            """
            Different merchant API keys or portal accounts making transactions from the exact same
            IP address. May indicate a single actor controlling multiple merchant accounts.
            """,
            PatternCategory.NetworkAnomaly),

        new(15, "Refund and reversal abuse",
            """
            High ratio of refunds or reversals to successful transactions for a merchant.
            Can indicate chargeback fraud or an actor testing for reversal exploits.
            """,
            PatternCategory.TransactionAnomaly),

        new(16, "Incremental amount probing",
            """
            A series of transactions to the same recipient with slowly increasing amounts
            (e.g. 10, 50, 100, 500, 1000). Tests for transaction limits or detection thresholds
            before executing a larger fraudulent transfer.
            """,
            PatternCategory.VelocityAbuse),

        new(17, "Dormant API key sudden activity",
            """
            An API key with no activity for more than 30 days suddenly initiates disbursements.
            May indicate a stolen or leaked key being used by an external actor.
            """,
            PatternCategory.AccountCompromise),

        new(18, "Single wallet funding multiple merchants",
            """
            One wallet used to fund disbursements across several merchant accounts in rapid
            succession. Legitimate wallets are scoped to one merchant.
            """,
            PatternCategory.TransactionAnomaly),

        new(19, "High-frequency small transactions",
            """
            Hundreds of small disbursements (under ZMW 100) to unique recipients in a short window.
            May indicate structuring to avoid detection thresholds or bulk commission fraud.

            REQUIRED: Check merchant's 30-day history first. Betting companies routinely pay out small
            winnings to many recipients — this is expected behaviour for them. Only flag if the volume
            is significantly above the merchant's own historical norm, or if recipients overlap with
            other suspicious patterns (sequential accounts, same IPs, etc.).
            """,
            PatternCategory.VelocityAbuse),

        new(20, "Processor field anomalies",
            """
            Transactions where the processor field is blank, null, or set to an unrecognised value.
            Legitimate transactions always have a valid processor assigned at creation.
            """,
            PatternCategory.DataIntegrity),

        new(21, "Reference ID reuse",
            """
            The same reference_id appearing on more than one transaction.
            Could indicate a replay attack or a double-disbursement exploit.
            """,
            PatternCategory.DataIntegrity),

        new(22, "Sudden merchant volume spike",
            """
            A merchant's transaction count or total ZMW amount in the last hour is significantly above
            their historical norm. Sharp spikes outside normal operating patterns warrant investigation
            even if individual transactions look clean.

            REQUIRED: Always query the merchant's 30-day hourly transaction history to establish their
            baseline before flagging. The threshold should be relative to each merchant's own history —
            not an absolute number.

            Context: Betting companies have naturally high and variable volumes, especially around
            major sporting events (football matches, end-of-season fixtures). A spike on a Saturday
            evening during a Champions League match is expected for a betting merchant. Cross-check
            timing against typical peak hours for that merchant. Only flag if the spike is anomalous
            relative to their own comparable periods (same day-of-week, same time-of-day).
            """,
            PatternCategory.VelocityAbuse),

        new(23, "Failed login clustering",
            """
            Multiple failed portal login attempts for the same account within a short window
            followed by a successful login. May indicate a brute-force or credential-stuffing
            attack that eventually succeeded.
            """,
            PatternCategory.IdentityAndAccess),

        new(24, "New user with immediate admin-level activity",
            """
            A portal user created less than 24 hours ago performing sensitive actions such as
            wallet transfers, API key changes, or merchant settings edits.
            """,
            PatternCategory.IdentityAndAccess),

        new(25, "Cross-border IP on domestic-only merchant",
            """
            A merchant whose business is domestic initiating API calls or portal sessions from
            foreign IPs not associated with known app servers or CDNs.
            """,
            PatternCategory.NetworkAnomaly),
    ];

    /// <summary>Returns all enabled patterns.</summary>
    public static IEnumerable<FraudPattern> GetEnabled() =>
        All.Where(p => p.EnabledByDefault);

    /// <summary>Returns enabled patterns filtered by one or more categories.</summary>
    public static IEnumerable<FraudPattern> GetByCategory(params PatternCategory[] categories) =>
        GetEnabled().Where(p => categories.Contains(p.Category));

    /// <summary>Disables a pattern by ID at runtime (e.g. known false positive environment).</summary>
    public static void Disable(int id)
    {
        var idx = All.FindIndex(p => p.Id == id);
        if (idx >= 0) All[idx] = All[idx] with { EnabledByDefault = false };
    }

    /// <summary>Formats all enabled patterns as a numbered prompt block.</summary>
    public static string ToPromptBlock()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var p in GetEnabled())
        {
            sb.AppendLine($"        {p.Id}. **{p.Name}**");
            
            foreach (var line in p.Description.Trim().Split('\n'))
            {
                sb.AppendLine($"           {line.Trim()}");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }
}