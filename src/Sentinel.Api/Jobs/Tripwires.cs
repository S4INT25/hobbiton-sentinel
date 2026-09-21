namespace Sentinel.Jobs;

/// <summary>
/// A deterministic SQL check run every minute against ClickHouse. Tripwires are the cheap
/// front half of fraud detection: they decide *whether* something happened, not *what it means*.
/// A hit wakes the LLM fraud agent, which does the expensive interpretation.
///
/// Deliberately not the same thing as <see cref="Sentinel.Agent.FraudPatternRegistry"/> — those are
/// prose descriptions handed to the agent to reason about. These are executable and must be
/// unambiguous enough to page a human at 3am with no judgement applied.
/// </summary>
/// <param name="Sql">
/// Must return a column named <c>entity_key</c> — the thing being cooled down on (a merchant id, an
/// account number). Every other column is free-form context rendered into the agent's prompt.
/// </param>
/// <param name="CooldownHours">
/// How long to stay quiet about the same (rule, entity_key) after firing. A compromised merchant
/// disburses continuously; you want one alert, not one per minute.
/// </param>
public record Tripwire(string Name, string Description, string Sql, int CooldownHours = 6);

public static class Tripwires
{
    /// <summary>
    /// How far back each rule looks. Must comfortably exceed the PeerDB replication lag (~15-60s)
    /// plus the scan interval, so a row can never slip between two consecutive scans. Re-scanning
    /// the same rows every minute is intentional — the cooldown table suppresses the repeats, which
    /// is far simpler than tracking a _peerdb_version cursor and correct under lag spikes.
    /// </summary>
    public const int LookbackMinutes = 15;

    /// <summary>
    /// Mandatory on every replicated table: FINAL collapses unmerged ReplacingMergeTree duplicates,
    /// _peerdb_is_deleted excludes rows deleted in Postgres. Omitting either is silently wrong.
    /// </summary>
    private const string Merchants =
        "(SELECT id, name, status, created_at FROM lipila_blaze.public_merchants FINAL WHERE _peerdb_is_deleted = 0)";


