import { useEffect, useRef, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { api, runWorkflowId, type RunLog } from '../api';
import { Spinner, StatusBadge, Markdown, btnOutline, fmtDateFull } from '../components/ui';

/** Wall-clock since a run started, ticking while it is live. */
function useElapsed(startedAt: string | undefined, live: boolean) {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    if (!live) return;
    const t = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(t);
  }, [live]);

  if (!startedAt) return null;
  const secs = Math.max(0, Math.round((now - new Date(startedAt).getTime()) / 1000));
  const m = Math.floor(secs / 60);
  return m > 0 ? `${m}m ${secs % 60}s` : `${secs}s`;
}

function LogRow({ log, defaultOpen }: { log: RunLog; defaultOpen: boolean }) {
  return (
    <details open={defaultOpen} className="group">
      <summary className="px-4 py-2.5 cursor-pointer hover:bg-emerald-500/[0.03] transition-colors flex items-center justify-between gap-3 select-none">
        <div className="flex items-center gap-2.5 min-w-0">
          <span className="text-[10px] text-gray-600 font-mono shrink-0 tnum">#{log.iteration}</span>
          <span className="text-xs text-emerald-300/90 font-mono">{log.toolName}</span>
        </div>
        <span className="text-[10px] text-gray-600 font-mono shrink-0 tnum">{log.durationMs}ms</span>
      </summary>
      <div className="px-4 pb-3 space-y-2">
        {log.args && (
          <div>
            <div className="kicker mb-1">Args</div>
            <pre className="text-[11px] text-gray-400 font-mono whitespace-pre-wrap bg-gray-950 border border-gray-800/60 rounded-md p-2 max-h-48 overflow-y-auto">{log.args}</pre>
          </div>
        )}
        {log.result && (
          <div>
            <div className="kicker mb-1">Result</div>
            <pre className="text-[11px] text-gray-400 font-mono whitespace-pre-wrap bg-gray-950 border border-gray-800/60 rounded-md p-2 max-h-48 overflow-y-auto">{log.result}</pre>
          </div>
        )}
      </div>
    </details>
  );
}

