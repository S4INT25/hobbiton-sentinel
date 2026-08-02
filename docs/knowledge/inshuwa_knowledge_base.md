# Inshuwa Knowledge Base

Inshuwa is a Zambian insurance platform — a digital broker/intermediary connecting clients, insurers, and intermediaries (agents, brokers). It manages policy issuance, premium collection, and commission distribution across multiple insurance product lines.

**Currency:** ZMW (primary), USD (minority)  
**Timezone:** CAT (UTC+2)  
**Revenue:** Net of credits minus reversals in `public_RevenueWallets`. Credits = fees earned on successful transactions. Reversals = fees returned due to mid-term cancellations (time-on-risk adjustments). This is the authoritative source.

---

## Products (Policy Types)

| PolicyType | Description |
|---|---|
| **Motor** | Vehicle insurance — dominant product by volume |
| **General** | General commercial insurance (fire, liability, engineering, marine, etc.) |
| **Travel** | Travel insurance |
| **Life** | Life insurance |
| **Crop** | Agricultural/crop insurance |
| **StatedBenefit** | Fixed-benefit insurance products |

## Classes of Business
Accident, Agriculture, Aviation, Bonds & Guarantees, Crop, Engineering, Fire, Liability, Life, Marine, MicroInsurance, Miscellaneous Accident, Motor, Travel.

---

## Database: `inshuwa`

Replicated from PostgreSQL via PeerDB. Apply mandatory filters (`_peerdb_is_deleted`, `FINAL`) and timezone rules from the ClickHouse Central KB.

---

## Key Tables & Views

### `public_PolicyTransactions` — main transaction ledger (active)
Every premium payment attempt is recorded here. The source of truth for transaction volume, success rates, and revenue calculation.

| Column | Notes |
|---|---|
| `Status` | `success`, `failed`, `cancelled`, `refunded`, `pending`, `reversed` |
| `Type` | `new_business`, `renewal`, `extension`, `revision` |
| `Amount` | Gross premium amount |
| `InsuranceLevy` | Regulatory levy — excluded from revenue base |
| `TransactionFeeRate` | Inshuwa's fee rate (%) |
| `ExchangeRate` | FX rate applied |
| `PaymentMethod` | Rail used |
| `PaymentGateway` | Payment gateway |
| `IntermediaryCommissionRate` | Commission rate for agent/broker |
| `PartnerCommissionRate` | Commission rate for partner |
| `PolicyId` | Links to `all_policies` |
| `SourceOfBusiness` | Raw JSON — do not parse directly. Join `public_PolicyTransactions.Id = public_TransactionSourceOfBusinesses.TransactionId` to get clean `Type` and `Platform` values. |
| `CreatedAt` | UTC — display as CAT |

> **Important:** Successful status is `'success'` — not `'successful'`.  
> Do not calculate revenue from this table directly — use `public_RevenueWallets` instead. The formula-based approach does not account for time-on-risk adjustments on mid-term cancellations.

---

### `public_RevenueWallets` — Inshuwa's earned revenue ledger
Tracks the running balance of revenue earned by Inshuwa.

| Column | Notes |
|---|---|
| `TransactionType` | `deposit` (fee earned) or `reversal` (fee reversed) |
| `Status` | `active` (current) or `inactive` (reversed/voided) |
| `CreditAmount` | Amount credited to revenue wallet |
| `DebitAmount` | Amount debited (reversals) |
| `TransactionId` | Links to `public_PolicyTransactions` |
| `Rate` | Fee rate applied |

> **Net Revenue = Credits − Debits** for the period:
> - `SUM(CreditAmount)` where `TransactionType='deposit'` and `Status='active'` — fees earned
> - minus `SUM(DebitAmount)` where `TransactionType='reversal'` and `Status='active'` — fees reversed due to cancellations (time-on-risk adjustments)

---

### `public_Payments` — outgoing payment disbursements
Tracks all money paid out by the platform: commission payouts, cancellation refunds, wallet operations.

| Column | Notes |
|---|---|
| `PaymentType` | See Payment Types below |
| `PaymentMethod` | `airtel_money`, `mtn_money`, `cash`, `internal`, `external`, `mobile_money` |
| `Status` | `success` or `failed` |
| `Amount` | ZMW value |
| `IntermediaryId` / `InsurerId` / `PartnerId` / `ClientId` | Recipient of the payment |
| `ServiceChargeRate` / `TaxRate` | Charges applied |
| `PaymentGateway` | Gateway used for mobile money payouts |

**Payment Types:**

| PaymentType | Meaning |
|---|---|
| `commission_pay_out` | Earning paid to agent or broker |
| `commission_reversal` | Commission clawed back |
| `cancellation_refund` | Premium refunded on a cancelled policy |
| `wallet_top_up` | Wallet funded from external source |
| `wallet_withdrawal` | Withdrawal from wallet |
| `internal_wallet_transfer` | Transfer between internal wallets |

---

### `all_policies` — consolidated policy ledger
Single view across all policy types. Primary table for policy-level analysis.

| Column | Notes |
|---|---|
| `PolicyType` | `Motor`, `Life`, `Travel`, `General`, `Crop`, `StatedBenefit` |
| `Status` | See Policy Statuses below |
| `BasicPremium` | Base premium before extensions/discounts |
| `GrossPremium` | BasicPremium + ExtensionsPremium − DiscountsPremium |
| `ExtensionsPremium` | Additional cover premiums |
| `DiscountsPremium` | Discounts applied |
| `PremiumLevy` | Regulatory levy |
| `Currency` | `zmw` (majority) or `usd` |
| `CancellationType` | `refund` or `non_refund` — populated only on cancelled policies |
| `StartDate` / `EndDate` | Policy term |
| `ApprovedDate` | When policy was activated |
| `InsurerId` / `IntermediaryId` / `PartnerId` | Ecosystem players on the policy |
| `CreatedAt` | UTC — display as CAT |

