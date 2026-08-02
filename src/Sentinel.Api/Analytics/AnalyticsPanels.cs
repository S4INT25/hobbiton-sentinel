namespace Sentinel.Analytics;

/// <summary>
/// One dashboard panel: a fixed ClickHouse query plus how to draw it.
/// </summary>
/// <param name="Id">Stable identifier, used as the cache key suffix and the React key.</param>
/// <param name="Title">Panel heading.</param>
/// <param name="Chart">"line" | "area" | "bar" | "donut" | "table".</param>
/// <param name="Sql">
/// Query template. <c>{from}</c> and <c>{to}</c> are substituted with the window bounds, so the
/// same SQL serves both the current period and the one before it.
/// </param>
/// <param name="Span">Grid columns to occupy (1 = half width, 2 = full width).</param>
/// <param name="Note">Optional caveat shown under the panel.</param>
/// <param name="Compare">
/// Run the query a second time over the preceding window and report the delta. Only worth it for
/// panels with a meaningful period total — a top-10 table or a status snapshot has nothing to
/// compare, and each comparison costs another full ClickHouse scan.
/// </param>
public sealed record AnalyticsPanel(
    string Id,
    string Title,
    string Chart,
    string Sql,
    int Span = 1,
    string? Note = null,
    bool Compare = false);

public sealed record AnalyticsPlatform(
    string Key,
    string Label,
    string Database,
    IReadOnlyList<AnalyticsPanel> Panels);

/// <summary>
/// Deterministic panel definitions for the analytics dashboard.
///
/// These deliberately do NOT go through the LLM agent: a dashboard needs the same number on
/// every refresh, in milliseconds, at no token cost. The agent stays for open-ended questions.
///
/// The metrics mirror the four platform report prompts, so an email and the dashboard cannot
/// disagree about what "revenue" means.
///
/// Every query must carry the two ClickHouse house rules — <c>_peerdb_is_deleted = 0</c> and
/// <c>FINAL</c> — or it will silently count soft-deleted and superseded rows.
/// </summary>
public static class AnalyticsPanels
{
    public const int DefaultDays = 30;
    public const int MinDays = 7;
    public const int MaxDays = 180;

    public static int ClampDays(int? days) =>
        Math.Clamp(days ?? DefaultDays, MinDays, MaxDays);

    public static IReadOnlyList<AnalyticsPlatform> All => [Lipila, Patumba, Inshuwa, Bnpl];

