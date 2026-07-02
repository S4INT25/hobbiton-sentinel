import { useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { api, type RunSummary, type WorkflowDefinition } from '../api';
import {
  Feedback, Dialog, Spinner, StatusBadge, Markdown,
  btnPrimary, btnDanger, btnGhost, btnOutline, fmtDate, fmtDateFull,
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
        <div className="text-center py-8 text-gray-600 text-sm">Workflow not found</div>
      </div>
    );
  }

  const emailRuns = runs.filter((r) => r.alertsSent > 0 && r.emailSubject);

  return (
    <div className="space-y-4">
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2">
        <div className="flex items-center gap-3 min-w-0">
          <Link to="/workflows" className="text-gray-500 hover:text-gray-300 text-sm shrink-0">← Workflows</Link>
          <h1 className="text-lg font-semibold text-white truncate">{workflow.name}</h1>
          {!workflow.enabled && <span className="px-1.5 py-0.5 text-[10px] rounded bg-gray-800 text-gray-500 shrink-0">Disabled</span>}
        </div>
        <div className="flex items-center gap-2">
          <button onClick={() => triggerMut.mutate()} disabled={triggerMut.isPending} className={btnPrimary}>
            {triggerMut.isPending ? 'Queuing…' : 'Run now'}
          </button>
          <button onClick={() => setEditing({ ...workflow })} className={btnOutline}>Edit</button>
          <button onClick={() => setDeleteOpen(true)} className="px-2.5 py-1.5 text-xs border border-gray-800 rounded text-gray-600 hover:text-rose-400 hover:bg-gray-900 transition-colors">
            Delete
          </button>
        </div>
      </div>

      {feedback && <Feedback message={feedback.message} kind={feedback.kind} onDismiss={() => setFeedback(null)} />}

      {/* Info card */}
      <div className="border border-gray-800 rounded-lg p-4 bg-gray-900/30">
        <dl className="grid grid-cols-2 md:grid-cols-4 gap-x-6 gap-y-3 text-xs">
          <div>
            <dt className="text-gray-500">Schedule</dt>
            <dd className="text-gray-200 font-mono mt-0.5">{workflow.cronExpression}</dd>
          </div>
          <div>
            <dt className="text-gray-500">Time zone</dt>
            <dd className="text-gray-200 mt-0.5">{workflow.timeZoneId}</dd>
          </div>
          <div>
            <dt className="text-gray-500">Action</dt>
            <dd className="text-gray-200 mt-0.5">{workflow.actionType}</dd>
          </div>
          <div>
            <dt className="text-gray-500">Database</dt>
            <dd className="text-gray-200 mt-0.5">{workflow.targetDatabase || '—'}</dd>
          </div>
          {workflow.emailSubject && (
            <div className="col-span-2">
              <dt className="text-gray-500">Email subject</dt>
              <dd className="text-gray-200 mt-0.5 truncate" title={workflow.emailSubject}>{workflow.emailSubject}</dd>
            </div>
          )}
          {workflow.emailRecipients && (
            <div className="col-span-2">
              <dt className="text-gray-500">Recipients</dt>
              <dd className="text-gray-200 mt-0.5 truncate">{workflow.emailRecipients}</dd>
            </div>
          )}
        </dl>
        {workflow.customPrompt && (
          <div className="mt-3 pt-3 border-t border-gray-800/60">
            <div className="text-[10px] text-gray-500 uppercase tracking-wider mb-1">Agent instructions</div>
            <p className="text-xs text-gray-400 whitespace-pre-wrap">{workflow.customPrompt}</p>
          </div>
        )}
      </div>

      {/* Email history */}
      {emailRuns.length > 0 && (
        <div>
          <h2 className="text-sm font-medium text-white mb-2">Sent reports ({emailRuns.length})</h2>
          <div className="border border-gray-800 rounded-lg divide-y divide-gray-800/50 overflow-hidden">
            {emailRuns.slice(0, 10).map((run) => (
              <button
                key={run.runId}
                onClick={() => setSelectedEmailRun(run)}
                className="w-full text-left px-4 py-2.5 hover:bg-gray-900/30 transition-colors flex items-center justify-between gap-3"
              >
                <div className="min-w-0">
                  <div className="text-sm text-gray-200 truncate">{run.emailSubject ?? 'Report'}</div>
                  <div className="text-[10px] text-gray-600 mt-0.5">{fmtDateFull(run.startedAt)}</div>
                </div>
                <StatusBadge status={run.status} />
              </button>
            ))}
          </div>
        </div>
      )}

      {/* Run history */}
      <div>
        <h2 className="text-sm font-medium text-white mb-2">Run history</h2>
        <div className="border border-gray-800 rounded-lg overflow-hidden">
          <div className="overflow-x-auto">
            <table className="w-full text-sm min-w-[560px]">
              <thead className="bg-gray-900/50">
                <tr className="text-xs text-gray-500 border-b border-gray-800">
                  <th className="text-left px-4 py-2 font-medium">Run</th>
                  <th className="text-left px-4 py-2 font-medium">Status</th>
                  <th className="text-left px-4 py-2 font-medium">Started</th>
                  <th className="text-left px-4 py-2 font-medium">Alerts</th>
                  <th className="text-left px-4 py-2 font-medium">Tokens</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-800/50">
                {runs.map((r) => (
                  <tr key={r.runId} className="hover:bg-gray-900/30 transition-colors">
                    <td className="px-4 py-2.5">
                      <Link to={`/runs/${r.runId}`} className="font-mono text-xs text-emerald-400 hover:text-emerald-300">
                        {r.runId.slice(0, 10)}
                      </Link>
                    </td>
                    <td className="px-4 py-2.5"><StatusBadge status={r.status} /></td>
                    <td className="px-4 py-2.5 text-xs text-gray-500">{fmtDate(r.startedAt)}</td>
                    <td className="px-4 py-2.5 text-xs text-gray-400">{r.alertsSent}</td>
                    <td className="px-4 py-2.5 text-xs text-gray-500">{(r.inputTokens + r.outputTokens).toLocaleString()}</td>
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
          <div className="border border-gray-800 rounded-lg p-4 bg-gray-900/30">
            <div className="text-[10px] text-gray-500 uppercase tracking-wider mb-2">Fraud patterns ({patterns.length})</div>
            <div className="space-y-1.5">
              {patterns.map((p) => (
                <div key={p.id} className={`text-xs ${p.enabled ? 'text-gray-300' : 'text-gray-600 line-through'}`}>
                  {p.name} <span className="text-gray-600">· {p.category}</span>
                </div>
              ))}
              {patterns.length === 0 && <div className="text-xs text-gray-600">None scoped to this workflow</div>}
            </div>
          </div>
          <div className="border border-gray-800 rounded-lg p-4 bg-gray-900/30">
            <div className="text-[10px] text-gray-500 uppercase tracking-wider mb-2">Evidence sources ({sources.length})</div>
            <div className="space-y-1.5">
              {sources.map((s) => (
                <div key={s.id} className={`text-xs ${s.enabled ? 'text-gray-300' : 'text-gray-600 line-through'}`}>
                  {s.name} <span className="text-gray-600">· {s.evidenceDatabase}</span>
                </div>
              ))}
              {sources.length === 0 && <div className="text-xs text-gray-600">None scoped to this workflow</div>}
            </div>
          </div>
        </div>
      )}

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
          <div className="text-[10px] text-gray-600">{fmtDateFull(selectedEmailRun.startedAt)}</div>
          <div className="max-h-[60vh] overflow-y-auto">
            <Markdown text={selectedEmailRun.emailBody ?? '_No body captured._'} />
          </div>
          <div className="flex items-center justify-end pt-1">
            <button onClick={() => setSelectedEmailRun(null)} className={btnGhost}>Close</button>
          </div>
        </Dialog>
      )}
    </div>
  );
}
