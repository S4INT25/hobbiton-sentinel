import { marked } from 'marked';
import { motion } from 'motion/react';

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

const badge = 'inline-flex px-1.5 py-0.5 rounded font-mono text-[10px] font-medium uppercase tracking-wide border';

export function SeverityBadge({ severity }: { severity: string }) {
  const cls =
    severity === 'critical'
      ? 'bg-rose-500/10 text-rose-400 border-rose-500/25'
      : severity === 'high'
        ? 'bg-orange-500/10 text-orange-400 border-orange-500/25'
        : severity === 'warning' || severity === 'medium'
          ? 'bg-amber-500/10 text-amber-400 border-amber-500/25'
          : 'bg-sky-500/10 text-sky-400 border-sky-500/25';
  return <span className={`${badge} ${cls}`}>{severity}</span>;
}

export function ConfidenceBadge({ confidence }: { confidence: number }) {
  const cls =
    confidence >= 90
      ? 'bg-rose-500/10 text-rose-400 border-rose-500/25'
      : confidence >= 70
        ? 'bg-orange-500/10 text-orange-400 border-orange-500/25'
        : confidence >= 50
          ? 'bg-amber-500/10 text-amber-400 border-amber-500/25'
          : 'bg-sky-500/10 text-sky-400 border-sky-500/25';
  return <span className={`${badge} ${cls} tnum`}>{confidence}%</span>;
}

export function StatusBadge({ status }: { status: string }) {
  const running = status === 'running' || status === 'pending' || status === 'queued';
  const cls =
    status === 'completed' || status === 'resolved'
      ? 'bg-emerald-500/10 text-emerald-400 border-emerald-500/25'
      : status === 'error' || status === 'failed'
        ? 'bg-rose-500/10 text-rose-400 border-rose-500/25'
        : running
          ? 'bg-sky-500/10 text-sky-400 border-sky-500/25'
          : 'bg-gray-500/10 text-gray-400 border-gray-600/40';
  return (
    <span className={`${badge} ${cls} items-center gap-1.5`}>
      {running && <span className="inline-block h-1 w-1 rounded-full bg-sky-400 animate-pulse" />}
      {status}
    </span>
  );
}

// ── layout primitives ──

export function PageHeader({ title, subtitle, children }: { title: string; subtitle?: string; children?: React.ReactNode }) {
  return (
    <div className="flex flex-col sm:flex-row sm:items-end justify-between gap-3 mb-5">
      <div className="min-w-0">
        <h1 className="font-display text-xl font-semibold text-white tracking-tight flex items-center gap-2.5">
          <span aria-hidden className="h-4 w-1 rounded-full bg-emerald-400/90 shadow-[0_0_12px_rgb(16_185_129/0.6)]" />
          {title}
        </h1>
        {subtitle && <p className="text-xs text-gray-500 mt-1 ml-3.5">{subtitle}</p>}
      </div>
      {children && <div className="flex items-center gap-2 shrink-0">{children}</div>}
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
        ? 'border-sky-500/40 bg-sky-500/10 text-sky-300'
        : 'border-emerald-500/40 bg-emerald-500/10 text-emerald-300';
  return (
    <div className={`rise rounded-lg border px-3 py-2 text-xs mb-3 backdrop-blur-sm ${cls}`} role="status">
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
    <motion.div
      initial={{ opacity: 0 }}
      animate={{ opacity: 1 }}
      transition={{ duration: 0.15 }}
      className="fixed inset-0 z-50 bg-black/70 backdrop-blur-sm flex items-center justify-center p-4"
      onClick={onClose}
    >
      <motion.div
        initial={{ opacity: 0, scale: 0.96, y: 8 }}
        animate={{ opacity: 1, scale: 1, y: 0 }}
        transition={{ duration: 0.2, ease: [0.22, 1, 0.36, 1] }}
        className="panel w-full max-w-md p-4 space-y-3 shadow-2xl max-h-[85vh] overflow-y-auto"
        onClick={(e) => e.stopPropagation()}
      >
        <h3 className="font-display text-sm font-semibold text-white">{title}</h3>
        {children}
      </motion.div>
    </motion.div>
  );
}

export const btnPrimary =
  'px-3 py-1.5 text-xs font-semibold bg-emerald-500 hover:bg-emerald-400 disabled:opacity-40 disabled:hover:bg-emerald-500 text-gray-950 rounded-md transition-all duration-200 active:scale-[0.98] hover:shadow-[0_0_20px_-4px_rgb(16_185_129/0.5)]';
export const btnDanger =
  'px-3 py-1.5 text-xs font-semibold bg-rose-600 hover:bg-rose-500 disabled:opacity-40 text-white rounded-md transition-all duration-200 active:scale-[0.98]';
export const btnGhost = 'px-3 py-1.5 text-xs text-gray-400 hover:text-white transition-colors rounded-md hover:bg-gray-800/50';
export const btnOutline =
  'px-2.5 py-1.5 text-xs border border-gray-700/80 rounded-md text-gray-400 hover:text-gray-200 hover:border-gray-600 hover:bg-gray-900/80 transition-all whitespace-nowrap';
export const inputCls =
  'w-full bg-gray-950/60 border border-gray-700/80 rounded-md px-3 py-1.5 text-sm text-white placeholder-gray-600 focus:border-emerald-400/60 focus:ring-2 focus:ring-emerald-500/15 focus:outline-none transition-all';
export const selectCls =
  'px-2.5 py-1.5 bg-gray-950/60 border border-gray-700/80 rounded-md text-xs text-gray-200 focus:border-emerald-400/60 focus:outline-none transition-colors';

// shared table recipe — keeps the 10+ tables in the app consistent
export const tableWrap = 'panel overflow-hidden';
export const thCls =
  'text-left px-4 py-2.5 font-mono text-[10px] font-medium uppercase tracking-[0.12em] text-gray-500 whitespace-nowrap';
export const tdCls = 'px-4 py-2.5 text-xs text-gray-300';

export function Spinner({ className = 'h-4 w-4' }: { className?: string }) {
  return (
    <div
      className={`animate-spin rounded-full border-2 border-gray-700/60 border-t-emerald-400 ${className}`}
    />
  );
}

export function EmptyState({ icon, title, hint }: { icon?: string; title: string; hint?: string }) {
  return (
    <div className="panel p-10 text-center rise">
      {icon ? (
        <div className="text-2xl mb-3">{icon}</div>
      ) : (
        <div className="radar h-14 w-14 mx-auto mb-4 opacity-80" aria-hidden />
      )}
      <p className="font-display text-sm text-gray-300">{title}</p>
      {hint && <p className="text-xs text-gray-600 mt-1.5">{hint}</p>}
    </div>
  );
}
