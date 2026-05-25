using OpenAI.Chat;

namespace Sentinel.Agent;

public static class FraudAgentTools
{
    public static IList<ChatTool> GetToolDefinitions() =>
    [
        ChatTool.CreateFunctionTool(
            functionName: "run_sql",
            functionDescription: """
                Execute one or more read-only ClickHouse queries.
                ALWAYS use the "queries" array — even for a single query.
                All queries in the array run in parallel and results are returned together, labeled by index.
                This saves iterations. Never call run_sql multiple times when you can batch them here.
                Always include LIMIT on each query.
                """,
            functionParameters: BinaryData.FromString("""
                {
                    "type": "object",
                    "properties": {
                        "queries": {
                            "type": "array",
                            "items": { "type": "string" },
                            "description": "One or more ClickHouse SQL queries to run in parallel. Always use this — even for a single query. Include LIMIT on each."
                        }
                    },
                    "required": ["queries"]
                }
                """)
        ),

        ChatTool.CreateFunctionTool(
            functionName: "create_case",
            functionDescription: """
                Create a new fraud case to track a suspicious pattern across future runs.
                Use this when you find something suspicious that needs ongoing monitoring.
                The case will be loaded automatically in future hourly runs so you can follow up.
                """,
            functionParameters: BinaryData.FromString("""
                {
                    "type": "object",
                    "properties": {
                        "title": {
                            "type": "string",
                            "description": "Short descriptive title e.g. 'Bulk disbursements from merchant 6 to 27 recipients'"
                        },
                        "category": {
                            "type": "string",
                            "enum": ["ghost_tx", "unknown_ip", "bulk_disbursement", "unverified_merchant", "known_recipient", "admin_compromise", "api_key_abuse", "pattern"],
                            "description": "Category of fraud pattern"
                        },
                        "severity": {
                            "type": "string",
                            "enum": ["low", "medium", "high", "critical"]
                        },
                        "notes": {
                            "type": "string",
                            "description": "Your analyst notes — what you found, why it's suspicious, what to watch for"
                        },
                        "affected_entities": {
                            "type": "array",
                            "items": { "type": "string" },
                            "description": "List of affected merchant IDs, IPs, wallet IDs, phone numbers, emails"
                        },
                        "follow_up_queries": {
                            "type": "array",
                            "items": { "type": "string" },
                            "description": "SQL queries to run in future runs to check if this pattern continues"
                        }
                    },
                    "required": ["title", "category", "severity", "notes"]
                }
                """)
        ),

        ChatTool.CreateFunctionTool(
            functionName: "update_case",
            functionDescription: """
                Update an existing open case with new findings from this run.
                Use this when a previously flagged pattern appears again or has escalated.
                """,
            functionParameters: BinaryData.FromString("""
                {
                    "type": "object",
                    "properties": {
                        "case_id": {
                            "type": "string",
                            "description": "The case ID (e.g. 'A3F2B1C9')"
                        },
                        "notes": {
                            "type": "string",
                            "description": "New findings to append to this case"
                        },
                        "severity": {
                            "type": "string",
                            "enum": ["low", "medium", "high", "critical"],
                            "description": "Updated severity if it has changed"
                        },
                        "status": {
                            "type": "string",
                            "enum": ["open", "escalated", "watching"],
                            "description": "Update status if needed"
                        },
                        "follow_up_queries": {
                            "type": "array",
                            "items": { "type": "string" },
                            "description": "Updated or additional follow-up queries"
                        }
                    },
                    "required": ["case_id", "notes"]
                }
                """)
        ),

        ChatTool.CreateFunctionTool(
            functionName: "resolve_case",
            functionDescription: """
                Mark a case as resolved when the suspicious pattern has stopped
                or been confirmed as a false positive.
                """,
            functionParameters: BinaryData.FromString("""
                {
                    "type": "object",
                    "properties": {
                        "case_id": {
                            "type": "string"
                        },
                        "resolution": {
                            "type": "string",
                            "description": "Why this case is resolved e.g. 'Pattern stopped after API key rotation on 2026-05-21'"
                        }
                    },
                    "required": ["case_id", "resolution"]
                }
                """)
        ),

        ChatTool.CreateFunctionTool(
            functionName: "send_alert",
            functionDescription: """
                Send an email alert with your fraud investigation findings.
                Only call this when warranted — do NOT send on clean runs with no findings and no open cases.

                Call this when ANY of the following is true:
                - Severity is Warning or Critical (fraud findings exist)
                - There are open cases needing attention
                - A case was resolved this run (send one final notification)

                If the run is fully clean with no findings and no open cases, do NOT call this tool.

                ════════════════════════════════════════
                MANDATORY REPORT STRUCTURE — follow exactly whenever you do send:
                ════════════════════════════════════════

                ## Run Summary
                | Field        | Value                                      |
                |---|---|
                | Run ID       | <run_id>                                   |
                | Period       | Last <N> minutes ending <YYYY-MM-DD HH:MM> CAT |
                | Severity     | 🟢 Clean / 🟡 Watching / 🟠 Warning / 🔴 Critical |
                | Findings     | <N> fraud findings, <N> observations        |
                | Open Cases   | <N> active cases                           |

                One or two sentences describing the overall picture.

                ---

                ## Fraud Findings
                If none: write "No fraud patterns detected this run."
                Otherwise, for EACH finding use this block — no exceptions:

                ### Finding <N>: <Short Title>
                | Field       | Detail |
                |---|---|
                | Pattern     | <which of the 11 patterns this matches> |
                | Severity    | 🔴 Critical / 🟠 High / 🟡 Medium / 🟢 Low |
                | Merchant    | <name> (ID: <id>) |
                | Wallet      | <name> (ID: <id>, Balance: ZMW <amount>) |
                | Period      | <start datetime> → <end datetime> CAT |
                | Volume      | ZMW <amount> across <N> transactions |

                **What happened:** One paragraph explaining what was found.

                **Evidence:**
                | Timestamp (CAT) | Amount (ZMW) | Recipient | Status | Reference |
                |---|---|---|---|---|
                | <time> | <amount> | `<number>` | <status> | `<ref>` |

                **Why suspicious:** Bullet points of specific reasons.

                **Case:** Created / Updated / Watching — Case ID `<id>`

                ---

                ## Interesting Observations
                If none: write "No notable observations this run."
                Otherwise use numbered list. For each observation:

                **<N>. <Title>**
                - **Merchant:** <name> (ID: <id>)
                - **Detail:** What was observed, with numbers
                - **Significance:** Why it matters or what to watch

                ---

                ## Open Cases
                If none: write "No open cases."
                Otherwise:

                | Case ID | Title | Severity | Status | Age | Last Seen |
                |---|---|---|---|---|---|
                | `<id>` | <title> | <sev> | <status> | <N> runs | <date> CAT |

                For EACH open case add one line: "→ This run: <what happened — pattern continued / escalated / quiet>"

                ---

                ## Recommended Actions
                If nothing to do: write "No actions required."
                Otherwise numbered list, ordered by priority.

                These are SUGGESTIONS based on observed patterns — not confirmed facts.
                Each action must acknowledge uncertainty and avoid stating fraud as definite.
                Use language like: "consider reviewing", "may be worth investigating",
                "if confirmed fraudulent", "recommended if pattern continues".

                1. [URGENT] <suggested action> — <who> — <why this may be needed>
                2. [HIGH] <suggested action> — <who> — <why>
                3. [MEDIUM] <suggested action> — <who> — <why>

                Always end the section with this note:
                > These recommendations are based on automated pattern detection and may include false positives. Human review is required before taking any action.

                ════════════════════════════════════════
                FORMATTING RULES — non-negotiable:
                ════════════════════════════════════════
                - No emojis anywhere in the report — not in headings, labels, bullets, or anywhere else
                - Every table must have a header row AND a separator row (|---|---|)
                - Use **bold** for: merchant names, ZMW amounts, IP addresses, account numbers
                - Use `backticks` for: reference IDs, case IDs, phone numbers, SQL identifiers
                - Timestamps always in CAT (UTC+2), format: YYYY-MM-DD HH:MM CAT
                - ZMW amounts always include comma separators: ZMW 1,234,567.00
                - Never write walls of text — break into tables and bullets
                - Section dividers: use --- between major sections
                - Never skip a required section — write the empty-state text instead
                ════════════════════════════════════════
                """,
            functionParameters: BinaryData.FromString("""
                {
                    "type": "object",
                    "properties": {
                        "subject": {
                            "type": "string",
                            "description": "Short subject line e.g. '3 suspicious disbursements detected' or 'All clear - no anomalies'"
                        },
                        "body": {
                            "type": "string",
                            "description": "Full investigation report in markdown format as described above."
                        },
                        "severity": {
                            "type": "string",
                            "enum": ["clean", "watching", "warning", "critical"],
                            "description": "Overall severity of this run"
                        }
                    },
                    "required": ["subject", "body", "severity"]
                }
                """)
        ),

        ChatTool.CreateFunctionTool(
            functionName: "lookup_ip",
            functionDescription: """
                Look up geolocation and threat intelligence for an IP address.
                Returns country, region, ISP, organisation, ASN, and flags for
                proxy, VPN, and hosting/datacenter origin.

                Only call this for IPs you have already identified as suspicious or
                worth investigating — not for every IP you encounter.
                You can pass multiple IPs in one call (up to 10).
                """,
            functionParameters: BinaryData.FromString("""
                {
                    "type": "object",
                    "properties": {
                        "ips": {
                            "type": "array",
                            "items": { "type": "string" },
                            "description": "List of IPv4 or IPv6 addresses to look up (max 10).",
                            "maxItems": 10
                        }
                    },
                    "required": ["ips"]
                }
                """)
        )
    ];
}
