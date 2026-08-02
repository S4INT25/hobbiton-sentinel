# BNPL Knowledge Base

BNPL operates three separate lending channels in Zambia. It enables customers to receive short-term loans disbursed via mobile money or wallet, repaid over a defined duration with interest.

**Currency:** ZMW  
**Timezone:** CAT (UTC+2)  
**Three Channels:**
1. **Merchant** (primary, 247k+ txns) — loans via merchant partners. Table: `public_MerchantTransactions`
2. **Money Lender** (pilot, 98 txns) — loans via money lender network. Table: `public_MoneyLenderLoanTransactions`
3. **MOU** (not yet launched, 0 txns) — direct micro-credit via MOU. Status: TBD

**Revenue:** Interest earned on successful loan disbursements (`InterestAmount` per channel). Actual cash collected from repayment transactions.

---

## Products / Loan Types (Merchant Channel)

| LoanType | Description |
|---|---|
| `mobile_money_loan` | Loan disbursed directly to customer's mobile money number — dominant type |
| `wallet_loan` | Loan disbursed to a Patumba wallet |

## BNPL Transaction Types (Merchant Channel)

| BnplTransactionType | Description |
|---|---|
| `mobile_money` | Standard mobile money loan disbursement |
| `patumba_wallet_loan` | Loan via Patumba wallet |
| `lipila_merchant_transfer` | Disbursement routed through a Lipila merchant |

## Repayment Types

| RepaymentType | Description |
|---|---|
| `self_loan_repayment` | Customer repays their own loan |
| `allocation_repayment` | Repayment from an allocated fund |
| `wallet_repayment` | Repayment from wallet balance |

---

## Database: `bnpl`

Replicated from PostgreSQL via PeerDB. Apply mandatory filters (`_peerdb_is_deleted`, `FINAL`) and timezone rules from the ClickHouse Central KB. **Note:** uses PascalCase column names (e.g. `CreatedAt`, `Status`, `Amount`) — unlike Lipila which uses snake_case.

---

## Key Tables by Channel

### Merchant Channel

#### `public_MerchantTransactions` — loan disbursements (active)
Every loan disbursement attempt is recorded here. All records have `TransactionType = 'loan_disbursement'`.

| Column | Notes |
|---|---|
| `Status` | `successful`, `failed`, `pending` — lowercase |
| `TransactionType` | Always `loan_disbursement` |
| `LoanType` | `mobile_money_loan` or `wallet_loan` |
| `Amount` | Loan principal disbursed |
| `InterestAmount` | Interest charged on this loan — revenue signal |
| `Duration` | Loan term (days or months) |
| `MaturityDate` | When repayment is due |
| `PhoneNumber` | Borrower's mobile number |
| `MerchantId` | Merchant/MFI associated |
| `MouId` | MOU (agreement) reference |
| `CreatedAt` | UTC — display as CAT |

---

#### `public_MerchantRepaymentTransactions` — loan repayments (active)
All loan repayment attempts. All records have `TransactionType = 'loan_repayment'`.

| Column | Notes |
|---|---|
| `Status` | `successful`, `failed` — lowercase |
| `TransactionType` | Always `loan_repayment` |
| `Amount` | Repayment amount |
| `RepaymentType` | `self_loan_repayment`, `allocation_repayment`, `wallet_repayment` |
| `LoanId` | Links to the original loan |
| `MouId` | MOU reference |
| `WalletMovementTransactionId` | Links to wallet movement |

---

#### `public_BnplTransactions` — consolidated loan records (all channels)
Unified view across all channels with lifecycle tracking.

| Column | Notes |
|---|---|
| `Status` | `successful`, `failed`, `pending` |
| `BnplTransactionType` | `mobile_money`, `patumba_wallet_loan`, `lipila_merchant_transfer` |
| `LoanStatus` | `open`, `overdue`, `fully_settled`, `failed` — current state of the loan |
| `Amount` | Loan principal |
| `InitialInterestAmount` | Interest at origination |
| `MaturityDate` | Repayment due date |
| `LipilaId` / `LipilaRecipientMerchantId` | Links to Lipila platform |
| `MemberId` | Borrower member ID |

---

#### `public_RecoveryTransactions` — recovery/collection attempts (merchant channel)

| Column | Notes |
|---|---|
| `Status` | `successful`, `failed`, `pending` |
| `RepaymentType` | `self_loan_repayment`, `allocation_repayment`, `wallet_repayment` |
| `Amount` | Recovery amount |

### Money Lender Channel (Pilot)

#### `public_MoneyLenderLoanTransactions` — money lender loan disbursements
Currently 98 total disbursements (pilot phase). Same structure as merchant channel but separate table for accounting.

**Status columns:**
| Status | Meaning |
|---|---|
| `pending` | Awaiting confirmation |
| `approved` | Approved by lender |
| `disbursed` | Funds sent to borrower |
| `cancelled` | Cancelled before disbursement |
| `rejected` | Application rejected |

#### `public_MoneyLenderRecoveries` — money lender recovery transactions
Collections/recoveries for overdue money lender loans.

| Column | Notes |
|---|---|
| `Status` | `successful`, `failed`, `pending` |
| `RepaymentType` | `self_loan_repayment`, `allocation_repayment`, `wallet_repayment` |
| `Amount` | Recovery amount |

