# Patumba Knowledge Base

Patumba is a Zambian savings and investments super-app. Currency: ZMW. Timezone: CAT (UTC+2).

**Revenue lines (5 streams):**

| Stream | Table | Column | Note |
|---|---|---|---|
| Withdrawal fee | `public_wallet_transactions` | `service_fee` | Largest revenue line. Only ~50% of withdrawals are fee-bearing. |
| Transfer fee | `public_wallet_transactions` | `service_fee` | On `wallet_transfer` type. Small volume. |
| Brokerage fee | `public_trade_transactions` | `service_fee` | Only on `trade_status='settled'` orders. All settled trades carry a fee. |
| Challenge fees | `public_wallet_transactions` | `amount` | `challenge_join_fee` and `challenge_create_fee` — the transaction amount IS the fee (no `service_fee`). |
| CSD account fee | `public_csd_transactions` | `amount` | One-time fee per CSD account opening on `status='successful'`. |
| Fixed-term deposit fee | `public_transactions` | `chargeAmount` | Flat ZMW 2.00 per `account_type='fixed_term'` deposit on the investment platform. **Now the largest single fee line** — see Revenue reality check below. |

> **Note:** Loan disbursements and interest are not Patumba revenue — loans are a BNPL channel. Loan data is tracked for portfolio visibility only.  
> Fund management fees are charged at the NAV level and are not captured in transaction data.  
> Deposits, airtime, bills, and investments carry **zero** `service_fee` in `public_wallet_transactions`.

---

## Products

- **Wallet** — core e-wallet, entry point for all money movement
- **Fund Investments** — unit trust funds: Investment, Education, General, Retirement objectives
- **Fixed Deposits** — term deposits with tenor (days) and maturity date
- **MOU Loans** — micro-credit, disbursed/repaid through wallet
- **Stocks** — equity trading on Lusaka Stock Exchange (LUSE) via CSD account
- **SACCO** — savings and credit cooperative
- **Invest Your Change** — round-up micro-investment on transactions
- **Savings Challenges** — group savings; members pay join/create fees
- **Bill Payments** — ZESCO electricity, airtime (Airtel/MTN/Zamtel), Pay TV, insurance, merchants

## Payment Rails

- `airtel_money` — dominant rail (largest deposit share)
- `mtn_money` — second largest
- `zamtel_kwacha` — smallest MNO
- `card` — debit/credit card
- `wallet_transfer` — internal P2P or product funding

---

## Database — read this before writing any query

Active DB: **`patumba`**. The app backend and the USSD investment platform were merged into a
single database; `patumba` is now the only source of truth. Verified 2026-09-05.

| Database | State | Last row |
|---|---|---|
| **`patumba`** | **ACTIVE — use this** | live |
| `patumba_app` | frozen at cutover | 2026-09-04 19:53 |
| `patumba_airtel` | dead | 2026-04-13 |
| `patumba_mtn` | dead | 2026-01-25 |
| `patumba_zamtel` | dead | 2026-01-27 |

`patumba` contains every table `patumba_app` had (only the three `public_*_statistics` tables
were dropped), so a query is ported by changing the database name and nothing else. The three
MNO databases are superseded by the `provider` column on `patumba.public_transactions`.

Apply mandatory filters (`_peerdb_is_deleted`, `FINAL`) and timezone rules from the ClickHouse
Central KB. Every table in `patumba` is ReplacingMergeTree, so `FINAL` is required throughout.

### Two ledgers, not one

This is the single most important thing about the merged database. There are two separate
transaction ledgers with different schemas, different customers and wildly different volumes:

| | App ledger | Investment platform ledger |
|---|---|---|
| Table | `public_wallet_transactions` | `public_transactions` |
| Rows (all time) | 450,301 | 62,214,296 |
| Timestamp column | `created_at` | `dateCreated` |
| Customer table | `public_users` (120,881) | `public_customers` (8,010,847) |
| Customer key | `id` / `created_by_id` | `MSISDN` |
| Channel | smartphone app | USSD via Airtel / MTN / Zamtel |
| Rail column | `payment_method` | `provider` |
| Product column | `wallet_transaction_type` | `account_type` |
| Fee column | `service_fee` | `chargeAmount` |

