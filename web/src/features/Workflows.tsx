import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { AnimatePresence } from 'motion/react';
import { api, type WorkflowDefinition } from '../api';
import {
  PageHeader, Feedback, Dialog, Spinner, EmptyState,
  btnPrimary, btnDanger, btnGhost, inputCls, fmtDate,
} from '../components/ui';

export const TIMEZONES = [
  { id: 'Africa/Lusaka', label: 'Central Africa Time (CAT)' },
  { id: 'UTC', label: 'UTC' },
];

export const ACTION_TYPES = [
  { value: 'email_report', label: 'Email report (analytics agent)' },
  { value: 'fraud_run', label: 'Fraud detection run' },
];

const EMPTY: Partial<WorkflowDefinition> = {
  name: '',
  description: '',
  actionType: 'email_report',
  cronExpression: '0 8 * * *',
  timeZoneId: 'Africa/Lusaka',
  enabled: true,
  targetDatabase: '',
  emailSubject: '',
  emailRecipients: '',
  customPrompt: '',
};

export function WorkflowForm({
  value,
  onChange,
}: {
  value: Partial<WorkflowDefinition>;
  onChange: (v: Partial<WorkflowDefinition>) => void;
}) {
  const { data: products = [] } = useQuery({ queryKey: ['products-enabled'], queryFn: api.enabledProducts });
  return (
    <div className="space-y-3">
      <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
        <div>
          <label className="block font-mono text-[10px] uppercase tracking-wider text-gray-500 mb-1">Name</label>
          <input value={value.name ?? ''} onChange={(e) => onChange({ ...value, name: e.target.value })} className={inputCls} />
        </div>
        <div>
          <label className="block font-mono text-[10px] uppercase tracking-wider text-gray-500 mb-1">Action</label>
          <select value={value.actionType} onChange={(e) => onChange({ ...value, actionType: e.target.value })} className={inputCls}>
            {ACTION_TYPES.map((a) => <option key={a.value} value={a.value}>{a.label}</option>)}
          </select>
        </div>
      </div>
      <div>
        <label className="block font-mono text-[10px] uppercase tracking-wider text-gray-500 mb-1">Description</label>
        <input value={value.description ?? ''} onChange={(e) => onChange({ ...value, description: e.target.value })} className={inputCls} />
      </div>
      <div className="grid grid-cols-2 gap-3">
        <div>
          <label className="block font-mono text-[10px] uppercase tracking-wider text-gray-500 mb-1">Cron expression</label>
          <input value={value.cronExpression ?? ''} onChange={(e) => onChange({ ...value, cronExpression: e.target.value })} className={`${inputCls} font-mono`} placeholder="0 8 * * *" />
        </div>
        <div>
          <label className="block font-mono text-[10px] uppercase tracking-wider text-gray-500 mb-1">Time zone</label>
          <select value={value.timeZoneId} onChange={(e) => onChange({ ...value, timeZoneId: e.target.value })} className={inputCls}>
            {TIMEZONES.map((t) => <option key={t.id} value={t.id}>{t.label}</option>)}
          </select>
        </div>
      </div>
      <div>
        <label className="block font-mono text-[10px] uppercase tracking-wider text-gray-500 mb-1">Target database</label>
        <select value={value.targetDatabase ?? ''} onChange={(e) => onChange({ ...value, targetDatabase: e.target.value })} className={inputCls}>
          <option value="">— select —</option>
          {products.map((p) => <option key={p.databaseName} value={p.databaseName}>{p.displayName}</option>)}
        </select>
      </div>
      {value.actionType !== 'fraud_run' && (
        <>
          <div>
            <label className="block font-mono text-[10px] uppercase tracking-wider text-gray-500 mb-1">Email subject</label>
            <input value={value.emailSubject ?? ''} onChange={(e) => onChange({ ...value, emailSubject: e.target.value })} className={inputCls} />
          </div>
          <div>
            <label className="block font-mono text-[10px] uppercase tracking-wider text-gray-500 mb-1">Recipients (comma-separated)</label>
            <input value={value.emailRecipients ?? ''} onChange={(e) => onChange({ ...value, emailRecipients: e.target.value })} className={inputCls} placeholder="a@hobbiton.co.zm, b@hobbiton.co.zm" />
          </div>
        </>
      )}
      <div>
        <label className="block font-mono text-[10px] uppercase tracking-wider text-gray-500 mb-1">Prompt / instructions for the agent</label>
        <textarea value={value.customPrompt ?? ''} onChange={(e) => onChange({ ...value, customPrompt: e.target.value })} rows={4} className={`${inputCls} leading-relaxed`} placeholder="What should this workflow analyse and report?" />
      </div>
      <label className="flex items-center gap-2 cursor-pointer">
        <input type="checkbox" checked={value.enabled ?? true} onChange={(e) => onChange({ ...value, enabled: e.target.checked })} className="accent-emerald-500" />
        <span className="text-xs text-gray-300">Enabled (runs on schedule)</span>
      </label>
    </div>
  );
}