    public static AnalyticsPlatform? Find(string? key) =>
        All.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));

    // ── Lipila Blaze ────────────────────────────────────────────────────────
    // snake_case columns, lowercase status values. charge_amount on failed rows is garbage
    // test data (billions of ZMW), so status='successful' is mandatory on any revenue query.

    private static AnalyticsPlatform Lipila => new("lipila", "Lipila Blaze", "lipila_blaze",
    [
        new AnalyticsPanel("revenue", "Revenue", "area", """
            SELECT toDate(created_at) AS day,
                   round(sum(charge_amount), 2) AS revenue
            FROM lipila_blaze.public_transactions FINAL
            WHERE _peerdb_is_deleted = 0
              AND status = 'successful'
              AND created_at >= {from}
              AND created_at < {to}
            GROUP BY day
            ORDER BY day
            """, Span: 2, Compare: true),

        new AnalyticsPanel("flow", "Collections vs disbursements", "line", """
            SELECT toDate(created_at) AS day,
                   round(sumIf(amount, type = 'collection'), 2) AS collections,
                   round(sumIf(amount, type = 'disbursement'), 2) AS disbursements
            FROM lipila_blaze.public_transactions FINAL
            WHERE _peerdb_is_deleted = 0
              AND status = 'successful'
              AND created_at >= {from}
              AND created_at < {to}
            GROUP BY day
            ORDER BY day
            """, Compare: true),

        new AnalyticsPanel("rails", "Success rate by rail", "table", """
            SELECT payment_type AS rail,
                   count() AS attempts,
                   countIf(status = 'successful') AS successful,
                   round(100.0 * countIf(status = 'successful') / count(), 1) AS success_rate,
                   round(sumIf(charge_amount, status = 'successful'), 2) AS revenue
            FROM lipila_blaze.public_transactions FINAL
            WHERE _peerdb_is_deleted = 0
              AND created_at >= {from}
              AND created_at < {to}
            GROUP BY rail
            ORDER BY attempts DESC
            """, Note: "~32% failure rate overall is the standing baseline, driven by MNO rejections."),

        new AnalyticsPanel("merchants", "Top merchants by collection value", "table", """
            -- The merchant name column here is `name`. `business_name` exists only in the legacy
            -- `lipila` database (on public_business) — not in lipila_blaze.
            SELECT m.name AS merchant,
                   count() AS collections,
                   round(sum(t.amount), 2) AS value,
                   round(sum(t.charge_amount), 2) AS revenue
            FROM lipila_blaze.public_transactions AS t FINAL
            INNER JOIN lipila_blaze.public_merchants AS m FINAL ON t.merchant_id = m.id
            WHERE t._peerdb_is_deleted = 0
              AND m._peerdb_is_deleted = 0
              AND t.status = 'successful'
              AND t.type = 'collection'
              AND t.created_at >= {from}
              AND t.created_at < {to}
            GROUP BY merchant
            ORDER BY value DESC
            LIMIT 10
            """),
    ]);

    // ── Patumba ─────────────────────────────────────────────────────────────
    // Loans are a BNPL channel, not Patumba revenue, so no loan interest here.

    private static AnalyticsPlatform Patumba => new("patumba", "Patumba", "patumba_app",
    [
        new AnalyticsPanel("revenue", "Fee income by stream", "area", """
            SELECT toDate(created_at) AS day,
                   round(sumIf(service_fee, wallet_transaction_type = 'withdraw'), 2) AS withdrawal_fees,
                   round(sumIf(service_fee, wallet_transaction_type = 'wallet_transfer'), 2) AS transfer_fees,
                   round(sumIf(amount, wallet_transaction_type IN ('challenge_join_fee', 'challenge_create_fee')), 2) AS challenge_fees
            FROM patumba_app.public_wallet_transactions FINAL
            WHERE _peerdb_is_deleted = 0
              AND status = 'successful'
              AND created_at >= {from}
              AND created_at < {to}
            GROUP BY day
            ORDER BY day
            """, Span: 2, Note: "Brokerage and CSD fees live in separate tables and are not in this panel.", Compare: true),

        new AnalyticsPanel("flow", "Deposits vs withdrawals", "line", """
            SELECT toDate(created_at) AS day,
                   round(sumIf(amount, wallet_transaction_type = 'deposit' AND mode = 'credit'), 2) AS deposits,
                   round(sumIf(amount, wallet_transaction_type = 'withdraw' AND mode = 'debit'), 2) AS withdrawals
            FROM patumba_app.public_wallet_transactions FINAL
            WHERE _peerdb_is_deleted = 0
              AND status = 'successful'
              AND created_at >= {from}
              AND created_at < {to}
            GROUP BY day
            ORDER BY day
            """, Compare: true),

        new AnalyticsPanel("rails", "Deposits by payment rail", "donut", """
            SELECT payment_method AS rail,
                   round(sum(amount), 2) AS value
            FROM patumba_app.public_wallet_transactions FINAL
            WHERE _peerdb_is_deleted = 0
              AND status = 'successful'
              AND wallet_transaction_type = 'deposit'
              AND mode = 'credit'
              AND created_at >= {from}
              AND created_at < {to}
            GROUP BY rail
            ORDER BY value DESC
            """),

        new AnalyticsPanel("investors", "Top investors by deposit value", "table", """
            SELECT concat(u.first_name, ' ', u.last_name) AS investor,
                   count() AS deposits,
                   round(sum(w.amount), 2) AS total_deposited
            FROM patumba_app.public_wallet_transactions AS w FINAL
            INNER JOIN patumba_app.public_users AS u FINAL ON w.created_by_id = u.id
            WHERE w._peerdb_is_deleted = 0
              AND u._peerdb_is_deleted = 0
              AND w.status = 'successful'
              AND w.wallet_transaction_type = 'deposit'
              AND w.mode = 'credit'
              AND w.created_at >= {from}
              AND w.created_at < {to}
            GROUP BY investor
            ORDER BY total_deposited DESC
            LIMIT 10
            """),
    ]);

    // ── Inshuwa ─────────────────────────────────────────────────────────────
    // Revenue comes from RevenueWallets (credits − debits), never a formula on transaction
    // amounts — the formula misses time-on-risk adjustments on mid-term cancellations.
    // Successful status here is 'success', not 'successful'.

    private static AnalyticsPlatform Inshuwa => new("inshuwa", "Inshuwa", "inshuwa",
    [
        new AnalyticsPanel("revenue", "Net fee income", "area", """
            SELECT toDate(CreatedAt) AS day,
                   round(sumIf(CreditAmount, TransactionType = 'deposit')
                       - sumIf(DebitAmount, TransactionType = 'reversal'), 2) AS net_revenue
            FROM inshuwa.public_RevenueWallets FINAL
            WHERE _peerdb_is_deleted = 0
              AND Status = 'active'
              AND CreatedAt >= {from}
              AND CreatedAt < {to}
            GROUP BY day
            ORDER BY day
            """, Span: 2, Note: "Credits minus reversals — reversals are time-on-risk refunds on cancellations.", Compare: true),

        new AnalyticsPanel("transactions", "Premium transactions by status", "bar", """
            SELECT toDate(CreatedAt) AS day,
                   countIf(Status = 'success') AS successful,
                   countIf(Status = 'failed') AS failed
            FROM inshuwa.public_PolicyTransactions FINAL
            WHERE _peerdb_is_deleted = 0
              AND CreatedAt >= {from}
              AND CreatedAt < {to}
            GROUP BY day
            ORDER BY day
            """, Compare: true),

        new AnalyticsPanel("insurers", "Top insurers", "table", """
            SELECT i.Name AS insurer,
                   count() AS policies,
                   round(sum(t.Amount), 2) AS gwp
            FROM inshuwa.public_PolicyTransactions AS t FINAL
            INNER JOIN inshuwa.public_Insurers AS i FINAL ON t.InsurerId = i.Id
            WHERE t._peerdb_is_deleted = 0
              AND i._peerdb_is_deleted = 0
              AND t.Status = 'success'
              AND t.CreatedAt >= {from}
              AND t.CreatedAt < {to}
            GROUP BY insurer
            ORDER BY policies DESC
            LIMIT 10
            """),

        new AnalyticsPanel("agents", "Top intermediaries", "table", """
            SELECT a.Name AS agent,
                   count() AS policies,
                   round(sum(t.Amount), 2) AS gwp
            FROM inshuwa.public_PolicyTransactions AS t FINAL
            INNER JOIN inshuwa.public_Agents AS a FINAL ON t.IntermediaryId = a.Id
            WHERE t._peerdb_is_deleted = 0
              AND a._peerdb_is_deleted = 0
              AND t.Status = 'success'
              AND t.CreatedAt >= {from}
              AND t.CreatedAt < {to}
            GROUP BY agent
            ORDER BY policies DESC
            LIMIT 10
            """, Note: "Agents only — brokers sit in public_Brokers against the same IntermediaryId."),
    ]);

    // ── BNPL ────────────────────────────────────────────────────────────────
    // Merchant channel only: Money Lender is a 98-transaction pilot and MOU has not launched,
    // so neither would render as anything but a flat line.

    private static AnalyticsPlatform Bnpl => new("bnpl", "BNPL", "bnpl",
    [
        new AnalyticsPanel("revenue", "Interest originated", "area", """
            SELECT toDate(CreatedAt) AS day,
                   round(sum(InterestAmount), 2) AS interest_originated
            FROM bnpl.public_MerchantTransactions FINAL
            WHERE _peerdb_is_deleted = 0
              AND Status = 'successful'
              AND CreatedAt >= {from}
              AND CreatedAt < {to}
            GROUP BY day
            ORDER BY day
            """, Span: 2, Note: "Originated, not collected — realised revenue depends on repayment.", Compare: true),

        new AnalyticsPanel("disbursements", "Disbursements by loan type", "table", """
            SELECT LoanType AS loan_type,
                   count() AS attempts,
                   countIf(Status = 'successful') AS successful,
                   round(100.0 * countIf(Status = 'successful') / count(), 1) AS success_rate,
                   round(sumIf(Amount, Status = 'successful'), 2) AS disbursed
            FROM bnpl.public_MerchantTransactions FINAL
            WHERE _peerdb_is_deleted = 0
              AND CreatedAt >= {from}
              AND CreatedAt < {to}
            GROUP BY loan_type
            ORDER BY attempts DESC
            """, Note: "Success rate is trending up hard — 34.6% over 180d vs 88.2% over 7d (2026-08-02). Compare windows before calling anything a regression."),

        new AnalyticsPanel("portfolio", "Portfolio by loan status", "donut", """
            SELECT LoanStatus AS status,
                   count() AS loans
            FROM bnpl.public_BnplTransactions FINAL
            WHERE _peerdb_is_deleted = 0
              AND LoanStatus != ''
            GROUP BY status
            ORDER BY loans DESC
            """, Note: "Current portfolio state across all channels — not windowed."),

        new AnalyticsPanel("repayments", "Repayments received", "line", """
            SELECT toDate(CreatedAt) AS day,
                   round(sum(Amount), 2) AS repaid
            FROM bnpl.public_MerchantRepaymentTransactions FINAL
            WHERE _peerdb_is_deleted = 0
              AND Status = 'successful'
              AND CreatedAt >= {from}
              AND CreatedAt < {to}
            GROUP BY day
            ORDER BY day
            """, Compare: true),
    ]);
}
