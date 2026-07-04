import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { api, type Me } from '../api';
import { btnPrimary, inputCls, Spinner } from '../components/ui';

const labelCls = 'block font-mono text-[10px] uppercase tracking-wider text-gray-500 mb-1';

export default function Login({ onLogin }: { onLogin: (me: Me) => void }) {
  const navigate = useNavigate();
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    setBusy(true);
    try {
      const me = await api.login(username, password);
      onLogin(me);
      navigate(me.role === 'admin' || me.role === 'developer' ? '/' : '/chat');
    } catch {
      setError('Invalid username or password.');
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
        <form onSubmit={submit} className="panel p-5 space-y-4">
          <h1 className="font-display text-sm font-semibold text-white">Sign in</h1>
          {error && (
            <div className="rise rounded-lg border border-rose-500/40 bg-rose-500/10 px-3 py-2 text-xs text-rose-300">{error}</div>
          )}
          <div>
            <label className={labelCls}>Username or email</label>
            <input
              value={username}
              onChange={(e) => setUsername(e.target.value)}
              className={inputCls}
              autoFocus
              autoComplete="username"
            />
          </div>
          <div>
            <label className={labelCls}>Password</label>
            <input
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              className={inputCls}
              autoComplete="current-password"
            />
          </div>
          <button type="submit" disabled={busy || !username || !password} className={`${btnPrimary} w-full flex items-center justify-center gap-2 py-2`}>
            {busy && <Spinner className="h-3 w-3" />}
            {busy ? 'Signing in…' : 'Sign in'}
          </button>
          <div className="flex items-center justify-between text-[11px] text-gray-500">
            <Link to="/forgot-password" className="hover:text-gray-300 transition-colors">
              Forgot password?
            </Link>
            <Link to="/signup" className="hover:text-gray-300 transition-colors">
              Create account
            </Link>
          </div>
        </form>
      </div>
    </div>
  );
}
