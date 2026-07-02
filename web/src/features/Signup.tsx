import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { api, type Me } from '../api';
import { btnPrimary, inputCls, Spinner } from '../components/ui';

const labelCls = 'block font-mono text-[10px] uppercase tracking-wider text-gray-500 mb-1';

export default function Signup({ onLogin }: { onLogin: (me: Me) => void }) {
  const navigate = useNavigate();
  const [email, setEmail] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    setBusy(true);
    try {
      const me = await api.signup({ email, displayName, password, confirmPassword: confirm });
      onLogin(me);
      navigate('/chat');
    } catch (err) {
      setError((err as { error?: string }).error ?? 'Signup failed.');
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
            <span className="glow-dot" style={{ height: 5, width: 5 }} />
            <span className="font-mono text-[9px] text-gray-500 uppercase tracking-wider">Fraud intelligence by Hobbiton</span>
          </div>
        </div>
        <form onSubmit={submit} className="panel p-5 space-y-4">
          <h1 className="font-display text-sm font-semibold text-white">Create account</h1>
          {error && (
            <div className="rise rounded-lg border border-rose-500/40 bg-rose-500/10 px-3 py-2 text-xs text-rose-300">{error}</div>
          )}
          <div>
            <label className={labelCls}>Work email</label>
            <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} className={inputCls} placeholder="you@hobbiton.co.zm" autoFocus />
          </div>
          <div>
            <label className={labelCls}>Display name</label>
            <input value={displayName} onChange={(e) => setDisplayName(e.target.value)} className={inputCls} />
          </div>
          <div>
            <label className={labelCls}>Password</label>
            <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} className={inputCls} autoComplete="new-password" />
          </div>
          <div>
            <label className={labelCls}>Confirm password</label>
            <input type="password" value={confirm} onChange={(e) => setConfirm(e.target.value)} className={inputCls} autoComplete="new-password" />
          </div>
          <button type="submit" disabled={busy || !email || !password} className={`${btnPrimary} w-full flex items-center justify-center gap-2 py-2`}>
            {busy && <Spinner className="h-3 w-3" />}
            {busy ? 'Creating…' : 'Create account'}
          </button>
          <div className="text-center text-[11px] text-gray-500">
            Already have an account?{' '}
            <Link to="/login" className="text-emerald-400 hover:text-emerald-300 transition-colors">Sign in</Link>
          </div>
        </form>
      </div>
    </div>
  );
}
