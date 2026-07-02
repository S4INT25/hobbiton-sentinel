import { useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { api, type ActiveRunState, type RunSummary } from '../api';
import { PageHeader, Feedback, Spinner, StatusBadge, btnPrimary, fmtDate } from '../components/ui';

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
    <div className="space-y-4">
      <PageHeader title="Run History">
        <button onClick={() => triggerMut.mutate()} disabled={triggerMut.isPending} className={btnPrimary}>
          {triggerMut.isPending ? 'Queueing…' : 'Run Now'}
        </button>
      </PageHeader>

      {feedback && <Feedback message={feedback.message} kind={feedback.kind} onDismiss={() => setFeedback(null)} />}

      {active.length > 0 && (
        <div className="border border-gray-800 rounded-lg p-3 bg-gray-900/30 space-y-2">
          {/* ponytail: Blazor also showed a live tool-call count here; needs per-run getRun polling — add if missed */}
          {active.map((a) => (
            <div key={a.runId} className="flex items-center justify-between">
              <div className="flex items-center gap-2 text-xs">
                <span className="text-gray-400">Current run:</span>
                <Link to={`/runs/${a.runId}`} className="text-emerald-400 hover:text-emerald-300 font-mono">
                  {a.runId}
                </Link>
                <StatusBadge status={a.status} />
              </div>
              {(a.status === 'running' || a.status === 'queued') && (
                <button
                  onClick={() => stopMut.mutate(a.runId)}
                  disabled={stopMut.isPending}
                  className="px-2.5 py-1 text-xs font-medium bg-rose-600/80 hover:bg-rose-500 text-white rounded-md transition-all duration-200 disabled:opacity-50 disabled:cursor-not-allowed"
                >
                  {stopMut.isPending ? 'Stopping…' : 'Stop'}
                </button>
              )}
            </div>
          ))}
        </div>
      )}

      <div className="border border-gray-800 rounded-lg overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full text-sm min-w-[860px]">
            <thead className="bg-gray-900/50">
              <tr className="text-xs text-gray-500 border-b border-gray-800">
                <th className="text-left px-4 py-2 font-medium">Run ID</th>
                <th className="text-left px-4 py-2 font-medium">Started</th>
                <th className="text-left px-4 py-2 font-medium">Duration</th>
                <th className="text-left px-4 py-2 font-medium">Iterations</th>
                <th className="text-left px-4 py-2 font-medium">Tokens (in/out)</th>
                <th className="text-left px-4 py-2 font-medium">Cases</th>
                <th className="text-left px-4 py-2 font-medium">Alerts</th>
                <th className="text-left px-4 py-2 font-medium">Status</th>
                <th className="text-left px-4 py-2 font-medium">Trigger</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-800/50">
              {paged.map((run) => (
                <tr key={run.runId} className="hover:bg-gray-900/30 transition-colors">
                  <td className="px-4 py-2.5">
                    <Link to={`/runs/${run.runId}`} className="text-emerald-400 hover:text-emerald-300 font-mono text-xs">
                      {run.runId}
                    </Link>
                  </td>
                  <td className="px-4 py-2.5 text-gray-400 text-xs">{fmtDate(run.startedAt)}</td>
                  <td className="px-4 py-2.5 text-gray-400 font-mono text-xs">{durationOf(run)}</td>
                  <td className="px-4 py-2.5 text-gray-300 font-mono text-xs">{run.iterations}</td>
                  <td className="px-4 py-2.5 text-gray-300 font-mono text-xs">
                    {run.inputTokens.toLocaleString()} / {run.outputTokens.toLocaleString()}
                  </td>
                  <td className="px-4 py-2.5 text-gray-300 font-mono text-xs">{run.casesCreated}</td>
                  <td className="px-4 py-2.5 text-gray-300 font-mono text-xs">{run.alertsSent}</td>
                  <td className="px-4 py-2.5">
                    <StatusBadge status={run.status} />
                  </td>
                  <td className="px-4 py-2.5 text-gray-500 text-xs max-w-[220px] truncate" title={run.triggeredBy}>
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
          <span className="text-gray-500">Page {page} / {totalPages}</span>
          <div className="flex items-center gap-2">
            <button
              onClick={() => setPage((p) => Math.max(1, p - 1))}
              disabled={page <= 1}
              className="px-2.5 py-1 border border-gray-800 rounded text-gray-300 disabled:opacity-40"
            >
              Previous
            </button>
            <button
              onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
              disabled={page >= totalPages}
              className="px-2.5 py-1 border border-gray-800 rounded text-gray-300 disabled:opacity-40"
            >
              Next
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
