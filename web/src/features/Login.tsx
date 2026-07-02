import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { api, type Me } from '../api';
import { btnPrimary, inputCls, Spinner } from '../components/ui';

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
    <div className="min-h-screen bg-gray-950 flex items-center justify-center p-4">
      <div className="w-full max-w-sm">
        <div className="flex items-center justify-center gap-2.5 mb-6">
          <img src="https://hobbiton.tech/assets/logo2-7db998ca.png" alt="Hobbiton" className="h-9 w-9 rounded object-contain" />
          <div>
            <div className="font-semibold text-white leading-none tracking-tight">Sentinel</div>
            <div className="text-[10px] text-gray-500 leading-none mt-1">by Hobbiton</div>
          </div>
        </div>
        <form onSubmit={submit} className="border border-gray-800 rounded-xl bg-gray-900/40 p-5 space-y-4">
          <h1 className="text-sm font-semibold text-white">Sign in</h1>
          {error && (
            <div className="rounded-lg border border-rose-500/40 bg-rose-500/10 px-3 py-2 text-xs text-rose-300">{error}</div>
          )}
          <div>
            <label className="block text-xs text-gray-400 mb-1">Username or email</label>
            <input
              value={username}
              onChange={(e) => setUsername(e.target.value)}
              className={inputCls}
              autoFocus
              autoComplete="username"
            />
          </div>
          <div>
            <label className="block text-xs text-gray-400 mb-1">Password</label>
            <input
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              className={inputCls}
              autoComplete="current-password"
            />
          </div>
          <button type="submit" disabled={busy || !username || !password} className={`${btnPrimary} w-full flex items-center justify-center gap-2`}>
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
