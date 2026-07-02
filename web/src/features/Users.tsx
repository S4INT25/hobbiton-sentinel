import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { AnimatePresence } from 'motion/react';
import { api, type AdminUser } from '../api';
import {
  PageHeader, Feedback, Dialog, Spinner,
  btnPrimary, btnDanger, btnGhost, inputCls, fmtDateFull, tableWrap, thCls, tdCls,
} from '../components/ui';
import { useMe } from '../App';

const ROLES = ['admin', 'analyst', 'developer'];
const label = 'block font-mono text-[10px] uppercase tracking-wider text-gray-500 mb-1';

export default function Users() {
  const qc = useQueryClient();
  const me = useMe();
  const { data: users = [], isLoading } = useQuery({ queryKey: ['users'], queryFn: api.listUsers });

  const [feedback, setFeedback] = useState<{ message: string; kind: 'success' | 'error' } | null>(null);
  const [createOpen, setCreateOpen] = useState(false);
  const [editTarget, setEditTarget] = useState<AdminUser | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<AdminUser | null>(null);
  const [form, setForm] = useState({ username: '', password: '', role: 'analyst', displayName: '', email: '' });
  const [editForm, setEditForm] = useState({ role: '', displayName: '', isActive: true, password: '' });

  const invalidate = () => qc.invalidateQueries({ queryKey: ['users'] });

  const createMut = useMutation({
    mutationFn: () => api.createUser({ ...form, email: form.email || undefined }),
    onSuccess: () => {
      invalidate();
      setCreateOpen(false);
      setFeedback({ message: 'User created.', kind: 'success' });
    },
    onError: (e: Error) => setFeedback({ message: `Failed to create user: ${e.message}`, kind: 'error' }),
  });

  const updateMut = useMutation({
    mutationFn: () =>
      api.updateUser(editTarget!.id, {
        role: editForm.role,
        displayName: editForm.displayName,
        isActive: editForm.isActive,
        password: editForm.password || undefined,
      }),
    onSuccess: () => {
      invalidate();
      setEditTarget(null);
      setFeedback({ message: 'User updated.', kind: 'success' });
    },
    onError: (e: Error) => setFeedback({ message: `Failed to update user: ${e.message}`, kind: 'error' }),
  });

  const deleteMut = useMutation({
    mutationFn: (id: string) => api.deleteUser(id),
    onSuccess: () => {
      invalidate();
      setDeleteTarget(null);
      setFeedback({ message: 'User deleted.', kind: 'success' });
    },
    onError: (e: Error) => setFeedback({ message: `Failed to delete user: ${e.message}`, kind: 'error' }),
  });

  return (
    <div className="space-y-4" data-stagger>
      <PageHeader title="Users" subtitle="Manage who can access Sentinel and what they can do">
        <button
          onClick={() => { setForm({ username: '', password: '', role: 'analyst', displayName: '', email: '' }); setCreateOpen(true); }}
          className={btnPrimary}
        >
          Add User
        </button>
      </PageHeader>

      {feedback && <Feedback message={feedback.message} kind={feedback.kind} onDismiss={() => setFeedback(null)} />}

      <div className={tableWrap}>
        <div className="overflow-x-auto">
          <table className="w-full text-sm min-w-[640px]">
            <thead className="bg-gray-900/60">
              <tr className="border-b border-gray-800">
                <th className={thCls}>Username</th>
                <th className={thCls}>Display Name</th>
                <th className={thCls}>Email</th>
                <th className={thCls}>Role</th>
                <th className={thCls}>Status</th>
                <th className={thCls}>Last Login</th>
                <th className={thCls}>Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-800/50">
              {users.map((u) => (
                <tr key={u.id} className="hover:bg-emerald-500/[0.03] transition-colors">
                  <td className={`${tdCls} text-gray-200 font-mono`}>{u.username}</td>
                  <td className={tdCls}>{u.displayName}</td>
                  <td className={`${tdCls} text-gray-500`}>{u.email ?? '—'}</td>
                  <td className={tdCls}>
                    <span className={`px-1.5 py-0.5 font-mono text-[10px] uppercase tracking-wide rounded border ${
                      u.role === 'admin'
                        ? 'bg-purple-500/10 text-purple-400 border-purple-500/25'
                        : u.role === 'developer'
                          ? 'bg-cyan-500/10 text-cyan-400 border-cyan-500/25'
                          : 'bg-sky-500/10 text-sky-400 border-sky-500/25'
                    }`}>{u.role}</span>
                  </td>
                  <td className={tdCls}>
                    {u.isActive
                      ? <span className="flex items-center gap-1.5 text-emerald-400"><span className="inline-block h-1 w-1 rounded-full bg-emerald-400" />active</span>
                      : <span className="text-gray-600">disabled</span>}
                  </td>
                  <td className={`${tdCls} font-mono text-gray-500`}>{u.lastLoginAt ? fmtDateFull(u.lastLoginAt) : 'never'}</td>
                  <td className={tdCls}>
                    <div className="flex items-center gap-2">
                      <button
                        onClick={() => {
                          setEditTarget(u);
                          setEditForm({ role: u.role, displayName: u.displayName, isActive: u.isActive, password: '' });
                        }}
                        className="text-xs text-gray-500 hover:text-white transition-colors"
                      >
                        Edit
                      </button>
                      {u.id !== me?.id && (
                        <button onClick={() => setDeleteTarget(u)} className="text-xs text-gray-600 hover:text-rose-400 transition-colors">
                          Delete
                        </button>
                      )}
                    </div>
                  </td>
                </tr>
              ))}
              {!isLoading && users.length === 0 && (
                <tr><td colSpan={7} className="px-4 py-8 text-center text-gray-600 text-xs">No users</td></tr>
              )}
              {isLoading && (
                <tr><td colSpan={7} className="px-4 py-8"><div className="flex justify-center"><Spinner /></div></td></tr>
              )}
            </tbody>
          </table>
        </div>
      </div>

      <AnimatePresence>
        {createOpen && (
          <Dialog title="Add user" onClose={() => setCreateOpen(false)}>
            <div className="space-y-3">
              <div>
                <label className={label}>Username</label>
                <input value={form.username} onChange={(e) => setForm({ ...form, username: e.target.value })} className={inputCls} />
              </div>
              <div>
                <label className={label}>Password</label>
                <input type="password" value={form.password} onChange={(e) => setForm({ ...form, password: e.target.value })} className={inputCls} />
              </div>
              <div>
                <label className={label}>Display name</label>
                <input value={form.displayName} onChange={(e) => setForm({ ...form, displayName: e.target.value })} className={inputCls} />
              </div>
              <div>
                <label className={label}>Email (optional)</label>
                <input type="email" value={form.email} onChange={(e) => setForm({ ...form, email: e.target.value })} className={inputCls} />
              </div>
              <div>
                <label className={label}>Role</label>
                <select value={form.role} onChange={(e) => setForm({ ...form, role: e.target.value })} className={inputCls}>
                  {ROLES.map((r) => <option key={r} value={r}>{r}</option>)}
                </select>
              </div>
            </div>
            <div className="flex items-center justify-end gap-2 pt-1">
              <button onClick={() => setCreateOpen(false)} className={btnGhost}>Cancel</button>
              <button onClick={() => createMut.mutate()} disabled={createMut.isPending || !form.username || !form.password} className={btnPrimary}>
                {createMut.isPending ? 'Creating…' : 'Create'}
              </button>
            </div>
          </Dialog>
        )}

        {editTarget && (
          <Dialog title={`Edit ${editTarget.username}`} onClose={() => setEditTarget(null)}>
            <div className="space-y-3">
              <div>
                <label className={label}>Display name</label>
                <input value={editForm.displayName} onChange={(e) => setEditForm({ ...editForm, displayName: e.target.value })} className={inputCls} />
              </div>
              <div>
                <label className={label}>Role</label>
                <select value={editForm.role} onChange={(e) => setEditForm({ ...editForm, role: e.target.value })} className={inputCls}>
                  {ROLES.map((r) => <option key={r} value={r}>{r}</option>)}
                </select>
              </div>
              <div>
                <label className={label}>New password (leave blank to keep)</label>
                <input type="password" value={editForm.password} onChange={(e) => setEditForm({ ...editForm, password: e.target.value })} className={inputCls} />
              </div>
              <label className="flex items-center gap-2 cursor-pointer">
                <input type="checkbox" checked={editForm.isActive} onChange={(e) => setEditForm({ ...editForm, isActive: e.target.checked })} className="accent-emerald-500" />
                <span className="text-xs text-gray-300">Active (can sign in)</span>
              </label>
            </div>
            <div className="flex items-center justify-end gap-2 pt-1">
              <button onClick={() => setEditTarget(null)} className={btnGhost}>Cancel</button>
              <button onClick={() => updateMut.mutate()} disabled={updateMut.isPending} className={btnPrimary}>
                {updateMut.isPending ? 'Saving…' : 'Save'}
              </button>
            </div>
          </Dialog>
        )}

        {deleteTarget && (
          <Dialog title="Delete user" onClose={() => setDeleteTarget(null)}>
            <p className="text-xs text-gray-400">
              Delete <span className="text-gray-200 font-mono">{deleteTarget.username}</span>? This cannot be undone.
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
