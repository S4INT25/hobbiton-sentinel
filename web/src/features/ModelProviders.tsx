import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { AnimatePresence } from 'motion/react';
import { api, type ProviderConfig, type LlmModel } from '../api';
import {
  PageHeader, Feedback, Dialog, Spinner,
  btnPrimary, btnDanger, btnGhost, btnOutline, inputCls, tableWrap, thCls, tdCls,
} from '../components/ui';

const label = 'block font-mono text-[10px] uppercase tracking-wider text-gray-500 mb-1';

// ── Provider form ────────────────────────────────────────────────────────────

function ProviderForm({
  value,
  onChange,
}: {
  value: Partial<ProviderConfig>;
  onChange: (v: Partial<ProviderConfig>) => void;
}) {
  return (
    <div className="space-y-3">
      <div className="grid grid-cols-2 gap-3">
        <div>
          <label className={label}>Display name</label>
          <input value={value.displayName ?? ''} onChange={(e) => onChange({ ...value, displayName: e.target.value })} className={inputCls} placeholder="e.g. DeepSeek" />
        </div>
        <div>
          <label className={label}>Slug (unique key)</label>
          <input value={value.slug ?? ''} onChange={(e) => onChange({ ...value, slug: e.target.value })} className={`${inputCls} font-mono`} placeholder="e.g. deepseek" />
        </div>
      </div>
      <div>
        <label className={label}>Endpoint URL</label>
        <input value={value.endpoint ?? ''} onChange={(e) => onChange({ ...value, endpoint: e.target.value })} className={`${inputCls} font-mono`} placeholder="https://api.deepseek.com" />
      </div>
      <div>
        <label className={label}>API Key</label>
        <input type="password" value={value.apiKey ?? ''} onChange={(e) => onChange({ ...value, apiKey: e.target.value })} className={`${inputCls} font-mono`} placeholder="sk-…" />
      </div>
      <div className="flex items-center gap-4">
        <label className="flex items-center gap-2 cursor-pointer">
          <input type="checkbox" checked={value.enabled ?? true} onChange={(e) => onChange({ ...value, enabled: e.target.checked })} className="accent-emerald-500" />
          <span className="text-xs text-gray-300">Enabled</span>
        </label>
        <label className="flex items-center gap-2 cursor-pointer">
          <input type="checkbox" checked={value.isDefault ?? false} onChange={(e) => onChange({ ...value, isDefault: e.target.checked })} className="accent-emerald-500" />
          <span className="text-xs text-gray-300">Default</span>
        </label>
        <div>
          <label className="block font-mono text-[10px] uppercase tracking-wider text-gray-500 mb-0.5">Sort</label>
          <input type="number" value={value.sortOrder ?? 0} onChange={(e) => onChange({ ...value, sortOrder: parseInt(e.target.value) || 0 })} className={`${inputCls} w-16`} />
        </div>
      </div>
    </div>
  );
}

// ── Model form ───────────────────────────────────────────────────────────────

function ModelForm({
  value,
  onChange,
}: {
  value: Partial<LlmModel>;
  onChange: (v: Partial<LlmModel>) => void;
}) {
  return (
    <div className="space-y-3">
      <div>
        <label className={label}>Model ID</label>
        <input value={value.modelId ?? ''} onChange={(e) => onChange({ ...value, modelId: e.target.value })} className={`${inputCls} font-mono`} placeholder="e.g. deepseek/deepseek-v4-flash" />
      </div>
      <div>
        <label className={label}>Display name</label>
        <input value={value.displayName ?? ''} onChange={(e) => onChange({ ...value, displayName: e.target.value })} className={inputCls} placeholder="e.g. DeepSeek V4 Flash" />
      </div>
      <div>
        <label className={label}>Description</label>
        <textarea value={value.description ?? ''} onChange={(e) => onChange({ ...value, description: e.target.value })} rows={2} className={inputCls} placeholder="What is this model good for?" />
      </div>
      <div className="grid grid-cols-2 gap-3">
        <div>
          <label className={label}>Sort order</label>
          <input type="number" value={value.sortOrder ?? 0} onChange={(e) => onChange({ ...value, sortOrder: parseInt(e.target.value) || 0 })} className={inputCls} />
        </div>
        <div className="flex items-end gap-4 pb-2">
          <label className="flex items-center gap-2 cursor-pointer">
            <input type="checkbox" checked={value.enabled ?? true} onChange={(e) => onChange({ ...value, enabled: e.target.checked })} className="accent-emerald-500" />
            <span className="text-xs text-gray-300">Enabled</span>
          </label>
          <label className="flex items-center gap-2 cursor-pointer">
            <input type="checkbox" checked={value.isDefault ?? false} onChange={(e) => onChange({ ...value, isDefault: e.target.checked })} className="accent-emerald-500" />
            <span className="text-xs text-gray-300">Default</span>
          </label>
        </div>
      </div>
    </div>
  );
}

// ── Main page ────────────────────────────────────────────────────────────────