The investment platform is ~99% of transaction volume and ~86% of new customers. Reporting
only `public_wallet_transactions` — which is what every pre-merge query did — describes about
1% of the business.

**Never sum the two ledgers into one figure without saying so**, and never join
`public_users` to `public_customers`: they are different populations keyed differently.

### Revenue reality check (30 days to 2026-09-05)

| Line | Source | 30-day value |
|---|---|---|
| Fixed-term deposit fee | `public_transactions.chargeAmount` | ZMW 88,736 |
| Withdrawal fee | `public_wallet_transactions.service_fee` | ZMW 43,925 |

The investment platform now earns more fee income than the app. Any revenue total that omits
`chargeAmount` understates the business by roughly two thirds.

---

## Tables — investment platform (merged in)

### `public_transactions` — investment platform ledger (62.2M rows, live)
The USSD savings/investment book, merged from the three MNO databases.

| Column | Notes |
|---|---|
| `dateCreated` | Timestamp — **not** `created_at`. UTC, display as CAT |
| `provider` | `airtel` (51.2M), `mtn` (10.1M), `zamtel` (965k) — replaces the old per-MNO databases |
| `MSISDN` | Customer key; joins to `public_customers.MSISDN` |
| `service_type` | `deposit` or `withdrawal` — only these two |
| `status` | `successful` / `failed` / `pending` |
| `account_type` | Product — see list below |
| `amount` | **Trust only on `status='successful'`** — see quirk 7 |
| `chargeAmount` | Fee charged. Non-zero only on `account_type='fixed_term'` (flat ZMW 2.00) |
| `charge`, `chargeUnits` | Always 0 in current data — do not use |
| `units`, `unitPrice` | Units bought/redeemed and NAV at the time |
| `tenor`, `maturity_date`, `maturity_status` | Fixed-deposit terms |
| `tenure_type` | Always `custom` — carries no information, do not group by it |
| `old_id` | Row's id in its original per-MNO database |

`account_type` values, by 30-day volume: `investment` (1.90M), `fixed_term` (104k),
`saving` (58k), `education_term` (46k), `education1year` (16k), `retirement` (10k),
`education2years`, `education10years`, `education3years`, `education5years`, `legacy_loan`,
`education8years`, `invest_your_change`. Query the distinct values rather than assuming this
list is still complete.

30-day success rate: 77.5%.

### `public_customers` — investment platform customers (8.01M)
Keyed by `MSISDN`. `dateCreated` is the signup timestamp; 119,777 new in the last 30 days.
`customer_status` is `active` for every row and `customer_type` is `individual` for every row —
neither column carries information, do not report a breakdown on them.

### `public_accounts` — investment accounts (8.42M)
One row per customer per product. `account_type` matches the list above (`investment` is 8.30M
of them). `account_status` is `active` on every row — it carries no information.

### `public_scheduled_investments` / `public_investment_schedules` — recurring investments
`public_investment_schedules` is the standing instruction (frequency, tenor, amount);
`public_scheduled_investments` (11.2M rows) is each execution, `status` in
(`successful`, `failed`).

### `public_withdraw_counts` — withdrawals by product and provider
Splits withdrawal activity into `service_type` `patumba` vs `sacco`, by `provider_type`. The
only place the SACCO book is separable. 30-day: patumba/airtel ZMW 153.3M, sacco/airtel 8.1M,
patumba/mtn 23.7M, sacco/mtn 2.8M, patumba/zamtel 0.5M.

### `public_balanceEligibleForWithdraw` — withdrawable unit balance per MSISDN per account type

### `public_unitPrices` / `public_unit_prices` — NAV history
Two tables, both current to 2026-09-05. `public_unit_prices` carries `asset_class_id`;
`public_unitPrices` carries `provider`. Check which one a question needs.

