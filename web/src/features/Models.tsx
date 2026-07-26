import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { AnimatePresence } from 'motion/react';
import { api, type LlmModel } from '../api';
import {
  PageHeader, Feedback, Dialog, Spinner,
  btnPrimary, btnDanger, btnGhost, inputCls, tableWrap, thCls, tdCls,
} from '../components/ui';

const EMPTY: Partial<LlmModel> = { displayName: '', modelId: '', description: '', providerId: 0, enabled: true, isDefault: false, sortOrder: 0 };
const label = 'block font-mono text-[10px] uppercase tracking-wider text-gray-500 mb-1';

export default function Models() {
  const qc = useQueryClient();
  const { data: models = [], isLoading } = useQuery({ queryKey: ['models'], queryFn: api.listModels });
  const { data: providers = [] } = useQuery({ queryKey: ['providers-enabled'], queryFn: api.enabledProviders });

  const [feedback, setFeedback] = useState<{ message: string; kind: 'success' | 'error' } | null>(null);
  const [editing, setEditing] = useState<Partial<LlmModel> | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<LlmModel | null>(null);

  const invalidate = () => {
    qc.invalidateQueries({ queryKey: ['models'] });
    qc.invalidateQueries({ queryKey: ['models-enabled'] });
  };

  const saveMut = useMutation({
    mutationFn: () => api.saveModel(editing!),
    onSuccess: () => {
      invalidate();
      setEditing(null);
      setFeedback({ message: 'Model saved.', kind: 'success' });
    },
    onError: (e: Error) => setFeedback({ message: `Failed to save: ${e.message}`, kind: 'error' }),
  });

  const deleteMut = useMutation({
    mutationFn: (id: number) => api.deleteModel(id),
    onSuccess: () => {
      invalidate();
      setDeleteTarget(null);
      setFeedback({ message: 'Model deleted.', kind: 'success' });
    },
    onError: (e: Error) => setFeedback({ message: `Failed to delete: ${e.message}`, kind: 'error' }),
  });

  return (
    <div className="space-y-4 px-4 lg:px-16" data-stagger>
      <PageHeader title="Models" subtitle="OpenRouter models available to chat and workflows — pick a default, add your own">
        <button onClick={() => setEditing({ ...EMPTY })} className={btnPrimary}>Add Model</button>
      </PageHeader>

      {feedback && <Feedback message={feedback.message} kind={feedback.kind} onDismiss={() => setFeedback(null)} />}

      <div className={tableWrap}>
        <div className="overflow-x-auto">
          <table className="w-full text-sm min-w-[640px]">
            <thead className="bg-gray-900/60">
              <tr className="border-b border-gray-800">
                <th className={thCls}>Model ID</th>
                <th className={thCls}>Provider</th>
                <th className={thCls}>Display Name</th>
                <th className={thCls}>Description</th>
                <th className={thCls}>Default</th>
                <th className={thCls}>Enabled</th>
                <th className={thCls}>Order</th>
                <th className={thCls}>Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-800/50">
              {models.map((m) => (
                <tr key={m.id} className={`hover:bg-emerald-500/[0.03] transition-colors ${!m.enabled ? 'opacity-60' : ''}`}>
                  <td className={`${tdCls} font-mono text-gray-300`}>{m.modelId}</td>
                  <td className={`${tdCls} text-gray-500`}>{providers.find(p => p.id === m.providerId)?.displayName ?? '—'}</td>
                  <td className={`${tdCls} text-gray-200`}>{m.displayName}</td>
                  <td className={`${tdCls} text-gray-500 max-w-sm truncate`}>{m.description}</td>
                  <td className={tdCls}>
                    {m.isDefault
                      ? <span className="px-1.5 py-0.5 font-mono text-[10px] uppercase tracking-wide rounded bg-emerald-500/10 text-emerald-400 border border-emerald-500/30">default</span>
                      : <span className="text-gray-700">—</span>}
                  </td>
                  <td className={tdCls}>
                    {m.enabled
                      ? <span className="flex items-center gap-1.5 text-emerald-400"><span className="glow-dot" style={{ height: 5, width: 5 }} />yes</span>
                      : <span className="text-gray-600">no</span>}
                  </td>
                  <td className={`${tdCls} font-mono text-gray-500 tnum`}>{m.sortOrder}</td>
                  <td className={tdCls}>
                    <div className="flex items-center gap-1">
                      <button onClick={() => setEditing({ ...m })} className="p-1.5 text-gray-500 hover:text-white rounded transition-colors" title="Edit">
                        <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z" />
                        </svg>
                      </button>
                      <button onClick={() => setDeleteTarget(m)} className="p-1.5 text-gray-500 hover:text-rose-400 rounded transition-colors" title="Delete">
                        <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                        </svg>
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
              {!isLoading && models.length === 0 && (
                <tr><td colSpan={8} className="px-4 py-8 text-center text-gray-600 text-xs">No models configured</td></tr>
              )}
              {isLoading && (
                <tr><td colSpan={8} className="px-4 py-8"><div className="flex justify-center"><Spinner /></div></td></tr>
              )}
            </tbody>
          </table>
        </div>
      </div>

      <AnimatePresence>
        {editing && (
          <Dialog title={editing.id ? 'Edit model' : 'New model'} onClose={() => setEditing(null)}>
            <div className="space-y-3">
              <div>
                <label className={label}>Provider</label>
                <select value={editing.providerId ?? 0} onChange={(e) => setEditing({ ...editing, providerId: parseInt(e.target.value) || 0 })} className={inputCls}>
                  <option value={0}>— select —</option>
                  {providers.map((p) => <option key={p.id} value={p.id}>{p.displayName}</option>)}
                </select>
              </div>
              <div>
                <label className={label}>Model ID</label>
                <input value={editing.modelId ?? ''} onChange={(e) => setEditing({ ...editing, modelId: e.target.value })} className={`${inputCls} font-mono`} placeholder="e.g. deepseek/deepseek-v4-flash" />
              </div>
              <div>
                <label className={label}>Display name</label>
                <input value={editing.displayName ?? ''} onChange={(e) => setEditing({ ...editing, displayName: e.target.value })} className={inputCls} placeholder="e.g. Claude Sonnet 4.5" />
              </div>
              <div>
                <label className={label}>Description</label>
                <textarea value={editing.description ?? ''} onChange={(e) => setEditing({ ...editing, description: e.target.value })} rows={2} className={inputCls} placeholder="What is this model good for?" />
              </div>
              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className={label}>Sort order</label>
                  <input type="number" value={editing.sortOrder ?? 0} onChange={(e) => setEditing({ ...editing, sortOrder: parseInt(e.target.value) || 0 })} className={inputCls} />
                </div>
                <div className="flex items-end gap-4 pb-2">
                  <label className="flex items-center gap-2 cursor-pointer">
                    <input type="checkbox" checked={editing.enabled ?? true} onChange={(e) => setEditing({ ...editing, enabled: e.target.checked })} className="accent-emerald-500" />
                    <span className="text-xs text-gray-300">Enabled</span>
                  </label>
                  <label className="flex items-center gap-2 cursor-pointer">
                    <input type="checkbox" checked={editing.isDefault ?? false} onChange={(e) => setEditing({ ...editing, isDefault: e.target.checked })} className="accent-emerald-500" />
                    <span className="text-xs text-gray-300">Default</span>
                  </label>
                </div>
              </div>
            </div>
            <div className="flex items-center justify-end gap-2 pt-1">
              <button onClick={() => setEditing(null)} className={btnGhost}>Cancel</button>
              <button
                onClick={() => saveMut.mutate()}
                disabled={saveMut.isPending || !editing.modelId || !editing.displayName}
                className={btnPrimary}
              >
                {saveMut.isPending ? 'Saving…' : 'Save'}
              </button>
            </div>
          </Dialog>
        )}

        {deleteTarget && (
          <Dialog title="Delete model" onClose={() => setDeleteTarget(null)}>
            <p className="text-xs text-gray-400">
              Delete <span className="text-gray-200 font-mono">{deleteTarget.modelId}</span>? Chat and workflows will no longer offer this model.
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
