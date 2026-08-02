# ClickHouse — Central Knowledge Base

All Hobbiton fintech databases (patumba_app, inshuwa, lipila_blaze, bnpl) are replicated from PostgreSQL to ClickHouse via PeerDB for analytics. This document covers behaviors that apply to every database. Platform-specific KBs do not repeat this material.

---

## Architecture

| Layer | Detail |
|---|---|
| Source | PostgreSQL — live operational databases |
| Replication | PeerDB — continuous CDC (change data capture) |
| Destination | ClickHouse — analytics layer (read-only) |
| Table engine | ReplacingMergeTree on all replicated tables |

---

## Mandatory Filters — Every Query, Every Database

| Filter | Why |
|---|---|
| `WHERE _peerdb_is_deleted = 0` | PeerDB marks deleted Postgres rows with this flag instead of removing them. Omitting it silently includes logically deleted records in all counts and sums. |
| `FINAL` (on recent data) | Forces deduplication of unmerged duplicate parts. See below. |

Both filters are required. Missing either produces wrong results silently.

---

## ReplacingMergeTree — How Deduplication Works

ClickHouse uses **ReplacingMergeTree** for all replicated tables. Key behaviors:

- **Deduplication is lazy.** ClickHouse merges duplicate parts in the background — not immediately on insert. At any moment, multiple versions of the same row may coexist across unmerged parts.
- **`_peerdb_version` is the version column.** On each PostgreSQL update, PeerDB increments this value. During background merge, ClickHouse retains only the row with the highest `_peerdb_version` per primary key.
- **Raw COUNT is inflated.** Without `FINAL`, a query counts 2–50+ phantom rows per actively-updated table. This is normal and expected.

**Example:** A table with 503,385 rows in PostgreSQL returns 503,434 in a raw ClickHouse count — because 49 unmerged duplicate parts haven't been collapsed yet by the background merge. This is not a data error; it resolves after the next background merge cycle.

---

## FINAL Keyword

```sql
SELECT COUNT(*) FROM public_transactions FINAL WHERE _peerdb_is_deleted = 0
```

`FINAL` forces ClickHouse to deduplicate at query time, keeping only the latest `_peerdb_version` per primary key.

**Use it when:**
- Doing exact row counts
- Querying data updated within the last few hours
- Count precision matters more than speed

**Trade-off:** `FINAL` is slower on large tables — it reads and processes all parts. For aggregation queries on historical data (e.g. totals for last 7 days on a settled table) where a few phantom rows don't move the needle, omitting `FINAL` is acceptable.

---

## Replication Timing Gap

After applying `FINAL`, a 1-row difference between ClickHouse and PostgreSQL is normal. This is a replication timing artifact: PeerDB may have captured a row inserted or updated between the two queries. A gap of 1–5 rows is noise. A gap in the hundreds or thousands warrants investigation.

---

## Timestamps — UTC Storage, CAT Display

All timestamps in all replicated databases are stored in UTC. All reports and user-facing output must display times in **CAT (UTC+2, Africa/Lusaka)**.

```sql
toTimezone(created_at, 'Africa/Lusaka')
```

Each platform uses different column name casing — `created_at` (snake_case in patumba_app, lipila_blaze) vs `CreatedAt` (PascalCase in inshuwa, bnpl). Always check the platform KB for the correct column name.
