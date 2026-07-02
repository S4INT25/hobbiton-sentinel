import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { api, type FraudCase } from '../api';
import {
  PageHeader, Feedback, Dialog, SeverityBadge, ConfidenceBadge, Spinner,
  btnPrimary, btnDanger, btnGhost, inputCls, selectCls, fmtDate,
} from '../components/ui';

const PAGE_SIZE = 15;

export default function Cases() {
  const qc = useQueryClient();
  const { data: cases = [], isLoading } = useQuery({ queryKey: ['cases'], queryFn: api.listCases });

  const [sortOrder, setSortOrder] = useState('newest');
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [feedback, setFeedback] = useState<{ message: string; kind: 'success' | 'error' } | null>(null);
  const [resolveTarget, setResolveTarget] = useState<FraudCase | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<FraudCase | null>(null);
  const [bulkOpen, setBulkOpen] = useState(false);
  const [reason, setReason] = useState('Manually resolved by analyst');

  const sorted = useMemo(() => {
    const sevRank = (s: string) => (s === 'critical' ? 4 : s === 'high' ? 3 : s === 'medium' ? 2 : 1);
    const list = [...cases];
    if (sortOrder === 'newest') list.sort((a, b) => b.firstSeen.localeCompare(a.firstSeen));
    else if (sortOrder === 'oldest') list.sort((a, b) => a.firstSeen.localeCompare(b.firstSeen));
    else list.sort((a, b) => sevRank(b.severity) - sevRank(a.severity));
    return list;
  }, [cases, sortOrder]);

  const totalPages = Math.max(1, Math.ceil(sorted.length / PAGE_SIZE));
  const paged = sorted.slice((page - 1) * PAGE_SIZE, page * PAGE_SIZE);

  const invalidate = () => qc.invalidateQueries({ queryKey: ['cases'] });

  const resolveMut = useMutation({
    mutationFn: ({ id, reason }: { id: string; reason: string }) => api.caseFeedback(id, 'resolve', reason),
    onSuccess: () => {
      invalidate();
      setFeedback({ message: 'Case resolved.', kind: 'success' });
      setResolveTarget(null);
    },
    onError: (e: Error) => setFeedback({ message: `Failed to resolve case: ${e.message}`, kind: 'error' }),
  });

  const deleteMut = useMutation({
    mutationFn: (id: string) => api.deleteCase(id),
    onSuccess: () => {
      invalidate();
      setFeedback({ message: 'Case deleted.', kind: 'success' });
      setDeleteTarget(null);
    },
    onError: (e: Error) => setFeedback({ message: `Failed to delete case: ${e.message}`, kind: 'error' }),
  });

  const bulkMut = useMutation({
    mutationFn: () => api.bulkResolve([...selected], reason),
    onSuccess: ({ count }) => {
      invalidate();
      setFeedback({ message: `${count} case${count === 1 ? '' : 's'} resolved.`, kind: 'success' });
      setSelected(new Set());
      setBulkOpen(false);
    },
    onError: (e: Error) => setFeedback({ message: `Failed to bulk resolve: ${e.message}`, kind: 'error' }),
  });

  const toggleSelect = (id: string) =>
    setSelected((prev) => {
      const next = new Set(prev);
      if (!next.delete(id)) next.add(id);
      return next;
    });

  const toggleSelectAll = (check: boolean) =>
    setSelected((prev) => {
      const next = new Set(prev);
      for (const c of paged) check ? next.add(c.id) : next.delete(c.id);
      return next;
    });

  const allPageSelected = paged.length > 0 && paged.every((c) => selected.has(c.id));

  return (
    <div className="space-y-4">
      <PageHeader title="Cases">
        {selected.size > 0 && (
          <button
            onClick={() => { setReason('Bulk resolved by analyst'); setBulkOpen(true); }}
            className="px-3 py-1.5 text-xs font-medium bg-emerald-700 hover:bg-emerald-600 text-white rounded-md transition-all"
          >
            Resolve {selected.size} selected
          </button>
        )}
        <select value={sortOrder} onChange={(e) => { setSortOrder(e.target.value); setPage(1); }} className={selectCls}>
          <option value="severity">Sort: Severity</option>
          <option value="newest">Sort: Newest first</option>
          <option value="oldest">Sort: Oldest first</option>
        </select>
      </PageHeader>

      {feedback && <Feedback message={feedback.message} kind={feedback.kind} onDismiss={() => setFeedback(null)} />}

      <div className="border border-gray-800 rounded-lg overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full text-sm min-w-[640px]">
            <thead className="bg-gray-900/50">
              <tr className="text-xs text-gray-500 border-b border-gray-800">
                <th className="px-4 py-2 w-8">
                  <input
                    type="checkbox"
                    checked={allPageSelected}
                    onChange={(e) => toggleSelectAll(e.target.checked)}
                    className="accent-emerald-500 cursor-pointer"
                    title="Select all on this page"
                  />
                </th>
                <th className="text-left px-4 py-2 font-medium">ID</th>
                <th className="text-left px-4 py-2 font-medium">Title</th>
                <th className="text-left px-4 py-2 font-medium">Severity</th>
                <th className="text-left px-4 py-2 font-medium">Confidence</th>
                <th className="text-left px-4 py-2 font-medium">Status</th>
                <th className="text-left px-4 py-2 font-medium">Created</th>
                <th className="text-left px-4 py-2 font-medium">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-800/50">
              {paged.map((c) => (
                <tr key={c.id} className={`hover:bg-gray-900/30 transition-colors ${selected.has(c.id) ? 'bg-emerald-950/20' : ''}`}>
                  <td className="px-4 py-2.5 w-8">
                    <input
                      type="checkbox"
                      checked={selected.has(c.id)}
                      onChange={() => toggleSelect(c.id)}
                      className="accent-emerald-500 cursor-pointer"
                    />
                  </td>
                  <td className="px-4 py-2.5 font-mono text-xs text-gray-400">{c.id.slice(0, 8)}</td>
                  <td className="px-4 py-2.5 text-xs text-gray-200 max-w-sm truncate">{c.title}</td>
                  <td className="px-4 py-2.5"><SeverityBadge severity={c.severity} /></td>
                  <td className="px-4 py-2.5"><ConfidenceBadge confidence={c.confidence} /></td>
                  <td className="px-4 py-2.5 text-xs text-gray-500">{c.status}</td>
                  <td className="px-4 py-2.5 text-xs text-gray-500">{fmtDate(c.firstSeen)}</td>
                  <td className="px-4 py-2.5">
                    <div className="flex items-center gap-2">
                      <Link to={`/cases/${c.id}`} className="text-xs text-emerald-400 hover:text-emerald-300 transition-colors">
                        View
                      </Link>
                      <button
                        onClick={() => { setReason('Manually resolved by analyst'); setResolveTarget(c); }}
                        className="text-xs text-gray-500 hover:text-emerald-400 transition-colors"
                      >
                        Resolve
                      </button>
                      <button
                        onClick={() => setDeleteTarget(c)}
                        className="text-xs text-gray-600 hover:text-rose-400 transition-colors"
                      >
                        Delete
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
              {!isLoading && cases.length === 0 && (
                <tr>
                  <td colSpan={8} className="px-4 py-8 text-center text-gray-600 text-xs">No open cases</td>
                </tr>
              )}
              {isLoading && (
                <tr>
                  <td colSpan={8} className="px-4 py-8">
                    <div className="flex justify-center"><Spinner /></div>
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </div>

      {sorted.length > PAGE_SIZE && (
        <div className="flex items-center justify-between text-xs">
          <span className="text-gray-500">{sorted.length} cases · page {page} of {totalPages}</span>
          <div className="flex items-center gap-2">
            <button onClick={() => setPage((p) => Math.max(1, p - 1))} disabled={page <= 1}
                    className="px-2.5 py-1 border border-gray-800 rounded text-gray-300 disabled:opacity-40 hover:border-gray-700 transition-colors">
              Previous
            </button>
            <button onClick={() => setPage((p) => Math.min(totalPages, p + 1))} disabled={page >= totalPages}
                    className="px-2.5 py-1 border border-gray-800 rounded text-gray-300 disabled:opacity-40 hover:border-gray-700 transition-colors">
              Next
            </button>
          </div>
        </div>
      )}

      {resolveTarget && (
        <Dialog title="Resolve case" onClose={() => setResolveTarget(null)}>
          <p className="text-xs text-gray-400">
            Resolve case <span className="text-gray-200 font-mono">{resolveTarget.id.slice(0, 8)}</span> directly from this list.
          </p>
          <div>
            <label className="block text-[10px] text-gray-500 uppercase tracking-wider mb-1">Resolution note</label>
            <input value={reason} onChange={(e) => setReason(e.target.value)} className={inputCls} />
          </div>
          <div className="flex items-center justify-end gap-2 pt-1">
            <button onClick={() => setResolveTarget(null)} className={btnGhost}>Cancel</button>
            <button
              onClick={() => resolveMut.mutate({ id: resolveTarget.id, reason })}
              disabled={resolveMut.isPending}
              className={btnPrimary}
            >
              {resolveMut.isPending ? 'Resolving…' : 'Resolve'}
            </button>
          </div>
        </Dialog>
      )}

      {deleteTarget && (
        <Dialog title="Confirm delete" onClose={() => setDeleteTarget(null)}>
          <p className="text-xs text-gray-400">
            Delete case <span className="text-gray-200 font-mono">{deleteTarget.id.slice(0, 8)}</span>? This action cannot be undone.
          </p>
          <div className="flex items-center justify-end gap-2 pt-1">
            <button onClick={() => setDeleteTarget(null)} className={btnGhost}>Cancel</button>
            <button onClick={() => deleteMut.mutate(deleteTarget.id)} disabled={deleteMut.isPending} className={btnDanger}>
              {deleteMut.isPending ? 'Deleting…' : 'Delete'}
            </button>
          </div>
        </Dialog>
      )}

      {bulkOpen && (
        <Dialog title={`Resolve ${selected.size} case${selected.size === 1 ? '' : 's'}`} onClose={() => setBulkOpen(false)}>
          <p className="text-xs text-gray-400">This will mark all selected cases as resolved. This action cannot be undone.</p>
          <div>
            <label className="block text-[10px] text-gray-500 uppercase tracking-wider mb-1">Resolution note</label>
            <input value={reason} onChange={(e) => setReason(e.target.value)} className={inputCls} />
          </div>
          <div className="flex items-center justify-end gap-2 pt-1">
            <button onClick={() => setBulkOpen(false)} className={btnGhost}>Cancel</button>
            <button onClick={() => bulkMut.mutate()} disabled={bulkMut.isPending} className={btnPrimary}>
              {bulkMut.isPending ? 'Resolving…' : `Resolve ${selected.size}`}
            </button>
          </div>
        </Dialog>
      )}
    </div>
  );
}
