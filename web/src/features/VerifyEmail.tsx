import { useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { api, type Me } from '../api';
import { btnPrimary, inputCls, Spinner } from '../components/ui';

const labelCls = 'block font-mono text-[10px] uppercase tracking-wider text-gray-500 mb-1';

export default function VerifyEmail({ onLogin }: { onLogin: (me: Me) => void }) {
  const [params] = useSearchParams();
  const email = params.get('email') ?? '';
  const navigate = useNavigate();
  const [code, setCode] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [resent, setResent] = useState(false);
  const [busy, setBusy] = useState(false);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    setBusy(true);
    try {
      const me = await api.verifyEmail(email, code);
      onLogin(me);
      navigate('/chat');
    } catch (err) {
      setError((err as { error?: string }).error ?? 'Verification failed.');
    } finally {
      setBusy(false);
    }
  };

  const resend = async () => {
    setError(null);
    setResent(false);
    await api.resendVerification(email);
    setResent(true);
  };

  return (
    <div className="min-h-screen bg-gray-950 flex items-center justify-center p-4 relative">
      <div className="atmosphere" aria-hidden />
      <div className="w-full max-w-sm relative z-10" data-stagger>
        <div className="flex flex-col items-center mb-7">
          <img src="https://hobbiton.tech/assets/logo2-7db998ca.png" alt="Hobbiton" className="h-10 w-10 rounded object-contain mb-3" />
          <div className="font-display font-semibold text-lg text-white leading-none tracking-[0.22em]">SENTINEL</div>
        </div>
        <div className="panel p-5 space-y-4">
          <h1 className="font-display text-sm font-semibold text-white">Verify your email</h1>
          {!email ? (
            <>
              <div className="rounded-lg border border-rose-500/40 bg-rose-500/10 px-3 py-2 text-xs text-rose-300">
                Missing email address. Please sign up again.
              </div>
              <Link to="/signup" className="block text-center text-[11px] text-gray-500 hover:text-gray-300 transition-colors">
                Back to sign up
              </Link>
            </>
          ) : (
            <form onSubmit={submit} className="space-y-4">
              <p className="text-xs text-gray-500">
                Enter the 6-digit code we sent to <span className="text-gray-300">{email}</span>.
              </p>
              {error && (
                <div className="rise rounded-lg border border-rose-500/40 bg-rose-500/10 px-3 py-2 text-xs text-rose-300">{error}</div>
              )}
              {resent && (
                <div className="rise rounded-lg border border-emerald-500/40 bg-emerald-500/10 px-3 py-2 text-xs text-emerald-300">
                  A new code has been sent.
                </div>
              )}
              <div>
                <label className={labelCls}>Verification code</label>
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
              <button type="button" onClick={resend} className="block w-full text-center text-[11px] text-gray-500 hover:text-gray-300 transition-colors">
                Resend code
              </button>
            </form>
          )}
        </div>
      </div>
    </div>
  );
}
