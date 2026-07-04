import {useState} from 'react';
import {Link} from 'react-router-dom';
import {api} from '../api';
import {btnPrimary, inputCls, Spinner} from '../components/ui';

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
        <div className="min-h-screen bg-gray-950 flex items-center justify-center p-4 relative">
            <div className="atmosphere" aria-hidden/>
            <div className="w-full max-w-sm relative z-10" data-stagger>
                <div className="flex flex-col items-center mb-7">
                    <img src="https://hobbiton.tech/assets/logo2-7db998ca.png" alt="Hobbiton"
                         className="h-10 w-10 rounded object-contain mb-3"/>
                    <div
                        className="font-display font-semibold text-lg text-white leading-none tracking-[0.22em]">SENTINEL
                    </div>
                    <div className="flex items-center gap-1.5 mt-2">
           
                    </div>
                </div>
                <div className="panel p-5 space-y-4">
                    <h1 className="font-display text-sm font-semibold text-white">Reset password</h1>
                    {sent ? (
                        <>
                            <div
                                className="rise rounded-lg border border-emerald-500/40 bg-emerald-500/10 px-3 py-2 text-xs text-emerald-300">
                                If that account exists, a reset link has been sent. Check your inbox — the link expires
                                in 1 hour.
                            </div>
                            <Link to="/login"
                                  className="block text-center text-[11px] text-gray-500 hover:text-gray-300 transition-colors">
                                Back to sign in
                            </Link>
                        </>
                    ) : (
                        <form onSubmit={submit} className="space-y-4">
                            <p className="text-xs text-gray-500">Enter your account email and we'll send you a reset
                                link.</p>
                            <input type="email" value={email} onChange={(e) => setEmail(e.target.value)}
                                   className={inputCls} placeholder="you@hobbiton.co.zm" autoFocus/>
                            <button type="submit" disabled={busy || !email}
                                    className={`${btnPrimary} w-full flex items-center justify-center gap-2 py-2`}>
                                {busy && <Spinner className="h-3 w-3"/>}
                                {busy ? 'Sending…' : 'Send reset link'}
                            </button>
                            <Link to="/login"
                                  className="block text-center text-[11px] text-gray-500 hover:text-gray-300 transition-colors">
                                Back to sign in
                            </Link>
                        </form>
                    )}
                </div>
            </div>
        </div>
    );
}