export default function Workflows() {
  const qc = useQueryClient();
  const { data: workflows = [], isLoading } = useQuery({ queryKey: ['workflows'], queryFn: api.listWorkflows });

  const [feedback, setFeedback] = useState<{ message: string; kind: 'success' | 'error' } | null>(null);
  const [editing, setEditing] = useState<Partial<WorkflowDefinition> | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<WorkflowDefinition | null>(null);

  const invalidate = () => qc.invalidateQueries({ queryKey: ['workflows'] });

  const saveMut = useMutation({
    mutationFn: (wf: Partial<WorkflowDefinition>) => api.saveWorkflow(wf),
    onSuccess: () => {
      invalidate();
      setEditing(null);
      setFeedback({ message: 'Workflow saved.', kind: 'success' });
    },
    onError: (e: Error) => setFeedback({ message: `Failed to save workflow: ${e.message}`, kind: 'error' }),
  });

  const deleteMut = useMutation({
    mutationFn: (id: string) => api.deleteWorkflow(id),
    onSuccess: () => {
      invalidate();
      setDeleteTarget(null);
      setFeedback({ message: 'Workflow deleted.', kind: 'success' });
    },
    onError: (e: Error) => setFeedback({ message: `Failed to delete workflow: ${e.message}`, kind: 'error' }),
  });

  const triggerMut = useMutation({
    mutationFn: (id: string) => api.triggerWorkflow(id),
    onSuccess: () => setFeedback({ message: 'Workflow run queued.', kind: 'success' }),
    onError: (e: Error) => setFeedback({ message: `Failed to trigger: ${e.message}`, kind: 'error' }),
  });

  const visible = workflows.filter((w) => !w.isDeleted);

  return (
    <div className="space-y-4">
      <PageHeader title="Workflows" subtitle="Scheduled agent jobs — reports and fraud sweeps on a cron">
        <button onClick={() => setEditing({ ...EMPTY })} className={btnPrimary}>New Workflow</button>
      </PageHeader>

      {feedback && <Feedback message={feedback.message} kind={feedback.kind} onDismiss={() => setFeedback(null)} />}

      {isLoading && <div className="flex justify-center py-8"><Spinner /></div>}

      <div className="grid grid-cols-1 md:grid-cols-2 gap-3" data-stagger>
        {visible.map((w) => (
          <div key={w.id} className={`panel panel-hover p-4 ${!w.enabled ? 'opacity-60' : ''}`}>
            <div className="flex items-start justify-between gap-2">
              <div className="min-w-0">
                <Link to={`/workflows/${w.id}`} className="font-display text-sm font-semibold text-white hover:text-emerald-300 transition-colors flex items-center gap-2">
                  {w.enabled && <span className="glow-dot shrink-0" style={{ height: 5, width: 5 }} />}
                  <span className="truncate">{w.name}</span>
                </Link>
                <p className="text-xs text-gray-500 mt-1 line-clamp-2">{w.description || w.customPrompt}</p>
              </div>
              {!w.enabled && (
                <span className="px-1.5 py-0.5 font-mono text-[10px] uppercase tracking-wide rounded bg-gray-800 text-gray-500 shrink-0">Disabled</span>
              )}
            </div>
            <div className="flex items-center gap-3 mt-3 font-mono text-[10px] text-gray-600">
              <span className="bg-gray-800/80 border border-gray-700/50 px-1.5 py-0.5 rounded">{w.cronExpression}</span>
              <span>{w.timeZoneId}</span>
              {w.targetDatabase && <span className="text-sky-400/80">{w.targetDatabase}</span>}
              <span>updated {fmtDate(w.updatedAt)}</span>
            </div>
            <div className="flex items-center gap-2 mt-3 pt-3 border-t border-gray-800/60">
              <button onClick={() => triggerMut.mutate(w.id)} disabled={triggerMut.isPending} className="text-xs text-emerald-400 hover:text-emerald-300 transition-colors disabled:opacity-50">
                Run now
              </button>
              <button onClick={() => saveMut.mutate({ ...w, enabled: !w.enabled })} className="text-xs text-gray-500 hover:text-gray-300 transition-colors">
                {w.enabled ? 'Disable' : 'Enable'}
              </button>
              <button onClick={() => setEditing({ ...w })} className="text-xs text-gray-500 hover:text-white transition-colors">Edit</button>
              <button onClick={() => setDeleteTarget(w)} className="text-xs text-gray-600 hover:text-rose-400 transition-colors ml-auto">Delete</button>
            </div>
          </div>
        ))}
      </div>

      {!isLoading && visible.length === 0 && (
        <EmptyState title="No workflows yet" hint="Create one to get scheduled reports or fraud sweeps." />
      )}

      <AnimatePresence>
      {editing && (
        <Dialog title={editing.id ? 'Edit workflow' : 'New workflow'} onClose={() => setEditing(null)}>
          <WorkflowForm value={editing} onChange={setEditing} />
          <div className="flex items-center justify-end gap-2 pt-1">
            <button onClick={() => setEditing(null)} className={btnGhost}>Cancel</button>
            <button
              onClick={() => saveMut.mutate(editing)}
              disabled={saveMut.isPending || !editing.name || !editing.cronExpression}
              className={btnPrimary}
            >
              {saveMut.isPending ? 'Saving…' : 'Save'}
            </button>
          </div>
        </Dialog>
      )}

      {deleteTarget && (
        <Dialog title="Delete workflow" onClose={() => setDeleteTarget(null)}>
          <p className="text-xs text-gray-400">
            Delete <span className="text-gray-200">{deleteTarget.name}</span>? Its schedule will be removed.
          </p>
          <div className="flex items-center justify-end gap-2 pt-1">
            <button onClick={() => setDeleteTarget(null)} className={btnGhost}>Cancel</button>
            <button onClick={() => deleteMut.mutate(deleteTarget.id)} disabled={deleteMut.isPending} className={btnDanger}>
              {deleteMut.isPending ? 'Deleting…' : 'Delete'}
            </button>
          </div>
        </Dialog>
      )}
      </AnimatePresence>
    </div>
  );
}