export default function ModelProviders() {
  const qc = useQueryClient();
  const { data: providers = [], isLoading: loadingProviders } = useQuery({ queryKey: ['providers'], queryFn: api.listProviders });
  const { data: models = [], isLoading: loadingModels } = useQuery({ queryKey: ['models'], queryFn: api.listModels });

  const [feedback, setFeedback] = useState<{ message: string; kind: 'success' | 'error' } | null>(null);
  const [editingProv, setEditingProv] = useState<Partial<ProviderConfig> | null>(null);
  const [deleteProv, setDeleteProv] = useState<ProviderConfig | null>(null);
  const [editingModel, setEditingModel] = useState<Partial<LlmModel> | null>(null);
  const [deleteModel, setDeleteModel] = useState<LlmModel | null>(null);

  const [selectedProviderId, setSelectedProviderId] = useState<number>(0);
  const activeProv = providers.find((p) => p.id === selectedProviderId);
  const providerModels = selectedProviderId ? models.filter((m) => m.providerId === selectedProviderId) : models;

  // Auto-select default or first provider on load
  if (selectedProviderId === 0 && providers.length > 0) {
    const def = providers.find((p) => p.isDefault) ?? providers[0];
    setSelectedProviderId(def.id);
  }

  const invalidate = () => {
    qc.invalidateQueries({ queryKey: ['providers'] });
    qc.invalidateQueries({ queryKey: ['providers-enabled'] });
    qc.invalidateQueries({ queryKey: ['models'] });
    qc.invalidateQueries({ queryKey: ['models-enabled'] });
  };

  const saveProvMut = useMutation({
    mutationFn: () => api.saveProvider(editingProv!),
    onSuccess: () => { invalidate(); setEditingProv(null); setFeedback({ message: 'Provider saved.', kind: 'success' }); },
    onError: (e: Error) => setFeedback({ message: `Provider failed: ${e.message}`, kind: 'error' }),
  });

  const deleteProvMut = useMutation({
    mutationFn: (id: number) => api.deleteProvider(id),
    onSuccess: () => { invalidate(); setDeleteProv(null); if (selectedProviderId === deleteProv?.id) setSelectedProviderId(0); setFeedback({ message: 'Provider deleted.', kind: 'success' }); },
    onError: (e: Error) => setFeedback({ message: `Delete failed: ${e.message}`, kind: 'error' }),
  });

  const saveModelMut = useMutation({
    mutationFn: () => api.saveModel({ ...editingModel!, providerId: selectedProviderId }),
    onSuccess: () => { invalidate(); setEditingModel(null); setFeedback({ message: 'Model saved.', kind: 'success' }); },
    onError: (e: Error) => setFeedback({ message: `Model failed: ${e.message}`, kind: 'error' }),
  });

  const deleteModelMut = useMutation({
    mutationFn: (id: number) => api.deleteModel(id),
    onSuccess: () => { invalidate(); setDeleteModel(null); setFeedback({ message: 'Model deleted.', kind: 'success' }); },
    onError: (e: Error) => setFeedback({ message: `Delete failed: ${e.message}`, kind: 'error' }),
  });

  const isLoading = loadingProviders || loadingModels;

  return (
    <div className="space-y-4 px-4 lg:px-16" data-stagger>
      <PageHeader title="Models &amp; Providers" subtitle="Manage LLM providers and the models assigned to each">
        <button onClick={() => setEditingProv({ displayName: '', slug: '', apiKey: '', endpoint: '', enabled: true, isDefault: false, sortOrder: 0 })} className={btnPrimary}>Add Provider</button>
      </PageHeader>

      {feedback && <Feedback message={feedback.message} kind={feedback.kind} onDismiss={() => setFeedback(null)} />}

      {/* ── Provider chips ── */}
      <div className="panel p-3">
        <div className="flex items-center gap-2 flex-wrap">
          <span className="text-[10px] uppercase tracking-wider text-gray-600 font-mono mr-1">Providers</span>
          {providers.map((p) => (
            <button
              key={p.id}
              onClick={() => setSelectedProviderId(p.id)}
              className={`inline-flex items-center gap-1.5 px-2.5 py-1 rounded-lg text-xs transition-colors ${
                p.id === selectedProviderId
                  ? 'bg-emerald-500/15 text-emerald-300 border border-emerald-500/30'
                  : 'bg-gray-800/60 text-gray-400 border border-gray-700/50 hover:border-gray-600'
              }`}
            >
              {p.isDefault && <span className="w-1.5 h-1.5 rounded-full bg-emerald-400 shrink-0" />}
              <span className="max-w-[10rem] truncate">{p.displayName}</span>
            </button>
          ))}
          {providers.length > 1 && selectedProviderId && (
            <button
              onClick={() => setEditingProv(providers.find((p) => p.id === selectedProviderId)!)}
              className="p-1 text-gray-500 hover:text-white rounded transition-colors"
              title="Edit provider"
            >
              <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z" />
              </svg>
            </button>
          )}
          {providers.length > 0 && selectedProviderId && !(activeProv?.slug === 'openrouter') && (
            <button
              onClick={() => setDeleteProv(activeProv!)}
              className="p-1 text-gray-500 hover:text-rose-400 rounded transition-colors"
              title="Delete provider"
            >
              <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
              </svg>
            </button>
          )}
        </div>
      </div>

      {/* ── Models table ── */}
      <div className="flex items-center justify-between">
        <h3 className="text-xs font-mono uppercase tracking-wider text-gray-500">
          {activeProv ? `${activeProv.displayName} models` : 'All models'}
        </h3>
        {selectedProviderId > 0 && (
          <button onClick={() => setEditingModel({ displayName: '', modelId: '', description: '', enabled: true, isDefault: false, sortOrder: 0, providerId: selectedProviderId })} className={btnOutline}>
            + Add model
          </button>
        )}
      </div>

      <div className={tableWrap}>
        <div className="overflow-x-auto">
          <table className="w-full text-sm min-w-[640px]">
            <thead className="bg-gray-900/60">
              <tr className="border-b border-gray-800">
                <th className={thCls}>Model ID</th>
                <th className={thCls}>Display Name</th>
                <th className={thCls}>Description</th>
                <th className={thCls}>Default</th>
                <th className={thCls}>Enabled</th>
                <th className={thCls}>Order</th>
                <th className={thCls}>Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-800/50">
              {providerModels.map((m) => (
                <tr key={m.id} className={`hover:bg-emerald-500/[0.03] transition-colors ${!m.enabled ? 'opacity-60' : ''}`}>
                  <td className={`${tdCls} font-mono text-gray-300`}>{m.modelId}</td>
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
                      <button onClick={() => setEditingModel({ ...m })} className="p-1.5 text-gray-500 hover:text-white rounded transition-colors" title="Edit">
                        <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z" />
                        </svg>
                      </button>
                      <button onClick={() => setDeleteModel(m)} className="p-1.5 text-gray-500 hover:text-rose-400 rounded transition-colors" title="Delete">
                        <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                        </svg>
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
              {!isLoading && providerModels.length === 0 && (
                <tr><td colSpan={7} className="px-4 py-8 text-center text-gray-600 text-xs">
                  {selectedProviderId ? 'No models assigned to this provider' : 'No models configured'}
                </td></tr>
              )}
              {isLoading && (
                <tr><td colSpan={7} className="px-4 py-8"><div className="flex justify-center"><Spinner /></div></td></tr>
              )}
            </tbody>
          </table>
        </div>
      </div>

      {/* ── Dialogs ── */}
      <AnimatePresence>
        {editingProv && (
          <Dialog title={editingProv.id ? 'Edit provider' : 'New provider'} onClose={() => setEditingProv(null)}>
            <ProviderForm value={editingProv} onChange={setEditingProv} />
            <div className="flex items-center justify-end gap-2 pt-1">
              <button onClick={() => setEditingProv(null)} className={btnGhost}>Cancel</button>
              <button onClick={() => saveProvMut.mutate()} disabled={saveProvMut.isPending || !editingProv.displayName || !editingProv.slug || !editingProv.endpoint} className={btnPrimary}>
                {saveProvMut.isPending ? 'Saving…' : 'Save'}
              </button>
            </div>
          </Dialog>
        )}

        {deleteProv && (
          <Dialog title="Delete provider" onClose={() => setDeleteProv(null)}>
            <p className="text-xs text-gray-400">
              Delete <span className="text-gray-200">{deleteProv.displayName}</span>? Its models will be moved to the default provider.
            </p>
            <div className="flex items-center justify-end gap-2 pt-1">
              <button onClick={() => setDeleteProv(null)} className={btnGhost}>Cancel</button>
              <button onClick={() => deleteProvMut.mutate(deleteProv.id)} disabled={deleteProvMut.isPending} className={btnDanger}>
                {deleteProvMut.isPending ? 'Deleting…' : 'Delete'}
              </button>
            </div>
          </Dialog>
        )}

        {editingModel && (
          <Dialog title={editingModel.id ? 'Edit model' : 'New model'} onClose={() => setEditingModel(null)}>
            <ModelForm value={editingModel} onChange={setEditingModel} />
            <div className="flex items-center justify-end gap-2 pt-1">
              <button onClick={() => setEditingModel(null)} className={btnGhost}>Cancel</button>
              <button onClick={() => saveModelMut.mutate()} disabled={saveModelMut.isPending || !editingModel.modelId || !editingModel.displayName} className={btnPrimary}>
                {saveModelMut.isPending ? 'Saving…' : 'Save'}
              </button>
            </div>
          </Dialog>
        )}

        {deleteModel && (
          <Dialog title="Delete model" onClose={() => setDeleteModel(null)}>
            <p className="text-xs text-gray-400">
              Delete <span className="text-gray-200 font-mono">{deleteModel.modelId}</span>? Chat and workflows will no longer offer this model.
            </p>
            <div className="flex items-center justify-end gap-2 pt-1">
              <button onClick={() => setDeleteModel(null)} className={btnGhost}>Cancel</button>
              <button onClick={() => deleteModelMut.mutate(deleteModel.id)} disabled={deleteModelMut.isPending} className={btnDanger}>
                {deleteModelMut.isPending ? 'Deleting…' : 'Delete'}
              </button>
            </div>
          </Dialog>
        )}
      </AnimatePresence>
    </div>
  );
}
