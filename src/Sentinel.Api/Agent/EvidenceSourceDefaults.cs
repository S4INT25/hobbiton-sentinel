using Sentinel.Admin.Models;

namespace Sentinel.Agent;

/// <summary>
/// Default evidence source definitions to seed into the store.
/// These were verified against live ClickHouse data.
/// </summary>
public static class EvidenceSourceDefaults
{
    public static List<EvidenceSource> GetDefaults() =>
    [
        new EvidenceSource
        {
            Id = 1,
            Name = "Patumba App (Savings & Investments)",
            EvidenceDatabase = "patumba_app",
            LipilaMerchantIds = "35",
            LipilaPartnerId = 0,
            JoinMappings = """
                           [
                               {
                                   "lipilaTable": "public_transactions",
                                   "lipilaColumn": "account_number",
                                   "evidenceTable": "public_wallet_transactions",
                                   "evidenceColumn": "billed_account"
                               }
                           ]
                           """,
            TableDescriptions = """
                                `public_wallet_transactions` — User payment transactions
                                  - id, external_txn_id, billed_account, payment_method, status, wallet_transaction_type
                                  - created_at, updated_at, external_id, transaction_id, amount, created_by_id
                                  - credited_account, service_fee, mode, source

                                `public_wallets` — User wallet ledger (balance history per transaction)
                                  - id, transaction_id, open_balance, closing_balance, transaction_type, status
                                  - user_id, created_at, amount, mobile_money_balance, other_balance, source

                                `public_lipila_wallet_transfers` — Internal wallet transfer records
                                  - id, transaction_id, wallet_id, amount, is_debited, is_credited
                                  - processed_amount, reference_id, request_id, status, type, payment_type
                                  - identifier, reference_data, created_at, transfer_type
                                """,
            EvidenceChecks = """
                             [
                                 "Volume spike: Compare the flagged account's recent txn count in patumba_app vs their 30-day hourly baseline",
                                 "Failed transaction clustering: High ratio of failed/total in patumba_app for the same billed_account",
                                 "Wallet balance anomaly: Rapid open_balance to closing_balance drops in public_wallets for the user",
                                 "Amount mismatch: Lipila disbursement amount differs significantly from corresponding patumba_app transaction",
                                 "Dormant-then-active: Account with no patumba_app activity for 30+ days suddenly transacting"
                             ]
                             """,
            Notes = """
                    Coverage: ~88% of Lipila merchant 35 accounts exist in Patumba.
                    Column names are snake_case (e.g. billed_account, wallet_transaction_type).
                    Patumba is a savings/investment platform — users deposit regularly into savings goals.
                    Unusual patterns include: sudden large withdrawals, dormant accounts reactivating, or
                    transaction volumes far exceeding historical norms for that user.
                    """,
            Enabled = true,
            CreatedBy = "system"
        },

        new EvidenceSource
        {
            Id = 2,
            Name = "Inshuwa (Insurance Platform)",
            EvidenceDatabase = "inshuwa",
            LipilaMerchantIds =
                "6,189,804,805,806,807,808,809,810,811,812,813,814,815,816,817,818,819,820,821,822,823,824,825,826,827,828",
            LipilaPartnerId = 1,
            JoinMappings = """
                           [
                               {
                                   "lipilaTable": "public_transactions",
                                   "lipilaColumn": "account_number",
                                   "evidenceTable": "public_PolicyTransactions",
                                   "evidenceColumn": "BilledAccount",
                                   "context": "Premium collections (merchant_id=6, wallet_id=15)"
                               },
                               {
                                   "lipilaTable": "public_transactions",
                                   "lipilaColumn": "account_number",
                                   "evidenceTable": "public_Payments",
                                   "evidenceColumn": "AccountNumber",
                                   "context": "Cancellation refunds (merchant_id=6, wallet_id=2953) and commission payouts (per-insurer merchant)"
                               }
                           ]
                           """,
            TableDescriptions = """
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
                                """,
            EvidenceChecks = """
                             [
                                 "Premium vs disbursement mismatch: Lipila collection on wallet 15 has no matching PolicyTransaction in inshuwa (ghost collection)",
                                 "Refund without policy cancellation: Lipila disbursement from wallet 2953 has no corresponding cancelled policy in inshuwa",
                                 "Commission amount validation: Commission payout on Lipila vs CommissionAmount in inshuwa.public_Commissions — flag large discrepancies",
                                 "Failed premium clustering: High failure rate for a BilledAccount in inshuwa.public_PolicyTransactions may indicate testing/probing",
                                 "Unusual insurer activity: An insurer's commission wallet suddenly disbursing to accounts not in their policy portfolio"
                             ]
                             """,
            Notes = """
                    Coverage: ~86% of Lipila partner-1 accounts exist in Inshuwa PolicyTransactions.
                    Column names are PascalCase (e.g. BilledAccount, AccountNumber, InsurerId).

                    Inshuwa is partner_id=1 on Lipila. It operates through multiple merchants — one per insurer.
                    Master merchant: Hobbiton Insurance (merchant_id 6):
                      - Wallet "Inshuwa" (id 15) = premium collections
                      - Wallet "Inshuwa Refunds" (id 2953) = cancellation refunds

                    Each insurer has its own Lipila merchant for commission disbursements.
                    Use `inshuwa.public_InsurerLipilaMerchants` to resolve InsurerId <-> Lipila MerchantId.
                    Commission wallets are named "Inshuwa-Commission-Withdraw-Wallet" on the per-insurer merchant.

                    Key insurer merchants: 821=African Grey, 813=Phoenix, 804=General Alliance, 189=Madison.
                    """,
            Enabled = true,
            CreatedBy = "system"
        }
    ];
}