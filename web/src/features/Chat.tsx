import { useEffect, useMemo, useRef, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { motion, AnimatePresence } from 'motion/react';
import { api, type ChatEntry, type Conversation, type QueryResult, type StreamEvent } from '../api';
import { Markdown, downloadCsv, Dialog, btnGhost, btnDanger } from '../components/ui';
import { DataChart, DataTable, applicableChartTypes } from '../components/charts';

// user prefs survive reloads; server-side cache prefs from the Blazor app move to localStorage
const DB_KEY = 'sentinel.chat.db';
const MODE_KEY = 'sentinel.chat.mode';

type MessageVM = {
  role: string;
  content: string;
  entry: ChatEntry | null;
};

export default function Chat() {
  const qc = useQueryClient();

  // conversation id lives in the URL so the command palette / history can deep-link
  const [searchParams, setSearchParams] = useSearchParams();
  const activeId = searchParams.get('c');
  const selectConv = (id: string | null) => setSearchParams(id ? { c: id } : {});

  const [database, setDatabase] = useState(() => localStorage.getItem(DB_KEY) ?? '');
  const [mode, setMode] = useState(() => localStorage.getItem(MODE_KEY) ?? 'general');
  const [input, setInput] = useState('');
  const [jobId, setJobId] = useState<string | null>(null);
  const [pendingPrompt, setPendingPrompt] = useState<string | null>(null);
  const [historyOpen, setHistoryOpen] = useState(true);
  const [historyQuery, setHistoryQuery] = useState('');
  const [menuConvId, setMenuConvId] = useState<string | null>(null);
  const [renameId, setRenameId] = useState<string | null>(null);
  const [renameText, setRenameText] = useState('');
  const [shareCopiedId, setShareCopiedId] = useState<string | null>(null);
  const [deleteConvId, setDeleteConvId] = useState<string | null>(null);
  const [chartTypeOverrides, setChartTypeOverrides] = useState<Record<string, string>>({});
  const bottomRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLTextAreaElement>(null);

  const { data: products = [] } = useQuery({ queryKey: ['products-enabled'], queryFn: api.enabledProducts });
  const { data: conversations = [] } = useQuery({ queryKey: ['conversations'], queryFn: api.listConversations });
  const { data: conversation } = useQuery({
    queryKey: ['conversation', activeId],
    queryFn: () => api.getConversation(activeId!),
    enabled: !!activeId,
  });

  // default database once products load
  useEffect(() => {
    if (!database && products.length > 0) setDatabase(products[0].databaseName);
  }, [products, database]);

  useEffect(() => localStorage.setItem(DB_KEY, database), [database]);
  useEffect(() => localStorage.setItem(MODE_KEY, mode), [mode]);

  // job polling — React Query refetchInterval stands in for streaming
  const { data: job } = useQuery({
    queryKey: ['job', jobId],
    queryFn: () => api.getJob(jobId!),
    enabled: !!jobId,
    refetchInterval: (q) => {
      const s = q.state.data?.status;
      return s === 'completed' || s === 'failed' ? false : 1000;
    },
  });

  useEffect(() => {
    if (!job) return;
    if (job.status === 'completed' || job.status === 'failed') {
      setJobId(null);
      setPendingPrompt(null);
      if (job.conversationId && job.conversationId !== activeId) selectConv(job.conversationId);
      qc.invalidateQueries({ queryKey: ['conversation', job.conversationId] });
      qc.invalidateQueries({ queryKey: ['conversations'] });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [job, activeId, qc]);

  // resume a running job after page reload
  useEffect(() => {
    if (!activeId || jobId) return;
    let cancelled = false;
    fetch(`/api/analytics/jobs?conversationId=${activeId}`, { credentials: 'include' })
      .then((r) => r.json())
      .then((jobs: { jobId: string; status: string }[]) => {
        const running = jobs.find((j) => j.status === 'pending' || j.status === 'running');
        if (running && !cancelled) setJobId(running.jobId);
      })
      .catch(() => {});
    return () => { cancelled = true; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeId]);

  const messages: MessageVM[] = useMemo(() => {
    const list: MessageVM[] = (conversation?.messages ?? []).map((m) => ({ role: m.role, content: m.content, entry: m }));
    if (pendingPrompt) list.push({ role: 'user', content: pendingPrompt, entry: null });
    return list;
  }, [conversation, pendingPrompt]);

  const historyGroups = useMemo(() => {
    const q = historyQuery.trim().toLowerCase();
    const filtered = q ? conversations.filter((c) => c.title.toLowerCase().includes(q)) : conversations;
    return groupConversations(filtered);
  }, [conversations, historyQuery]);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [messages.length, job?.streamEvents?.length]);

  const askMut = useMutation({
    mutationFn: (prompt: string) => api.ask(prompt, database, activeId ?? undefined, mode),
    onSuccess: (res, prompt) => {
      setJobId(res.jobId);
      setPendingPrompt(prompt);
      if (!activeId) selectConv(res.conversationId);
      qc.invalidateQueries({ queryKey: ['conversations'] });
    },
  });

  const send = () => {
    const prompt = input.trim();
    if (!prompt || jobId) return;
    setInput('');
    if (inputRef.current) inputRef.current.style.height = 'auto';
    askMut.mutate(prompt);
  };

  const quickAsk = (prompt: string) => {
    if (jobId) return;
    askMut.mutate(prompt);
  };

  const newConversation = () => {
    selectConv(null);
    setJobId(null);
    setPendingPrompt(null);
  };

  const shareConv = async (id: string) => {
    setMenuConvId(null);
    await api.shareConversation(id);
    const url = `${location.origin}/shared/${id}`;
    try {
      await navigator.clipboard.writeText(url);
    } catch {
      window.prompt('Copy this link:', url);
    }
    setShareCopiedId(id);
    setTimeout(() => setShareCopiedId(null), 2000);
  };

  const finishRename = async () => {
    if (renameId && renameText.trim()) {
      await api.renameConversation(renameId, renameText.trim());
      qc.invalidateQueries({ queryKey: ['conversations'] });
    }
    setRenameId(null);
  };

  const deleteConv = async () => {
    if (!deleteConvId) return;
    await api.deleteConversation(deleteConvId);
    if (deleteConvId === activeId) newConversation();
    setDeleteConvId(null);
    qc.invalidateQueries({ queryKey: ['conversations'] });
  };

  const autoGrow = () => {
    const el = inputRef.current;
    if (!el) return;
    el.style.height = 'auto';
    el.style.height = `${Math.min(el.scrollHeight, 200)}px`;
  };

  const loading = !!jobId;
  const streamEvents = (job?.streamEvents ?? []).filter((e) => e.type !== 'token' && e.type !== 'result');
  const empty = messages.length === 0 && !loading;

  const quickAskSuggestions = [
    {
      label: 'Today at a glance',
      desc: 'Volumes, success rates, anomalies',
      prompt: `Give me a comprehensive summary of today's key metrics, trends, and notable activity in '${database}'. Include total volumes, success/failure rates, and anything unusual.`,
    },
    {
      label: 'Weekly trend',
      desc: 'Daily volume + value, charted',
      prompt: `Show me the daily transaction volume and value for the last 7 days in '${database}' as a chart.`,
    },
    {
      label: 'Failure analysis',
      desc: 'Top failure reasons, last 24h',
      prompt: `What are the top failure reasons in '${database}' over the last 24 hours?`,
    },
  ];

  return (
    <div className="flex h-[calc(100vh-6rem)] md:h-full -m-4 md:-m-6 print:h-auto print:m-0">
      {/* Conversation history — docked panel */}
      <motion.div
        id="chat-history-panel"
        initial={false}
        animate={{ width: historyOpen ? 264 : 0 }}
        transition={{ duration: 0.25, ease: [0.22, 1, 0.36, 1] }}
        className="border-r border-gray-800/80 flex-col shrink-0 overflow-hidden hidden md:flex bg-gray-950/40"
      >
        <div className="flex items-center justify-between px-3 py-2.5 border-b border-gray-800/80 min-w-[16.5rem]">
          <span className="kicker">History</span>
          <div className="flex items-center gap-1.5">
            <button
              onClick={newConversation}
              className="px-2 py-1 font-mono text-[10px] uppercase tracking-wide bg-emerald-500 hover:bg-emerald-400 text-gray-950 font-semibold rounded transition-colors"
            >
              + New
            </button>
            <button onClick={() => setHistoryOpen(false)} className="p-1 text-gray-500 hover:text-gray-300 rounded hover:bg-gray-800 transition-colors">
              <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M11 19l-7-7 7-7" />
              </svg>
            </button>
          </div>
        </div>
        <div className="px-2 pt-2 min-w-[16.5rem]">
          <div className="relative">
            <svg className="absolute left-2 top-1/2 -translate-y-1/2 w-3.5 h-3.5 text-gray-600 pointer-events-none" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M21 21l-4.35-4.35M17 11a6 6 0 11-12 0 6 6 0 0112 0z" />
            </svg>
            <input
              value={historyQuery}
              onChange={(e) => setHistoryQuery(e.target.value)}
              placeholder="Search chats…"
              className="w-full bg-gray-900/60 border border-gray-800 rounded-lg pl-7 pr-2 py-1.5 text-xs text-gray-200 placeholder-gray-600 focus:border-emerald-400/50 focus:outline-none transition-colors"
            />
          </div>
        </div>
        <div className="flex-1 overflow-y-auto p-1.5 min-w-[16.5rem]">
          {historyGroups.map((g) => (
            <div key={g.label}>
              <div className="kicker px-2 pt-2.5 pb-1">{g.label}</div>
              <div className="space-y-0.5">
              {g.items.map((conv) => (
            <div
              key={conv.id}
              onClick={() => { selectConv(conv.id); setJobId(null); setPendingPrompt(null); }}
              className={`relative px-2.5 py-2 rounded-lg border cursor-pointer group transition-colors ${
                conv.id === activeId
                  ? 'bg-emerald-500/[0.07] border-emerald-500/25'
                  : 'border-transparent hover:bg-gray-900/70'
              }`}
            >
              {renameId === conv.id ? (
                <input
                  value={renameText}
                  onChange={(e) => setRenameText(e.target.value)}
                  onKeyDown={(e) => e.key === 'Enter' && finishRename()}
                  onBlur={finishRename}
                  onClick={(e) => e.stopPropagation()}
                  autoFocus
                  className="w-full bg-gray-950 border border-emerald-500/40 rounded px-1.5 py-0.5 text-xs text-white focus:outline-none"
                />
              ) : (
                <>
                  <div className={`text-xs truncate pr-6 ${conv.id === activeId ? 'text-white' : 'text-gray-300'}`}>{conv.title}</div>
                  <div className="font-mono text-[10px] text-gray-600 mt-0.5">
                    {shareCopiedId === conv.id ? (
                      <span className="text-emerald-400">Link copied!</span>
                    ) : (
                      fmtRelative(conv.updatedAt)
                    )}
                  </div>
                  <button
                    onClick={(e) => { e.stopPropagation(); setMenuConvId(menuConvId === conv.id ? null : conv.id); }}
                    className="absolute right-1.5 top-2 p-1 text-gray-600 hover:text-gray-300 opacity-0 group-hover:opacity-100 transition-opacity rounded"
                  >
                    <svg className="w-3.5 h-3.5" fill="currentColor" viewBox="0 0 24 24">
                      <path d="M12 8a2 2 0 110-4 2 2 0 010 4zm0 6a2 2 0 110-4 2 2 0 010 4zm0 6a2 2 0 110-4 2 2 0 010 4z" />
                    </svg>
                  </button>
                  <AnimatePresence>
                    {menuConvId === conv.id && (
                      <motion.div
                        initial={{ opacity: 0, scale: 0.95, y: -4 }}
                        animate={{ opacity: 1, scale: 1, y: 0 }}
                        exit={{ opacity: 0, scale: 0.95, y: -4 }}
                        transition={{ duration: 0.12 }}
                        className="absolute right-1 top-8 z-20 w-36 rounded-xl border border-gray-700/80 bg-gray-900 py-1 shadow-xl"
                        onClick={(e) => e.stopPropagation()}
                      >
                        <button onClick={() => shareConv(conv.id)} className="w-full text-left px-3 py-1.5 text-xs text-gray-300 hover:bg-gray-800 transition-colors">
                          Copy share link
                        </button>
                        <button
                          onClick={() => { setMenuConvId(null); setRenameId(conv.id); setRenameText(conv.title); }}
                          className="w-full text-left px-3 py-1.5 text-xs text-gray-300 hover:bg-gray-800 transition-colors"
                        >
                          Rename
                        </button>
                        <button
                          onClick={() => { setMenuConvId(null); setDeleteConvId(conv.id); }}
                          className="w-full text-left px-3 py-1.5 text-xs text-rose-400 hover:bg-gray-800 transition-colors"
                        >
                          Delete
                        </button>
                      </motion.div>
                    )}
                  </AnimatePresence>
                </>
              )}
            </div>
              ))}
              </div>
            </div>
          ))}
          {conversations.length === 0 && <div className="px-2.5 py-4 font-mono text-[11px] text-gray-600">No conversations yet</div>}
          {conversations.length > 0 && historyGroups.length === 0 && (
            <div className="px-2.5 py-4 font-mono text-[11px] text-gray-600">No chats match “{historyQuery}”</div>
          )}
        </div>
      </motion.div>

      {/* Main column */}
      <div className="flex flex-col flex-1 min-w-0">
        {/* Toolbar */}
        <div id="chat-toolbar" className="flex items-center justify-between gap-2 px-4 py-2 border-b border-gray-800/80 bg-gray-950/40 backdrop-blur-sm">
          <div className="flex items-center gap-2 min-w-0">
            {!historyOpen && (
              <button onClick={() => setHistoryOpen(true)} className="p-1.5 text-gray-500 hover:text-gray-300 rounded-md hover:bg-gray-900 transition-colors hidden md:block" title="Show history">
                <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M13 5l7 7-7 7M4 5l7 7-7 7" />
                </svg>
              </button>
            )}
            {loading && (
              <span className="hidden sm:flex items-center gap-1.5 font-mono text-[10px] uppercase tracking-wider text-emerald-400">
                <span className="glow-dot" /> Live
              </span>
            )}
          </div>
          <div className="flex items-center gap-2 shrink-0">
            {!empty && (
              <button
                onClick={() => window.print()}
                className="flex items-center gap-1.5 px-2.5 py-1.5 text-xs border border-gray-800 rounded-lg text-gray-400 hover:text-gray-200 hover:border-gray-700 hover:bg-gray-900/60 transition-colors whitespace-nowrap"
              >
                <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M6 9V2h12v7M6 18H4a2 2 0 01-2-2v-5a2 2 0 012-2h16a2 2 0 012 2v5a2 2 0 01-2 2h-2m-12 0h12v6H6v-6z" />
                </svg>
                Print
              </button>
            )}
            <button
              onClick={newConversation}
              className="flex items-center gap-1.5 px-2.5 py-1.5 text-xs border border-gray-800 rounded-lg text-gray-400 hover:text-gray-200 hover:border-gray-700 hover:bg-gray-900/60 transition-colors whitespace-nowrap"
            >
              <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M12 4v16m8-8H4" />
              </svg>
              New chat
            </button>
          </div>
        </div>

        {/* Messages */}
        <div className="flex-1 overflow-y-auto print:overflow-visible print:h-auto">
          <div className="max-w-4xl mx-auto w-full px-4 py-5 space-y-4">
            {empty && (
              <div className="flex flex-col items-center justify-center text-center pt-[10vh] rise">
                <div className="radar h-20 w-20 mb-6" aria-hidden />
                <div className="kicker mb-2">Sentinel Analytics</div>
                <h2 className="font-display text-xl font-semibold text-white">Ask the data anything</h2>
                <p className="text-xs text-gray-500 mt-2 max-w-sm">
                  Sentinel writes the SQL, runs it, and explains what it finds — with charts when they help.
                </p>
                <div className="grid sm:grid-cols-3 gap-2.5 mt-8 w-full max-w-2xl" data-stagger>
                  {quickAskSuggestions.map((s) => (
                    <button
                      key={s.label}
                      onClick={() => quickAsk(s.prompt)}
                      className="panel panel-hover p-3.5 text-left group"
                    >
                      <div className="font-display text-xs font-semibold text-gray-200 group-hover:text-emerald-300 transition-colors">
                        {s.label}
                      </div>
                      <div className="text-[11px] text-gray-600 mt-1 leading-snug">{s.desc}</div>
                    </button>
                  ))}
                </div>
              </div>
            )}

            {messages.map((msg, idx) =>
              msg.role === 'user' ? (
                <div key={idx} className="flex justify-end rise" style={{ animationDelay: `${Math.min(idx * 40, 300)}ms` }}>
                  <div className="max-w-xl px-4 py-2.5 bg-emerald-500/[0.13] border border-emerald-500/25 rounded-2xl rounded-br-md text-sm text-gray-100 whitespace-pre-wrap shadow-[0_2px_16px_-8px_rgb(16_185_129/0.25)]">
                    {msg.content}
                  </div>
                </div>
              ) : (
                <div key={idx} className="rise" style={{ animationDelay: `${Math.min(idx * 40, 300)}ms` }}>
                  <AssistantMessage
                    entry={msg.entry!}
                    idx={idx}
                    chartTypeOverrides={chartTypeOverrides}
                    onChartTypeChange={(key, t) => setChartTypeOverrides((prev) => ({ ...prev, [key]: t }))}
                  />
                </div>
              )
            )}

            {/* Live trace while the agent works */}
            {loading && (
              <div className="panel p-3.5 rise">
                <div className="flex items-center gap-2 mb-2">
                  <span className="glow-dot" />
                  <span className="kicker text-emerald-500/90">Live trace</span>
                </div>
                <div className="relative pl-4">
                  <span aria-hidden className="absolute left-[5px] top-1.5 bottom-1.5 w-px bg-gray-800" />
                  {streamEvents.length === 0 && (
                    <div className="relative flex items-center gap-2.5 py-1">
                      <span className="absolute -left-4 h-2.5 w-2.5 rounded-full border-2 border-emerald-400 bg-gray-950 animate-pulse" />
                      <span className="text-xs text-gray-400 ml-1">Thinking…</span>
                    </div>
                  )}
                  <AnimatePresence initial={false}>
                    {streamEvents.map((evt, i) => {
                      const isLatest = i === streamEvents.length - 1;
                      return (
                        <motion.div
                          key={i}
                          initial={{ opacity: 0, x: -6 }}
                          animate={{ opacity: 1, x: 0 }}
                          transition={{ duration: 0.2 }}
                          className="relative py-1"
                        >
                          <span
                            className={`absolute -left-4 top-2 h-2.5 w-2.5 rounded-full border-2 bg-gray-950 ${
                              isLatest ? 'border-emerald-400 animate-pulse' : 'border-gray-700'
                            }`}
                          />
                          <div className="ml-1 min-w-0">
                            <span className={`text-xs ${isLatest ? 'text-gray-200' : 'text-gray-600'}`}>{streamEventLabel(evt)}</span>
                            {isLatest && evt.type === 'executing_sql' && evt.sql && (
                              <pre className="mt-1.5 px-2.5 py-1.5 text-[10px] bg-gray-950/90 text-emerald-300/70 rounded-md font-mono whitespace-pre-wrap border border-gray-800/60 max-h-20 overflow-y-auto">
                                {evt.sql}
                              </pre>
                            )}
                          </div>
                        </motion.div>
                      );
                    })}
                  </AnimatePresence>
                </div>
              </div>
            )}
            <div ref={bottomRef} />
          </div>
        </div>

        {/* Input */}
        <div id="chat-input-area" className="px-4 pb-4 pt-1">
          <form
            onSubmit={(e) => { e.preventDefault(); send(); }}
            className="max-w-4xl mx-auto rounded-2xl border border-gray-700/70 bg-gray-900/70 backdrop-blur-md px-3.5 pt-3 pb-2.5 transition-all focus-within:border-emerald-400/50 focus-within:shadow-[0_0_28px_-10px_rgb(16_185_129/0.4)]"
          >
            <textarea
              ref={inputRef}
              value={input}
              onChange={(e) => { setInput(e.target.value); autoGrow(); }}
              onKeyDown={(e) => {
                if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); send(); }
              }}
              rows={1}
              disabled={loading}
              placeholder="Message Sentinel…"
              className="w-full bg-transparent border-0 text-sm text-white placeholder-gray-500 focus:outline-none disabled:opacity-50 resize-none max-h-[200px] px-1"
            />
            {/* context controls live in the composer — always visible where the user types */}
            <div className="flex items-center justify-between gap-2 pt-2 mt-1.5 border-t border-gray-800/60">
              <div className="flex items-center gap-1.5 min-w-0 flex-wrap">
                <ProductSelect products={products} value={database} onChange={setDatabase} disabled={loading} />
                <ModeSwitch value={mode} onChange={setMode} disabled={loading} />
              </div>
              <button
                type="submit"
                disabled={loading || !input.trim()}
                title="Send"
                className="shrink-0 h-8 w-8 rounded-xl bg-emerald-500 hover:bg-emerald-400 disabled:opacity-30 disabled:hover:bg-emerald-500 text-gray-950 flex items-center justify-center transition-all active:scale-95 hover:shadow-[0_0_16px_-4px_rgb(16_185_129/0.6)]"
              >
                {loading ? (
                  <div className="h-3.5 w-3.5 animate-spin rounded-full border-2 border-gray-950/30 border-t-gray-950" />
                ) : (
                  <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2.5" d="M5 10l7-7m0 0l7 7m-7-7v18" />
                  </svg>
                )}
              </button>
            </div>
          </form>
          <div className="max-w-4xl mx-auto mt-1.5 px-1 font-mono text-[10px] text-gray-700">
            Enter to send · Shift+Enter for a new line
          </div>
        </div>
      </div>

      <AnimatePresence>
        {deleteConvId && (
          <Dialog title="Delete conversation" onClose={() => setDeleteConvId(null)}>
            <p className="text-xs text-gray-400">Delete this conversation? This cannot be undone.</p>
            <div className="flex items-center justify-end gap-2 pt-1">
              <button onClick={() => setDeleteConvId(null)} className={btnGhost}>Cancel</button>
              <button onClick={deleteConv} className={btnDanger}>Delete</button>
            </div>
          </Dialog>
        )}
      </AnimatePresence>
    </div>
  );
}

function ProductSelect({
  products,
  value,
  onChange,
  disabled,
}: {
  products: { databaseName: string; displayName: string }[];
  value: string;
  onChange: (v: string) => void;
  disabled: boolean;
}) {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);
  const current = products.find((p) => p.databaseName === value);

  useEffect(() => {
    if (!open) return;
    const onDown = (e: MouseEvent) => {
      if (!ref.current?.contains(e.target as Node)) setOpen(false);
    };
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && setOpen(false);
    document.addEventListener('mousedown', onDown);
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('mousedown', onDown);
      document.removeEventListener('keydown', onKey);
    };
  }, [open]);

  return (
    <div ref={ref} className="relative">
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        disabled={disabled}
        aria-haspopup="listbox"
        aria-expanded={open}
        className="flex items-center gap-2 pl-2.5 pr-2 py-1.5 rounded-lg border border-gray-800 bg-gray-950/60 text-xs text-gray-200 hover:border-gray-700 transition-colors disabled:opacity-50"
      >
        <svg className="w-3.5 h-3.5 text-emerald-500/80 shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="1.5" d="M4 7v10c0 2.21 3.582 4 8 4s8-1.79 8-4V7M4 7c0 2.21 3.582 4 8 4s8-1.79 8-4M4 7c0-2.21 3.582-4 8-4s8 1.79 8 4" />
        </svg>
        <span className="max-w-[9rem] truncate">{current?.displayName ?? 'Select database'}</span>
        <svg className={`w-3 h-3 text-gray-600 transition-transform duration-200 shrink-0 ${open ? 'rotate-180' : ''}`} fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M19 9l-7 7-7-7" />
        </svg>
      </button>
      <AnimatePresence>
        {open && (
          <motion.div
            initial={{ opacity: 0, y: 4, scale: 0.98 }}
            animate={{ opacity: 1, y: 0, scale: 1 }}
            exit={{ opacity: 0, y: 4, scale: 0.98 }}
            transition={{ duration: 0.12 }}
            role="listbox"
            className="absolute left-0 bottom-full mb-1.5 z-30 w-60 rounded-xl border border-gray-700/80 bg-gray-900 py-1 shadow-xl"
          >
            {products.map((p) => (
              <button
                key={p.databaseName}
                type="button"
                role="option"
                aria-selected={p.databaseName === value}
                onClick={() => { onChange(p.databaseName); setOpen(false); }}
                className={`w-full flex items-center justify-between gap-2 px-3 py-2 text-left transition-colors ${
                  p.databaseName === value ? 'bg-emerald-500/[0.07]' : 'hover:bg-gray-800/60'
                }`}
              >
                <span className="min-w-0">
                  <span className={`block text-xs truncate ${p.databaseName === value ? 'text-emerald-300' : 'text-gray-200'}`}>
                    {p.displayName}
                  </span>
                  <span className="block font-mono text-[10px] text-gray-600 truncate">{p.databaseName}</span>
                </span>
                {p.databaseName === value && (
                  <svg className="w-3.5 h-3.5 text-emerald-400 shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M5 13l4 4L19 7" />
                  </svg>
                )}
              </button>
            ))}
            {products.length === 0 && <div className="px-3 py-2 text-xs text-gray-600">Loading…</div>}
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}

const MODES = [
  {
    id: 'general',
    label: 'General',
    icon: 'M9 19v-6a2 2 0 00-2-2H5a2 2 0 00-2 2v6a2 2 0 002 2h2a2 2 0 002-2zm0 0V9a2 2 0 012-2h2a2 2 0 012 2v10m-6 0a2 2 0 002 2h2a2 2 0 002-2m0 0V5a2 2 0 012-2h2a2 2 0 012 2v14a2 2 0 01-2 2h-2a2 2 0 01-2-2z',
    active: 'bg-emerald-500/10 border-emerald-500/30',
    activeText: 'text-emerald-300',
  },
  {
    id: 'fraud',
    label: 'Fraud',
    icon: 'M9 12l2 2 4-4m5.618-4.016A11.955 11.955 0 0112 2.944a11.955 11.955 0 01-8.618 3.04A12.02 12.02 0 003 9c0 5.591 3.824 10.29 9 11.622 5.176-1.332 9-6.03 9-11.622 0-1.042-.133-2.052-.382-3.016z',
    active: 'bg-amber-500/10 border-amber-500/35',
    activeText: 'text-amber-300',
  },
];

function ModeSwitch({ value, onChange, disabled }: { value: string; onChange: (v: string) => void; disabled: boolean }) {
  return (
    <div className="flex items-center rounded-lg border border-gray-800 bg-gray-950/60 p-0.5">
      {MODES.map((m) => (
        <button
          key={m.id}
          type="button"
          onClick={() => onChange(m.id)}
          disabled={disabled}
          title={`${m.label} mode`}
          className={`relative flex items-center gap-1.5 px-2.5 py-1.5 text-xs rounded-md transition-colors disabled:opacity-50 ${
            value === m.id ? m.activeText : 'text-gray-500 hover:text-gray-300'
          }`}
        >
          {value === m.id && (
            <motion.span
              layoutId="chat-mode-pill"
              transition={{ duration: 0.18, ease: [0.22, 1, 0.36, 1] }}
              className={`absolute inset-0 rounded-md border ${m.active}`}
            />
          )}
          <svg className="relative w-3.5 h-3.5 shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="1.5" d={m.icon} />
          </svg>
          <span className="relative">{m.label}</span>
        </button>
      ))}
    </div>
  );
}

// Today / Yesterday / Previous 7 days / Older buckets for the history panel
function groupConversations(convs: Conversation[]) {
  const now = new Date();
  const startOfDay = (d: Date) => new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime();
  const today = startOfDay(now);
  const yesterday = today - 86_400_000;
  const week = today - 6 * 86_400_000;
  const groups = [
    { label: 'Today', items: [] as Conversation[] },
    { label: 'Yesterday', items: [] as Conversation[] },
    { label: 'Previous 7 days', items: [] as Conversation[] },
    { label: 'Older', items: [] as Conversation[] },
  ];
  for (const c of convs) {
    const t = new Date(c.updatedAt).getTime();
    if (t >= today) groups[0].items.push(c);
    else if (t >= yesterday) groups[1].items.push(c);
    else if (t >= week) groups[2].items.push(c);
    else groups[3].items.push(c);
  }
  return groups.filter((g) => g.items.length > 0);
}

function fmtRelative(iso: string): string {
  const d = new Date(iso);
  const now = new Date();
  const startOfDay = (x: Date) => new Date(x.getFullYear(), x.getMonth(), x.getDate()).getTime();
  const diffDays = Math.round((startOfDay(now) - startOfDay(d)) / 86_400_000);
  if (diffDays === 0) return d.toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' });
  if (diffDays === 1) return 'Yesterday';
  if (diffDays < 7) return d.toLocaleDateString('en-GB', { weekday: 'short' });
  return d.toLocaleDateString('en-GB', { day: '2-digit', month: 'short' });
}

function streamEventLabel(evt: StreamEvent): string {
  if (evt.message) return evt.message;
  switch (evt.type) {
    case 'thinking': return 'Thinking…';
    case 'executing_sql': return 'Running query…';
    case 'rendering_chart': return 'Rendering chart…';
    case 'sending_report': return 'Sending report…';
    default: return evt.type;
  }
}

function AssistantMessage({
  entry,
  idx,
  chartTypeOverrides,
  onChartTypeChange,
}: {
  entry: ChatEntry;
  idx: number;
  chartTypeOverrides: Record<string, string>;
  onChartTypeChange: (key: string, t: string) => void;
}) {
  const r = entry.response;
  if (!r) return <div className="text-xs text-gray-500 whitespace-pre-wrap">{entry.content}</div>;

  const multiResults = (r.results ?? []).filter((q) => q.rows.length > 0);
  const showMulti = multiResults.length > 1;
  const tokens = (r.inputTokens ?? 0) + (r.outputTokens ?? 0);

  return (
    <div className="panel space-y-3 max-w-full p-4 border-l-2 border-l-emerald-500/40">
      <div className="flex items-center justify-between -mb-1">
        <span className="kicker text-emerald-600/80">Sentinel</span>
        {entry.timestamp && (
          <span className="font-mono text-[10px] text-gray-700">
            {new Date(entry.timestamp).toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' })}
          </span>
        )}
      </div>

      {r.error && (
        <div className="p-3 bg-rose-500/10 border border-rose-500/20 rounded-lg text-xs text-rose-400 font-mono whitespace-pre-wrap">{r.error}</div>
      )}

      {r.explanation && <Markdown text={r.explanation} />}

      {(r.summary || r.riskLevel || r.findings?.length > 0 || r.recommendedActions?.length > 0) && (
        <div className="border border-gray-800 rounded-lg p-3 bg-gray-950/50 space-y-2.5 break-inside-avoid">
          {r.summary && <Markdown text={r.summary} className="text-gray-200" />}
          {r.riskLevel && (
            <div className="text-xs">
              <span className={riskClasses(r.riskLevel)}>{r.riskLevel}</span>
            </div>
          )}
          {r.findings?.length > 0 && (
            <div>
              <div className="kicker mb-1.5">Findings</div>
              {r.findings.map((fi, i) => (
                <div key={i} className="text-xs text-gray-300 flex gap-2 py-0.5">
                  <span className="text-emerald-500/70 shrink-0">▸</span>
                  <span>{fi}</span>
                </div>
              ))}
            </div>
          )}
          {r.recommendedActions?.length > 0 && (
            <div>
              <div className="kicker mb-1.5">Recommended Actions</div>
              {r.recommendedActions.map((a, i) => (
                <div key={i} className="text-xs text-gray-300 flex gap-2 py-0.5">
                  <span className="text-amber-500/70 shrink-0">▸</span>
                  <span>{a}</span>
                </div>
              ))}
            </div>
          )}
        </div>
      )}

      {showMulti
        ? multiResults.map((qr, ri) => (
            <ResultBlock key={ri} qr={qr} blockKey={`${idx}-${ri}`} chartTypeOverrides={chartTypeOverrides} onChartTypeChange={onChartTypeChange} />
          ))
        : r.rows?.length > 0 && (
            <ResultBlock
              qr={{ label: null, sql: r.sql, chartType: r.chartType, columns: r.columns, rows: r.rows, rowCount: r.rowCount }}
              blockKey={`${idx}-single`}
              chartTypeOverrides={chartTypeOverrides}
              onChartTypeChange={onChartTypeChange}
            />
          )}

      <div className="flex items-center gap-3 font-mono text-[10px] text-gray-600">
        {tokens > 0 && <span className="tnum">{tokens.toLocaleString()} tokens</span>}
        {r.reportSent && <span className="text-emerald-500">✓ Report emailed</span>}
      </div>

      {r.thinking && (
        <details className="border border-gray-800/60 rounded-lg overflow-hidden">
          <summary className="px-3 py-2 font-mono text-[11px] uppercase tracking-wider text-gray-400 cursor-pointer hover:text-gray-300 select-none bg-gray-900/40">Reasoning</summary>
          <div className="p-3 text-xs text-gray-400 italic whitespace-pre-wrap bg-gray-950/40">{r.thinking}</div>
        </details>
      )}
    </div>
  );
}

function ResultBlock({
  qr,
  blockKey,
  chartTypeOverrides,
  onChartTypeChange,
}: {
  qr: QueryResult;
  blockKey: string;
  chartTypeOverrides: Record<string, string>;
  onChartTypeChange: (key: string, t: string) => void;
}) {
  if (qr.chartType === 'csv') {
    const filename = `${(qr.label || 'export').replace(/[^\w-]+/g, '-')}.csv`;
    return (
      <div className="border border-gray-800/60 rounded-lg overflow-hidden bg-gray-950/40 px-3 py-2.5 flex items-center justify-between gap-3 break-inside-avoid">
        <div className="flex items-center gap-2.5 min-w-0">
          <svg className="w-4 h-4 text-emerald-500 shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M12 10v6m0 0l-3-3m3 3l3-3M4 20h16" />
          </svg>
          <span className="font-display text-xs font-medium text-gray-300 truncate">{qr.label || 'Export'}</span>
          <span className="font-mono text-[10px] text-gray-600 tnum shrink-0">{qr.rowCount} rows</span>
        </div>
        <button
          onClick={() => downloadCsv(filename, qr.columns, qr.rows)}
          className="font-mono text-[10px] px-2.5 py-1 rounded-md border border-emerald-500/40 bg-emerald-500/10 text-emerald-300 hover:bg-emerald-500/20 transition-colors flex items-center gap-1 shrink-0 print:hidden"
        >
          Download CSV
        </button>
      </div>
    );
  }

  const types = applicableChartTypes(qr.columns, qr.rows);
  const stored = chartTypeOverrides[blockKey];
  const effective = stored ?? (qr.chartType !== 'none' && types.includes(qr.chartType) ? qr.chartType : types[0] ?? 'table');

  return (
    <div className="border border-gray-800/60 rounded-lg overflow-hidden bg-gray-950/40 break-inside-avoid">
      <div className="px-3 py-2 border-b border-gray-800/60 flex items-center justify-between">
        <span className="font-display text-xs font-medium text-gray-300">{qr.label || 'Result'}</span>
        <div className="flex items-center gap-2.5">
          <span className="font-mono text-[10px] text-gray-600 tnum">{qr.rowCount} rows</span>
          <button
            onClick={() => downloadCsv(`${(qr.label || 'result').replace(/[^\w-]+/g, '-')}.csv`, qr.columns, qr.rows)}
            className="font-mono text-[10px] text-gray-500 hover:text-emerald-400 transition-colors flex items-center gap-1 print:hidden"
          >
            <svg className="w-3 h-3" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M12 10v6m0 0l-3-3m3 3l3-3M4 20h16" />
            </svg>
            CSV
          </button>
        </div>
      </div>
      <div className="p-3">
        {types.length > 1 && (
          <div className="flex items-center gap-1 flex-wrap mb-2.5 print:hidden">
            {types.map((t) => (
              <button
                key={t}
                onClick={() => onChartTypeChange(blockKey, t)}
                className={`px-2 py-0.5 font-mono text-[10px] uppercase tracking-wide rounded-md border transition-all ${
                  effective === t
                    ? 'bg-emerald-500/15 border-emerald-500/40 text-emerald-300'
                    : 'border-gray-800 text-gray-600 hover:text-gray-300 hover:border-gray-700'
                }`}
              >
                {t}
              </button>
            ))}
          </div>
        )}
        {effective === 'table' ? (
          <DataTable columns={qr.columns} rows={qr.rows} />
        ) : (
          <DataChart chartType={effective} columns={qr.columns} rows={qr.rows} />
        )}
      </div>
    </div>
  );
}

function riskClasses(level: string): string {
  const base = 'inline-flex px-1.5 py-0.5 rounded font-mono text-[10px] font-medium uppercase tracking-wide border ';
  switch (level.toLowerCase()) {
    case 'critical': return base + 'bg-rose-500/10 text-rose-400 border-rose-500/25';
    case 'high': return base + 'bg-orange-500/10 text-orange-400 border-orange-500/25';
    case 'medium': return base + 'bg-amber-500/10 text-amber-400 border-amber-500/25';
    default: return base + 'bg-sky-500/10 text-sky-400 border-sky-500/25';
  }
}