### `public_fund_end_of_day` — daily fund position
`netAssetValue`, `totalUnits`, `unitPrice`, `totalCollection`, `totalWithdraws`,
`totalLiabilities`, `totalExpenses`, `totalInstrumentValue` by `date` and `provider_id`.
This is the AUM source. Current to 2026-09-05.

### `public_customer_fixed_term_deposits` — per-customer FD positions (1.08M)
Includes `interest_amount`, `withholding_tax_amount`, `maturity_date`.

---

## Tables — app side

### `public_wallet_transactions` — app ledger (450k rows, live)
Every money movement *in the smartphone app* passes through here. This is ~1% of Patumba's
transaction volume — see "Two ledgers" above before treating it as the whole business.

| Column | Notes |
|---|---|
| `wallet_transaction_type` | Business category — see Transaction Types below |
| `service_fee` | Fee earned by Patumba (can be 0 on non-fee-bearing types) |
| `mode` | `credit` = money in, `debit` = money out |
| `source` | Usually `wallet`; `airtel_talk_time` etc. for airtime |
| `invest_your_change_amount` | Round-up amount auto-invested |
| `payment_method` | Rail used |
| `status` | `successful` / `failed` / `pending`. 30-day success rate 81.3% |
| `created_at` | UTC — display as CAT (UTC+2) |

### `public_investment_transactions` — fund investments

| Column | Notes |
|---|---|
| `transaction_type` | `invest`, `re_invest`, `withdraw` |
| `unit_price` | NAV at time of transaction |
| `units` | Units bought/redeemed |
| `product_type` | `investment`, `education`, `general`, `retirement` |

### `public_integration_transactions` — fixed deposits

| Column | Notes |
|---|---|
| `type` | `deposit` (new FD) or `withdraw` (payout/redemption) |
| `tenor` | Term in days |
| `maturity_date` | Maturity date |
| `maturity_status` | `matured` or `not_matured` |
| `status` | `successful` / `failed` / `pending` |

Status combos: `successful+not_matured` = active FD; `successful+matured` = paid out; `failed+matured` = payout/reinvestment failed; `failed+not_matured` = creation failed.  
High failure rate is normal — mostly customers declining auto-reinvestment prompts.

### `public_loans` — MOU loan ledger (active)
Every micro-credit loan disbursement attempt. Primary table for loan portfolio analysis.

| Column | Notes |
|---|---|
| `status` | `successful`, `failed`, `pending` — disbursement outcome |
| `payment_status` | `cleared` (fully repaid), `pending` (outstanding), `due` (overdue) |
| `loans_settlement_status` | `settled`, `not_applicable` |
| `channel` | `ussd` (dominant), `app` |
| `amount` | Principal disbursed |
| `repayment` | Interest amount due — equals `amount × (interest_rate / 100)`. This is the interest component only, not principal + interest. |
| `interest_rate` | Rate applied (%) |
| `duration` | Loan term (days) |
| `start_date` / `end_date` | Loan period |
| `collateral_account` | Collateral account reference |

> ~66% of loan attempts fail (USSD/MNO-side rejections). This is a known pattern — not anomalous unless the rate spikes sharply above baseline.

---

### `public_loan_repayments` — loan repayment records
Repayment transactions against specific loans.

| Column | Notes |
|---|---|
| `status` | `successful`, `failed` |
| `repayment_type` | `mobile_money` (dominant), `direct_debit`, `wallet` |
| `channel` | `ussd`, `app` |
| `amount` | Repayment amount |
| `loan_id` | Links to `public_loans` |
| `unit_price` | Exchange/unit rate at repayment time |

---

### `public_integration_loans` — collateral-backed loans
Loans where a portion of a client's savings or fixed deposit is locked as collateral.

| Column | Notes |
|---|---|
| `status` | `active`, `rejected`, `inactive` |
| `locked_amount` | Amount of collateral locked |
| `current_amount` | Current outstanding balance |
| `percentage_locked` | Percentage of collateral locked (%) |
| `client_id` | Links to the borrower |

