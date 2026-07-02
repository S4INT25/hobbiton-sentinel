import { Link } from 'react-router-dom';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { api } from '../api';
import { PageHeader, Spinner, SeverityBadge, StatusBadge, btnPrimary, fmtDate } from '../components/ui';

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

  return (
    <div className="space-y-6">
      <PageHeader title="Dashboard" subtitle="Fraud monitoring at a glance">
        <button onClick={() => triggerMut.mutate()} disabled={triggerMut.isPending || isRunning} className={btnPrimary}>
          {isRunning ? 'Run in progress…' : triggerMut.isPending ? 'Starting…' : 'Trigger fraud sweep'}
        </button>
      </PageHeader>

      {/* Stat cards */}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-3">
        <Link to="/cases" className="p-4 bg-gray-900 border border-gray-800 rounded-lg hover:border-gray-700 transition-colors">
          <div className="text-xs text-gray-500">Open cases</div>
          <div className="text-2xl font-semibold text-white mt-1">{cases.length}</div>
          <div className="text-[10px] text-gray-600 mt-1">
            {critical > 0 && <span className="text-rose-400">{critical} critical</span>}
            {critical > 0 && high > 0 && ' · '}
            {high > 0 && <span className="text-orange-400">{high} high</span>}
            {critical === 0 && high === 0 && 'no critical or high'}
          </div>
        </Link>
        <Link to="/rules" className="p-4 bg-gray-900 border border-gray-800 rounded-lg hover:border-gray-700 transition-colors">
          <div className="text-xs text-gray-500">Active rules</div>
          <div className="text-2xl font-semibold text-white mt-1">{rules.length}</div>
          <div className="text-[10px] text-gray-600 mt-1">suppression &amp; downgrade</div>
        </Link>
        <Link to="/workflows" className="p-4 bg-gray-900 border border-gray-800 rounded-lg hover:border-gray-700 transition-colors">
          <div className="text-xs text-gray-500">Workflows</div>
          <div className="text-2xl font-semibold text-white mt-1">{enabledWorkflows.length}</div>
          <div className="text-[10px] text-gray-600 mt-1">scheduled &amp; enabled</div>
        </Link>
        <Link to="/runs" className="p-4 bg-gray-900 border border-gray-800 rounded-lg hover:border-gray-700 transition-colors">
          <div className="text-xs text-gray-500">Active runs</div>
          <div className="text-2xl font-semibold text-white mt-1 flex items-center gap-2">
            {activeRuns.length}
            {isRunning && <Spinner className="h-3.5 w-3.5" />}
          </div>
          <div className="text-[10px] text-gray-600 mt-1">{runs.length} in history</div>
        </Link>
      </div>

      {/* Open cases table */}
      <div>
        <div className="flex items-center justify-between mb-2">
          <h2 className="text-sm font-medium text-white">Open cases</h2>
          <Link to="/cases" className="text-xs text-emerald-400 hover:text-emerald-300 transition-colors">View all →</Link>
        </div>
        <div className="border border-gray-800 rounded-lg overflow-hidden">
          <div className="overflow-x-auto">
            <table className="w-full text-sm min-w-[640px]">
              <thead className="bg-gray-900/50">
                <tr className="text-xs text-gray-500 border-b border-gray-800">
                  <th className="text-left px-4 py-2 font-medium">ID</th>
                  <th className="text-left px-4 py-2 font-medium">Title</th>
                  <th className="text-left px-4 py-2 font-medium">Severity</th>
                  <th className="text-left px-4 py-2 font-medium">Status</th>
                  <th className="text-left px-4 py-2 font-medium">Last seen</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-800/50">
                {cases.slice(0, 6).map((c) => (
                  <tr key={c.id} className="hover:bg-gray-900/30 transition-colors">
                    <td className="px-4 py-2.5">
                      <Link to={`/cases/${c.id}`} className="font-mono text-xs text-emerald-400 hover:text-emerald-300">
                        {c.id.slice(0, 8)}
                      </Link>
                    </td>
                    <td className="px-4 py-2.5 text-xs text-gray-200 max-w-md truncate">{c.title}</td>
                    <td className="px-4 py-2.5"><SeverityBadge severity={c.severity} /></td>
                    <td className="px-4 py-2.5 text-xs text-gray-500">{c.status}</td>
                    <td className="px-4 py-2.5 text-xs text-gray-500">{fmtDate(c.lastSeen)}</td>
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
          <h2 className="text-sm font-medium text-white">Recent runs</h2>
          <Link to="/runs" className="text-xs text-emerald-400 hover:text-emerald-300 transition-colors">View all →</Link>
        </div>
        <div className="border border-gray-800 rounded-lg overflow-hidden">
          <div className="overflow-x-auto">
            <table className="w-full text-sm min-w-[640px]">
              <thead className="bg-gray-900/50">
                <tr className="text-xs text-gray-500 border-b border-gray-800">
                  <th className="text-left px-4 py-2 font-medium">Run</th>
                  <th className="text-left px-4 py-2 font-medium">Status</th>
                  <th className="text-left px-4 py-2 font-medium">Started</th>
                  <th className="text-left px-4 py-2 font-medium">Cases</th>
                  <th className="text-left px-4 py-2 font-medium">Alerts</th>
                  <th className="text-left px-4 py-2 font-medium">Triggered by</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-800/50">
                {recentRuns.map((r) => (
                  <tr key={r.runId} className="hover:bg-gray-900/30 transition-colors">
                    <td className="px-4 py-2.5">
                      <Link to={`/runs/${r.runId}`} className="font-mono text-xs text-emerald-400 hover:text-emerald-300">
                        {r.runId.slice(0, 10)}
                      </Link>
                    </td>
                    <td className="px-4 py-2.5"><StatusBadge status={r.status} /></td>
                    <td className="px-4 py-2.5 text-xs text-gray-500">{fmtDate(r.startedAt)}</td>
                    <td className="px-4 py-2.5 text-xs text-gray-400">+{r.casesCreated} / −{r.casesResolved}</td>
                    <td className="px-4 py-2.5 text-xs text-gray-400">{r.alertsSent}</td>
                    <td className="px-4 py-2.5 text-xs text-gray-500 font-mono max-w-[180px] truncate">{r.triggeredBy}</td>
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
