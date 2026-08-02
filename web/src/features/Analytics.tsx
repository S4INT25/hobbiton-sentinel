import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { api, type AnalyticsPanel, type PanelComparison } from '../api';
import { DataChart, DataTable } from '../components/charts';
import { PageHeader, Spinner, Tabs, selectCls, fmtDateFull } from '../components/ui';

const WINDOWS = [7, 30, 90, 180];

/** Sums a numeric column — used for the headline figure above a trend panel. */
function columnTotal(panel: AnalyticsPanel, column: string): number | null {
  if (!panel.rows.length) return null;
  let total = 0;
  for (const row of panel.rows) {
    const n = parseFloat((row[column] ?? '').replace(/,/g, ''));
    if (!Number.isFinite(n)) return null;
    total += n;
  }
  return total;
}

const compact = (n: number) =>
  Math.abs(n) >= 1_000_000 ? `${(n / 1_000_000).toFixed(2)}M`
  : Math.abs(n) >= 1_000 ? `${(n / 1_000).toFixed(1)}k`
  : n.toFixed(2);

const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
const ISO_DATE = /^(\d{4})-(\d{2})-(\d{2})$/;

/**
 * Shortens ISO day labels to "26 Jul" for the x-axis. Full dates get truncated to
 * "2026-07-..." at chart width, which tells the reader nothing.
 */
function shortenDateLabels(columns: string[], rows: Record<string, string>[]) {
  const key = columns[0];
  if (!rows.some((r) => ISO_DATE.test(r[key] ?? ''))) return rows;
  return rows.map((r) => {
    const m = ISO_DATE.exec(r[key] ?? '');
    return m ? { ...r, [key]: `${Number(m[3])} ${MONTHS[Number(m[2]) - 1]}` } : r;
  });
}

/**
 * Period-over-period delta. Rendered neutral rather than green/red: "up" is good for revenue
 * and bad for withdrawals, and the component cannot tell which it is holding. Colour here would
 * be a judgement the data does not support — the arrow states direction, the reader judges.
 */
function Delta({ cmp }: { cmp: PanelComparison }) {
  if (cmp.changePercent === null) {
    // Previous period was zero, so a percentage would be invented. Distinguish "went from
    // nothing to something" from "nothing in either period" — calling the latter "new" is a lie.
    const label = cmp.current === 0 ? 'no activity' : 'new';
    const title = cmp.current === 0
      ? 'Nothing in this period or the previous one'
      : 'No activity in the previous period';
    return <div className="font-mono text-[10px] text-gray-500 mt-0.5" title={title}>{label}</div>;
  }
  const pct = cmp.changePercent;
  const arrow = pct > 0 ? '↑' : pct < 0 ? '↓' : '→';
  // Sub-0.05% rounds to "0.0%", which reads as a change that did not happen.
  const shown = Math.abs(pct) < 0.05 ? 'flat' : `${arrow} ${Math.abs(pct).toFixed(1)}%`;
  return (
    <div
      className="font-mono text-[10px] text-gray-500 mt-0.5"
      title={`Previous period: ${compact(cmp.previous)}`}
    >
      {shown}
    </div>
  );
}