---

### `public_loan_wallets` — loan wallet balances
Tracks the wallet open/closing balance at loan creation per borrower (supporting table).

---

### `public_trade_transactions` — stock orders

| Column | Notes |
|---|---|
| `order_type` | `buy_order` or `sell_order` |
| `trade_status` | `settled`, `matched`, `awaiting_settlement`, `ats_submitted`, `partially_matched`, `un_matched`, `pending` |
| `service_fee` | Brokerage fee |
| `stock_name` | Listed company |
| `matched_quantity` | Shares filled (may be < ordered) |

### `public_csd_transactions` — CSD account creation
Opening and funding of Central Securities Depository accounts (required before trading).

### `public_patumba_transactions` — DEPRECATED
Last activity: October 2025. Superseded by `public_wallet_transactions`. Do not use for current metrics.

---

## Transaction Types (`wallet_transaction_type`)

Many of these are dormant or dead. Reporting a dead type as "0, down 100%" every day is noise;
reporting one that *wakes up* is a finding. Verified 2026-09-05.

**Live (activity in the last 30 days):**

| Type | 30d count | 30d success | Note |
|---|---|---|---|
| `deposit` | 33,254 | 73.5% | |
| `invest` | 20,848 | 98.2% | |
| `withdraw` | 11,549 | 70.0% | the only `service_fee` line in this table |
| `refund` | 6,915 | 100% | |
| `auto_investment` | 5,967 | 70.1% | |
| `loan_repayment` | 1,268 | 99.9% | |
| `invest_your_change` | 409 | **0%** | every attempt failed — see quirk 8 |
| `airtel_airtime` | 192 | **0%** | |
| `challenge_join_fee` | 132 | 100% | |
| `csd_account_creation` | 98 | 100% | |
| `mtn_airtime` | 89 | **0%** | |
| `stock_buy_order` | 69 | 100% | |
| `challenge_create_fee` | 28 | 100% | |
| `zamtel_airtime` | 17 | **0%** | |
| `wallet_transfer` | 1 | 100% | effectively dead; transfer-fee revenue is ~zero |

**Dormant — last seen:** `loan_disbursement` 2026-01-27 · `zesco` 2026-06-11 ·
`insurance` 2026-03-20 · `pay_tv` 2026-01-13 · `merchant_payment` 2025-12-16 ·
`mou_loan_repayment` 2026-07-02 · `mou_loan_disbursement` 2026-05-11 ·
`stock_sell_order` 2026-06-10 (6 all-time)

---

## Metric Definitions

| Metric | Filter |
|---|---|
| Deposits (app) | `wallet_transaction_type='deposit'`, `status='successful'`, `mode='credit'` on `public_wallet_transactions` |
| Deposits (investment platform) | `service_type='deposit'`, `status='successful'` on `public_transactions` |
| Deposits (group total) | the two above added, and say that you added them |
| Withdrawals | `wallet_transaction_type='withdraw'`, `status='successful'`, `mode='debit'` |
| Net Flow | Deposits value − Withdrawals value |
| Withdrawal Fee Income | `SUM(service_fee)` on `public_wallet_transactions` where `status='successful'` AND `wallet_transaction_type='withdraw'` |
| Transfer Fee Income | `SUM(service_fee)` on `public_wallet_transactions` where `status='successful'` AND `wallet_transaction_type='wallet_transfer'` |
| Brokerage Fee Income | `SUM(service_fee)` on `public_trade_transactions` where `trade_status='settled'` |
| Loan Interest Income | `SUM(repayment)` on `public_loans` where `status='successful'` — `repayment` is the interest component only |
| Challenge Fee Income | `SUM(amount)` on `public_wallet_transactions` where `status='successful'` AND `wallet_transaction_type IN ('challenge_join_fee','challenge_create_fee')` |
| CSD Account Fee Income | `SUM(amount)` on `public_csd_transactions` where `status='successful'` |
| Fixed-Term Deposit Fee Income | `SUM(chargeAmount)` on `public_transactions` where `status='successful'` — non-zero only on `account_type='fixed_term'` |
| Investment Platform AUM | latest `netAssetValue` per `provider_id` in `public_fund_end_of_day` |
| New Investment Customers | `public_customers` by `dateCreated` |
| New App Users | `public_users` by `created_at` — a different population, never added to the above |
| Fund Investments | `transaction_type IN ('invest','re_invest')` in investment table |
| Fund Redemptions | `transaction_type='withdraw'` in investment table |
| Loan Disbursements | `status='successful'` on `public_loans` |
| Loan Repayments | `status='successful'` on `public_loan_repayments` |
| Active Loans | `payment_status='pending'`, `status='successful'` on `public_loans` |
| Overdue Loans | `payment_status='due'`, `status='successful'` on `public_loans` |
| Collateral Loans Active | `status='active'` on `public_integration_loans` |
| Success Rate (app) | `COUNT(status='successful') / COUNT(*)` in `public_wallet_transactions` |
| Success Rate (investment platform) | same, on `public_transactions` |
| Refunds | `wallet_transaction_type='refund'` |

