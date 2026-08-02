# Lipila Knowledge Base

Lipila is a Zambian B2B payment collection and disbursement platform. It enables businesses and merchants to collect payments from customers via mobile money and card (API-driven), disburse funds, and manage settlements. Revenue comes from `charge_amount` on successful transactions.

**Currency:** ZMW (primary), USD (negligible volume)  
**Timezone:** CAT (UTC+2)  
**Revenue:** `charge_amount` on `status = 'successful'` transactions. This is Lipila's fee charged per transaction. `partner_charge` and `commission_amount` are paid out and should not be included in revenue.

---

## Transaction Types

| type | Description |
|---|---|
| `collection` | Customer pays a merchant — dominant type |
| `disbursement` | Merchant pays out to a customer or third party |
| `transfer` | Internal wallet transfer |
| `settlement` | Funds settled to a merchant's bank or mobile wallet |
| `allocation` | Internal fund allocation |
| `service` | Service-type transaction (investment, register-to-earn) |

## Payment Rails

| payment_type | Description |
|---|---|
| `airtel_money` | Airtel Money — dominant (~74% of volume) |
| `mtn_money` | MTN Mobile Money (~21%) |
| `zamtel_kwacha` | Zamtel Kwacha |
| `card` | Debit/credit card |
| `system` | Internal/system-generated |
| `bank` | Bank transfer |

## Channels

| channel | Description |
|---|---|
| `api` | Merchant API integration — dominant (~99%) |
| `ussd` | USSD self-service |
| `dashboard` | Merchant dashboard |
| `payment_link` | Payment link |
| `merchant_mobile_app` | Merchant mobile app |

---

## Database: `lipila_blaze`

Replicated from PostgreSQL via PeerDB. Apply mandatory filters (`_peerdb_is_deleted`, `FINAL`) and timezone rules from the ClickHouse Central KB.

---

## Key Tables

### `public_transactions` — main transaction ledger (active)
Every payment, disbursement, and transfer passes through here.

| Column | Notes |
|---|---|
| `type` | Transaction category — see Transaction Types above |
| `payment_type` | Rail used |
| `channel` | Origination channel |
| `status` | `successful`, `failed`, `pending` — lowercase |
| `amount` | Transaction amount in ZMW |
| `charge_amount` | Fee charged — **primary revenue signal** |
| `total_charge_amount` | Total charge including all components |
| `customer_charge` | Portion of fee borne by customer |
| `partner_charge` | Portion paid to partner (not Lipila revenue) |
| `commission_amount` | Commission component (not Lipila revenue) |
| `service_amount` | Service fee component (sparse) |
| `pre_balance` / `post_balance` | Wallet balance before/after |
| `is_credited` / `is_debited` | Whether wallet was credited/debited |
| `is_commission_credited` | Whether commission was settled |
| `is_invested` | Whether invest-your-change was triggered |
| `wallet_id` | Wallet involved |
| `merchant_id` | Merchant |
| `customer_account_id` | Links to `public_customer_accounts` |
| `settlement_account_id` | Linked settlement account |
| `created_at` | UTC — display as CAT |

---

### `public_customer_accounts` — customer/payer records

| Column | Notes |
|---|---|
| `type` | `msisdn` (mobile number, dominant), `email`, `bank_account` |
| `status` | `active`, `blacklisted` |
| `account_number` | Mobile number, email, or account number |
| `payment_provider_id` | Payment provider reference |

---

### `public_merchants` — registered merchants

| Column | Notes |
|---|---|
| `status` | `active`, `awaiting_verification`, `disabled`, `suspended` |
| `merchant_type` | `sole_proprietorship`, `partnership`, `limited_liability_company`, `cooperative` |

---

### `public_wallets` — merchant/system wallets

| Column | Notes |
|---|---|
| `wallet_type` | `standard`, `default`, `commission` |
| `status` | `active`, `inactive` |

---

### `public_webhooks` — outbound webhook delivery log
Tracks webhook delivery for every transaction event.

| Column | Notes |
|---|---|
| `status` | `successful`, `pending` |
| `status_code` | HTTP response code |
| `duration_ms` | Delivery latency |
| `transaction_id` | Links to `public_transactions` |

---

### `public_batch_payment_requests` — bulk disbursement requests

| Column | Notes |
|---|---|
| `status` | `processed`, `rejected`, `processing`, `verified`, `await_verification` |

### `public_batch_payment_transactions` — individual batch transactions

| Column | Notes |
|---|---|
| `status` | `verified`, `rejected` |

---

## Transaction Statuses

| Status | Meaning |
|---|---|
| `successful` | Completed and settled |
| `failed` | Rejected — by provider, insufficient funds, or timeout |
| `pending` | Awaiting provider confirmation |

> **Important:** Status values are **lowercase** (`successful`, `failed`) — unlike the legacy Lipila database. Filter with `status = 'successful'`.

---

## Metric Definitions

| Metric | Filter |
|---|---|
| Revenue | `SUM(charge_amount)` where `status='successful'` |
| Total Collections | `type='collection'`, `status='successful'` |
| Total Disbursements | `type='disbursement'`, `status='successful'` |
| Total Transfers | `type='transfer'`, `status='successful'` |
| Net Flow | Collections value − Disbursements value |
| Transaction Success Rate | `COUNT(status='successful') / COUNT(*)` |
| Failed Transactions | `status='failed'` |
| Settlements | `type='settlement'`, `status='successful'` |
| Active Merchants | `status='active'` on `public_merchants` |
| Merchants Awaiting Verification | `status='awaiting_verification'` on `public_merchants` |
| Disabled / Suspended Merchants | `status IN ('disabled','suspended')` on `public_merchants` |
| Webhook Success Rate | `COUNT(status='successful') / COUNT(*)` on `public_webhooks` |
| Webhook Pending | `status='pending'` on `public_webhooks` (delivery not yet confirmed) |
| Avg Webhook Latency | `AVG(duration_ms)` on `public_webhooks` |

---

## Known Quirks

1. **Lowercase status.** Status values are lowercase (`successful`/`failed`/`pending`). The legacy `lipila` database used Title Case — do not mix the two.
2. **`charge_amount` on failed transactions is not revenue — `status='successful'` filter is mandatory.** Failed transactions carry `charge_amount` values including clearly erroneous test records with amounts in the hundreds of billions of ZMW. Omitting the status filter inflates revenue by ~356× (2.85B vs 8M ZMW). `partner_charge` and `commission_amount` are paid out — also exclude from revenue.
3. **Mostly API traffic.** ~99% of transactions originate via API. USSD and dashboard are negligible.
4. **Airtel Money dominates by volume (~74%) but revenue per txn varies widely by rail.** Bank transfers and card transactions have higher charge_amount per txn than airtel/mtn. Monitor payment rail mix shifts — a switch from Airtel to card improves revenue even if txn volume appears flat.
5. **High failure rate (~32%).** About one-third of all transactions fail, driven by MNO-side rejections. Day-to-day variation in this rate is normal.
6. **Webhook delivery is asynchronous.** Pending webhooks (38k+) with 5s average latency are normal. Webhooks retried until delivered or dropped. Monitor pending count spikes and latency degradation — may indicate integration partner issues.
