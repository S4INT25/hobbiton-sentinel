import { useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { AnimatePresence } from 'motion/react';
import { api, type RunSummary, type WorkflowDefinition, type FraudPattern, type EvidenceSource } from '../api';
import {
  Feedback, Dialog, Spinner, StatusBadge, Markdown, Tabs,
  btnPrimary, btnDanger, btnGhost, btnOutline, inputCls,
  fmtDate, fmtDateFull, tableWrap, thCls, tdCls,
} from '../components/ui';
import { WorkflowForm } from './Workflows';

const label = 'block font-mono text-[10px] text-gray-500 uppercase tracking-wider mb-1';
const RUNS_PAGE_SIZE = 20;

const PATTERN_CATEGORIES = [
  'TransactionAnomaly', 'VelocityAbuse', 'IdentityAndAccess', 'AccountCompromise',
  'NetworkAnomaly', 'DataIntegrity', 'MerchantOnboarding',
];

const EMPTY_PATTERN: Partial<FraudPattern> = {
  name: '', description: '', category: 'TransactionAnomaly', enabled: true,
};

const EMPTY_SOURCE: Partial<EvidenceSource> = {
  name: '', evidenceDatabase: '', lipilaMerchantIds: '', lipilaPartnerId: 0,
  joinMappings: '{}', tableDescriptions: '', evidenceChecks: '[]', notes: '', enabled: true,
};

const editIcon = (
  <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z" />
  </svg>
);
const deleteIcon = (
  <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
  </svg>
);

type TabKey = 'overview' | 'runs' | 'patterns' | 'sources';

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

  const [tab, setTab] = useState<TabKey>('overview');
  const [feedback, setFeedback] = useState<{ message: string; kind: 'success' | 'error' } | null>(null);
  const [editing, setEditing] = useState<Partial<WorkflowDefinition> | null>(null);
  const [deleteOpen, setDeleteOpen] = useState(false);
  const [selectedEmailRun, setSelectedEmailRun] = useState<RunSummary | null>(null);
  const [runsPage, setRunsPage] = useState(1);
  const [editingPattern, setEditingPattern] = useState<Partial<FraudPattern> | null>(null);
  const [deletePatternTarget, setDeletePatternTarget] = useState<FraudPattern | null>(null);
  const [editingSource, setEditingSource] = useState<Partial<EvidenceSource> | null>(null);
  const [deleteSourceTarget, setDeleteSourceTarget] = useState<EvidenceSource | null>(null);

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

  const savePatternMut = useMutation({
    mutationFn: (p: Partial<FraudPattern>) => api.savePattern(p),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['workflow-patterns', id] });
      setEditingPattern(null);
      setFeedback({ message: 'Pattern saved.', kind: 'success' });
    },
    onError: (e: Error) => setFeedback({ message: `Failed to save pattern: ${e.message}`, kind: 'error' }),
  });

  const deletePatternMut = useMutation({
    mutationFn: (patternId: number) => api.deletePattern(patternId),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['workflow-patterns', id] });
      setDeletePatternTarget(null);
      setFeedback({ message: 'Pattern deleted.', kind: 'success' });
    },
    onError: (e: Error) => setFeedback({ message: `Failed to delete pattern: ${e.message}`, kind: 'error' }),
  });

  const saveSourceMut = useMutation({
    mutationFn: (s: Partial<EvidenceSource>) => api.saveEvidenceSource(s),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['workflow-sources', id] });
      setEditingSource(null);
      setFeedback({ message: 'Evidence source saved.', kind: 'success' });
    },
    onError: (e: Error) => setFeedback({ message: `Failed to save evidence source: ${e.message}`, kind: 'error' }),
  });

  const deleteSourceMut = useMutation({
    mutationFn: (sourceId: number) => api.deleteEvidenceSource(sourceId),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['workflow-sources', id] });
      setDeleteSourceTarget(null);
      setFeedback({ message: 'Evidence source deleted.', kind: 'success' });
    },
    onError: (e: Error) => setFeedback({ message: `Failed to delete evidence source: ${e.message}`, kind: 'error' }),
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
  const runsTotalPages = Math.max(1, Math.ceil(runs.length / RUNS_PAGE_SIZE));
  const pagedRuns = runs.slice((runsPage - 1) * RUNS_PAGE_SIZE, runsPage * RUNS_PAGE_SIZE);

  return (
    <div className="space-y-4 px-4 lg:px-16" data-stagger>
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

      <Tabs
        tabs={[
          { key: 'overview', label: 'Overview' },
          { key: 'runs', label: 'Run History', count: runs.length },
          { key: 'patterns', label: 'Fraud Patterns', count: patterns.length },
          { key: 'sources', label: 'Evidence Sources', count: sources.length },
        ]}
        active={tab}
        onChange={setTab}
      />

      {tab === 'overview' && (
        <div>
          {emailRuns.length > 0 ? (
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
          ) : (
            <div className="panel p-8 text-center text-gray-600 text-xs">No reports sent yet</div>
          )}
        </div>
      )}

      {tab === 'runs' && (
        <div>
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
                  {pagedRuns.map((r) => (
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

          {runs.length > RUNS_PAGE_SIZE && (
            <div className="mt-3 flex items-center justify-between text-xs">
              <span className="font-mono text-[11px] text-gray-500 tnum">Page {runsPage} / {runsTotalPages}</span>
              <div className="flex items-center gap-2">
                <button
                  onClick={() => setRunsPage((p) => Math.max(1, p - 1))}
                  disabled={runsPage <= 1}
                  className="px-2.5 py-1 border border-gray-800 rounded-md text-gray-300 disabled:opacity-40 hover:border-gray-700 hover:bg-gray-900/60 transition-colors"
                >
                  Previous
                </button>
                <button
                  onClick={() => setRunsPage((p) => Math.min(runsTotalPages, p + 1))}
                  disabled={runsPage >= runsTotalPages}
                  className="px-2.5 py-1 border border-gray-800 rounded-md text-gray-300 disabled:opacity-40 hover:border-gray-700 hover:bg-gray-900/60 transition-colors"
                >
                  Next
                </button>
              </div>
            </div>
          )}
        </div>
      )}

      {tab === 'patterns' && (
        <div>
          <div className="flex items-center justify-end mb-2">
            <button onClick={() => setEditingPattern({ ...EMPTY_PATTERN, workflowId: id })} className={btnPrimary}>
              Add Pattern
            </button>
          </div>
          <div className={tableWrap}>
            <div className="overflow-x-auto">
              <table className="w-full text-sm min-w-[560px]">
                <thead className="bg-gray-900/60">
                  <tr className="border-b border-gray-800">
                    <th className={thCls}>Name</th>
                    <th className={thCls}>Category</th>
                    <th className={thCls}>Status</th>
                    <th className={thCls}>Actions</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-800/50">
                  {patterns.map((p) => (
                    <tr key={p.id} className="hover:bg-emerald-500/[0.03] transition-colors">
                      <td className={`${tdCls} ${p.enabled ? 'text-gray-200' : 'text-gray-600 line-through'}`}>{p.name}</td>
                      <td className={`${tdCls} font-mono text-gray-500`}>{p.category}</td>
                      <td className={tdCls}>
                        <button
                          onClick={() => savePatternMut.mutate({ ...p, enabled: !p.enabled })}
                          className={`px-1.5 py-0.5 font-mono text-[10px] uppercase tracking-wide rounded border transition-colors ${
                            p.enabled
                              ? 'bg-emerald-500/10 text-emerald-400 border-emerald-500/25 hover:bg-emerald-500/20'
                              : 'bg-gray-800/80 text-gray-500 border-gray-700/50 hover:bg-gray-700/60'
                          }`}
                        >
                          {p.enabled ? 'Enabled' : 'Disabled'}
                        </button>
                      </td>
                      <td className={tdCls}>
                        <div className="flex items-center gap-1">
                          <button onClick={() => setEditingPattern({ ...p })} className="p-1.5 text-gray-500 hover:text-white rounded transition-colors" title="Edit">
                            {editIcon}
                          </button>
                          <button onClick={() => setDeletePatternTarget(p)} className="p-1.5 text-gray-500 hover:text-rose-400 rounded transition-colors" title="Delete">
                            {deleteIcon}
                          </button>
                        </div>
                      </td>
                    </tr>
                  ))}
                  {patterns.length === 0 && (
                    <tr><td colSpan={4} className="px-4 py-8 text-center text-gray-600 text-xs">No patterns scoped to this workflow yet</td></tr>
                  )}
                </tbody>
              </table>
            </div>
          </div>
        </div>
      )}

      {tab === 'sources' && (
        <div>
          <div className="flex items-center justify-end mb-2">
            <button onClick={() => setEditingSource({ ...EMPTY_SOURCE, workflowId: id })} className={btnPrimary}>
              Add Evidence Source
            </button>
          </div>
          <div className={tableWrap}>
            <div className="overflow-x-auto">
              <table className="w-full text-sm min-w-[560px]">
                <thead className="bg-gray-900/60">
                  <tr className="border-b border-gray-800">
                    <th className={thCls}>Name</th>
                    <th className={thCls}>Database</th>
                    <th className={thCls}>Status</th>
                    <th className={thCls}>Actions</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-800/50">
                  {sources.map((s) => (
                    <tr key={s.id} className="hover:bg-emerald-500/[0.03] transition-colors">
                      <td className={`${tdCls} ${s.enabled ? 'text-gray-200' : 'text-gray-600 line-through'}`}>{s.name}</td>
                      <td className={`${tdCls} font-mono text-gray-500`}>{s.evidenceDatabase}</td>
                      <td className={tdCls}>
                        <button
                          onClick={() => saveSourceMut.mutate({ ...s, enabled: !s.enabled })}
                          className={`px-1.5 py-0.5 font-mono text-[10px] uppercase tracking-wide rounded border transition-colors ${
                            s.enabled
                              ? 'bg-emerald-500/10 text-emerald-400 border-emerald-500/25 hover:bg-emerald-500/20'
                              : 'bg-gray-800/80 text-gray-500 border-gray-700/50 hover:bg-gray-700/60'
                          }`}
                        >
                          {s.enabled ? 'Enabled' : 'Disabled'}
                        </button>
                      </td>
                      <td className={tdCls}>
                        <div className="flex items-center gap-1">
                          <button onClick={() => setEditingSource({ ...s })} className="p-1.5 text-gray-500 hover:text-white rounded transition-colors" title="Edit">
                            {editIcon}
                          </button>
                          <button onClick={() => setDeleteSourceTarget(s)} className="p-1.5 text-gray-500 hover:text-rose-400 rounded transition-colors" title="Delete">
                            {deleteIcon}
                          </button>
                        </div>
                      </td>
                    </tr>
                  ))}
                  {sources.length === 0 && (
                    <tr><td colSpan={4} className="px-4 py-8 text-center text-gray-600 text-xs">No evidence sources scoped to this workflow yet</td></tr>
                  )}
                </tbody>
              </table>
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

        {editingPattern && (
          <Dialog title={editingPattern.id ? 'Edit pattern' : 'New pattern'} onClose={() => setEditingPattern(null)}>
            <div>
              <label className={label}>Name</label>
              <input
                value={editingPattern.name ?? ''}
                onChange={(e) => setEditingPattern({ ...editingPattern, name: e.target.value })}
                className={inputCls}
                placeholder="e.g. Velocity abuse"
              />
            </div>
            <div>
              <label className={label}>Category</label>
              <select
                value={editingPattern.category ?? 'TransactionAnomaly'}
                onChange={(e) => setEditingPattern({ ...editingPattern, category: e.target.value })}
                className={inputCls}
              >
                {PATTERN_CATEGORIES.map((c) => <option key={c} value={c}>{c}</option>)}
              </select>
            </div>
            <div>
              <label className={label}>Description</label>
              <textarea
                value={editingPattern.description ?? ''}
                onChange={(e) => setEditingPattern({ ...editingPattern, description: e.target.value })}
                rows={5}
                className={`${inputCls} leading-relaxed`}
                placeholder="What should the agent look for, and what context or exemptions apply?"
              />
            </div>
            <label className="flex items-center gap-2 cursor-pointer">
              <input
                type="checkbox"
                checked={editingPattern.enabled ?? true}
                onChange={(e) => setEditingPattern({ ...editingPattern, enabled: e.target.checked })}
                className="accent-emerald-500"
              />
              <span className="text-xs text-gray-300">Enabled</span>
            </label>
            <div className="flex items-center justify-end gap-2 pt-1">
              <button onClick={() => setEditingPattern(null)} className={btnGhost}>Cancel</button>
              <button
                onClick={() => savePatternMut.mutate(editingPattern)}
                disabled={savePatternMut.isPending || !editingPattern.name || !editingPattern.description}
                className={btnPrimary}
              >
                {savePatternMut.isPending ? 'Saving…' : 'Save'}
              </button>
            </div>
          </Dialog>
        )}

        {deletePatternTarget && (
          <Dialog title="Delete pattern" onClose={() => setDeletePatternTarget(null)}>
            <p className="text-xs text-gray-400">
              Delete pattern <span className="text-gray-200">{deletePatternTarget.name}</span>? The agent will stop checking for it on this workflow's runs.
            </p>
            <div className="flex items-center justify-end gap-2 pt-1">
              <button onClick={() => setDeletePatternTarget(null)} className={btnGhost}>Cancel</button>
              <button onClick={() => deletePatternMut.mutate(deletePatternTarget.id)} disabled={deletePatternMut.isPending} className={btnDanger}>
                {deletePatternMut.isPending ? 'Deleting…' : 'Delete'}
              </button>
            </div>
          </Dialog>
        )}

        {editingSource && (
          <Dialog title={editingSource.id ? 'Edit evidence source' : 'New evidence source'} onClose={() => setEditingSource(null)}>
            <div className="grid grid-cols-2 gap-3">
              <div>
                <label className={label}>Name</label>
                <input
                  value={editingSource.name ?? ''}
                  onChange={(e) => setEditingSource({ ...editingSource, name: e.target.value })}
                  className={inputCls}
                  placeholder="e.g. Patumba App"
                />
              </div>
              <div>
                <label className={label}>Evidence database</label>
                <input
                  value={editingSource.evidenceDatabase ?? ''}
                  onChange={(e) => setEditingSource({ ...editingSource, evidenceDatabase: e.target.value })}
                  className={`${inputCls} font-mono`}
                  placeholder="e.g. patumba_app"
                />
              </div>
            </div>
            <div className="grid grid-cols-2 gap-3">
              <div>
                <label className={label}>Lipila merchant IDs</label>
                <input
                  value={editingSource.lipilaMerchantIds ?? ''}
                  onChange={(e) => setEditingSource({ ...editingSource, lipilaMerchantIds: e.target.value })}
                  className={`${inputCls} font-mono`}
                  placeholder="comma-separated"
                />
              </div>
              <div>
                <label className={label}>Lipila partner ID</label>
                <input
                  type="number"
                  value={editingSource.lipilaPartnerId ?? 0}
                  onChange={(e) => setEditingSource({ ...editingSource, lipilaPartnerId: Number(e.target.value) })}
                  className={`${inputCls} font-mono`}
                />
              </div>
            </div>
            <div>
              <label className={label}>Join mappings (JSON)</label>
              <textarea
                value={editingSource.joinMappings ?? ''}
                onChange={(e) => setEditingSource({ ...editingSource, joinMappings: e.target.value })}
                rows={3}
                className={`${inputCls} font-mono leading-relaxed`}
                placeholder='{"lipila.merchant_id": "app.user_id"}'
              />
            </div>
            <div>
              <label className={label}>Table descriptions</label>
              <textarea
                value={editingSource.tableDescriptions ?? ''}
                onChange={(e) => setEditingSource({ ...editingSource, tableDescriptions: e.target.value })}
                rows={3}
                className={`${inputCls} leading-relaxed`}
                placeholder="Tables available in this database and what they contain"
              />
            </div>
            <div>
              <label className={label}>Evidence checks (JSON array)</label>
              <textarea
                value={editingSource.evidenceChecks ?? ''}
                onChange={(e) => setEditingSource({ ...editingSource, evidenceChecks: e.target.value })}
                rows={3}
                className={`${inputCls} font-mono leading-relaxed`}
                placeholder='["Check withdrawal history for this user", "..."]'
              />
            </div>
            <div>
              <label className={label}>Notes</label>
              <textarea
                value={editingSource.notes ?? ''}
                onChange={(e) => setEditingSource({ ...editingSource, notes: e.target.value })}
                rows={2}
                className={`${inputCls} leading-relaxed`}
              />
            </div>
            <label className="flex items-center gap-2 cursor-pointer">
              <input
                type="checkbox"
                checked={editingSource.enabled ?? true}
                onChange={(e) => setEditingSource({ ...editingSource, enabled: e.target.checked })}
                className="accent-emerald-500"
              />
              <span className="text-xs text-gray-300">Enabled</span>
            </label>
            <div className="flex items-center justify-end gap-2 pt-1">
              <button onClick={() => setEditingSource(null)} className={btnGhost}>Cancel</button>
              <button
                onClick={() => saveSourceMut.mutate(editingSource)}
                disabled={saveSourceMut.isPending || !editingSource.name || !editingSource.evidenceDatabase}
                className={btnPrimary}
              >
                {saveSourceMut.isPending ? 'Saving…' : 'Save'}
              </button>
            </div>
          </Dialog>
        )}

        {deleteSourceTarget && (
          <Dialog title="Delete evidence source" onClose={() => setDeleteSourceTarget(null)}>
            <p className="text-xs text-gray-400">
              Delete evidence source <span className="text-gray-200">{deleteSourceTarget.name}</span>?
            </p>
            <div className="flex items-center justify-end gap-2 pt-1">
              <button onClick={() => setDeleteSourceTarget(null)} className={btnGhost}>Cancel</button>
              <button onClick={() => deleteSourceMut.mutate(deleteSourceTarget.id)} disabled={deleteSourceMut.isPending} className={btnDanger}>
                {deleteSourceMut.isPending ? 'Deleting…' : 'Delete'}
              </button>
            </div>
          </Dialog>
        )}
      </AnimatePresence>
    </div>
  );
}
