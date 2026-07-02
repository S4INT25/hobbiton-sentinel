import { marked } from 'marked';

// ── formatting ──

export const fmtDate = (iso: string | null | undefined, opts?: Intl.DateTimeFormatOptions) =>
  iso
    ? new Date(iso).toLocaleString('en-GB', {
        day: '2-digit',
        month: 'short',
        hour: '2-digit',
        minute: '2-digit',
        ...opts,
      })
    : '—';

export const fmtDateFull = (iso: string | null | undefined) =>
  iso
    ? new Date(iso).toLocaleString('en-GB', {
        day: '2-digit',
        month: 'short',
        year: 'numeric',
        hour: '2-digit',
        minute: '2-digit',
      })
    : '—';

export function markdownHtml(md: string | null | undefined): string {
  if (!md) return '';
  return marked.parse(md, { async: false }) as string;
}

export function Markdown({ text, className }: { text: string | null | undefined; className?: string }) {
  return (
    <div
      className={`prose-chat max-w-none ${className ?? ''}`}
      dangerouslySetInnerHTML={{ __html: markdownHtml(text) }}
    />
  );
}

// ── CSV download ──

export function downloadCsv(filename: string, columns: string[], rows: Record<string, string>[]) {
  const esc = (v: string) => `"${(v ?? '').replace(/"/g, '""')}"`;
  const csv = [
    columns.map(esc).join(','),
    ...rows.map((r) => columns.map((c) => esc(r[c] ?? '')).join(',')),
  ].join('\n');
  const blob = new Blob(['﻿' + csv], { type: 'text/csv;charset=utf-8;' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  a.click();
  URL.revokeObjectURL(url);
}

// ── badges ──

const badge = 'inline-flex px-1.5 py-0.5 rounded text-[10px] font-medium border';

export function SeverityBadge({ severity }: { severity: string }) {
  const cls =
    severity === 'critical'
      ? 'bg-rose-500/10 text-rose-400 border-rose-500/20'
      : severity === 'high'
        ? 'bg-orange-500/10 text-orange-400 border-orange-500/20'
        : severity === 'warning' || severity === 'medium'
          ? 'bg-amber-500/10 text-amber-400 border-amber-500/20'
          : 'bg-blue-500/10 text-blue-400 border-blue-500/20';
  return <span className={`${badge} ${cls}`}>{severity}</span>;
}

export function ConfidenceBadge({ confidence }: { confidence: number }) {
  const cls =
    confidence >= 90
      ? 'bg-rose-500/10 text-rose-400 border-rose-500/20'
      : confidence >= 70
        ? 'bg-orange-500/10 text-orange-400 border-orange-500/20'
        : confidence >= 50
          ? 'bg-amber-500/10 text-amber-400 border-amber-500/20'
          : 'bg-blue-500/10 text-blue-400 border-blue-500/20';
  return <span className={`${badge} ${cls}`}>{confidence}%</span>;
}

export function StatusBadge({ status }: { status: string }) {
  const cls =
    status === 'completed' || status === 'resolved'
      ? 'bg-emerald-500/10 text-emerald-400 border-emerald-500/20'
      : status === 'error' || status === 'failed'
        ? 'bg-rose-500/10 text-rose-400 border-rose-500/20'
        : status === 'running' || status === 'pending' || status === 'queued'
          ? 'bg-blue-500/10 text-blue-400 border-blue-500/20'
          : 'bg-gray-500/10 text-gray-400 border-gray-500/20';
  return <span className={`${badge} ${cls}`}>{status}</span>;
}

// ── layout primitives ──

export function PageHeader({ title, subtitle, children }: { title: string; subtitle?: string; children?: React.ReactNode }) {
  return (
    <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2 mb-4">
      <div>
        <h1 className="text-lg font-semibold text-white">{title}</h1>
        {subtitle && <p className="text-xs text-gray-500 mt-0.5">{subtitle}</p>}
      </div>
      {children && <div className="flex items-center gap-2">{children}</div>}
    </div>
  );
}

export function Feedback({
  message,
  kind = 'success',
  onDismiss,
}: {
  message: string;
  kind?: 'success' | 'error' | 'info';
  onDismiss: () => void;
}) {
  const cls =
    kind === 'error'
      ? 'border-rose-500/40 bg-rose-500/10 text-rose-300'
      : kind === 'info'
        ? 'border-blue-500/40 bg-blue-500/10 text-blue-300'
        : 'border-emerald-500/40 bg-emerald-500/10 text-emerald-300';
  return (
    <div className={`rounded-lg border px-3 py-2 text-xs mb-3 ${cls}`} role="status">
      <div className="flex items-center justify-between gap-3">
        <span>{message}</span>
        <button onClick={onDismiss} className="text-[11px] text-gray-300 hover:text-white transition-colors">
          Dismiss
        </button>
      </div>
    </div>
  );
}

export function Dialog({
  title,
  children,
  onClose,
}: {
  title: string;
  children: React.ReactNode;
  onClose?: () => void;
}) {
  return (
    <div className="fixed inset-0 z-50 bg-black/70 backdrop-blur-sm flex items-center justify-center p-4" onClick={onClose}>
      <div
        className="w-full max-w-md border border-gray-800 bg-gray-900 rounded-xl p-4 space-y-3 shadow-2xl max-h-[85vh] overflow-y-auto"
        onClick={(e) => e.stopPropagation()}
      >
        <h3 className="text-sm font-semibold text-white">{title}</h3>
        {children}
      </div>
    </div>
  );
}

export const btnPrimary =
  'px-3 py-1.5 text-xs font-medium bg-emerald-600 hover:bg-emerald-500 disabled:opacity-50 text-white rounded-md transition-all duration-200';
export const btnDanger =
  'px-3 py-1.5 text-xs font-medium bg-rose-600 hover:bg-rose-500 disabled:opacity-50 text-white rounded-md transition-all duration-200';
export const btnGhost = 'px-3 py-1.5 text-xs text-gray-400 hover:text-white transition-colors';
export const btnOutline =
  'px-2.5 py-1.5 text-xs border border-gray-800 rounded text-gray-500 hover:text-gray-300 hover:bg-gray-900 transition-colors whitespace-nowrap';
export const inputCls =
  'w-full bg-gray-900 border border-gray-700 rounded px-3 py-1.5 text-sm text-white placeholder-gray-600 focus:border-emerald-500 focus:outline-none transition-colors';
export const selectCls =
  'px-2.5 py-1.5 bg-gray-900 border border-gray-800 rounded text-xs text-white focus:border-emerald-500 focus:outline-none';

export function Spinner({ className = 'h-4 w-4' }: { className?: string }) {
  return <div className={`animate-spin border-2 border-emerald-500 border-t-transparent rounded-full ${className}`} />;
}

export function EmptyState({ icon, title, hint }: { icon?: string; title: string; hint?: string }) {
  return (
    <div className="border border-dashed border-gray-700 rounded-lg p-8 text-center">
      {icon && <div className="text-2xl mb-2">{icon}</div>}
      <p className="text-sm text-gray-400">{title}</p>
      {hint && <p className="text-xs text-gray-600 mt-1">{hint}</p>}
    </div>
  );
}
