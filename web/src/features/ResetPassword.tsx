import { useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { api } from '../api';
import { btnPrimary, inputCls, Spinner } from '../components/ui';

const labelCls = 'block font-mono text-[10px] uppercase tracking-wider text-gray-500 mb-1';

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
    <div className="min-h-screen bg-gray-950 flex items-center justify-center p-4 relative">
      <div className="atmosphere" aria-hidden />
      <div className="w-full max-w-sm relative z-10" data-stagger>
        <div className="flex flex-col items-center mb-7">
          <img src="https://hobbiton.tech/assets/logo2-7db998ca.png" alt="Hobbiton" className="h-10 w-10 rounded object-contain mb-3" />
          <div className="font-display font-semibold text-lg text-white leading-none tracking-[0.22em]">SENTINEL</div>
          <div className="flex items-center gap-1.5 mt-2">
           
          </div>
        </div>
        <div className="panel p-5 space-y-4">
          <h1 className="font-display text-sm font-semibold text-white">Choose a new password</h1>
          {done ? (
            <>
              <div className="rise rounded-lg border border-emerald-500/40 bg-emerald-500/10 px-3 py-2 text-xs text-emerald-300">
                Password updated. You can now sign in with your new password.
              </div>
              <Link to="/login" className={`${btnPrimary} w-full block text-center py-2`}>
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
                <div className="rise rounded-lg border border-rose-500/40 bg-rose-500/10 px-3 py-2 text-xs text-rose-300">{error}</div>
              )}
              <div>
                <label className={labelCls}>New password</label>
                <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} className={inputCls} autoFocus autoComplete="new-password" />
              </div>
              <div>
                <label className={labelCls}>Confirm password</label>
                <input type="password" value={confirm} onChange={(e) => setConfirm(e.target.value)} className={inputCls} autoComplete="new-password" />
              </div>
              <button type="submit" disabled={busy || !password} className={`${btnPrimary} w-full flex items-center justify-center gap-2 py-2`}>
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
