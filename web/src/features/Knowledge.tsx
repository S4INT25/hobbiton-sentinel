import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { AnimatePresence } from 'motion/react';
import { api, type AgentMemory } from '../api';
import {
  PageHeader, Dialog, Spinner, EmptyState,
  btnPrimary, btnDanger, btnGhost, inputCls, fmtDateFull,
} from '../components/ui';

const DATABASES = [
  { value: '', label: 'All databases' },
  { value: 'inshuwa', label: 'Inshuwa (Insurance)' },
  { value: 'lipila_blaze', label: 'Lipila Blaze' },
  { value: 'bnpl', label: 'BNPL' },
  { value: 'patumba_app', label: 'Patumba App' },
];

const EMPTY: Partial<AgentMemory> = { term: '', definition: '', database: '', enabled: true };
const label = 'block font-mono text-[10px] uppercase tracking-wider text-gray-500 mb-1';

export default function Knowledge() {
  const qc = useQueryClient();
  const { data: memories = [], isLoading } = useQuery({ queryKey: ['knowledge'], queryFn: api.listKnowledge });

  const [editing, setEditing] = useState<Partial<AgentMemory> | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<AgentMemory | null>(null);
  const [formError, setFormError] = useState<string | null>(null);
  const [expanded, setExpanded] = useState<Set<number>>(new Set());

  const toggleExpand = (id: number) =>
    setExpanded((prev) => {
      const next = new Set(prev);
      if (!next.delete(id)) next.add(id);
      return next;
    });

  const saveMut = useMutation({
    mutationFn: () => api.saveKnowledge(editing!),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['knowledge'] });
      setEditing(null);
    },
  });

  const deleteMut = useMutation({
    mutationFn: (id: number) => api.deleteKnowledge(id),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['knowledge'] });
      setDeleteTarget(null);
    },
  });

  const save = () => {
    setFormError(null);
    if (!editing?.term?.trim()) { setFormError('Term is required.'); return; }
    if (!editing?.definition?.trim()) { setFormError('Definition is required.'); return; }
    saveMut.mutate();
  };

  return (
    <div className="space-y-6">
      <PageHeader
        title="Agent Knowledge Base"
        subtitle="Define business terms and calculation rules the agent will use when answering questions"
      >
        <button onClick={() => { setFormError(null); setEditing({ ...EMPTY }); }} className={btnPrimary}>
          Add Definition
        </button>
      </PageHeader>

      {isLoading && <div className="flex justify-center py-8"><Spinner /></div>}

      {!isLoading && memories.length === 0 && (
        <EmptyState
          title="No definitions yet."
          hint='Add terms like "Revenue", "Active Merchant", or "Churn Rate" and tell the agent exactly how to calculate them.'
        />
      )}

      {memories.length > 0 && (
        <div className="space-y-2" data-stagger>
          {memories.map((m) => {
            const isExpanded = expanded.has(m.id);
            const isLong = (m.definition?.length ?? 0) > 200;
            return (
              <div
                key={m.id}
                className={`panel panel-hover p-4 ${!m.enabled ? 'opacity-60' : ''}`}
              >
                <div className="flex items-start justify-between gap-3">
                  <div className="flex-1 min-w-0">
                    <div className="flex items-center gap-2 flex-wrap">
                      <span className="font-display text-sm font-semibold text-white">{m.term}</span>
                      {!m.enabled && (
                        <span className="px-1.5 py-0.5 font-mono text-[10px] uppercase tracking-wide rounded bg-gray-800 text-gray-500">Disabled</span>
                      )}
                      {m.database ? (
                        <span className="px-1.5 py-0.5 font-mono text-[10px] rounded bg-sky-500/10 border border-sky-500/25 text-sky-300">{m.database}</span>
                      ) : (
                        <span className="px-1.5 py-0.5 font-mono text-[10px] rounded bg-gray-800 text-gray-500">All databases</span>
                      )}
                    </div>
                    <div className="relative mt-1.5">
                      <p className={`text-xs text-gray-400 leading-relaxed whitespace-pre-wrap ${isExpanded ? '' : 'line-clamp-3'}`}>
                        {m.definition}
                      </p>
                      {!isExpanded && isLong && (
                        <div className="absolute bottom-0 left-0 right-0 h-6 bg-gradient-to-t from-gray-900/90 to-transparent pointer-events-none" />
                      )}
                    </div>
                    {isLong && (
                      <button onClick={() => toggleExpand(m.id)} className="mt-1 text-[10px] text-emerald-400 hover:text-emerald-300 transition-colors">
                        {isExpanded ? 'Show less' : 'Show more'}
                      </button>
                    )}
                    <p className="mt-1.5 font-mono text-[10px] text-gray-600">
                      Updated {fmtDateFull(m.updatedAt)}
                      {m.createdBy && <span> · {m.createdBy}</span>}
                    </p>
                  </div>
                  <div className="flex items-center gap-1 shrink-0">
                    <button
                      onClick={() => { setFormError(null); setEditing({ ...m }); }}
                      className="p-1.5 text-gray-500 hover:text-white rounded transition-colors"
                      title="Edit"
                    >
                      <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z" />
                      </svg>
                    </button>
                    <button
                      onClick={() => setDeleteTarget(m)}
                      className="p-1.5 text-gray-500 hover:text-rose-400 rounded transition-colors"
                      title="Delete"
                    >
                      <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                      </svg>
                    </button>
                  </div>
                </div>
              </div>
            );
          })}
        </div>
      )}

      <AnimatePresence>
        {editing && (
          <Dialog title={editing.id ? 'Edit Definition' : 'New Definition'} onClose={() => setEditing(null)}>
            <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
              <div>
                <label className={label}>Term <span className="text-rose-400">*</span></label>
                <input value={editing.term ?? ''} onChange={(e) => setEditing({ ...editing, term: e.target.value })} className={inputCls} placeholder="e.g. Revenue" />
              </div>
              <div>
                <label className={label}>Database Scope</label>
                <select value={editing.database ?? ''} onChange={(e) => setEditing({ ...editing, database: e.target.value })} className={inputCls}>
                  {DATABASES.map((d) => <option key={d.value} value={d.value}>{d.label}</option>)}
                </select>
              </div>
            </div>
            <div>
              <label className={label}>Definition / Calculation Rule <span className="text-rose-400">*</span></label>
              <textarea
                value={editing.definition ?? ''}
                onChange={(e) => setEditing({ ...editing, definition: e.target.value })}
                rows={5}
                className={`${inputCls} leading-relaxed`}
                placeholder="Explain exactly how this metric is calculated. E.g.: Revenue is the SUM of the `amount` column in public_transactions WHERE status = 'completed'..."
              />
            </div>
            <label className="flex items-center gap-2 cursor-pointer">
              <input type="checkbox" checked={editing.enabled ?? true} onChange={(e) => setEditing({ ...editing, enabled: e.target.checked })} className="accent-emerald-500" />
              <span className="text-xs text-gray-400">Enabled (agent will use this definition)</span>
            </label>
            {formError && <p className="text-xs text-rose-400">{formError}</p>}
            <div className="flex items-center gap-2 pt-1">
              <button onClick={save} disabled={saveMut.isPending} className={btnPrimary}>
                {saveMut.isPending ? 'Saving…' : 'Save'}
              </button>
              <button onClick={() => setEditing(null)} className={btnGhost}>Cancel</button>
            </div>
          </Dialog>
        )}

        {deleteTarget && (
          <Dialog title="Delete definition" onClose={() => setDeleteTarget(null)}>
            <p className="text-xs text-gray-400">
              Delete <span className="text-gray-200">{deleteTarget.term}</span>? The agent will stop using this definition.
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
