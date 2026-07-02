import { useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { api, type ActiveRunState, type RunSummary } from '../api';
import { PageHeader, Feedback, Spinner, StatusBadge, btnPrimary, fmtDate, tableWrap, thCls, tdCls } from '../components/ui';

const PAGE_SIZE = 20;
const NO_ACTIVE: ActiveRunState[] = [];

const fmtTrigger = (t: string) => {
  if (!t?.trim()) return '-';
  if (t.toLowerCase().startsWith('workflow:')) return `workflow:${t.slice('workflow:'.length).slice(0, 10)}`;
  return t;
};

const durationOf = (r: RunSummary) =>
  `${Math.max(0, Math.round((new Date(r.finishedAt).getTime() - new Date(r.startedAt).getTime()) / 60000))}m`;

export default function Runs() {
  const qc = useQueryClient();
  const [page, setPage] = useState(1);
  const [feedback, setFeedback] = useState<{ message: string; kind: 'success' | 'error' } | null>(null);

  const { data: runs = [], isLoading } = useQuery({
    queryKey: ['runs'],
    queryFn: () => api.listRuns(200, 0),
  });

  // poll active runs every 5s while any run is queued/running
  const activeQuery = useQuery({
    queryKey: ['activeRuns'],
    queryFn: api.activeRuns,
    refetchInterval: (query) => (query.state.data?.length ? 5000 : false),
  });
  const active = activeQuery.data ?? NO_ACTIVE;

  // active set changed (new run, status change, completion) → refresh history
  useEffect(() => {
    qc.invalidateQueries({ queryKey: ['runs'] });
  }, [activeQuery.data, qc]);

  const displayRuns = useMemo<RunSummary[]>(() => {
    // show queued/running runs that haven't reached the history table yet
    const synthetic = active
      .filter((a) => runs.every((r) => r.runId !== a.runId))
      .map((a) => ({
        runId: a.runId,
        startedAt: a.startedAtUtc,
        finishedAt: a.startedAtUtc,
        iterations: 0,
        inputTokens: 0,
        outputTokens: 0,
        casesCreated: 0,
        casesResolved: 0,
        alertsSent: 0,
        status: a.status,
        triggeredBy: a.triggeredBy,
        error: null,
        emailSubject: null,
        emailBody: null,
      }));
    return [...synthetic, ...runs];
  }, [runs, active]);

  const totalPages = Math.max(1, Math.ceil(displayRuns.length / PAGE_SIZE));
  const paged = displayRuns.slice((page - 1) * PAGE_SIZE, page * PAGE_SIZE);

  const triggerMut = useMutation({
    mutationFn: () => api.triggerRun(),
    onSuccess: () => {
      setFeedback({ message: 'Run queued.', kind: 'success' });
      qc.invalidateQueries({ queryKey: ['activeRuns'] });
      qc.invalidateQueries({ queryKey: ['runs'] });
    },
    onError: () => setFeedback({ message: 'Failed to trigger run', kind: 'error' }),
  });

  const stopMut = useMutation({
    mutationFn: (runId: string) => api.stopRun(runId),
    onSuccess: () => {
      setFeedback({ message: 'Run stopped.', kind: 'success' });
      qc.invalidateQueries({ queryKey: ['activeRuns'] });
      qc.invalidateQueries({ queryKey: ['runs'] });
    },
    onError: (e: Error) => setFeedback({ message: `Failed to stop run: ${e.message}`, kind: 'error' }),
  });

  return (
    <div className="space-y-4" data-stagger>
      <PageHeader title="Run History">
        <button onClick={() => triggerMut.mutate()} disabled={triggerMut.isPending} className={btnPrimary}>
          {triggerMut.isPending ? 'Queueing…' : 'Run Now'}
        </button>
      </PageHeader>

      {feedback && <Feedback message={feedback.message} kind={feedback.kind} onDismiss={() => setFeedback(null)} />}

      {active.length > 0 && (
        <div className="panel p-3.5 space-y-2 border-l-2 border-l-emerald-500/50">
          <div className="flex items-center gap-2">
            <span className="glow-dot" />
            <span className="kicker text-emerald-500/90">Active run</span>
          </div>
          {/* ponytail: Blazor also showed a live tool-call count here; needs per-run getRun polling — add if missed */}
          {active.map((a) => (
            <div key={a.runId} className="flex items-center justify-between">
              <div className="flex items-center gap-2 text-xs">
                <Link to={`/runs/${a.runId}`} className="text-emerald-400 hover:text-emerald-300 font-mono">
                  {a.runId}
                </Link>
                <StatusBadge status={a.status} />
              </div>
              {(a.status === 'running' || a.status === 'queued') && (
                <button
                  onClick={() => stopMut.mutate(a.runId)}
                  disabled={stopMut.isPending}
                  className="px-2.5 py-1 text-xs font-semibold bg-rose-600/80 hover:bg-rose-500 text-white rounded-md transition-all duration-200 disabled:opacity-50 disabled:cursor-not-allowed"
                >
                  {stopMut.isPending ? 'Stopping…' : 'Stop'}
                </button>
              )}
            </div>
          ))}
        </div>
      )}

      <div className={tableWrap}>
        <div className="overflow-x-auto">
          <table className="w-full text-sm min-w-[860px]">
            <thead className="bg-gray-900/60">
              <tr className="border-b border-gray-800">
                <th className={thCls}>Run ID</th>
                <th className={thCls}>Started</th>
                <th className={thCls}>Duration</th>
                <th className={thCls}>Iterations</th>
                <th className={thCls}>Tokens (in/out)</th>
                <th className={thCls}>Cases</th>
                <th className={thCls}>Alerts</th>
                <th className={thCls}>Status</th>
                <th className={thCls}>Trigger</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-800/50">
              {paged.map((run) => (
                <tr key={run.runId} className="hover:bg-emerald-500/[0.03] transition-colors">
                  <td className={tdCls}>
                    <Link to={`/runs/${run.runId}`} className="text-emerald-400 hover:text-emerald-300 font-mono text-xs">
                      {run.runId}
                    </Link>
                  </td>
                  <td className={`${tdCls} font-mono text-gray-400`}>{fmtDate(run.startedAt)}</td>
                  <td className={`${tdCls} font-mono text-gray-400 tnum`}>{durationOf(run)}</td>
                  <td className={`${tdCls} font-mono tnum`}>{run.iterations}</td>
                  <td className={`${tdCls} font-mono tnum`}>
                    {run.inputTokens.toLocaleString()} / {run.outputTokens.toLocaleString()}
                  </td>
                  <td className={`${tdCls} font-mono tnum`}>{run.casesCreated}</td>
                  <td className={`${tdCls} font-mono tnum`}>{run.alertsSent}</td>
                  <td className={tdCls}>
                    <StatusBadge status={run.status} />
                  </td>
                  <td className={`${tdCls} text-gray-500 max-w-[220px] truncate`} title={run.triggeredBy}>
                    {fmtTrigger(run.triggeredBy)}
                  </td>
                </tr>
              ))}
              {!isLoading && displayRuns.length === 0 && (
                <tr>
                  <td colSpan={9} className="px-4 py-8 text-center text-gray-600 text-xs">No runs recorded yet</td>
                </tr>
              )}
              {isLoading && (
                <tr>
                  <td colSpan={9} className="px-4 py-8">
                    <div className="flex justify-center"><Spinner /></div>
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </div>

      {displayRuns.length > PAGE_SIZE && (
        <div className="mt-3 flex items-center justify-between text-xs">
          <span className="font-mono text-[11px] text-gray-500 tnum">Page {page} / {totalPages}</span>
          <div className="flex items-center gap-2">
            <button
              onClick={() => setPage((p) => Math.max(1, p - 1))}
              disabled={page <= 1}
              className="px-2.5 py-1 border border-gray-800 rounded-md text-gray-300 disabled:opacity-40 hover:border-gray-700 hover:bg-gray-900/60 transition-colors"
            >
              Previous
            </button>
            <button
              onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
              disabled={page >= totalPages}
              className="px-2.5 py-1 border border-gray-800 rounded-md text-gray-300 disabled:opacity-40 hover:border-gray-700 hover:bg-gray-900/60 transition-colors"
            >
              Next
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
