import { Link, useParams } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { api } from '../api';
import { Spinner, StatusBadge, Markdown, fmtDateFull } from '../components/ui';

export default function RunDetail() {
  const { id = '' } = useParams();
  const { data, isLoading } = useQuery({
    queryKey: ['run', id],
    queryFn: () => api.getRun(id),
  });

  if (isLoading) return <div className="flex justify-center py-12"><Spinner /></div>;

  const summary = data?.summary;
  const logs = data?.logs ?? [];

  if (!summary) {
    return (
      <div className="space-y-4">
        <Link to="/runs" className="text-gray-500 hover:text-gray-300 text-sm">← Runs</Link>
        <div className="text-center py-8 text-gray-600 text-sm">Run not found</div>
      </div>
    );
  }

  const durationMin = Math.max(
    0,
    Math.round((new Date(summary.finishedAt).getTime() - new Date(summary.startedAt).getTime()) / 60000)
  );

  return (
    <div className="space-y-4">
      <div className="flex items-center gap-3">
        <Link to="/runs" className="text-gray-500 hover:text-gray-300 text-sm">← Runs</Link>
        <h1 className="text-lg font-semibold text-white font-mono">{summary.runId.slice(0, 12)}</h1>
        <StatusBadge status={summary.status} />
      </div>

      <div className="grid grid-cols-2 md:grid-cols-4 lg:grid-cols-7 gap-3 text-xs">
        {[
          ['Started', fmtDateFull(summary.startedAt)],
          ['Duration', `${durationMin}m`],
          ['Iterations', String(summary.iterations)],
          ['Tokens', `${(summary.inputTokens + summary.outputTokens).toLocaleString()}`],
          ['Cases created', String(summary.casesCreated)],
          ['Cases resolved', String(summary.casesResolved)],
          ['Alerts sent', String(summary.alertsSent)],
        ].map(([label, value]) => (
          <div key={label} className="p-3 bg-gray-900 border border-gray-800 rounded-lg">
            <div className="text-gray-500 mb-0.5">{label}</div>
            <div className="text-white font-medium">{value}</div>
          </div>
        ))}
      </div>

      <div className="text-xs text-gray-500">Triggered by <span className="text-gray-300 font-mono">{summary.triggeredBy}</span></div>

      {summary.error && (
        <div className="p-3 bg-rose-500/10 border border-rose-500/20 rounded-lg text-xs text-rose-400 font-mono whitespace-pre-wrap">
          {summary.error}
        </div>
      )}

      {summary.emailSubject && (
        <div className="border border-gray-800 rounded-lg overflow-hidden">
          <div className="px-4 py-2.5 bg-gray-900/50 border-b border-gray-800">
            <div className="text-[10px] text-gray-500 uppercase tracking-wider">Email sent</div>
            <div className="text-sm text-gray-200 mt-0.5">{summary.emailSubject}</div>
          </div>
          {summary.emailBody && (
            <div className="p-4">
              <Markdown text={summary.emailBody} />
            </div>
          )}
        </div>
      )}

      <div className="border border-gray-800 rounded-lg overflow-hidden">
        <div className="px-4 py-2.5 bg-gray-900/50 border-b border-gray-800 text-[10px] text-gray-500 uppercase tracking-wider">
          Tool calls ({logs.length})
        </div>
        <div className="divide-y divide-gray-800/50">
          {logs.map((l, i) => (
            <details key={i} className="group">
              <summary className="px-4 py-2.5 cursor-pointer hover:bg-gray-900/30 transition-colors flex items-center justify-between gap-3 select-none">
                <div className="flex items-center gap-2.5 min-w-0">
                  <span className="text-[10px] text-gray-600 font-mono shrink-0">#{l.iteration}</span>
                  <span className="text-xs text-gray-200 font-mono">{l.toolName}</span>
                </div>
                <span className="text-[10px] text-gray-600 shrink-0">{l.durationMs}ms</span>
              </summary>
              <div className="px-4 pb-3 space-y-2">
                {l.args && (
                  <div>
                    <div className="text-[10px] text-gray-600 uppercase tracking-wider mb-1">Args</div>
                    <pre className="text-[11px] text-gray-400 font-mono whitespace-pre-wrap bg-gray-950 border border-gray-800/60 rounded p-2 max-h-48 overflow-y-auto">{l.args}</pre>
                  </div>
                )}
                {l.result && (
                  <div>
                    <div className="text-[10px] text-gray-600 uppercase tracking-wider mb-1">Result</div>
                    <pre className="text-[11px] text-gray-400 font-mono whitespace-pre-wrap bg-gray-950 border border-gray-800/60 rounded p-2 max-h-48 overflow-y-auto">{l.result}</pre>
                  </div>
                )}
              </div>
            </details>
          ))}
          {logs.length === 0 && (
            <div className="px-4 py-6 text-center text-gray-600 text-xs">No tool calls logged</div>
          )}
        </div>
      </div>
    </div>
  );
}
