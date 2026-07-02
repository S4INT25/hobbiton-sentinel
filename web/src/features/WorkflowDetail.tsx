import { useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { AnimatePresence } from 'motion/react';
import { api, type RunSummary, type WorkflowDefinition } from '../api';
import {
  Feedback, Dialog, Spinner, StatusBadge, Markdown,
  btnPrimary, btnDanger, btnGhost, btnOutline, fmtDate, fmtDateFull, tableWrap, thCls, tdCls,
} from '../components/ui';
import { WorkflowForm } from './Workflows';

export default function WorkflowDetail() {
  const { id = '' } = useParams();
  const navigate = useNavigate();
  const qc = useQueryClient();

  const { data: workflow, isLoading } = useQuery({
    queryKey: ['workflow', id],
    queryFn: () => api.getWorkflow(id),
  });
  const { data: runs = [] } = useQuery({
    queryKey: ['workflow-runs', id],
    queryFn: () => api.workflowRuns(id),
    enabled: !!workflow,
  });
  const { data: patterns = [] } = useQuery({
    queryKey: ['workflow-patterns', id],
    queryFn: () => api.workflowPatterns(id),
    enabled: !!workflow,
  });
  const { data: sources = [] } = useQuery({
    queryKey: ['workflow-sources', id],
    queryFn: () => api.workflowEvidenceSources(id),
    enabled: !!workflow,
  });

  const [feedback, setFeedback] = useState<{ message: string; kind: 'success' | 'error' } | null>(null);
  const [editing, setEditing] = useState<Partial<WorkflowDefinition> | null>(null);
  const [deleteOpen, setDeleteOpen] = useState(false);
  const [selectedEmailRun, setSelectedEmailRun] = useState<RunSummary | null>(null);

  const saveMut = useMutation({
    mutationFn: (wf: Partial<WorkflowDefinition>) => api.saveWorkflow(wf),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['workflow', id] });
      qc.invalidateQueries({ queryKey: ['workflows'] });
      setEditing(null);
      setFeedback({ message: 'Workflow saved.', kind: 'success' });
    },
    onError: (e: Error) => setFeedback({ message: `Failed to save: ${e.message}`, kind: 'error' }),
  });

  const deleteMut = useMutation({
    mutationFn: () => api.deleteWorkflow(id),
    onSuccess: () => navigate('/workflows'),
  });

  const triggerMut = useMutation({
    mutationFn: () => api.triggerWorkflow(id),
    onSuccess: () => setFeedback({ message: 'Workflow run queued.', kind: 'success' }),
    onError: (e: Error) => setFeedback({ message: `Failed to trigger: ${e.message}`, kind: 'error' }),
  });

  if (isLoading) return <div className="flex justify-center py-12"><Spinner /></div>;

  if (!workflow) {
    return (
      <div className="space-y-4">
        <Link to="/workflows" className="text-gray-500 hover:text-gray-300 text-sm">← Workflows</Link>
        <div className="panel p-8 text-center text-gray-600 text-sm">Workflow not found</div>
      </div>
    );
  }

  const emailRuns = runs.filter((r) => r.alertsSent > 0 && r.emailSubject);

  return (
    <div className="space-y-4" data-stagger>
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2">
        <div className="flex items-center gap-3 min-w-0">
          <Link to="/workflows" className="text-gray-500 hover:text-gray-300 text-sm shrink-0">← Workflows</Link>
          <h1 className="font-display text-lg font-semibold text-white truncate flex items-center gap-2">
            {workflow.enabled && <span className="glow-dot shrink-0" style={{ height: 6, width: 6 }} />}
            {workflow.name}
          </h1>
          {!workflow.enabled && (
            <span className="px-1.5 py-0.5 font-mono text-[10px] uppercase tracking-wide rounded bg-gray-800 text-gray-500 shrink-0">Disabled</span>
          )}
        </div>
        <div className="flex items-center gap-2">
          <button onClick={() => triggerMut.mutate()} disabled={triggerMut.isPending} className={btnPrimary}>
            {triggerMut.isPending ? 'Queuing…' : 'Run now'}
          </button>
          <button onClick={() => setEditing({ ...workflow })} className={btnOutline}>Edit</button>
          <button onClick={() => setDeleteOpen(true)} className="px-2.5 py-1.5 text-xs border border-gray-800 rounded-md text-gray-600 hover:text-rose-400 hover:bg-gray-900 transition-colors">
            Delete
          </button>
        </div>
      </div>

      {feedback && <Feedback message={feedback.message} kind={feedback.kind} onDismiss={() => setFeedback(null)} />}

      {/* Info card */}
      <div className="panel p-4">
        <dl className="grid grid-cols-2 md:grid-cols-4 gap-x-6 gap-y-3 text-xs">
          <div>
            <dt className="kicker">Schedule</dt>
            <dd className="text-gray-200 font-mono mt-1">{workflow.cronExpression}</dd>
          </div>
          <div>
            <dt className="kicker">Time zone</dt>
            <dd className="text-gray-200 mt-1">{workflow.timeZoneId}</dd>
          </div>
          <div>
            <dt className="kicker">Action</dt>
            <dd className="text-gray-200 font-mono mt-1">{workflow.actionType}</dd>
          </div>
          <div>
            <dt className="kicker">Database</dt>
            <dd className="text-gray-200 font-mono mt-1">{workflow.targetDatabase || '—'}</dd>
          </div>
          {workflow.emailSubject && (
            <div className="col-span-2">
              <dt className="kicker">Email subject</dt>
              <dd className="text-gray-200 mt-1 truncate" title={workflow.emailSubject}>{workflow.emailSubject}</dd>
            </div>
          )}
          {workflow.emailRecipients && (
            <div className="col-span-2">
              <dt className="kicker">Recipients</dt>
              <dd className="text-gray-200 font-mono mt-1 truncate">{workflow.emailRecipients}</dd>
            </div>
          )}
        </dl>
        {workflow.customPrompt && (
          <div className="mt-3 pt-3 border-t border-gray-800/60">
            <div className="kicker mb-1.5">Agent instructions</div>
            <p className="text-xs text-gray-400 whitespace-pre-wrap">{workflow.customPrompt}</p>
          </div>
        )}
      </div>

      {/* Email history */}
      {emailRuns.length > 0 && (
        <div>
          <h2 className="font-display text-sm font-medium text-white mb-2">Sent reports ({emailRuns.length})</h2>
          <div className="panel divide-y divide-gray-800/50 overflow-hidden">
            {emailRuns.slice(0, 10).map((run) => (
              <button
                key={run.runId}
                onClick={() => setSelectedEmailRun(run)}
                className="w-full text-left px-4 py-2.5 hover:bg-emerald-500/[0.03] transition-colors flex items-center justify-between gap-3"
              >
                <div className="min-w-0">
                  <div className="text-sm text-gray-200 truncate">{run.emailSubject ?? 'Report'}</div>
                  <div className="font-mono text-[10px] text-gray-600 mt-0.5">{fmtDateFull(run.startedAt)}</div>
                </div>
                <StatusBadge status={run.status} />
              </button>
            ))}
          </div>
        </div>
      )}

      {/* Run history */}
      <div>
        <h2 className="font-display text-sm font-medium text-white mb-2">Run history</h2>
        <div className={tableWrap}>
          <div className="overflow-x-auto">
            <table className="w-full text-sm min-w-[560px]">
              <thead className="bg-gray-900/60">
                <tr className="border-b border-gray-800">
                  <th className={thCls}>Run</th>
                  <th className={thCls}>Status</th>
                  <th className={thCls}>Started</th>
                  <th className={thCls}>Alerts</th>
                  <th className={thCls}>Tokens</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-800/50">
                {runs.map((r) => (
                  <tr key={r.runId} className="hover:bg-emerald-500/[0.03] transition-colors">
                    <td className={tdCls}>
                      <Link to={`/runs/${r.runId}`} className="font-mono text-xs text-emerald-400 hover:text-emerald-300">
                        {r.runId.slice(0, 10)}
                      </Link>
                    </td>
                    <td className={tdCls}><StatusBadge status={r.status} /></td>
                    <td className={`${tdCls} font-mono text-gray-500`}>{fmtDate(r.startedAt)}</td>
                    <td className={`${tdCls} text-gray-400 tnum`}>{r.alertsSent}</td>
                    <td className={`${tdCls} text-gray-500 tnum`}>{(r.inputTokens + r.outputTokens).toLocaleString()}</td>
                  </tr>
                ))}
                {runs.length === 0 && (
                  <tr><td colSpan={5} className="px-4 py-6 text-center text-gray-600 text-xs">No runs yet</td></tr>
                )}
              </tbody>
            </table>
          </div>
        </div>
      </div>

      {/* Scoped fraud config */}
      {(patterns.length > 0 || sources.length > 0) && (
        <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
          <div className="panel p-4">
            <div className="kicker mb-2">Fraud patterns ({patterns.length})</div>
            <div className="space-y-1.5">
              {patterns.map((p) => (
                <div key={p.id} className={`text-xs ${p.enabled ? 'text-gray-300' : 'text-gray-600 line-through'}`}>
                  {p.name} <span className="font-mono text-gray-600">· {p.category}</span>
                </div>
              ))}
              {patterns.length === 0 && <div className="text-xs text-gray-600">None scoped to this workflow</div>}
            </div>
          </div>
          <div className="panel p-4">
            <div className="kicker mb-2">Evidence sources ({sources.length})</div>
            <div className="space-y-1.5">
              {sources.map((s) => (
                <div key={s.id} className={`text-xs ${s.enabled ? 'text-gray-300' : 'text-gray-600 line-through'}`}>
                  {s.name} <span className="font-mono text-gray-600">· {s.evidenceDatabase}</span>
                </div>
              ))}
              {sources.length === 0 && <div className="text-xs text-gray-600">None scoped to this workflow</div>}
            </div>
          </div>
        </div>
      )}

      <AnimatePresence>
        {editing && (
          <Dialog title="Edit workflow" onClose={() => setEditing(null)}>
            <WorkflowForm value={editing} onChange={setEditing} />
            <div className="flex items-center justify-end gap-2 pt-1">
              <button onClick={() => setEditing(null)} className={btnGhost}>Cancel</button>
              <button onClick={() => saveMut.mutate(editing)} disabled={saveMut.isPending || !editing.name} className={btnPrimary}>
                {saveMut.isPending ? 'Saving…' : 'Save'}
              </button>
            </div>
          </Dialog>
        )}

        {deleteOpen && (
          <Dialog title="Delete workflow" onClose={() => setDeleteOpen(false)}>
            <p className="text-xs text-gray-400">
              Delete <span className="text-gray-200">{workflow.name}</span>? Its schedule will be removed. Run history is kept.
            </p>
            <div className="flex items-center justify-end gap-2 pt-1">
              <button onClick={() => setDeleteOpen(false)} className={btnGhost}>Cancel</button>
              <button onClick={() => deleteMut.mutate()} disabled={deleteMut.isPending} className={btnDanger}>
                {deleteMut.isPending ? 'Deleting…' : 'Delete'}
              </button>
            </div>
          </Dialog>
        )}

        {selectedEmailRun && (
          <Dialog title={selectedEmailRun.emailSubject ?? 'Report'} onClose={() => setSelectedEmailRun(null)}>
            <div className="font-mono text-[10px] text-gray-600">{fmtDateFull(selectedEmailRun.startedAt)}</div>
            <div className="max-h-[60vh] overflow-y-auto">
              <Markdown text={selectedEmailRun.emailBody ?? '_No body captured._'} />
            </div>
            <div className="flex items-center justify-end pt-1">
              <button onClick={() => setSelectedEmailRun(null)} className={btnGhost}>Close</button>
            </div>
          </Dialog>
        )}
      </AnimatePresence>
    </div>
  );
}
