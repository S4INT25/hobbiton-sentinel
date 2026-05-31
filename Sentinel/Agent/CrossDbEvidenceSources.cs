namespace Sentinel.Agent;

/// <summary>
/// Defines cross-database evidence sources that the fraud agent can query
/// for corroboration when investigating flagged merchants.
/// </summary>
public static class CrossDbEvidenceSources
{
    public static string BuildPromptBlock()
    {
        return """
               ## Cross-Database Evidence Sources

               Some Lipila merchants are part of the Hobbiton organisation and share the same ClickHouse cluster.
               When investigating these merchants, you MUST cross-reference the linked database for corroboration.
               This gives you a wider view of user behaviour beyond what Lipila alone can show.

               ---

               ### Merchant 35 — Patumba App (Savings & Investments)
               - **Evidence database:** `patumba_app`
               - **Join key:** `lipila_blaze.public_transactions.account_number` = `patumba_app.public_wallet_transactions.billed_account`
               - **Coverage:** ~88% of Lipila merchant 35 accounts exist in Patumba

               **Available tables in patumba_app:**

               `public_wallet_transactions` — User payment transactions
                 - id, external_txn_id, billed_account, payment_method, status, wallet_transaction_type
                 - created_at, updated_at, external_id, transaction_id, amount, created_by_id
                 - credited_account, service_fee, mode, source
                 - Statuses: check with `SELECT DISTINCT status FROM patumba_app.public_wallet_transactions LIMIT 20`
                 - Types: check with `SELECT DISTINCT wallet_transaction_type FROM patumba_app.public_wallet_transactions LIMIT 20`

               `public_wallets` — User wallet ledger (balance history per transaction)
                 - id, transaction_id, open_balance, closing_balance, transaction_type, status
                 - user_id, created_at, amount, mobile_money_balance, other_balance, source

               `public_lipila_wallet_transfers` — Internal wallet transfer records
                 - id, transaction_id, wallet_id, amount, is_debited, is_credited
                 - processed_amount, reference_id, request_id, status, type, payment_type
                 - identifier, reference_data, created_at, transfer_type

               **Evidence checks to run when merchant 35 accounts are flagged:**
               1. **Volume spike:** Compare the flagged account's recent txn count in patumba_app vs their 30-day hourly baseline
               2. **Failed transaction clustering:** High ratio of failed/total in patumba_app for the same billed_account
               3. **Wallet balance anomaly:** Rapid open_balance → closing_balance drops in public_wallets for the user
               4. **Amount mismatch:** Lipila disbursement amount differs significantly from corresponding patumba_app transaction
               5. **Dormant-then-active:** Account with no patumba_app activity for 30+ days suddenly transacting

               ---

               ### Partner 1 — Inshuwa (Insurance Platform)
               Inshuwa is partner_id = 1 on Lipila. It operates through multiple merchants — one per insurer.
               The master merchant is **Hobbiton Insurance** (merchant_id 6, wallet "Inshuwa" id 15 for collections, "Inshuwa Refunds" id 2953 for cancellations).
               Each insurer also has its own Lipila merchant for commission disbursements.

               - **Evidence database:** `inshuwa`
               - **Coverage:** ~86% of Lipila partner-1 accounts exist in Inshuwa PolicyTransactions

               **Insurer ↔ Lipila Merchant mapping** (use this to resolve commission merchants):
               Query `inshuwa.public_InsurerLipilaMerchants` to get the mapping:
               ```
               SELECT InsurerId, MerchantId FROM inshuwa.public_InsurerLipilaMerchants WHERE _peerdb_is_deleted = 0
               ```
               This maps each Inshuwa InsurerId to its Lipila MerchantId. Use it to identify which insurer
               a commission disbursement belongs to.

               **Transaction flows and join keys:**

               | Flow | Lipila filter | Inshuwa table | Join |
               |------|--------------|---------------|------|
               | Premium collections | merchant_id=6, wallet_id=15 | `public_PolicyTransactions` | `account_number` = `BilledAccount` |
               | Cancellation refunds | merchant_id=6, wallet_id=2953 | `public_Payments` (PaymentType='cancellation_refund') | `account_number` = `AccountNumber` |
               | Commission payouts | merchant per insurer (see mapping), wallet name contains 'Commission' | `public_Payments` (PaymentType='commission_pay_out') | `account_number` = `AccountNumber` |

               **Key tables in inshuwa:**

               `public_PolicyTransactions` — Premium payment attempts
                 - Id, ExternalTxnId, Amount, Status, PaymentMethod, BilledAccount
                 - PartnerId, IntermediaryId, InsurerId, Type, PolicyId, RequestId
                 - CreatedAt, UpdatedAt, Currency, Narration, PaymentGateway, ErrorMessage
                 - Status values: success, failed, cancelled, refunded, reversed, pending
                 - Type values: new_business, revision, renewal, extension

               `public_Payments` — Outbound payments (refunds, commissions, settlements)
                 - Id, PxnId, Amount, PaymentMethod, PaymentType, AccountNumber
                 - IntermediaryId, InsurerId, ClientId, PartnerId, CreatedAt, Narration
                 - Status, TransactionId, ExchangeRate, PaymentGateway, ErrorMessage
                 - PaymentType values: cancellation_refund, commission_pay_out, wallet_top_up,
                   commission_reversal, wallet_withdrawal, settlement, internal_wallet_transfer

               `public_Commissions` — Commission records
                 - Id, TransactionId, CommissionRate, IntermediaryId, CommissionStatus
                 - PaymentRequisitionId, PartnerId, Balance, CommissionAmount, TaxAmount, TaxRate

               `public_CommissionSettlements` — Links commissions to payments
                 - Id, CommissionId, PaymentId, CreatedAt

               `public_Insurers` — Insurer directory
                 - Id, Name

               `public_InsurerLipilaMerchants` — Maps InsurerId to Lipila MerchantId
                 - Id, InsurerId, MerchantId, LipilaType, Businesses, Wallets

               **Evidence checks to run for Inshuwa-related merchants:**
               1. **Premium vs disbursement mismatch:** Lipila collection on wallet 15 has no matching PolicyTransaction in inshuwa (ghost collection)
               2. **Refund without policy cancellation:** Lipila disbursement from wallet 2953 has no corresponding cancelled policy in inshuwa
               3. **Commission amount validation:** Commission payout on Lipila vs CommissionAmount in inshuwa.public_Commissions — flag large discrepancies
               4. **Failed premium clustering:** High failure rate for a BilledAccount in inshuwa.public_PolicyTransactions may indicate testing/probing
               5. **Unusual insurer activity:** An insurer's commission wallet suddenly disbursing to accounts not in their policy portfolio

               ---

               ## Rules for ALL cross-DB evidence
               - Use ONLY for corroboration — never as a primary detection source
               - Always fully qualify tables: `patumba_app.<table>`, `inshuwa.<table>`
               - If evidence supports a Lipila finding, note "Corroborated by <db> data" and increase confidence
               - If evidence contradicts (e.g. account has legitimate policy history), note it as reducing suspicion
               - Timing tolerance: ±5 minutes when matching transactions across databases
               - Do NOT run cross-DB checks on every account — only on accounts already flagged by Lipila detection
               - Column names in inshuwa are PascalCase (e.g. `BilledAccount`, `AccountNumber`, `InsurerId`)
               - Column names in patumba_app are snake_case (e.g. `billed_account`, `wallet_transaction_type`)
               """;
    }
}
