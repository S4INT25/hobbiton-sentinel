using OpenAI.Chat;

namespace Sentinel.Agent;

public static class AnalyticsAgentTools
{
    public static IList<ChatTool> GetToolDefinitions() =>
    [
        ChatTool.CreateFunctionTool(
            functionName: "run_sql",
            functionDescription: """
                Execute one or more read-only ClickHouse queries.
                Use the "queries" array — even for a single query. Results come back labeled by index.
                All queries run in parallel. Always include LIMIT.
                """,
            functionParameters: BinaryData.FromString("""
                {
                    "type": "object",
                    "properties": {
                        "queries": {
                            "type": "array",
                            "items": { "type": "string" },
                            "description": "One or more ClickHouse SQL queries to run in parallel. Include LIMIT on each."
                        }
                    },
                    "required": ["queries"]
                }
                """)
        ),

        ChatTool.CreateFunctionTool(
            functionName: "get_schema",
            functionDescription: """
                Load the full schema (tables, columns, types, categorical values) for a database.
                Use when you need to explore a database not already loaded, or need to refresh your knowledge.
                """,
            functionParameters: BinaryData.FromString("""
                {
                    "type": "object",
                    "properties": {
                        "database": {
                            "type": "string",
                            "description": "The ClickHouse database name e.g. 'lipila_blaze', 'inshuwa'"
                        }
                    },
                    "required": ["database"]
                }
                """)
        ),

        ChatTool.CreateFunctionTool(
            functionName: "describe_table",
            functionDescription: """
                Get detailed schema for a specific table — columns, types, categorical values, row count.
                Use when you need specifics about one table rather than the full database schema.
                """,
            functionParameters: BinaryData.FromString("""
                {
                    "type": "object",
                    "properties": {
                        "database": {
                            "type": "string",
                            "description": "The ClickHouse database name"
                        },
                        "table": {
                            "type": "string",
                            "description": "The table name e.g. 'public_transactions'"
                        }
                    },
                    "required": ["database", "table"]
                }
                """)
        ),

        ChatTool.CreateFunctionTool(
            functionName: "emit_chart",
            functionDescription: """
                Emit chart data for the UI to render. Use this when you have query results that would
                benefit from visual representation. Decide the best chart type based on the data shape.
                Only call this when visualization adds value — don't chart everything.
                """,
            functionParameters: BinaryData.FromString("""
                {
                    "type": "object",
                    "properties": {
                        "chart_type": {
                            "type": "string",
                            "enum": ["bar", "line", "area", "pie", "donut", "scatter", "radar", "heatmap", "treemap", "radialbar", "none"],
                            "description": "The chart type that best represents this data"
                        },
                        "title": {
                            "type": "string",
                            "description": "Short chart title for display"
                        },
                        "sql": {
                            "type": "string",
                            "description": "The SQL query that produced this data (for reference)"
                        },
                        "columns": {
                            "type": "array",
                            "items": { "type": "string" },
                            "description": "Column names from the query result"
                        },
                        "rows": {
                            "type": "array",
                            "items": { "type": "object" },
                            "description": "Array of row objects (column_name: value)"
                        }
                    },
                    "required": ["chart_type", "title", "columns", "rows"]
                }
                """)
        ),

        ChatTool.CreateFunctionTool(
            functionName: "send_report",
            functionDescription: """
                Send a professional email report with your analysis findings. This will be read by management
                and stakeholders — make it clean, polished, and executive-ready.
                In chat mode, only use this when the user explicitly asks to send an email/report.
                 
                Use template "fraud_alert" for security/fraud findings (include severity, evidence, recommendations).
                Use template "insights" for general analytics/business intelligence reports.
                Use template "custom" for any other structured report.
                
                The body must be well-formatted markdown suitable for a professional audience:
                - Start with a brief executive summary (2-3 sentences)
                - Use tables for metrics with properly formatted numbers
                - Include section headers for scannability
                - End with actionable recommendations if applicable
                - Keep it concise — respect the reader's time
                """,
            functionParameters: BinaryData.FromString("""
                {
                    "type": "object",
                    "properties": {
                        "template": {
                            "type": "string",
                            "enum": ["fraud_alert", "insights", "custom"],
                            "description": "The email template style to use"
                        },
                        "subject": {
                            "type": "string",
                            "description": "Email subject line"
                        },
                        "body": {
                            "type": "string",
                            "description": "Full report body in markdown format. Structure it yourself — include whatever sections make sense for this report."
                        },
                        "severity": {
                            "type": "string",
                            "enum": ["clean", "watching", "warning", "critical"],
                            "description": "Overall severity/urgency of this report"
                        },
                        "recipients": {
                            "type": "array",
                            "items": { "type": "string" },
                            "description": "Email addresses to send to. If empty, uses the default configured recipient."
                        }
                    },
                    "required": ["template", "subject", "body", "severity"]
                }
                """)
        ),

        ChatTool.CreateFunctionTool(
            functionName: "save_memory",
            functionDescription: """
                Save a durable business definition to the agent knowledge base.
                Use this when the user explicitly defines a metric/term or asks you to remember a rule for future analysis.
                Do NOT save transient one-off preferences or temporary run context.
                """,
            functionParameters: BinaryData.FromString("""
                {
                    "type": "object",
                    "properties": {
                        "term": {
                            "type": "string",
                            "description": "Business term name, e.g. 'Revenue'"
                        },
                        "definition": {
                            "type": "string",
                            "description": "Precise rule/definition the agent should reuse in future answers"
                        },
                        "database": {
                            "type": "string",
                            "description": "Optional database scope. Omit or empty to apply to all databases."
                        }
                    },
                    "required": ["term", "definition"]
                }
                """)
        ),

        ChatTool.CreateFunctionTool(
            functionName: "ask_user",
            functionDescription: """
                Ask the user a clarifying question when you need more information to proceed.
                Provide choices when possible for faster interaction. Only works in interactive mode —
                in autonomous/workflow mode this tool is skipped and you should make reasonable defaults.
                
                Use this when:
                - The question is ambiguous (which database? which time period?)
                - Multiple valid approaches exist and you want user preference
                - You need confirmation before a potentially expensive operation
                """,
            functionParameters: BinaryData.FromString("""
                {
                    "type": "object",
                    "properties": {
                        "question": {
                            "type": "string",
                            "description": "The question to ask the user"
                        },
                        "choices": {
                            "type": "array",
                            "items": { "type": "string" },
                            "description": "Optional list of choices. Provide when possible for faster UX."
                        }
                    },
                    "required": ["question"]
                }
                """)
        )
    ];
}
