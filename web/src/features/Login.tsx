import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { api, type Me } from '../api';
import { btnPrimary, inputCls, Spinner } from '../components/ui';

const labelCls = 'block font-mono text-[10px] uppercase tracking-wider text-gray-500 mb-1';

export default function Login({ onLogin }: { onLogin: (me: Me) => void }) {
  const navigate = useNavigate();
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [challenge, setChallenge] = useState<string | null>(null);
  const [code, setCode] = useState('');
  const [otpMode, setOtpMode] = useState(false);
  const [otpEmail, setOtpEmail] = useState('');
  const [otpSent, setOtpSent] = useState(false);
  const [otpCode, setOtpCode] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const afterLogin = (me: Me) => {
    onLogin(me);
    navigate(me.role === 'admin' || me.role === 'developer' ? '/' : '/chat');
  };

  const backToPassword = () => {
    setOtpMode(false);
    setOtpSent(false);
    setOtpCode('');
    setError(null);
  };

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    setBusy(true);
    try {
      const result = await api.login(username, password);
      if ('twoFactorRequired' in result) {
        setChallenge(result.challenge);
      } else {
        afterLogin(result);
      }
    } catch (err) {
      const body = err as { error?: string; verificationRequired?: boolean; email?: string };
      if (body.verificationRequired && body.email) {
        navigate(`/verify-email?email=${encodeURIComponent(body.email)}`);
        return;
      }
      setError(body.error ?? 'Invalid username or password.');
    } finally {
      setBusy(false);
    }
  };

  const submitCode = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    setBusy(true);
    try {
      const me = await api.verifyLogin2fa(challenge!, code);
      afterLogin(me);
    } catch (err) {
      setError((err as { error?: string }).error ?? 'Incorrect code.');
    } finally {
      setBusy(false);
    }
  };

  const requestOtp = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    setBusy(true);
    try {
      await api.requestLoginEmailOtp(otpEmail);
      setOtpSent(true);
    } finally {
      setBusy(false);
    }
  };

  const submitOtp = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    setBusy(true);
    try {
      const result = await api.verifyLoginEmailOtp(otpEmail, otpCode);
      if ('twoFactorRequired' in result) {
        setChallenge(result.challenge);
      } else {
        afterLogin(result);
      }
    } catch (err) {
      setError((err as { error?: string }).error ?? 'Incorrect code.');
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
        {challenge ? (
          <form onSubmit={submitCode} className="panel p-5 space-y-4">
            <h1 className="font-display text-sm font-semibold text-white">Two-factor verification</h1>
            <p className="text-xs text-gray-500">Enter the 6-digit code from your authenticator app.</p>
            {error && (
              <div className="rise rounded-lg border border-rose-500/40 bg-rose-500/10 px-3 py-2 text-xs text-rose-300">{error}</div>
            )}
            <div>
              <label className={labelCls}>Authentication code</label>
              <input
                value={code}
                onChange={(e) => setCode(e.target.value)}
                className={`${inputCls} font-mono tracking-[0.3em] text-center`}
                inputMode="numeric"
                maxLength={6}
                autoFocus
              />
            </div>
            <button type="submit" disabled={busy || code.length !== 6} className={`${btnPrimary} w-full flex items-center justify-center gap-2 py-2`}>
              {busy && <Spinner className="h-3 w-3" />}
              {busy ? 'Verifying…' : 'Verify'}
            </button>
            <button
              type="button"
              onClick={() => { setChallenge(null); setCode(''); setError(null); }}
              className="block w-full text-center text-[11px] text-gray-500 hover:text-gray-300 transition-colors"
            >
              Back to sign in
            </button>
          </form>
        ) : otpMode ? (
          otpSent ? (
            <form onSubmit={submitOtp} className="panel p-5 space-y-4">
              <h1 className="font-display text-sm font-semibold text-white">Enter your code</h1>
              <p className="text-xs text-gray-500">
                Enter the 6-digit code we sent to <span className="text-gray-300">{otpEmail}</span>.
              </p>
              {error && (
                <div className="rise rounded-lg border border-rose-500/40 bg-rose-500/10 px-3 py-2 text-xs text-rose-300">{error}</div>
              )}
              <div>
                <label className={labelCls}>Sign-in code</label>
                <input
                  value={otpCode}
                  onChange={(e) => setOtpCode(e.target.value)}
                  className={`${inputCls} font-mono tracking-[0.3em] text-center`}
                  inputMode="numeric"
                  maxLength={6}
                  autoFocus
                />
              </div>
              <button type="submit" disabled={busy || otpCode.length !== 6} className={`${btnPrimary} w-full flex items-center justify-center gap-2 py-2`}>
                {busy && <Spinner className="h-3 w-3" />}
                {busy ? 'Signing in…' : 'Sign in'}
              </button>
              <button type="button" onClick={backToPassword} className="block w-full text-center text-[11px] text-gray-500 hover:text-gray-300 transition-colors">
                Back to sign in with password
              </button>
            </form>
          ) : (
            <form onSubmit={requestOtp} className="panel p-5 space-y-4">
              <h1 className="font-display text-sm font-semibold text-white">Sign in with an email code</h1>
              {error && (
                <div className="rise rounded-lg border border-rose-500/40 bg-rose-500/10 px-3 py-2 text-xs text-rose-300">{error}</div>
              )}
              <div>
                <label className={labelCls}>Username or email</label>
                <input
                  value={otpEmail}
                  onChange={(e) => setOtpEmail(e.target.value)}
                  className={inputCls}
                  autoFocus
                  autoComplete="username"
                />
              </div>
              <button type="submit" disabled={busy || !otpEmail} className={`${btnPrimary} w-full flex items-center justify-center gap-2 py-2`}>
                {busy && <Spinner className="h-3 w-3" />}
                {busy ? 'Sending…' : 'Send code'}
              </button>
              <button type="button" onClick={backToPassword} className="block w-full text-center text-[11px] text-gray-500 hover:text-gray-300 transition-colors">
                Back to sign in with password
              </button>
            </form>
          )
        ) : (
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
            <button
              type="button"
              onClick={() => { setOtpMode(true); setOtpEmail(username); setError(null); }}
              className="block w-full text-center text-[11px] text-gray-500 hover:text-gray-300 transition-colors"
            >
              Sign in with an email code instead
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
        )}
      </div>
    </div>
  );
}
