import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { api, type DatabaseProduct } from '../api';
import {
  PageHeader, Feedback, Dialog, Spinner,
  btnPrimary, btnDanger, btnGhost, inputCls, btnOutline,
} from '../components/ui';

const EMPTY: Partial<DatabaseProduct> = { databaseName: '', displayName: '', description: '', enabled: true, sortOrder: 0 };

export default function Products() {
  const qc = useQueryClient();
  const { data: products = [], isLoading } = useQuery({ queryKey: ['products'], queryFn: api.listProducts });

  const [feedback, setFeedback] = useState<{ message: string; kind: 'success' | 'error' } | null>(null);
  const [editing, setEditing] = useState<Partial<DatabaseProduct> | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<DatabaseProduct | null>(null);

  const invalidate = () => {
    qc.invalidateQueries({ queryKey: ['products'] });
    qc.invalidateQueries({ queryKey: ['products-enabled'] });
  };

  const saveMut = useMutation({
    mutationFn: () => api.saveProduct(editing!),
    onSuccess: () => {
      invalidate();
      setEditing(null);
      setFeedback({ message: 'Product saved.', kind: 'success' });
    },
    onError: (e: Error) => setFeedback({ message: `Failed to save: ${e.message}`, kind: 'error' }),
  });

  const deleteMut = useMutation({
    mutationFn: (id: number) => api.deleteProduct(id),
    onSuccess: () => {
      invalidate();
      setDeleteTarget(null);
      setFeedback({ message: 'Product deleted.', kind: 'success' });
    },
    onError: (e: Error) => setFeedback({ message: `Failed to delete: ${e.message}`, kind: 'error' }),
  });

  const refreshMut = useMutation({
    mutationFn: api.refreshSchema,
    onSuccess: () => setFeedback({ message: 'Schema cache refreshed.', kind: 'success' }),
    onError: (e: Error) => setFeedback({ message: `Refresh failed: ${e.message}`, kind: 'error' }),
  });

  return (
    <div className="space-y-4">
      <PageHeader title="Database Products" subtitle="ClickHouse databases the analytics agent can query, with friendly names">
        <button onClick={() => refreshMut.mutate()} disabled={refreshMut.isPending} className={btnOutline}>
          {refreshMut.isPending ? 'Refreshing…' : 'Refresh schema cache'}
        </button>
        <button onClick={() => setEditing({ ...EMPTY })} className={btnPrimary}>Add Product</button>
      </PageHeader>

      {feedback && <Feedback message={feedback.message} kind={feedback.kind} onDismiss={() => setFeedback(null)} />}

      <div className="border border-gray-800 rounded-lg overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full text-sm min-w-[640px]">
            <thead className="bg-gray-900/50">
              <tr className="text-xs text-gray-500 border-b border-gray-800">
                <th className="text-left px-4 py-2 font-medium">Database</th>
                <th className="text-left px-4 py-2 font-medium">Display Name</th>
                <th className="text-left px-4 py-2 font-medium">Description</th>
                <th className="text-left px-4 py-2 font-medium">Enabled</th>
                <th className="text-left px-4 py-2 font-medium">Order</th>
                <th className="text-left px-4 py-2 font-medium">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-800/50">
              {products.map((p) => (
                <tr key={p.id} className={`hover:bg-gray-900/30 transition-colors ${!p.enabled ? 'opacity-60' : ''}`}>
                  <td className="px-4 py-2.5 text-xs font-mono text-gray-300">{p.databaseName}</td>
                  <td className="px-4 py-2.5 text-xs text-gray-200">{p.displayName}</td>
                  <td className="px-4 py-2.5 text-xs text-gray-500 max-w-sm truncate">{p.description}</td>
                  <td className="px-4 py-2.5 text-xs">
                    {p.enabled ? <span className="text-emerald-400">yes</span> : <span className="text-gray-600">no</span>}
                  </td>
                  <td className="px-4 py-2.5 text-xs text-gray-500">{p.sortOrder}</td>
                  <td className="px-4 py-2.5">
                    <div className="flex items-center gap-2">
                      <button onClick={() => setEditing({ ...p })} className="text-xs text-gray-500 hover:text-white transition-colors">Edit</button>
                      <button onClick={() => setDeleteTarget(p)} className="text-xs text-gray-600 hover:text-rose-400 transition-colors">Delete</button>
                    </div>
                  </td>
                </tr>
              ))}
              {!isLoading && products.length === 0 && (
                <tr><td colSpan={6} className="px-4 py-8 text-center text-gray-600 text-xs">No products configured</td></tr>
              )}
              {isLoading && (
                <tr><td colSpan={6} className="px-4 py-8"><div className="flex justify-center"><Spinner /></div></td></tr>
              )}
            </tbody>
          </table>
        </div>
      </div>

      {editing && (
        <Dialog title={editing.id ? 'Edit product' : 'New product'} onClose={() => setEditing(null)}>
          <div className="space-y-3">
            <div>
              <label className="block text-xs text-gray-400 mb-1">ClickHouse database name</label>
              <input value={editing.databaseName ?? ''} onChange={(e) => setEditing({ ...editing, databaseName: e.target.value })} className={inputCls} placeholder="e.g. lipila_blaze" />
            </div>
            <div>
              <label className="block text-xs text-gray-400 mb-1">Display name</label>
              <input value={editing.displayName ?? ''} onChange={(e) => setEditing({ ...editing, displayName: e.target.value })} className={inputCls} placeholder="e.g. Lipila Payments" />
            </div>
            <div>
              <label className="block text-xs text-gray-400 mb-1">Description</label>
              <textarea value={editing.description ?? ''} onChange={(e) => setEditing({ ...editing, description: e.target.value })} rows={3} className={inputCls} />
            </div>
            <div className="grid grid-cols-2 gap-3">
              <div>
                <label className="block text-xs text-gray-400 mb-1">Sort order</label>
                <input type="number" value={editing.sortOrder ?? 0} onChange={(e) => setEditing({ ...editing, sortOrder: parseInt(e.target.value) || 0 })} className={inputCls} />
              </div>
              <label className="flex items-end gap-2 cursor-pointer pb-2">
                <input type="checkbox" checked={editing.enabled ?? true} onChange={(e) => setEditing({ ...editing, enabled: e.target.checked })} className="accent-emerald-500" />
                <span className="text-xs text-gray-300">Enabled</span>
              </label>
            </div>
          </div>
          <div className="flex items-center justify-end gap-2 pt-1">
            <button onClick={() => setEditing(null)} className={btnGhost}>Cancel</button>
            <button
              onClick={() => saveMut.mutate()}
              disabled={saveMut.isPending || !editing.databaseName || !editing.displayName}
              className={btnPrimary}
            >
              {saveMut.isPending ? 'Saving…' : 'Save'}
            </button>
          </div>
        </Dialog>
      )}

      {deleteTarget && (
        <Dialog title="Delete product" onClose={() => setDeleteTarget(null)}>
          <p className="text-xs text-gray-400">
            Delete <span className="text-gray-200 font-mono">{deleteTarget.databaseName}</span>? The agent will no longer offer this database.
          </p>
          <div className="flex items-center justify-end gap-2 pt-1">
            <button onClick={() => setDeleteTarget(null)} className={btnGhost}>Cancel</button>
            <button onClick={() => deleteMut.mutate(deleteTarget.id)} disabled={deleteMut.isPending} className={btnDanger}>
              {deleteMut.isPending ? 'Deleting…' : 'Delete'}
            </button>
          </div>
        </Dialog>
      )}
    </div>
  );
}