---

## Investor Lookups

To identify top investors by deposits or withdrawals:

```
JOIN public_wallet_transactions.created_by_id = public_users.id
```

`public_users` columns: `first_name`, `last_name`, `phone_number`, `email`

Use `created_by_id` on `public_wallet_transactions` — this is the investor/user who initiated the transaction.

This covers **app users only** (120,881 people). For the investment platform's 8.01M customers,
join `public_transactions.MSISDN = public_customers.MSISDN` and use `first_name`, `last_name`.
A "top investors" table built from one ledger must say which one — the two lists are not
comparable and must never be merged into a single ranking.

---

## Known Quirks

1. **High MOU loan failure rate (~66%).** Most loan attempts via USSD fail at the MNO side. This is a known baseline — use `status='successful'` strictly and only flag deviations from baseline, not the rate itself.
2. **Airtel dominance.** Airtel is the largest deposit rail. Disruptions will heavily skew total deposit figures — always break down by `payment_method` when deposits move materially.
3. **`service_fee` can be zero.** Not all transaction types are fee-bearing (e.g. wallet transfers). Expected.
4. **High FD failure rate is normal.** Most failures in `public_integration_transactions` are customers declining auto-reinvestment — not a systemic issue.
5. **`public_patumba_transactions` is deprecated.** Last row 2025-10-28. Do not use for current-period analysis.
6. **`patumba_app`, `patumba_mtn`, `patumba_airtel` and `patumba_zamtel` are frozen.** They still
   answer queries and still return plausible-looking numbers — they just stop before today.
   `patumba_app` ends 2026-09-04, the MNO databases end between January and April 2026. Any
   query still pointing at them silently reports a dead snapshot. Use `patumba`.
7. **`amount` on `public_transactions` is only trustworthy where `status='successful'`.**
   Failed and pending rows carry corrupt values: one failed deposit holds 5e39 and one pending
   deposit holds 772,081,361, against a successful-deposit p99 of ZMW 2,026. A single
   `SUM(amount)` without a status filter returns a meaningless number that will not look
   obviously wrong in a total.
8. **`invest_your_change` and all three airtime types have a 0% success rate** over the last 30
   days (409 and 298 attempts respectively, none successful). Either genuinely broken or
   permanently misconfigured — worth confirming once, but do not report it as a new incident
   every day.
9. **Dirty timestamps on `public_transactions`.** 74,354 `mtn` rows sit at the Unix epoch
   (1970-01-01) and one `airtel` row is dated 1964-10-24. `MIN(dateCreated)` and
   `MAX(dateCreated)` are therefore both useless on the raw table; bound the window explicitly.
   Recent-window aggregates are unaffected.
10. **All-`active` status columns.** `public_accounts.account_status`,
   `public_customers.customer_status` and `public_customers.customer_type` hold a single value
   across every row. A breakdown by any of them is a table with one line in it, not a finding.
