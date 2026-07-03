import { useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { motion } from 'motion/react';
import { api } from '../api';

export type PaletteItem = {
  label: string;
  to: string;
  icon?: string;
  section: string;
  hint?: string;
};

export default function CommandPalette({ items, onClose }: { items: PaletteItem[]; onClose: () => void }) {
  const navigate = useNavigate();
  const [query, setQuery] = useState('');
  const [selected, setSelected] = useState(0);
  const listRef = useRef<HTMLDivElement>(null);

  const { data: conversations = [] } = useQuery({
    queryKey: ['conversations'],
    queryFn: api.listConversations,
    staleTime: 30_000,
  });

  const all: PaletteItem[] = useMemo(
    () => [
      { label: 'New conversation', to: '/chat', section: 'Actions', hint: 'start fresh' },
      ...items,
      ...conversations.slice(0, 8).map((c) => ({
        label: c.title,
        to: `/chat?c=${c.id}`,
        section: 'Conversations',
        hint: new Date(c.updatedAt).toLocaleDateString('en-GB', { day: '2-digit', month: 'short' }),
      })),
    ],
    [items, conversations]
  );

  const results = useMemo(() => {
    const q = query.trim().toLowerCase();
    if (!q) return all;
    const starts = all.filter((i) => i.label.toLowerCase().startsWith(q));
    const contains = all.filter((i) => !i.label.toLowerCase().startsWith(q) && i.label.toLowerCase().includes(q));
    return [...starts, ...contains];
  }, [all, query]);

  useEffect(() => setSelected(0), [query]);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
      if (e.key === 'ArrowDown') {
        e.preventDefault();
        setSelected((s) => Math.min(s + 1, results.length - 1));
      }
      if (e.key === 'ArrowUp') {
        e.preventDefault();
        setSelected((s) => Math.max(s - 1, 0));
      }
      if (e.key === 'Enter' && results[selected]) {
        navigate(results[selected].to);
        onClose();
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [results, selected, navigate, onClose]);

  useEffect(() => {
    listRef.current
      ?.querySelector(`[data-idx="${selected}"]`)
      ?.scrollIntoView({ block: 'nearest' });
  }, [selected]);

  // group in display order while keeping flat index for keyboard nav
  const sections = useMemo(() => {
    const order: string[] = [];
    const map = new Map<string, { item: PaletteItem; idx: number }[]>();
    results.forEach((item, idx) => {
      if (!map.has(item.section)) {
        map.set(item.section, []);
        order.push(item.section);
      }
      map.get(item.section)!.push({ item, idx });
    });
    return order.map((s) => ({ section: s, entries: map.get(s)! }));
  }, [results]);

  return (
    <motion.div
      initial={{ opacity: 0 }}
      animate={{ opacity: 1 }}
      exit={{ opacity: 0 }}
      transition={{ duration: 0.12 }}
      className="fixed inset-0 z-50 bg-black/60 backdrop-blur-sm flex items-start justify-center pt-[18vh] px-4"
      onClick={onClose}
    >
      <motion.div
        initial={{ opacity: 0, scale: 0.97, y: -8 }}
        animate={{ opacity: 1, scale: 1, y: 0 }}
        exit={{ opacity: 0, scale: 0.97, y: -8 }}
        transition={{ duration: 0.18, ease: [0.22, 1, 0.36, 1] }}
        className="w-full max-w-lg rounded-xl border border-gray-700/80 bg-gray-900 shadow-2xl overflow-hidden"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-center gap-2.5 px-4 border-b border-gray-800">
          <svg className="w-4 h-4 text-gray-500 shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M21 21l-4.35-4.35M17 11a6 6 0 11-12 0 6 6 0 0112 0z" />
          </svg>
          <input
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            autoFocus
            placeholder="Jump to a page or conversation…"
            className="flex-1 bg-transparent py-3.5 text-sm text-white placeholder-gray-600 focus:outline-none"
          />
          <kbd>esc</kbd>
        </div>
        <div ref={listRef} className="max-h-80 overflow-y-auto py-2">
          {sections.map(({ section, entries }) => (
            <div key={section}>
              <div className="kicker px-4 pt-2 pb-1">{section}</div>
              {entries.map(({ item, idx }) => (
                <button
                  key={`${item.to}-${idx}`}
                  data-idx={idx}
                  onClick={() => {
                    navigate(item.to);
                    onClose();
                  }}
                  onMouseMove={() => setSelected(idx)}
                  className={`w-full flex items-center gap-3 px-4 py-2 text-left text-sm transition-colors ${
                    idx === selected ? 'bg-emerald-500/10 text-white' : 'text-gray-400'
                  }`}
                >
                  <span
                    className={`h-3.5 w-0.5 rounded-full shrink-0 ${idx === selected ? 'bg-emerald-400' : 'bg-transparent'}`}
                  />
                  {item.icon && (
                    <svg className="w-4 h-4 shrink-0 opacity-70" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="1.5" d={item.icon} />
                    </svg>
                  )}
                  <span className="flex-1 truncate">{item.label}</span>
                  {item.hint && <span className="font-mono text-[10px] text-gray-600">{item.hint}</span>}
                </button>
              ))}
            </div>
          ))}
          {results.length === 0 && (
            <div className="px-4 py-8 text-center text-xs text-gray-600">Nothing matches “{query}”</div>
          )}
        </div>
      </motion.div>
    </motion.div>
  );
}