    /// <summary>
    /// ponytail: a static list, not a CRUD table. Rules are SQL — SQL that lives in a database row
    /// nobody can run in a test is worse than SQL that lives next to the code. Move these to
    /// Postgres when someone genuinely needs to add a rule without a deploy.
    ///
    /// Every rule here is absolute, never rate-based. Velocity thresholds on this platform produce
    /// noise, not signal: betting and toll merchants legitimately sustain 50-200 disbursements/hour,
    /// so a rate rule has to be measured against each merchant's own 30-day baseline. That belongs
    /// in the agent, which can look the baseline up — not in a tripwire that must answer yes/no.
    /// </summary>
    public static IReadOnlyList<Tripwire> All =>
    [
        new("suspended_merchant_disbursing",
            "A merchant in suspended, disabled or awaiting_verification status moved money out. "
            + "Regardless of amount this means a platform control was bypassed.",
            $"""
             SELECT
                 toString(t.merchant_id) AS entity_key,
                 m.name                  AS merchant_name,
                 m.status                AS merchant_status,
                 count()                 AS txn_count,
                 sum(t.amount)           AS total_amount,
                 max(t.created_at)       AS latest_txn
             FROM lipila_blaze.public_transactions t FINAL
             INNER JOIN {Merchants} m ON m.id = t.merchant_id
             WHERE t._peerdb_is_deleted = 0
               AND t.type = 'disbursement'
               AND t.status IN ('successful', 'pending')
               AND t.created_at > now() - INTERVAL {LookbackMinutes} MINUTE
               AND m.status IN ('awaiting_verification', 'suspended', 'disabled')
             GROUP BY entity_key, merchant_name, merchant_status
             """),

        new("new_merchant_large_disbursement",
            "A merchant registered less than 7 days ago disbursed a large amount. Legitimate "
            + "merchants ramp up gradually; immediate large payouts are a classic bust-out.",
            $"""
             SELECT
                 toString(t.merchant_id) AS entity_key,
                 m.name                  AS merchant_name,
                 m.created_at            AS merchant_created_at,
                 count()                 AS txn_count,
                 sum(t.amount)           AS total_amount,
                 max(t.amount)           AS largest_amount
             FROM lipila_blaze.public_transactions t FINAL
             INNER JOIN {Merchants} m ON m.id = t.merchant_id
             WHERE t._peerdb_is_deleted = 0
               AND t.type = 'disbursement'
               AND t.status IN ('successful', 'pending')
               AND t.created_at > now() - INTERVAL {LookbackMinutes} MINUTE
               AND t.amount >= 10000
               AND m.created_at > t.created_at - INTERVAL 7 DAY
             GROUP BY entity_key, merchant_name, merchant_created_at
             """),

        // The two rules below are integrity checks, not behavioural ones. Every pattern in
        // FraudPatternRegistry describes someone acting suspiciously; these describe the ledger
        // itself being wrong. An attacker exploiting a replay or a balance race looks like an
        // ordinary merchant making ordinary payouts, so nothing that watches behaviour sees them.
        new("duplicate_request_id",
            "The same request_id produced more than one successful disbursement — a replay that "
            + "paid out twice. Idempotency has failed and money was created from nothing.",
            $"""
             SELECT
                 toString(t.request_id)  AS entity_key,
                 count()                 AS txn_count,
                 sum(t.amount)           AS total_amount,
                 groupUniqArray(t.id)    AS transaction_ids,
                 any(t.merchant_id)      AS merchant_id,
                 any(t.account_number)   AS account_number
             FROM lipila_blaze.public_transactions t FINAL
             WHERE t._peerdb_is_deleted = 0
               AND t.type = 'disbursement'
               AND t.status = 'successful'
               AND t.created_at > now() - INTERVAL 7 DAY
               AND toString(t.request_id) != '00000000-0000-0000-0000-000000000000'
               -- The duplicate is only news when one leg just landed, but its twin may be days
               -- old, so the group is counted over 7 days and filtered to ids seen in the window.
               AND t.request_id IN (
                   SELECT request_id FROM lipila_blaze.public_transactions FINAL
                   WHERE _peerdb_is_deleted = 0 AND type = 'disbursement' AND status = 'successful'
                     AND created_at > now() - INTERVAL {LookbackMinutes} MINUTE)
             GROUP BY entity_key
             HAVING txn_count > 1
             """,
            // Never suppress a second occurrence for long — each one is another double payout.
            CooldownHours: 1),

        new("negative_wallet_balance",
            "A wallet went below zero on a successful transaction. Either the balance check was "
            + "bypassed or concurrent debits raced past it.",
            $"""
             SELECT
                 toString(t.wallet_id) AS entity_key,
                 count()               AS txn_count,
                 min(t.post_balance)   AS lowest_balance,
                 sum(t.amount)         AS total_amount,
                 groupUniqArray(t.id)  AS transaction_ids
             FROM lipila_blaze.public_transactions t FINAL
             WHERE t._peerdb_is_deleted = 0
               AND t.status = 'successful'
               AND t.created_at > now() - INTERVAL {LookbackMinutes} MINUTE
               AND t.post_balance < 0
               AND t.wallet_id != 0
             GROUP BY entity_key
             """,
            CooldownHours: 1),

        new("dormant_api_key_disbursing",
            "An API key with no activity in 30 days started disbursing — the signature of a leaked "
            + "or stolen key (FraudPatternRegistry pattern 17). No api_keys table is replicated, so "
            + "a key being used for the very first time matches identically: check whether this is "
            + "a new merchant integration before treating it as compromise.",
            $"""
             SELECT
                 toString(t.api_key_id) AS entity_key,
                 any(t.merchant_id)     AS merchant_id,
                 count()                AS txn_count,
                 sum(t.amount)          AS total_amount,
                 groupUniqArray(t.ip_address) AS source_ips
             FROM lipila_blaze.public_transactions t FINAL
             WHERE t._peerdb_is_deleted = 0
               AND t.type = 'disbursement'
               AND t.api_key_id != 0
               AND t.created_at > now() - INTERVAL {LookbackMinutes} MINUTE
               AND t.api_key_id NOT IN (
                   SELECT api_key_id FROM lipila_blaze.public_transactions FINAL
                   WHERE _peerdb_is_deleted = 0 AND api_key_id != 0
                     AND created_at BETWEEN now() - INTERVAL 30 DAY
                                        AND now() - INTERVAL {LookbackMinutes} MINUTE)
             GROUP BY entity_key
             """,
            // A woken key keeps disbursing; one alert per day is enough to act on.
            CooldownHours: 24),

        new("wallet_funding_multiple_merchants",
            "One wallet funded disbursements for more than one merchant. Wallets are scoped to a "
            + "single merchant (FraudPatternRegistry pattern 18), so this means the binding broke "
            + "or one actor is driving several merchant accounts.",
            $"""
             SELECT
                 toString(t.wallet_id)         AS entity_key,
                 uniq(t.merchant_id)           AS merchant_count,
                 groupUniqArray(t.merchant_id) AS merchant_ids,
                 count()                       AS txn_count,
                 sum(t.amount)                 AS total_amount
             FROM lipila_blaze.public_transactions t FINAL
             WHERE t._peerdb_is_deleted = 0
               AND t.type = 'disbursement'
               AND t.wallet_id != 0
               AND t.created_at > now() - INTERVAL {LookbackMinutes} MINUTE
             GROUP BY entity_key
             HAVING merchant_count > 1
             """)
    ];
}
