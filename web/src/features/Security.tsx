import { useEffect, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { AnimatePresence } from 'motion/react';
import QRCode from 'qrcode';
import { api } from '../api';
import { PageHeader, Feedback, Dialog, Spinner, btnPrimary, btnDanger, btnGhost, inputCls } from '../components/ui';

const labelCls = 'block font-mono text-[10px] uppercase tracking-wider text-gray-500 mb-1';

export default function Security() {
  const qc = useQueryClient();
  const { data, isLoading } = useQuery({ queryKey: ['2fa-status'], queryFn: api.get2faStatus });

  const [setupData, setSetupData] = useState<{ secret: string; otpauthUrl: string } | null>(null);
  const [qr, setQr] = useState<string | null>(null);
  const [code, setCode] = useState('');
  const [disableOpen, setDisableOpen] = useState(false);
  const [password, setPassword] = useState('');
  const [feedback, setFeedback] = useState<{ message: string; kind: 'success' | 'error' } | null>(null);

  useEffect(() => {
    if (!setupData) { setQr(null); return; }
    QRCode.toDataURL(setupData.otpauthUrl, { margin: 1, width: 200 }).then(setQr);
  }, [setupData]);

  const invalidateStatus = () => qc.invalidateQueries({ queryKey: ['2fa-status'] });

  const setupMut = useMutation({
    mutationFn: api.setup2fa,
    onSuccess: setSetupData,
    onError: () => setFeedback({ message: 'Could not start 2FA setup.', kind: 'error' }),
  });

  const enableMut = useMutation({
    mutationFn: () => api.enable2fa(code),
    onSuccess: () => {
      invalidateStatus();
      setSetupData(null);
      setCode('');
      setFeedback({ message: 'Two-factor authentication enabled.', kind: 'success' });
    },
    onError: (e: { error?: string }) => setFeedback({ message: e.error ?? 'Incorrect code.', kind: 'error' }),
  });

  const disableMut = useMutation({
    mutationFn: () => api.disable2fa(password),
    onSuccess: () => {
      invalidateStatus();
      setDisableOpen(false);
      setPassword('');
      setFeedback({ message: 'Two-factor authentication disabled.', kind: 'success' });
    },
    onError: (e: { error?: string }) => setFeedback({ message: e.error ?? 'Incorrect password.', kind: 'error' }),
  });

  return (
    <div className="space-y-4 px-4 lg:px-16 max-w-lg" data-stagger>
      <PageHeader title="Security" subtitle="Manage two-factor authentication for your account" />

      {feedback && <Feedback message={feedback.message} kind={feedback.kind} onDismiss={() => setFeedback(null)} />}

      <div className="panel p-5">
        {isLoading ? (
          <div className="flex justify-center py-4"><Spinner /></div>
        ) : setupData ? (
          <div className="space-y-4">
            <p className="text-xs text-gray-400">
              Scan this QR code with your authenticator app (Google Authenticator, Authy, 1Password…), then enter
              the 6-digit code it shows to confirm.
            </p>
            {qr && <img src={qr} alt="Two-factor setup QR code" className="mx-auto rounded-lg border border-gray-800" />}
            <div>
              <label className={labelCls}>Can't scan? Enter this key manually</label>
              <code className="block text-xs text-gray-300 bg-gray-950/60 border border-gray-800 rounded-md px-3 py-1.5 break-all">
                {setupData.secret}
              </code>
            </div>
            <div>
              <label className={labelCls}>Confirmation code</label>
              <input
                value={code}
                onChange={(e) => setCode(e.target.value)}
                className={`${inputCls} font-mono tracking-[0.3em] text-center`}
                inputMode="numeric"
                maxLength={6}
                autoFocus
              />
            </div>
            <div className="flex items-center justify-end gap-2">
              <button onClick={() => { setSetupData(null); setCode(''); }} className={btnGhost}>Cancel</button>
              <button
                onClick={() => enableMut.mutate()}
                disabled={enableMut.isPending || code.length !== 6}
                className={btnPrimary}
              >
                {enableMut.isPending ? 'Confirming…' : 'Confirm & enable'}
              </button>
            </div>
          </div>
        ) : data?.enabled ? (
          <div className="flex items-center justify-between gap-3">
            <div className="flex items-center gap-2 text-emerald-400 text-sm">
              <span className="inline-block h-1.5 w-1.5 rounded-full bg-emerald-400" />
              Two-factor authentication is enabled
            </div>
            <button onClick={() => setDisableOpen(true)} className={btnDanger}>Disable</button>
          </div>
        ) : (
          <div className="flex items-center justify-between gap-3">
            <div className="text-sm text-gray-400">Two-factor authentication is disabled.</div>
            <button onClick={() => setupMut.mutate()} disabled={setupMut.isPending} className={btnPrimary}>
              {setupMut.isPending ? 'Starting…' : 'Enable 2FA'}
            </button>
          </div>
        )}
      </div>

      <AnimatePresence>
        {disableOpen && (
          <Dialog title="Disable two-factor authentication" onClose={() => setDisableOpen(false)}>
            <p className="text-xs text-gray-400">Enter your password to confirm.</p>
            <div>
              <label className={labelCls}>Password</label>
              <input
                type="password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                className={inputCls}
                autoFocus
              />
            </div>
            <div className="flex items-center justify-end gap-2 pt-1">
              <button onClick={() => setDisableOpen(false)} className={btnGhost}>Cancel</button>
              <button
                onClick={() => disableMut.mutate()}
                disabled={disableMut.isPending || !password}
                className={btnDanger}
              >
                {disableMut.isPending ? 'Disabling…' : 'Disable'}
              </button>
            </div>
          </Dialog>
        )}
      </AnimatePresence>
    </div>
  );
}