export default function RunDetail() {
  const { id = '' } = useParams();
  const qc = useQueryClient();
  const [follow, setFollow] = useState(true);
  const tailRef = useRef<HTMLDivElement>(null);

  const { data, isLoading } = useQuery({
    queryKey: ['run', id],
    queryFn: () => api.getRun(id),
    // Poll only while the run is in flight — a finished run never changes, and polling it
    // forever would hammer the API from any tab left open.
    refetchInterval: (q) => (q.state.data?.live ? 2000 : false),
  });

  const live = data?.live ?? false;
  const summary = data?.summary;
  const logs = data?.logs ?? [];
  const elapsed = useElapsed(summary?.startedAt, live);
  const workflowId = runWorkflowId(summary?.triggeredBy);

  const stopMut = useMutation({
    mutationFn: () => api.stopRun(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['run', id] }),
  });

  // Keep the newest tool call in view while following a live run.
  useEffect(() => {
    if (live && follow) tailRef.current?.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
  }, [logs.length, live, follow]);

  if (isLoading) return <div className="flex justify-center py-12"><Spinner /></div>;

  if (!summary) {
    return (
      <div className="space-y-4">
        <Link to="/runs" className="text-gray-500 hover:text-gray-300 text-sm">← Runs</Link>
        <div className="panel p-8 text-center text-gray-600 text-sm">Run not found</div>
      </div>
    );
  }

  const durationLabel = live
    ? (elapsed ?? '—')
    : `${Math.max(0, Math.round((new Date(summary.finishedAt).getTime() - new Date(summary.startedAt).getTime()) / 60000))}m`;

  return (
    <div className="space-y-4" data-stagger>
      <div className="flex items-center gap-3 flex-wrap">
        <Link to="/runs" className="text-gray-500 hover:text-gray-300 text-sm">← Runs</Link>
        <h1 className="font-display text-lg font-semibold text-white font-mono">{summary.runId.slice(0, 12)}</h1>
        <StatusBadge status={summary.status} />
        {live && (
          <span className="inline-flex items-center gap-1.5 px-2 py-0.5 rounded-full border border-emerald-500/30 bg-emerald-500/10 font-mono text-[10px] uppercase tracking-wide text-emerald-300">
            <span className="h-1.5 w-1.5 rounded-full bg-emerald-400 animate-pulse" />
            Live
          </span>
        )}
        {workflowId && (
          <Link to={`/workflows/${workflowId}`} className="text-xs text-emerald-400 hover:text-emerald-300">
            View workflow →
          </Link>
        )}
        {live && (
          <button
            onClick={() => stopMut.mutate()}
            disabled={stopMut.isPending}
            className={`${btnOutline} ml-auto`}
          >
            {stopMut.isPending ? 'Stopping…' : 'Stop run'}
          </button>
        )}
      </div>

      <div className="grid grid-cols-2 md:grid-cols-4 lg:grid-cols-7 gap-3 text-xs">
        {[
          ['Started', fmtDateFull(summary.startedAt)],
          [live ? 'Elapsed' : 'Duration', durationLabel],
          ['Tool calls', String(logs.length)],
          ['Tokens', live ? '—' : (summary.inputTokens + summary.outputTokens).toLocaleString()],
          ['Cases created', live ? '—' : String(summary.casesCreated)],
          ['Cases resolved', live ? '—' : String(summary.casesResolved)],
          ['Alerts sent', live ? '—' : String(summary.alertsSent)],
        ].map(([label, value]) => (
          <div key={label} className="panel p-3">
            <div className="kicker mb-1">{label}</div>
            <div className="text-white font-mono font-medium tnum">{value}</div>
          </div>
        ))}
      </div>

      <div className="text-xs text-gray-500">
        Triggered by <span className="text-gray-300 font-mono">{summary.triggeredBy}</span>
        {live && <span className="text-gray-600"> · totals are counted when the run finishes</span>}
      </div>

      {summary.error && (
        <div className="p-3 bg-rose-500/10 border border-rose-500/20 rounded-lg text-xs text-rose-400 font-mono whitespace-pre-wrap">
          {summary.error}
        </div>
      )}

      {summary.emailSubject && (
        <div className="panel overflow-hidden">
          <div className="px-4 py-2.5 bg-gray-900/60 border-b border-gray-800">
            <div className="kicker">Email sent</div>
            <div className="text-sm text-gray-200 mt-0.5">{summary.emailSubject}</div>
          </div>
          {summary.emailBody && (
            <div className="p-4">
              <Markdown text={summary.emailBody} />
            </div>
          )}
        </div>
      )}

      <div className="panel overflow-hidden">
        <div className="px-4 py-2.5 bg-gray-900/60 border-b border-gray-800 flex items-center justify-between gap-3">
          <span className="kicker">Tool calls ({logs.length})</span>
          {live && (
            <label className="flex items-center gap-1.5 cursor-pointer text-[10px] font-mono uppercase tracking-wide text-gray-500">
              <input
                type="checkbox"
                checked={follow}
                onChange={(e) => setFollow(e.target.checked)}
                className="accent-emerald-500"
              />
              Follow
            </label>
          )}
        </div>
        <div className="divide-y divide-gray-800/50 max-h-[60vh] overflow-y-auto">
          {logs.map((l, i) => (
            // On a live run the newest call is the interesting one, so expand it.
            <LogRow key={i} log={l} defaultOpen={live && i === logs.length - 1} />
          ))}
          {logs.length === 0 && (
            <div className="px-4 py-6 text-center text-gray-600 text-xs">
              {live ? 'Waiting for the first tool call…' : 'No tool calls logged'}
            </div>
          )}
          <div ref={tailRef} />
        </div>
      </div>
    </div>
  );
}