function PanelCard({ panel }: { panel: AnalyticsPanel }) {
  const span = panel.span >= 2 ? 'lg:col-span-2' : '';

  // Trend panels get their period total called out — a line chart alone makes you
  // squint at the axis to answer "so what was it overall?".
  const totals =
    panel.error || panel.chart === 'table' || panel.chart === 'donut' || !panel.columns.length
      ? []
      : panel.columns
          .slice(1)
          .map((c) => ({ column: c, total: columnTotal(panel, c) }))
          .filter((t): t is { column: string; total: number } => t.total !== null);

  return (
    <div className={`panel p-4 ${span}`}>
      <div className="flex flex-wrap items-start justify-between gap-x-4 gap-y-2 mb-3">
        <div className="min-w-0 flex-1 basis-40">
          <h3 className="font-display text-sm font-medium text-white">{panel.title}</h3>
          {panel.note && <p className="text-[11px] text-gray-600 mt-0.5 leading-relaxed">{panel.note}</p>}
        </div>
        {totals.length > 0 && (
          <div className="flex items-center gap-4 shrink-0">
            {totals.map((t) => {
              const cmp = panel.comparisons?.find((c) => c.column === t.column);
              return (
                <div key={t.column} className="text-right">
                  <div className="kicker">{t.column.replace(/_/g, ' ')}</div>
                  <div className="font-display text-base font-semibold text-white tnum">{compact(t.total)}</div>
                  {cmp && <Delta cmp={cmp} />}
                </div>
              );
            })}
          </div>
        )}
      </div>

      {panel.error ? (
        <div className="rounded-md border border-rose-500/30 bg-rose-500/5 p-3">
          <p className="font-mono text-[10px] uppercase tracking-wider text-rose-400 mb-1">Query failed</p>
          <p className="font-mono text-[11px] text-gray-400 break-words leading-relaxed">{panel.error}</p>
        </div>
      ) : panel.rows.length === 0 ? (
        <p className="text-xs text-gray-600 py-8 text-center">No data in this window.</p>
      ) : panel.chart === 'table' ? (
        <DataTable columns={panel.columns} rows={panel.rows} maxRows={10} />
      ) : (
        <DataChart
          chartType={panel.chart}
          columns={panel.columns}
          rows={shortenDateLabels(panel.columns, panel.rows)}
          height={260}
        />
      )}
    </div>
  );
}

export default function Analytics() {
  const [platform, setPlatform] = useState('lipila');
  const [days, setDays] = useState(30);

  const { data: platforms = [] } = useQuery({
    queryKey: ['analytics-platforms'],
    queryFn: api.analyticsPlatforms,
    staleTime: Infinity,
  });

  const { data, isLoading, isFetching } = useQuery({
    queryKey: ['analytics-dashboard', platform, days],
    queryFn: () => api.analyticsDashboard(platform, days),
    // Panels are cached server-side for 10 minutes; refetching faster just burns
    // ClickHouse time for the same numbers.
    staleTime: 5 * 60 * 1000,
  });

  const failed = data?.panels.filter((p) => p.error).length ?? 0;

  return (
    <div className="space-y-5 px-4 lg:px-16">
      {/* Count comes from the data — a hardcoded "four platforms" went stale the moment Gari landed. */}
      <PageHeader
        title="Analytics"
        subtitle={platforms.length ? `Business metrics across ${platforms.length} platforms` : 'Business metrics'}
      >
        <select value={days} onChange={(e) => setDays(Number(e.target.value))} className={selectCls}>
          {WINDOWS.map((d) => <option key={d} value={d}>Last {d} days</option>)}
        </select>
      </PageHeader>

      {platforms.length > 0 && (
        <Tabs
          tabs={platforms.map((p) => ({ key: p.key, label: p.label }))}
          active={platform}
          onChange={setPlatform}
        />
      )}

      {isLoading && <div className="flex justify-center py-16"><Spinner /></div>}

      {data && (
        <>
          {failed > 0 && (
            <div className="rounded-lg border border-amber-500/30 bg-amber-500/5 px-3 py-2 text-xs text-amber-300">
              {failed} of {data.panels.length} panels failed to load — see the error on each card.
            </div>
          )}

          <div className="grid grid-cols-1 lg:grid-cols-2 gap-3" data-stagger>
            {data.panels.map((p) => <PanelCard key={p.id} panel={p} />)}
          </div>

          <p className="font-mono text-[10px] text-gray-600 pt-1">
            {data.database} · {data.days} complete days · deltas vs the previous {data.days} days
            · generated {fmtDateFull(data.generatedAt)}
            {isFetching && ' · refreshing…'}
          </p>
        </>
      )}
    </div>
  );
}
