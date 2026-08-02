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

## Database

Active DB: **`patumba_app`** — replicated from PostgreSQL via PeerDB.  
Legacy DB: `patumba` — older backend, not source of truth for current transactions.  
Apply mandatory filters (`_peerdb_is_deleted`, `FINAL`) and timezone rules from the ClickHouse Central KB.

---

## Tables (`patumba_app`)

### `public_wallet_transactions` — main ledger (active)
Every money movement passes through here.

| Column | Notes |
|---|---|
| `wallet_transaction_type` | Business category — see Transaction Types below |
| `service_fee` | Fee earned by Patumba (can be 0 on non-fee-bearing types) |
| `mode` | `credit` = money in, `debit` = money out |
| `source` | Usually `wallet`; `airtel_talk_time` etc. for airtime |
| `invest_your_change_amount` | Round-up amount auto-invested |
| `payment_method` | Rail used |
| `status` | `successful` / `failed` / `pending`. Baseline success rate ~79% |
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

**Money movement:** `deposit`, `withdraw`, `wallet_transfer`, `refund`

**Investments:** `invest`, `auto_investment`, `invest_your_change`, `stock_buy_order`, `stock_sell_order`, `csd_account_creation`

**Loans:** `loan_disbursement`, `loan_repayment`, `mou_loan_disbursement`, `mou_loan_repayment`

**Bills:** `airtel_airtime`, `mtn_airtime`, `zamtel_airtime`, `zesco`, `pay_tv`, `insurance`, `merchant_payment`

**Challenges:** `challenge_join_fee`, `challenge_create_fee`

---

## Metric Definitions

| Metric | Filter |
|---|---|
| Deposits | `wallet_transaction_type='deposit'`, `status='successful'`, `mode='credit'` |
| Withdrawals | `wallet_transaction_type='withdraw'`, `status='successful'`, `mode='debit'` |
| Net Flow | Deposits value − Withdrawals value |
| Withdrawal Fee Income | `SUM(service_fee)` on `public_wallet_transactions` where `status='successful'` AND `wallet_transaction_type='withdraw'` |
| Transfer Fee Income | `SUM(service_fee)` on `public_wallet_transactions` where `status='successful'` AND `wallet_transaction_type='wallet_transfer'` |
| Brokerage Fee Income | `SUM(service_fee)` on `public_trade_transactions` where `trade_status='settled'` |
| Loan Interest Income | `SUM(repayment)` on `public_loans` where `status='successful'` — `repayment` is the interest component only |
| Challenge Fee Income | `SUM(amount)` on `public_wallet_transactions` where `status='successful'` AND `wallet_transaction_type IN ('challenge_join_fee','challenge_create_fee')` |
| CSD Account Fee Income | `SUM(amount)` on `public_csd_transactions` where `status='successful'` |
| Fund Investments | `transaction_type IN ('invest','re_invest')` in investment table |
| Fund Redemptions | `transaction_type='withdraw'` in investment table |
| Loan Disbursements | `status='successful'` on `public_loans` |
| Loan Repayments | `status='successful'` on `public_loan_repayments` |
| Active Loans | `payment_status='pending'`, `status='successful'` on `public_loans` |
| Overdue Loans | `payment_status='due'`, `status='successful'` on `public_loans` |
| Collateral Loans Active | `status='active'` on `public_integration_loans` |
| Success Rate | `COUNT(status='successful') / COUNT(*)` in `public_wallet_transactions` |
| Refunds | `wallet_transaction_type='refund'` |

---

## Investor Lookups

To identify top investors by deposits or withdrawals:

```
JOIN public_wallet_transactions.created_by_id = public_users.id
```

`public_users` columns: `first_name`, `last_name`, `phone_number`, `email`

Use `created_by_id` on `public_wallet_transactions` — this is the investor/user who initiated the transaction.

---

## Known Quirks

1. **High MOU loan failure rate (~66%).** Most loan attempts via USSD fail at the MNO side. This is a known baseline — use `status='successful'` strictly and only flag deviations from baseline, not the rate itself.
2. **Airtel dominance.** Airtel is the largest deposit rail. Disruptions will heavily skew total deposit figures — always break down by `payment_method` when deposits move materially.
3. **`service_fee` can be zero.** Not all transaction types are fee-bearing (e.g. wallet transfers). Expected.
4. **High FD failure rate is normal.** Most failures in `public_integration_transactions` are customers declining auto-reinvestment — not a systemic issue.
5. **`public_patumba_transactions` is deprecated.** Do not use for current-period analysis.
