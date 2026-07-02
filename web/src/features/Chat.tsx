import { useEffect, useMemo, useRef, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { motion, AnimatePresence } from 'motion/react';
import { api, type ChatEntry, type QueryResult, type StreamEvent } from '../api';
import { Markdown, downloadCsv, Dialog, btnGhost, btnDanger, selectCls } from '../components/ui';
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
  const [debugMode, setDebugMode] = useState(false);
  const [historyOpen, setHistoryOpen] = useState(true);
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
  const hasAssistant = messages.some((m) => m.role === 'assistant');
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
    <div className="flex h-[calc(100vh-6rem)] md:h-full -m-4 md:-m-6">
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
        <div className="flex-1 overflow-y-auto p-1.5 space-y-0.5 min-w-[16.5rem]" data-stagger>
          {conversations.map((conv) => (
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
                      new Date(conv.updatedAt).toLocaleDateString('en-GB', { day: '2-digit', month: 'short' })
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
                        className="absolute right-1 top-8 z-20 w-36 panel py-1 shadow-xl"
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
          {conversations.length === 0 && <div className="px-2.5 py-4 font-mono text-[11px] text-gray-600">No conversations yet</div>}
        </div>
      </motion.div>

      {/* Main column */}
      <div className="flex flex-col flex-1 min-w-0">
        {/* Toolbar */}
        <div className="flex items-center justify-between gap-2 px-4 py-2 border-b border-gray-800/80 bg-gray-950/40 backdrop-blur-sm">
          <div className="flex items-center gap-2 min-w-0">
            {!historyOpen && (
              <button onClick={() => setHistoryOpen(true)} className="p-1.5 text-gray-500 hover:text-gray-300 rounded-md hover:bg-gray-900 transition-colors hidden md:block" title="Show history">
                <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M13 5l7 7-7 7M4 5l7 7-7 7" />
                </svg>
              </button>
            )}
            <select value={database} onChange={(e) => setDatabase(e.target.value)} className={selectCls} disabled={loading}>
              {products.map((p) => (
                <option key={p.databaseName} value={p.databaseName}>{p.displayName}</option>
              ))}
              {products.length === 0 && <option value="">Loading…</option>}
            </select>
            <select value={mode} onChange={(e) => setMode(e.target.value)} className={selectCls} disabled={loading}>
              <option value="general">General</option>
              <option value="fraud">Fraud</option>
            </select>
            {loading && (
              <span className="hidden sm:flex items-center gap-1.5 font-mono text-[10px] uppercase tracking-wider text-emerald-400">
                <span className="glow-dot" /> Live
              </span>
            )}
          </div>
          <div id="chat-toolbar" className="flex items-center gap-1.5">
            <button
              onClick={() => window.print()}
              disabled={!hasAssistant}
              className="px-2.5 py-1.5 text-xs border border-gray-800 rounded-md text-gray-500 hover:text-gray-300 hover:bg-gray-900 transition-colors disabled:opacity-40 whitespace-nowrap"
              title="Save chat as PDF"
            >
              PDF
            </button>
            <button
              onClick={() => setDebugMode((v) => !v)}
              className={`px-2.5 py-1.5 text-xs border rounded-md transition-colors whitespace-nowrap ${
                debugMode
                  ? 'bg-amber-500/15 border-amber-500/40 text-amber-300'
                  : 'border-gray-800 text-gray-500 hover:text-gray-300 hover:bg-gray-900'
              }`}
            >
              Debug
            </button>
            <button onClick={newConversation} className="px-2.5 py-1.5 text-xs text-gray-500 hover:text-gray-300 border border-gray-800 rounded-md hover:bg-gray-900 transition-colors whitespace-nowrap">
              Clear
            </button>
          </div>
        </div>

        {/* Messages */}
        <div className="flex-1 overflow-y-auto">
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
                    debugMode={debugMode}
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
            className="max-w-4xl mx-auto flex items-end gap-2 rounded-2xl border border-gray-700/70 bg-gray-900/70 backdrop-blur-md px-4 py-2.5 transition-all focus-within:border-emerald-400/50 focus-within:shadow-[0_0_28px_-10px_rgb(16_185_129/0.4)]"
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
              className="flex-1 bg-transparent border-0 text-sm text-white placeholder-gray-500 focus:outline-none disabled:opacity-50 resize-none max-h-[200px] py-1"
            />
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
  debugMode,
  chartTypeOverrides,
  onChartTypeChange,
}: {
  entry: ChatEntry;
  idx: number;
  debugMode: boolean;
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
        <div className="border border-gray-800 rounded-lg p-3 bg-gray-950/50 space-y-2.5">
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

      {debugMode && (r.sql || multiResults.some((q) => q.sql)) && (
        <details className="border border-gray-800/60 rounded-lg overflow-hidden">
          <summary className="px-3 py-2 font-mono text-[11px] uppercase tracking-wider text-gray-400 cursor-pointer hover:text-gray-300 select-none bg-gray-900/40">
            SQL{multiResults.length > 1 ? ` · ${multiResults.length} queries` : ''}
          </summary>
          {multiResults.length > 1 ? (
            <div className="divide-y divide-gray-800/40">
              {multiResults.filter((q) => q.sql).map((q, i) => (
                <div key={i}>
                  <div className="px-3 py-1 font-mono text-[10px] text-gray-500 bg-gray-900/50">{q.label || `Query ${i + 1}`}</div>
                  <pre className="px-3 py-2 text-xs text-emerald-300 font-mono whitespace-pre-wrap bg-gray-950">{q.sql}</pre>
                </div>
              ))}
            </div>
          ) : (
            <pre className="p-3 text-xs text-emerald-300 font-mono whitespace-pre-wrap bg-gray-950">{r.sql}</pre>
          )}
        </details>
      )}

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
  const types = applicableChartTypes(qr.columns, qr.rows);
  const stored = chartTypeOverrides[blockKey];
  const effective = stored ?? (qr.chartType !== 'none' && types.includes(qr.chartType) ? qr.chartType : types[0] ?? 'table');

  return (
    <div className="border border-gray-800/60 rounded-lg overflow-hidden bg-gray-950/40">
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
