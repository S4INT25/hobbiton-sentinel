import { useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { api, type Me } from '../api';
import { btnPrimary, inputCls, Spinner } from '../components/ui';

const labelCls = 'block font-mono text-[10px] uppercase tracking-wider text-gray-500 mb-1';

// Set by the Google callback redirect (/api/auth/google/callback).
const GOOGLE_ERRORS: Record<string, string> = {
  google_failed: 'Google sign-in failed. Please try again.',
  google_domain: 'That Google account is not part of the allowed organisation.',
  google_disabled: 'Google sign-in is not enabled.',
  disabled: 'Your account has been disabled. Contact an administrator.',
};

export default function Login({ onLogin }: { onLogin: (me: Me) => void }) {
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const { data: google } = useQuery({ queryKey: ['google-enabled'], queryFn: api.googleEnabled });
  const pending = params.get('pending') === '1';
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [challenge, setChallenge] = useState<string | null>(null);
  const [code, setCode] = useState('');
  const [otpMode, setOtpMode] = useState(false);
  const [otpEmail, setOtpEmail] = useState('');
  const [otpSent, setOtpSent] = useState(false);
  const [otpCode, setOtpCode] = useState('');
  const [error, setError] = useState<string | null>(GOOGLE_ERRORS[params.get('error') ?? ''] ?? null);
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
      const body = err as { error?: string };
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
        {pending && (
          <div className="rise panel mb-4 p-4 text-xs text-gray-300 border-amber-500/30">
            <div className="font-display text-sm font-semibold text-amber-300 mb-1">Waiting for approval</div>
            Your Google account is registered. An administrator has been notified and must approve it before you can sign in.
          </div>
        )}
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
            {google?.enabled && (
              <>
                {/* Full navigation, not fetch: the server redirects to Google's consent page. */}
                <a
                  href="/api/auth/google/start"
                  className="flex w-full items-center justify-center gap-2 rounded-lg border border-gray-700 bg-white px-3 py-2 text-sm font-medium text-gray-900 hover:bg-gray-100 transition-colors"
                >
                  <svg className="h-4 w-4" viewBox="0 0 48 48" aria-hidden>
                    <path fill="#FFC107" d="M43.6 20.5H42V20H24v8h11.3C33.7 32.7 29.2 36 24 36c-6.6 0-12-5.4-12-12s5.4-12 12-12c3.1 0 5.8 1.2 7.9 3.1l5.7-5.7C34 6.1 29.3 4 24 4 12.9 4 4 12.9 4 24s8.9 20 20 20 20-8.9 20-20c0-1.3-.1-2.4-.4-3.5z" />
                    <path fill="#FF3D00" d="M6.3 14.7l6.6 4.8C14.7 15.1 19 12 24 12c3.1 0 5.8 1.2 7.9 3.1l5.7-5.7C34 6.1 29.3 4 24 4 16.3 4 9.7 8.3 6.3 14.7z" />
                    <path fill="#4CAF50" d="M24 44c5.2 0 9.9-2 13.4-5.2l-6.2-5.2C29.2 35.1 26.7 36 24 36c-5.2 0-9.6-3.3-11.3-8l-6.5 5C9.5 39.6 16.2 44 24 44z" />
                    <path fill="#1976D2" d="M43.6 20.5H42V20H24v8h11.3c-.8 2.2-2.2 4.2-4.1 5.6l6.2 5.2C37 39.2 44 34 44 24c0-1.3-.1-2.4-.4-3.5z" />
                  </svg>
                  Continue with Google
                </a>
                <div className="flex items-center gap-2 text-[10px] font-mono uppercase tracking-wider text-gray-600">
                  <span className="h-px flex-1 bg-gray-800" />or<span className="h-px flex-1 bg-gray-800" />
                </div>
              </>
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
            <div className="text-center text-[11px] text-gray-500">
              <Link to="/forgot-password" className="hover:text-gray-300 transition-colors">
                Forgot password?
              </Link>
            </div>
          </form>
        )}
      </div>
    </div>
  );
}
