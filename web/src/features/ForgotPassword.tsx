import { useState } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api';
import { btnPrimary, inputCls, Spinner } from '../components/ui';

export default function ForgotPassword() {
  const [email, setEmail] = useState('');
  const [sent, setSent] = useState(false);
  const [busy, setBusy] = useState(false);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    try {
      await api.forgotPassword(email);
    } finally {
      setSent(true);
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
          <h1 className="text-sm font-semibold text-white">Reset password</h1>
          {sent ? (
            <>
              <div className="rounded-lg border border-emerald-500/40 bg-emerald-500/10 px-3 py-2 text-xs text-emerald-300">
                If that account exists, a reset link has been sent. Check your inbox — the link expires in 1 hour.
              </div>
              <Link to="/login" className="block text-center text-[11px] text-gray-500 hover:text-gray-300 transition-colors">
                Back to sign in
              </Link>
            </>
          ) : (
            <form onSubmit={submit} className="space-y-4">
              <p className="text-xs text-gray-500">Enter your account email and we'll send you a reset link.</p>
              <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} className={inputCls} placeholder="you@hobbiton.co.zm" autoFocus />
              <button type="submit" disabled={busy || !email} className={`${btnPrimary} w-full flex items-center justify-center gap-2`}>
                {busy && <Spinner className="h-3 w-3" />}
                {busy ? 'Sending…' : 'Send reset link'}
              </button>
              <Link to="/login" className="block text-center text-[11px] text-gray-500 hover:text-gray-300 transition-colors">
                Back to sign in
              </Link>
            </form>
          )}
        </div>
      </div>
    </div>
  );
}