---

### `public_Commissions` — intermediary commission records
Commission earned by agents/brokers per transaction.

| Column | Notes |
|---|---|
| `CommissionAmount` | ZMW commission earned |
| `CommissionRate` | Rate applied (%) |
| `CommissionStatus` | `un_paid`, `paid`, `reversed`, `partial_paid` |
| `TaxAmount` / `TaxRate` | Withholding tax |
| `Balance` | Outstanding payable amount |
| `TransactionId` | Links to `public_PolicyTransactions` |

---

### `public_DebitNotes` — premium billing notes
A debit note is raised for each policy premium due.

| Column | Notes |
|---|---|
| `Amount` | Premium billed |
| `AmountPaid` | Amount received |
| `Balance` | Outstanding (Amount − AmountPaid) |
| `Status` | `paid` for all current records — use `Balance` to identify outstanding amounts |

---

---

## Policy Statuses

| Status | Meaning |
|---|---|
| `draft` | Created but not yet active — not yet paid or approved |
| `active` | In-force, premium paid, cover in effect |
| `expired` | Term ended naturally |
| `cancelled` | Cancelled before expiry. `CancellationType`: `refund` (premium returned) or `non_refund` |
| `in_cancellation` | Cancellation initiated but not yet finalised |
| `in_claim` | Claim currently active against this policy |

> Draft policies are the single largest status group. Exclude from active portfolio, GWP, and premium calculations unless analysing the sales funnel.

---

## Transaction Statuses (`public_PolicyTransactions`)

| Status | Meaning |
|---|---|
| `success` | Completed — policy activated, revenue earned |
| `failed` | Payment failed |
| `cancelled` | Transaction cancelled before completion |
| `refunded` | Premium refunded |
| `pending` | Awaiting payment confirmation |
| `reversed` | Reversed after completion |

---

## Metric Definitions

| Metric | Definition |
|---|---|
| Revenue (Fee Income) | `SUM(CreditAmount) WHERE TransactionType='deposit' AND Status='active'` minus `SUM(DebitAmount) WHERE TransactionType='reversal' AND Status='active'` — both from `public_RevenueWallets`. Net of reversals from cancellations. |
| Total Transactions | `COUNT(*)` on `public_PolicyTransactions` for the period |
| Successful Transactions | `Status = 'success'` on `public_PolicyTransactions` |
| Transaction Success Rate | `successful / total` on `public_PolicyTransactions` |
| Failed Transactions | `Status IN ('failed', 'cancelled', 'reversed')` |
| Refunds / Reversals | `Status = 'refunded'` on `public_PolicyTransactions` + `PaymentType = 'cancellation_refund'` on `public_Payments` where `Status='success'` |
| Policies Sold | `COUNT(*)` on `all_policies` where `Status='active'` and `ApprovedDate` in period |
| Policies Renewed | `Type = 'renewal'` on `public_PolicyTransactions` where `Status='success'` in period |
| GWP | `SUM(GrossPremium)` on `all_policies` where `Status='active'` and `ApprovedDate` in period |
| Net Premium Earned | GWP − `SUM(GrossPremium)` on cancelled policies with `CancellationType='refund'` in period |
| Commission Paid | `SUM(Amount)` on `public_Payments` where `PaymentType='commission_pay_out'` and `Status='success'` |

---

## Claims Data — Important Limitation

The `inshuwa` database has **very limited claims data**. Only `public_ClaimHistory` exists as a small audit trail. There is no active claims table with meaningful volume.

Claims-related metrics (Total Claims, Approved, Rejected, Average Claim Value, High-Risk Claims, Fraud Alerts) must be written as **N/A** unless claims data is explicitly provided in the DATA INPUT. Claims are likely managed in a separate system not yet replicated to this database.

---

## Intermediary & Insurer Lookups

To get names on top intermediaries and insurers, join from `public_PolicyTransactions`:

**Top Agents (primary intermediaries):**
```
JOIN public_PolicyTransactions.IntermediaryId = public_Agents.Id
→ public_Agents.Name
```
Covers ~95% of intermediary transactions (SourceType = 'intermediary').

**Top Brokers:**
```
JOIN public_PolicyTransactions.IntermediaryId = public_Brokers.Id
→ public_Brokers.Name
```
Covers the broker segment (SourceType = 'broker', ~5% of intermediary volume).

**Top Insurers:**
```
JOIN public_PolicyTransactions.InsurerId = public_Insurers.Id
→ public_Insurers.Name (or ShortName)
```

---

## Known Quirks

1. **`'success'` not `'successful'`** — Successful transaction status is `'success'` in `public_PolicyTransactions`. Do not confuse with `'successful'` used in other platforms.
2. **Motor dominates volume.** Motor is the largest policy type by far. Always break down by `PolicyType` for meaningful analysis.
3. **Draft-heavy portfolio.** A large proportion of policies are `draft`. Always exclude drafts from revenue and active portfolio metrics.
4. **USD policies exist.** A minority of policies are in USD. Filter to ZMW or apply `ExchangeRate` before summing premiums.
5. **`DebitNotes.Status` is not useful.** All records show `paid`. Use `Balance` to find outstanding amounts.
