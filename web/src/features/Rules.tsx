import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { AnimatePresence } from 'motion/react';
import { api, type FeedbackRule } from '../api';
import {
  PageHeader, Feedback, Dialog, Spinner,
  btnPrimary, btnDanger, btnGhost, inputCls, fmtDate, tableWrap, thCls, tdCls,
} from '../components/ui';

const RULE_TYPES = ['ip', 'cidr', 'asn', 'pattern_id', 'keyword', 'recipient'];
const ACTIONS = ['suppress', 'downgrade', 'info_only'];

const EMPTY: Partial<FeedbackRule> = { ruleType: 'ip', matchValue: '', action: 'suppress', reason: '' };
const label = 'block font-mono text-[10px] uppercase tracking-wider text-gray-500 mb-1';

export default function Rules() {
  const qc = useQueryClient();
  const { data: rules = [], isLoading } = useQuery({ queryKey: ['rules'], queryFn: api.listRules });

  const [feedback, setFeedback] = useState<{ message: string; kind: 'success' | 'error' } | null>(null);
  const [editing, setEditing] = useState<Partial<FeedbackRule> | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<FeedbackRule | null>(null);

  const saveMut = useMutation({
    mutationFn: () => api.saveRule(editing!),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['rules'] });
      setEditing(null);
      setFeedback({ message: 'Rule saved.', kind: 'success' });
    },
    onError: (e: Error) => setFeedback({ message: `Failed to save rule: ${e.message}`, kind: 'error' }),
  });

  const deleteMut = useMutation({
    mutationFn: (id: string) => api.deleteRule(id),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['rules'] });
      setDeleteTarget(null);
      setFeedback({ message: 'Rule deleted.', kind: 'success' });
    },
    onError: (e: Error) => setFeedback({ message: `Failed to delete rule: ${e.message}`, kind: 'error' }),
  });

  return (
    <div className="space-y-4" data-stagger>
      <PageHeader title="Feedback Rules" subtitle="Suppression and downgrade rules the fraud agent applies before raising cases">
        <button onClick={() => setEditing({ ...EMPTY })} className={btnPrimary}>Add Rule</button>
      </PageHeader>

      {feedback && <Feedback message={feedback.message} kind={feedback.kind} onDismiss={() => setFeedback(null)} />}

      <div className={tableWrap}>
        <div className="overflow-x-auto">
          <table className="w-full text-sm min-w-[720px]">
            <thead className="bg-gray-900/60">
              <tr className="border-b border-gray-800">
                <th className={thCls}>Type</th>
                <th className={thCls}>Match</th>
                <th className={thCls}>Action</th>
                <th className={thCls}>Reason</th>
                <th className={thCls}>Hits</th>
                <th className={thCls}>Created</th>
                <th className={thCls}>Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-800/50">
              {rules.map((r) => (
                <tr key={r.id} className="hover:bg-emerald-500/[0.03] transition-colors">
                  <td className={tdCls}>
                    <span className="px-1.5 py-0.5 text-[10px] rounded bg-gray-800/80 border border-gray-700/50 text-gray-300 font-mono uppercase tracking-wide">{r.ruleType}</span>
                  </td>
                  <td className={`${tdCls} font-mono`}>{r.matchValue}</td>
                  <td className={tdCls}>
                    <span className={`px-1.5 py-0.5 font-mono text-[10px] uppercase tracking-wide rounded border ${
                      r.action === 'suppress'
                        ? 'bg-rose-500/10 text-rose-400 border-rose-500/25'
                        : r.action === 'downgrade'
                          ? 'bg-amber-500/10 text-amber-400 border-amber-500/25'
                          : 'bg-sky-500/10 text-sky-400 border-sky-500/25'
                    }`}>{r.action}</span>
                  </td>
                  <td className={`${tdCls} text-gray-400 max-w-sm truncate`}>{r.reason}</td>
                  <td className={`${tdCls} font-mono text-gray-500 tnum`}>{r.hitCount}</td>
                  <td className={`${tdCls} font-mono text-gray-500`}>{fmtDate(r.createdAt)} · {r.createdBy}</td>
                  <td className={tdCls}>
                    <div className="flex items-center gap-2">
                      <button onClick={() => setEditing({ ...r })} className="text-xs text-gray-500 hover:text-white transition-colors">Edit</button>
                      <button onClick={() => setDeleteTarget(r)} className="text-xs text-gray-600 hover:text-rose-400 transition-colors">Delete</button>
                    </div>
                  </td>
                </tr>
              ))}
              {!isLoading && rules.length === 0 && (
                <tr><td colSpan={7} className="px-4 py-8 text-center text-gray-600 text-xs">No rules — the agent flags everything it finds</td></tr>
              )}
              {isLoading && (
                <tr><td colSpan={7} className="px-4 py-8"><div className="flex justify-center"><Spinner /></div></td></tr>
              )}
            </tbody>
          </table>
        </div>
      </div>

      <AnimatePresence>
        {editing && (
          <Dialog title={editing.id ? 'Edit rule' : 'New rule'} onClose={() => setEditing(null)}>
            <div className="grid grid-cols-2 gap-3">
              <div>
                <label className={label}>Rule type</label>
                <select value={editing.ruleType} onChange={(e) => setEditing({ ...editing, ruleType: e.target.value })} className={inputCls}>
                  {RULE_TYPES.map((t) => <option key={t} value={t}>{t}</option>)}
                </select>
              </div>
              <div>
                <label className={label}>Action</label>
                <select value={editing.action} onChange={(e) => setEditing({ ...editing, action: e.target.value })} className={inputCls}>
                  {ACTIONS.map((a) => <option key={a} value={a}>{a}</option>)}
                </select>
              </div>
            </div>
            <div>
              <label className={label}>Match value</label>
              <input value={editing.matchValue ?? ''} onChange={(e) => setEditing({ ...editing, matchValue: e.target.value })} className={`${inputCls} font-mono`} placeholder="e.g. 159.89.0.0/16 or AS14061" />
            </div>
            <div>
              <label className={label}>Reason</label>
              <input value={editing.reason ?? ''} onChange={(e) => setEditing({ ...editing, reason: e.target.value })} className={inputCls} placeholder="Why this rule exists" />
            </div>
            <div className="flex items-center justify-end gap-2 pt-1">
              <button onClick={() => setEditing(null)} className={btnGhost}>Cancel</button>
              <button onClick={() => saveMut.mutate()} disabled={saveMut.isPending || !editing.matchValue} className={btnPrimary}>
                {saveMut.isPending ? 'Saving…' : 'Save'}
              </button>
            </div>
          </Dialog>
        )}

        {deleteTarget && (
          <Dialog title="Delete rule" onClose={() => setDeleteTarget(null)}>
            <p className="text-xs text-gray-400">
              Delete rule <span className="text-gray-200 font-mono">{deleteTarget.ruleType}={deleteTarget.matchValue}</span>?
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
