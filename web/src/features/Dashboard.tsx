import { Link } from 'react-router-dom';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { api } from '../api';
import { PageHeader, Spinner, SeverityBadge, StatusBadge, btnPrimary, fmtDate, tableWrap, thCls, tdCls } from '../components/ui';

export default function Dashboard() {
  const qc = useQueryClient();
  const { data, isLoading } = useQuery({
    queryKey: ['dashboard'],
    queryFn: api.dashboard,
    refetchInterval: 5000,
  });

  const triggerMut = useMutation({
    mutationFn: api.triggerRun,
    onSuccess: () => qc.invalidateQueries({ queryKey: ['dashboard'] }),
  });

  if (isLoading || !data) return <div className="flex justify-center py-12"><Spinner /></div>;

  const { cases, rules, runs, activeRuns, workflows } = data;
  const critical = cases.filter((c) => c.severity === 'critical').length;
  const high = cases.filter((c) => c.severity === 'high').length;
  const recentRuns = runs.slice(0, 8);
  const enabledWorkflows = workflows.filter((w) => w.enabled && !w.isDeleted);
  const isRunning = activeRuns.length > 0;

  const stat = 'panel panel-hover p-4 block relative overflow-hidden';
  const statLabel = 'kicker';
  const statValue = 'font-display text-2xl font-semibold text-white mt-1.5 tnum';
  const statHint = 'font-mono text-[10px] text-gray-600 mt-1.5';

  return (
    <div className="space-y-6 px-4 lg:px-16" data-stagger>
      <PageHeader title="Dashboard" subtitle="Fraud monitoring at a glance">
        <button onClick={() => triggerMut.mutate()} disabled={triggerMut.isPending || isRunning} className={btnPrimary}>
          {isRunning ? 'Run in progress…' : triggerMut.isPending ? 'Starting…' : 'Trigger fraud sweep'}
        </button>
      </PageHeader>

      {/* Stat cards */}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-3">
        <Link to="/cases" className={stat}>
          {critical > 0 && <span aria-hidden className="absolute inset-x-0 top-0 h-px bg-gradient-to-r from-transparent via-rose-500/60 to-transparent" />}
          <div className={statLabel}>Open cases</div>
          <div className={statValue}>{cases.length}</div>
          <div className={statHint}>
            {critical > 0 && <span className="text-rose-400">{critical} critical</span>}
            {critical > 0 && high > 0 && ' · '}
            {high > 0 && <span className="text-orange-400">{high} high</span>}
            {critical === 0 && high === 0 && 'no critical or high'}
          </div>
        </Link>
        <Link to="/rules" className={stat}>
          <div className={statLabel}>Active rules</div>
          <div className={statValue}>{rules.length}</div>
          <div className={statHint}>suppression &amp; downgrade</div>
        </Link>
        <Link to="/workflows" className={stat}>
          <div className={statLabel}>Workflows</div>
          <div className={statValue}>{enabledWorkflows.length}</div>
          <div className={statHint}>scheduled &amp; enabled</div>
        </Link>
        <Link to="/runs" className={stat}>
          {isRunning && <span aria-hidden className="absolute inset-x-0 top-0 h-px bg-gradient-to-r from-transparent via-emerald-500/60 to-transparent" />}
          <div className={`${statLabel} flex items-center gap-1.5`}>
            Active runs
            {isRunning && <span className="glow-dot" style={{ height: 5, width: 5 }} />}
          </div>
          <div className={statValue}>{activeRuns.length}</div>
          <div className={statHint}>{runs.length} in history</div>
        </Link>
      </div>

      {/* Open cases table */}
      <div>
        <div className="flex items-center justify-between mb-2">
          <h2 className="font-display text-sm font-medium text-white">Open cases</h2>
          <Link to="/cases" className="text-xs text-emerald-400 hover:text-emerald-300 transition-colors">View all →</Link>
        </div>
        <div className={tableWrap}>
          <div className="overflow-x-auto">
            <table className="w-full text-sm min-w-[640px]">
              <thead className="bg-gray-900/60">
                <tr className="border-b border-gray-800">
                  <th className={thCls}>ID</th>
                  <th className={thCls}>Title</th>
                  <th className={thCls}>Severity</th>
                  <th className={thCls}>Status</th>
                  <th className={thCls}>Last seen</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-800/50">
                {cases.slice(0, 6).map((c) => (
                  <tr key={c.id} className="hover:bg-emerald-500/[0.03] transition-colors">
                    <td className={tdCls}>
                      <Link to={`/cases/${c.id}`} className="font-mono text-xs text-emerald-400 hover:text-emerald-300">
                        {c.id.slice(0, 8)}
                      </Link>
                    </td>
                    <td className={`${tdCls} text-gray-200 max-w-md truncate`}>{c.title}</td>
                    <td className={tdCls}><SeverityBadge severity={c.severity} /></td>
                    <td className={`${tdCls} text-gray-500`}>{c.status}</td>
                    <td className={`${tdCls} font-mono text-gray-500`}>{fmtDate(c.lastSeen)}</td>
                  </tr>
                ))}
                {cases.length === 0 && (
                  <tr><td colSpan={5} className="px-4 py-6 text-center text-gray-600 text-xs">No open cases — all clear</td></tr>
                )}
              </tbody>
            </table>
          </div>
        </div>
      </div>

      {/* Recent runs */}
      <div>
        <div className="flex items-center justify-between mb-2">
          <h2 className="font-display text-sm font-medium text-white">Recent runs</h2>
          <Link to="/runs" className="text-xs text-emerald-400 hover:text-emerald-300 transition-colors">View all →</Link>
        </div>
        <div className={tableWrap}>
          <div className="overflow-x-auto">
            <table className="w-full text-sm min-w-[640px]">
              <thead className="bg-gray-900/60">
                <tr className="border-b border-gray-800">
                  <th className={thCls}>Run</th>
                  <th className={thCls}>Status</th>
                  <th className={thCls}>Started</th>
                  <th className={thCls}>Cases</th>
                  <th className={thCls}>Alerts</th>
                  <th className={thCls}>Triggered by</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-800/50">
                {recentRuns.map((r) => (
                  <tr key={r.runId} className="hover:bg-emerald-500/[0.03] transition-colors">
                    <td className={tdCls}>
                      <Link to={`/runs/${r.runId}`} className="font-mono text-xs text-emerald-400 hover:text-emerald-300">
                        {r.runId.slice(0, 10)}
                      </Link>
                    </td>
                    <td className={tdCls}><StatusBadge status={r.status} /></td>
                    <td className={`${tdCls} font-mono text-gray-500`}>{fmtDate(r.startedAt)}</td>
                    <td className={`${tdCls} text-gray-400 tnum`}>+{r.casesCreated} / −{r.casesResolved}</td>
                    <td className={`${tdCls} text-gray-400 tnum`}>{r.alertsSent}</td>
                    <td className={`${tdCls} font-mono text-gray-500 max-w-[180px] truncate`}>{r.triggeredBy}</td>
                  </tr>
                ))}
                {recentRuns.length === 0 && (
                  <tr><td colSpan={6} className="px-4 py-6 text-center text-gray-600 text-xs">No runs yet</td></tr>
                )}
              </tbody>
            </table>
          </div>
        </div>
      </div>
    </div>
  );
}
