import { useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { api } from '../api';
import { btnPrimary, inputCls, Spinner } from '../components/ui';

export default function ResetPassword() {
  const [params] = useSearchParams();
  const token = params.get('token') ?? '';
  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState(false);
  const [busy, setBusy] = useState(false);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    setBusy(true);
    try {
      await api.resetPassword(token, password, confirm);
      setDone(true);
    } catch (err) {
      setError((err as { error?: string }).error ?? 'Reset failed.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="min-h-screen bg-gray-950 flex items-center justify-center p-4">
      <div className="w-full max-w-sm">
        <div className="flex items-center justify-center gap-2.5 mb-6">
          <img src="https://hobbiton.tech/assets/logo2-7db998ca.png" alt="Hobbiton" className="h-9 w-9 rounded object-contain" />
          <div>
            <div className="font-semibold text-white leading-none tracking-tight">Sentinel</div>
            <div className="text-[10px] text-gray-500 leading-none mt-1">by Hobbiton</div>
          </div>
        </div>
        <div className="border border-gray-800 rounded-xl bg-gray-900/40 p-5 space-y-4">
          <h1 className="text-sm font-semibold text-white">Choose a new password</h1>
          {done ? (
            <>
              <div className="rounded-lg border border-emerald-500/40 bg-emerald-500/10 px-3 py-2 text-xs text-emerald-300">
                Password updated. You can now sign in with your new password.
              </div>
              <Link to="/login" className={`${btnPrimary} w-full block text-center`}>
                Go to sign in
              </Link>
            </>
          ) : !token ? (
            <>
              <div className="rounded-lg border border-rose-500/40 bg-rose-500/10 px-3 py-2 text-xs text-rose-300">
                This reset link is invalid. Request a new one.
              </div>
              <Link to="/forgot-password" className="block text-center text-[11px] text-gray-500 hover:text-gray-300 transition-colors">
                Request reset link
              </Link>
            </>
          ) : (
            <form onSubmit={submit} className="space-y-4">
              {error && (
                <div className="rounded-lg border border-rose-500/40 bg-rose-500/10 px-3 py-2 text-xs text-rose-300">{error}</div>
              )}
              <div>
                <label className="block text-xs text-gray-400 mb-1">New password</label>
                <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} className={inputCls} autoFocus autoComplete="new-password" />
              </div>
              <div>
                <label className="block text-xs text-gray-400 mb-1">Confirm password</label>
                <input type="password" value={confirm} onChange={(e) => setConfirm(e.target.value)} className={inputCls} autoComplete="new-password" />
              </div>
              <button type="submit" disabled={busy || !password} className={`${btnPrimary} w-full flex items-center justify-center gap-2`}>
                {busy && <Spinner className="h-3 w-3" />}
                {busy ? 'Updating…' : 'Update password'}
              </button>
            </form>
          )}
        </div>
      </div>
    </div>
  );
}