---

### MOU Channel (Not Yet Launched)

Currently 0 transactions. Schema exists but not yet operational.

---

### Supporting Tables (All Channels)

- `public_Microfins` — microfinance institutions (MFIs). Each MFI is dedicated to one channel.
- `public_OrganisationMembers` — borrower members per organisation
- `public_LenderLoanReviews` — loan review/approval records

---

## Loan Statuses (`public_BnplTransactions.LoanStatus`)

| Status | Meaning |
|---|---|
| `open` | Loan active, repayment not yet due |
| `overdue` | Repayment past due date — at risk |
| `fully_settled` | Loan fully repaid |
| `failed` | Disbursement failed — loan never activated |
| `pending` | Awaiting disbursement confirmation |

---

## Transaction Statuses

| Status | Meaning |
|---|---|
| `successful` | Transaction completed |
| `failed` | Transaction rejected or failed |
| `pending` | Awaiting confirmation |

> **Important:** BNPL uses **lowercase** status values (`successful`, `failed`) — unlike Lipila which uses Title Case (`Successful`, `Failed`).

---

## Metric Definitions

### Merchant Channel
| Metric | Definition |
|---|---|
| Mobile Money Loans Disbursed | `COUNT(*)` on `public_MerchantTransactions` where `LoanType='mobile_money_loan'` AND `Status='successful'` |
| Wallet Loans Disbursed | `COUNT(*)` on `public_MerchantTransactions` where `LoanType='wallet_loan'` AND `Status='successful'` |
| Mobile Money Disbursement Success Rate | `COUNT(Status='successful') / COUNT(*)` on `public_MerchantTransactions` where `LoanType='mobile_money_loan'` — see the success-rate note below before using any fixed baseline |
| Wallet Loan Success Rate | `COUNT(Status='successful') / COUNT(*)` on `public_MerchantTransactions` where `LoanType='wallet_loan'` — dormant, see note |
| Total Disbursement Value | `SUM(Amount)` where `Status='successful'` on `public_MerchantTransactions` |
| Interest Income (originated) | `SUM(InterestAmount)` where `Status='successful'` on `public_MerchantTransactions` |
| Loans Repaid | `COUNT(*)` on `public_MerchantRepaymentTransactions` where `Status='successful'` in period |
| Repayment Value | `SUM(Amount)` where `Status='successful'` on `public_MerchantRepaymentTransactions` |
| Recovery Collections | `SUM(Amount)` where `Status='successful'` on `public_RecoveryTransactions` |

### Cross-Channel (Portfolio Health)
| Metric | Definition |
|---|---|
| Open Loans | `LoanStatus='open'` on `public_BnplTransactions` (all channels) |
| Overdue Loans | `LoanStatus='overdue'` on `public_BnplTransactions` (all channels) |
| Fully Settled Loans | `LoanStatus='fully_settled'` on `public_BnplTransactions` (all channels) |
| Failed Disbursements | `LoanStatus='failed'` on `public_BnplTransactions` (all channels) |
| Default Rate | `(overdue_count + failed_count) / (open_count + overdue_count + fully_settled_count + failed_count)` |
| At-Risk Principal | `SUM(Amount)` where `LoanStatus IN ('overdue','open')` on `public_BnplTransactions` |
| Net Loan Flow | Disbursement Value − Repayment Value |

### Money Lender Channel (Pilot)
For Money Lender metrics, use identical filters on `public_MoneyLenderLoanTransactions` and `public_MoneyLenderRecoveries` (98 total transactions). Expected volume is negligible.

---

## Known Quirks

1. **Lowercase status.** BNPL uses lowercase `successful`/`failed`. Do not mix filters with other platforms.
2. **PascalCase columns.** All column names are PascalCase (`CreatedAt`, `Status`, `Amount`) — unlike Lipila's snake_case.
3. **Disbursement success rate is moving fast — do not use a fixed baseline.** Measured 2026-08-02 on `mobile_money_loan`:

   | Window | Attempts | Success rate |
   |---|---|---|
   | Last 7 days | 1,999 | 88.2% |
   | Last 30 days | 10,171 | 81.1% |
   | Last 90 days | 44,220 | 55.0% |
   | Last 180 days | 133,159 | 34.6% |

   The widely-quoted "~35% success / ~65% MNO failure" is the **180-day lifetime average** and is now badly
   out of date — the rail has been recovering steadily. Anchoring on it is actively harmful: it would class a
   fall from 88% to 50% as "normal", when that is a severe regression. Always compare against a recent
   trailing window (7–30 days), and recompute rather than quoting a number from this file.

4. **`wallet_loan` is dormant.** Zero disbursement attempts in the last 180 days (checked 2026-08-02).
   Any historical wallet-loan success rate has no current basis. Treat new wallet-loan volume as a
   notable event rather than routine.
5. **`InterestAmount` is originated interest** — not collected interest. Actual revenue realised depends on repayment. Use repayment transactions for cash-in reporting.
6. **`public_BnplTransactions` links to Lipila.** `LipilaId` and `LipilaRecipientMerchantId` reference the Lipila platform for merchant-routed disbursements.
