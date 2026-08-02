import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { AnimatePresence } from 'motion/react';
import { api, runWorkflowId, type WorkflowDefinition } from '../api';
import {
  PageHeader, Feedback, Dialog, Spinner, EmptyState,
  btnPrimary, btnDanger, btnGhost, btnOutline, inputCls, fmtDate, LiveRunPill,
} from '../components/ui';
import { MarkdownEditor } from '../components/MarkdownEditor';

export const TIMEZONES = [
  { id: 'Africa/Lusaka', label: 'Central Africa Time (CAT)' },
  { id: 'UTC', label: 'UTC' },
];

/** Target databases are stored as one comma-separated string, mirroring emailRecipients. */
export const parseDatabases = (v: string | undefined | null): string[] =>
  (v ?? '').split(',').map((s) => s.trim()).filter(Boolean);

/**
 * First readable sentences of a prompt, for the card when no description is set.
 * Prompts are markdown now, so the raw text leads with "#" and "|" noise — strip the
 * syntax and skip heading/table lines rather than showing a wall of pipes.
 */
function summarisePrompt(prompt: string | undefined | null): string {
  return (prompt ?? '')
    .split('\n')
    .map((l) => l.trim())
    .filter((l) => l && !l.startsWith('#') && !l.startsWith('|') && !l.startsWith('---') && !l.startsWith('```'))
    .join(' ')
    .replace(/[*_`>]/g, '')
    .slice(0, 220);
}

function DatabasePicker({
  products,
  selected,
  onChange,
  multiple,
}: {
  products: { databaseName: string; displayName: string }[];
  selected: string[];
  onChange: (dbs: string[]) => void;
  multiple: boolean;
}) {
  const toggle = (db: string) => {
    if (!multiple) return onChange(selected[0] === db ? [] : [db]);
    // Order matters — the first selected database is the agent's primary.
    onChange(selected.includes(db) ? selected.filter((d) => d !== db) : [...selected, db]);
  };

  return (
    <div className="flex flex-wrap gap-1.5">
      {products.map((p) => {
        const idx = selected.indexOf(p.databaseName);
        const on = idx >= 0;
        return (
          <button
            key={p.databaseName}
            type="button"
            onClick={() => toggle(p.databaseName)}
            className={`px-2.5 py-1 rounded-md border text-xs transition-all ${
              on
                ? 'border-emerald-500/50 bg-emerald-500/10 text-emerald-300'
                : 'border-gray-700/80 text-gray-400 hover:border-gray-600 hover:text-gray-200'
            }`}
          >
            {on && multiple && (
              <span className="mr-1.5 font-mono text-[10px] text-emerald-500/80">{idx === 0 ? 'primary' : idx + 1}</span>
            )}
            {p.displayName}
          </button>
        );
      })}
      {products.length === 0 && <span className="text-xs text-gray-600">No databases configured.</span>}
    </div>
  );
}

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
  model: '',
  reasoningEffort: '',
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
  const { data: models = [] } = useQuery({ queryKey: ['models-enabled'], queryFn: api.enabledModels });
  const { data: providers = [] } = useQuery({ queryKey: ['providers-enabled'], queryFn: api.enabledProviders });

  const defaultProviderId = providers.find((p) => p.isDefault)?.id;
  const providerModels = defaultProviderId ? models.filter((m) => m.providerId === defaultProviderId) : models;
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
        <label className="block font-mono text-[10px] uppercase tracking-wider text-gray-500 mb-1">
          Target databases
          {value.actionType === 'fraud_run' && <span className="ml-2 normal-case tracking-normal text-gray-600">fraud runs use the first one only</span>}
        </label>
        <DatabasePicker
          products={products}
          selected={parseDatabases(value.targetDatabase)}
          onChange={(dbs) => onChange({ ...value, targetDatabase: dbs.join(',') })}
          multiple={value.actionType !== 'fraud_run'}
        />
      </div>
      <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
        <div>
          <label className="block font-mono text-[10px] uppercase tracking-wider text-gray-500 mb-1">Model</label>
          <select value={value.model ?? ''} onChange={(e) => onChange({ ...value, model: e.target.value })} className={inputCls}>
            <option value="">— system default —</option>
            {providerModels.map((m) => <option key={m.modelId} value={m.modelId}>{m.displayName}{m.isDefault ? ' (default)' : ''}</option>)}
          </select>
        </div>
        <div>
          <label className="block font-mono text-[10px] uppercase tracking-wider text-gray-500 mb-1">Reasoning effort</label>
          <select value={value.reasoningEffort ?? ''} onChange={(e) => onChange({ ...value, reasoningEffort: e.target.value })} className={inputCls}>
            <option value="">— model default —</option>
            <option value="low">Low</option>
            <option value="medium">Medium</option>
            <option value="high">High</option>
          </select>
        </div>
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
        <MarkdownEditor
          value={value.customPrompt ?? ''}
          onChange={(v) => onChange({ ...value, customPrompt: v })}
          rows={16}
          placeholder="What should this workflow analyse and report? Markdown supported — use headings for report sections and tables for the metric layout."
        />
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

  // Runs record their origin in `triggeredBy` rather than a foreign key, so this is how a
  // workflow learns it is currently running. Polled so the badge appears without a refresh.
  const { data: activeRuns = [] } = useQuery({
    queryKey: ['active-runs'],
    queryFn: api.activeRuns,
    refetchInterval: 5000,
  });

  const runByWorkflow = new Map(
    activeRuns
      .map((r) => [runWorkflowId(r.triggeredBy), r] as const)
      .filter((e): e is [string, (typeof activeRuns)[number]] => e[0] !== null)
  );

  const visible = workflows.filter((w) => !w.isDeleted);

  return (
    <div className="space-y-4 px-4 lg:px-16">
      <PageHeader title="Workflows" subtitle="Scheduled agent jobs — reports and fraud sweeps on a cron">
        <button onClick={() => setEditing({ ...EMPTY })} className={btnPrimary}>New Workflow</button>
      </PageHeader>

      {feedback && <Feedback message={feedback.message} kind={feedback.kind} onDismiss={() => setFeedback(null)} />}

      {isLoading && <div className="flex justify-center py-8"><Spinner /></div>}

      {/* Full width until xl: a workflow can target five databases, and two narrow columns
          forced the metadata row to overflow. */}
      <div className="grid grid-cols-1 xl:grid-cols-2 gap-3" data-stagger>
        {visible.map((w) => {
          const databases = parseDatabases(w.targetDatabase);
          const isReport = w.actionType !== 'fraud_run';
          const activeRun = runByWorkflow.get(w.id);
          return (
            <div key={w.id} className={`panel panel-hover p-5 flex flex-col ${!w.enabled ? 'opacity-60' : ''}`}>
              <div className="flex items-start justify-between gap-3">
                <div className="min-w-0">
                  <Link
                    to={`/workflows/${w.id}`}
                    className="font-display text-[15px] font-semibold text-white hover:text-emerald-300 transition-colors flex items-center gap-2"
                  >
                    {w.enabled && <span className="glow-dot shrink-0" style={{ height: 6, width: 6 }} />}
                    <span className="truncate">{w.name}</span>
                  </Link>
                  {(w.description || w.customPrompt) && (
                    <p className="text-xs text-gray-500 mt-1.5 leading-relaxed line-clamp-2">
                      {w.description || summarisePrompt(w.customPrompt)}
                    </p>
                  )}
                </div>
                <div className="flex items-center gap-1.5 shrink-0">
                  {activeRun && <LiveRunPill to={`/runs/${activeRun.runId}`} label={activeRun.status} />}
                  <span className={`px-2 py-0.5 font-mono text-[10px] uppercase tracking-wide rounded border ${
                    isReport
                      ? 'bg-sky-500/10 border-sky-500/25 text-sky-300'
                      : 'bg-amber-500/10 border-amber-500/25 text-amber-300'
                  }`}>
                    {isReport ? 'Report' : 'Fraud'}
                  </span>
                  {!w.enabled && (
                    <span className="px-2 py-0.5 font-mono text-[10px] uppercase tracking-wide rounded bg-gray-800 text-gray-500">
                      Paused
                    </span>
                  )}
                </div>
              </div>

              {/* Labelled rows rather than one undifferentiated strip of 10px text */}
              <dl className="mt-4 space-y-2 text-xs">
                <div className="flex items-baseline gap-3">
                  <dt className="kicker w-20 shrink-0">Schedule</dt>
                  <dd className="flex items-center gap-2 flex-wrap min-w-0">
                    <code className="font-mono text-[11px] bg-gray-800/80 border border-gray-700/50 px-1.5 py-0.5 rounded text-gray-300">
                      {w.cronExpression}
                    </code>
                    <span className="text-gray-600">{w.timeZoneId}</span>
                  </dd>
                </div>
                <div className="flex items-baseline gap-3">
                  <dt className="kicker w-20 shrink-0">{databases.length > 1 ? 'Databases' : 'Database'}</dt>
                  <dd className="flex items-center gap-1.5 flex-wrap min-w-0">
                    {databases.length === 0 && <span className="text-gray-600">—</span>}
                    {databases.map((db, i) => (
                      <span
                        key={db}
                        title={i === 0 && databases.length > 1 ? 'Primary — supplies the default schema' : undefined}
                        className={`font-mono text-[11px] px-1.5 py-0.5 rounded border ${
                          i === 0
                            ? 'bg-sky-500/10 border-sky-500/25 text-sky-300'
                            : 'bg-gray-800/60 border-gray-700/50 text-gray-400'
                        }`}
                      >
                        {db}
                      </span>
                    ))}
                  </dd>
                </div>
                {w.model && (
                  <div className="flex items-baseline gap-3">
                    <dt className="kicker w-20 shrink-0">Model</dt>
                    <dd className="font-mono text-[11px] text-violet-300/90 truncate">{w.model}</dd>
                  </div>
                )}
              </dl>

              <div className="flex items-center gap-2 mt-4 pt-3 border-t border-gray-800/60">
                {activeRun ? (
                  <Link to={`/runs/${activeRun.runId}`} className={btnOutline}>View live logs</Link>
                ) : (
                  <button
                    onClick={() => triggerMut.mutate(w.id)}
                    disabled={triggerMut.isPending}
                    className={btnOutline}
                  >
                    {triggerMut.isPending && triggerMut.variables === w.id ? 'Queueing…' : 'Run now'}
                  </button>
                )}
                <button onClick={() => saveMut.mutate({ ...w, enabled: !w.enabled })} className={btnOutline}>
                  {w.enabled ? 'Pause' : 'Enable'}
                </button>
                <button onClick={() => setEditing({ ...w })} className={btnOutline}>Edit</button>
                <span className="ml-auto flex items-center gap-2">
                  <span className="font-mono text-[10px] text-gray-600 hidden sm:inline">
                    {fmtDate(w.updatedAt)}
                  </span>
                  <button
                    onClick={() => setDeleteTarget(w)}
                    className="p-1.5 text-gray-600 hover:text-rose-400 rounded transition-colors"
                    title="Delete workflow"
                  >
                    <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                    </svg>
                  </button>
                </span>
              </div>
            </div>
          );
        })}
      </div>

      {!isLoading && visible.length === 0 && (
        <EmptyState title="No workflows yet" hint="Create one to get scheduled reports or fraud sweeps." />
      )}

      <AnimatePresence>
      {editing && (
        <Dialog title={editing.id ? 'Edit workflow' : 'New workflow'} onClose={() => setEditing(null)} size="lg">
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
