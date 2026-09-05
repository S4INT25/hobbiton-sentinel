# Gari Knowledge Base

Gari is Zambian motor insurance — quotations, policies, vehicles, agent commissions, claims.
Currency: ZMW. Timezone: CAT (UTC+2). Database: `gari`.

**Gari and Inshuwa are separate businesses.** Both write insurance. Never sum them into a single
"insurance" figure, and never compare one's volume against the other as if they were channels of
the same book.

Apply the mandatory `_peerdb_is_deleted = 0` filter and `FINAL` from the ClickHouse Central KB —
every table here is a ReplacingMergeTree CDC replica.

---

## Tables

| Table | Use for |
|---|---|
| `public_Quotations` | Quotes raised |
| `public_Policies` | Policies issued |
| `public_Transactions` | Premium payments, commission payouts, renewals, extensions |
| `public_Claims` | Claims — rare, so any new claim is worth a line |
| `public_GariAgents`, `public_GariAgentCommissions` | Agent activity and commission earnings |
| `public_Client`, `public_GariUser`, `public_Vehicles` | Clients, users, insured vehicles |

Column and table names are PascalCase here, unlike `patumba` and `lipila_blaze`.

---

## Known Quirks

1. **`Status` is a different type depending on the table.** It is a string on
   `public_Transactions` but an **integer code** on `public_Policies`, `public_Quotations` and
   `public_Claims`. Filtering policies with `Status = 'active'` does not error — it silently
   matches nothing and returns zero, which reads as "no policies" rather than as a bug.
2. **Do not guess what the policy status codes mean.** Codes `1` and `0` dominate, with `2` and
   `4` negligible. The mapping is not documented anywhere we can verify, so report counts by
   code or describe them neutrally. Do not assert that `1` means active.
3. **Successful transactions use `success`, not `successful`.** Same as Inshuwa, the opposite of
   Lipila, BNPL and Patumba. Filtering on `successful` silently returns nothing.
4. **Never net premium payments against commission payouts.** `TransactionType` values:
   `premium_payment` (dominant), `commission_pay_out`, `policy_extension`, `policy_renewal`.
   Premium payments are money in, commission payouts are money out. They also carry
   substantially different failure rates — rate them separately, never against one blended
   Gari number.
