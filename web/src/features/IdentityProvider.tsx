import { useEffect, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api';
import { PageHeader, Feedback, Spinner, btnPrimary, btnGhost, inputCls } from '../components/ui';

const labelCls = 'block font-mono text-[10px] uppercase tracking-wider text-gray-500 mb-1';

function Step({ n, title, children }: { n: number; title: string; children: React.ReactNode }) {
  return (
    <div className="relative pl-9">
      <span className="absolute left-0 top-0 h-6 w-6 rounded-full border border-emerald-500/40 bg-emerald-500/10 font-mono text-[11px] text-emerald-300 flex items-center justify-center">
        {n}
      </span>
      <h2 className="font-display text-sm font-semibold text-white leading-6">{title}</h2>
      <div className="mt-1.5 space-y-2 text-xs text-gray-400 leading-relaxed">{children}</div>
    </div>
  );
}

export default function IdentityProvider() {
  const qc = useQueryClient();
  const { data, isLoading } = useQuery({ queryKey: ['idp-google'], queryFn: api.getGoogleIdp });
  const [form, setForm] = useState({ enabled: false, clientId: '', clientSecret: '', allowedDomain: '' });
  const [copied, setCopied] = useState(false);
  const [feedback, setFeedback] = useState<{ message: string; kind: 'success' | 'error' } | null>(null);

  useEffect(() => {
    if (data) setForm({ enabled: data.enabled, clientId: data.clientId, clientSecret: '', allowedDomain: data.allowedDomain });
  }, [data]);

  const saveMut = useMutation({
    mutationFn: () => api.saveGoogleIdp({ ...form, clientSecret: form.clientSecret || undefined }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['idp-google'] });
      setFeedback({ message: form.enabled ? 'Saved. Google sign-in is live on the login page.' : 'Saved. Google sign-in is off.', kind: 'success' });
    },
    onError: (e: { error?: string }) => setFeedback({ message: e.error ?? 'Could not save settings.', kind: 'error' }),
  });

  const copy = async () => {
    await navigator.clipboard.writeText(data!.redirectUri);
    setCopied(true);
    setTimeout(() => setCopied(false), 1500);
  };

  if (isLoading || !data) return <div className="flex justify-center py-12"><Spinner /></div>;

  const hasSecret = data.hasClientSecret || form.clientSecret !== '';

  return (
    <div className="space-y-4 px-4 lg:px-16 max-w-2xl" data-stagger>
      <PageHeader title="Identity Provider" subtitle="Let people sign in with their Google Workspace account. New accounts wait for your approval on the Users page." />

      {feedback && <Feedback message={feedback.message} kind={feedback.kind} onDismiss={() => setFeedback(null)} />}

      <div className="panel p-5 space-y-6">
        <Step n={1} title="Create an OAuth client in Google Cloud">
          <p>
            Open{' '}
            <a href="https://console.cloud.google.com/apis/credentials" target="_blank" rel="noreferrer" className="text-emerald-400 hover:underline">
              Google Cloud → APIs &amp; Services → Credentials
            </a>
            , then <span className="text-gray-200">Create credentials → OAuth client ID → Web application</span>.
          </p>
          <p>
            If asked to configure the consent screen first, choose <span className="text-gray-200">Internal</span> so only
            your Workspace accounts can use it.
          </p>
        </Step>

        <Step n={2} title="Add this authorized redirect URI">
          <p>Paste it under <span className="text-gray-200">Authorized redirect URIs</span> exactly as shown, then click Create.</p>
          <div className="flex items-center gap-2">
            <code className="flex-1 min-w-0 truncate rounded-md border border-gray-800 bg-gray-950/80 px-2.5 py-1.5 font-mono text-[11px] text-emerald-300/90">
              {data.redirectUri}
            </code>
            <button onClick={copy} className={btnGhost}>{copied ? 'Copied' : 'Copy'}</button>
          </div>
        </Step>

        <Step n={3} title="Paste the client credentials">
          <div>
            <label className={labelCls}>Client ID</label>
            <input
              value={form.clientId}
              onChange={(e) => setForm({ ...form, clientId: e.target.value })}
              placeholder="1234567890-abc.apps.googleusercontent.com"
              className={`${inputCls} font-mono`}
            />
          </div>
          <div>
            <label className={labelCls}>Client secret</label>
            <input
              type="password"
              value={form.clientSecret}
              onChange={(e) => setForm({ ...form, clientSecret: e.target.value })}
              placeholder={data.hasClientSecret ? '•••••••• stored — leave blank to keep' : 'GOCSPX-…'}
              className={`${inputCls} font-mono`}
              autoComplete="off"
            />
          </div>
        </Step>

        <Step n={4} title="Restrict to your Workspace domain">
          <p>Only Google accounts in this domain can sign in. Leave blank to allow any verified Google account (not recommended).</p>
          <input
            value={form.allowedDomain}
            onChange={(e) => setForm({ ...form, allowedDomain: e.target.value })}
            placeholder="hobbiton.co.zm"
            className={`${inputCls} font-mono`}
          />
        </Step>

        <Step n={5} title="Turn it on">
          <label className="flex items-center gap-2 cursor-pointer">
            <input
              type="checkbox"
              checked={form.enabled}
              onChange={(e) => setForm({ ...form, enabled: e.target.checked })}
              className="accent-emerald-500"
            />
            <span className="text-xs text-gray-300">Show “Continue with Google” on the login page</span>
          </label>
          <div className="pt-1">
            <button
              onClick={() => saveMut.mutate()}
              disabled={saveMut.isPending || (form.enabled && (!form.clientId || !hasSecret))}
              className={btnPrimary}
            >
              {saveMut.isPending ? 'Saving…' : 'Save'}
            </button>
          </div>
        </Step>
      </div>
    </div>
  );
}
