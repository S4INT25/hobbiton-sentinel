import { useEffect, useMemo, useRef, useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { api, type ChatEntry, type QueryResult, type StreamEvent } from '../api';
import { Markdown, downloadCsv, Dialog, btnGhost, btnPrimary, Spinner, selectCls } from '../components/ui';
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

  const [activeId, setActiveId] = useState<string | null>(null);
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

  // job polling — the Blazor page polled the job store; React Query does it with refetchInterval
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
      if (job.conversationId && job.conversationId !== activeId) setActiveId(job.conversationId);
      qc.invalidateQueries({ queryKey: ['conversation', job.conversationId] });
      qc.invalidateQueries({ queryKey: ['conversations'] });
    }
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
      if (!activeId) setActiveId(res.conversationId);
      qc.invalidateQueries({ queryKey: ['conversations'] });
    },
  });

  const send = () => {
    const prompt = input.trim();
    if (!prompt || jobId) return;
    setInput('');
    askMut.mutate(prompt);
  };

  const quickAsk = (prompt: string) => {
    if (jobId) return;
    askMut.mutate(prompt);
  };

  const newConversation = () => {
    setActiveId(null);
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

  const loading = !!jobId;
  const streamEvents = (job?.streamEvents ?? []).filter((e) => e.type !== 'token' && e.type !== 'result');
  const hasAssistant = messages.some((m) => m.role === 'assistant');

  const quickAskSuggestions = [
    { label: 'Today at a glance', prompt: `Give me a comprehensive summary of today's key metrics, trends, and notable activity in '${database}'. Include total volumes, success/failure rates, and anything unusual.` },
    { label: 'Weekly trend', prompt: `Show me the daily transaction volume and value for the last 7 days in '${database}' as a chart.` },
    { label: 'Failure analysis', prompt: `What are the top failure reasons in '${database}' over the last 24 hours?` },
  ];

  return (
    <div className="flex h-[calc(100vh-6rem)] -m-4 md:-m-6">
      {/* Conversation sidebar */}
      <div
        id="chat-history-panel"
        className={`${historyOpen ? 'w-64' : 'w-0'} border-r border-gray-800 flex-col shrink-0 overflow-hidden transition-all duration-300 hidden md:flex`}
      >
        <div className="flex items-center justify-between px-3 py-2 border-b border-gray-800 min-w-[16rem]">
          <span className="text-xs font-medium text-gray-400">History</span>
          <div className="flex items-center gap-1.5">
            <button onClick={newConversation} className="px-2 py-1 text-[10px] bg-emerald-600 hover:bg-emerald-500 text-white rounded transition-colors">
              + New
            </button>
            <button onClick={() => setHistoryOpen(false)} className="p-1 text-gray-500 hover:text-gray-300 rounded hover:bg-gray-800 transition-colors">
              <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M6 18L18 6M6 6l12 12" />
              </svg>
            </button>
          </div>
        </div>
        <div className="flex-1 overflow-y-auto p-1.5 space-y-0.5 min-w-[16rem]">
          {conversations.map((conv) => (
            <div
              key={conv.id}
              onClick={() => { setActiveId(conv.id); setJobId(null); setPendingPrompt(null); }}
              className={`relative px-2.5 py-2 rounded border cursor-pointer group transition-colors ${
                conv.id === activeId ? 'bg-gray-800/60 border-emerald-500/30' : 'border-transparent hover:bg-gray-900'
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
                  className="w-full bg-gray-900 border border-emerald-500/40 rounded px-1.5 py-0.5 text-xs text-white focus:outline-none"
                />
              ) : (
                <>
                  <div className="text-xs text-gray-300 truncate pr-6">{conv.title}</div>
                  <div className="text-[10px] text-gray-600 mt-0.5">
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
                  {menuConvId === conv.id && (
                    <div className="absolute right-1 top-8 z-20 w-36 bg-gray-900 border border-gray-700 rounded-lg shadow-xl py-1" onClick={(e) => e.stopPropagation()}>
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
                    </div>
                  )}
                </>
              )}
            </div>
          ))}
          {conversations.length === 0 && <div className="px-2.5 py-4 text-[11px] text-gray-600">No conversations yet</div>}
        </div>
      </div>

      {/* Main column */}
      <div className="flex flex-col flex-1 min-w-0">
        {/* Toolbar */}
        <div className="flex items-center justify-between gap-2 px-4 py-2 border-b border-gray-800">
          <div className="flex items-center gap-2 min-w-0">
            {!historyOpen && (
              <button onClick={() => setHistoryOpen(true)} className="p-1.5 text-gray-500 hover:text-gray-300 rounded hover:bg-gray-900 transition-colors hidden md:block" title="Show history">
                <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M4 6h16M4 12h16M4 18h16" />
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
              <span className="flex items-center gap-1.5 text-[11px] text-emerald-400">
                <Spinner className="h-3 w-3" /> Query running
              </span>
            )}
          </div>
          <div id="chat-toolbar" className="flex items-center gap-2">
            <button
              onClick={() => window.print()}
              disabled={!hasAssistant}
              className="px-2.5 py-1.5 text-xs border border-gray-800 rounded text-gray-500 hover:text-gray-300 hover:bg-gray-900 transition-colors disabled:opacity-50 whitespace-nowrap"
              title="Save chat as PDF"
            >
              Save PDF
            </button>
            <button
              onClick={() => setDebugMode((v) => !v)}
              className={`px-2.5 py-1.5 text-xs border rounded transition-colors whitespace-nowrap ${
                debugMode ? 'bg-amber-600/20 border-amber-500/40 text-amber-300' : 'border-gray-800 text-gray-500 hover:text-gray-300 hover:bg-gray-900'
              }`}
            >
              Debug
            </button>
            <button onClick={newConversation} className="px-2.5 py-1.5 text-xs text-gray-500 hover:text-gray-300 border border-gray-800 rounded hover:bg-gray-900 transition-colors whitespace-nowrap">
              Clear
            </button>
          </div>
        </div>

        {/* Messages */}
        <div className="flex-1 overflow-y-auto space-y-4 p-4">
          {messages.length === 0 && !loading && (
            <div className="h-full flex flex-col items-center justify-center text-center px-6">
              <div className="text-3xl mb-3">📊</div>
              <p className="text-sm text-gray-400">Ask anything about your data.</p>
              <p className="text-xs text-gray-600 mt-1 max-w-sm">
                Sentinel writes the SQL, runs it, and explains what it finds — with charts when they help.
              </p>
            </div>
          )}

          {messages.map((msg, idx) =>
            msg.role === 'user' ? (
              <div key={idx} className="flex justify-end">
                <div className="max-w-2xl px-3 py-2 bg-emerald-600/20 border border-emerald-500/20 rounded-xl text-sm text-gray-200 whitespace-pre-wrap">
                  {msg.content}
                </div>
              </div>
            ) : (
              <AssistantMessage
                key={idx}
                entry={msg.entry!}
                idx={idx}
                debugMode={debugMode}
                chartTypeOverrides={chartTypeOverrides}
                onChartTypeChange={(key, t) => setChartTypeOverrides((prev) => ({ ...prev, [key]: t }))}
              />
            )
          )}

          {/* Live activity feed while the agent works */}
          {loading && (
            <div className="space-y-0.5 py-1">
              {streamEvents.length === 0 && (
                <div className="flex items-center gap-2 text-xs text-gray-500">
                  <Spinner className="h-3 w-3" /> Thinking…
                </div>
              )}
              {streamEvents.map((evt, i) => {
                const isLatest = i === streamEvents.length - 1;
                return (
                  <div key={i} className={`flex items-start gap-2.5 py-1 ${isLatest ? '' : 'opacity-50'}`}>
                    {isLatest ? <Spinner className="h-3 w-3 mt-0.5 shrink-0" /> : (
                      <svg className="w-3 h-3 mt-0.5 shrink-0 text-emerald-500" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M5 13l4 4L19 7" />
                      </svg>
                    )}
                    <div className="min-w-0 flex-1">
                      <span className={`text-xs ${isLatest ? 'text-gray-300' : 'text-gray-600'}`}>{streamEventLabel(evt)}</span>
                      {isLatest && evt.type === 'executing_sql' && evt.sql && (
                        <pre className="mt-1 px-2.5 py-1.5 text-[10px] bg-gray-950/80 text-emerald-300/70 rounded font-mono whitespace-pre-wrap border border-gray-800/50 max-h-20 overflow-y-auto">
                          {evt.sql}
                        </pre>
                      )}
                    </div>
                  </div>
                );
              })}
            </div>
          )}
          <div ref={bottomRef} />
        </div>

        {/* Input */}
        <div id="chat-input-area" className="border-t border-gray-800 p-3">
          <div className="mb-2 flex flex-wrap gap-2">
            {quickAskSuggestions.map((s) => (
              <button
                key={s.label}
                onClick={() => quickAsk(s.prompt)}
                disabled={loading}
                className="px-2 py-1 text-[10px] text-gray-500 border border-gray-800 rounded hover:bg-gray-900 hover:text-gray-300 transition-colors disabled:opacity-50"
              >
                {s.label}
              </button>
            ))}
          </div>
          <form
            onSubmit={(e) => { e.preventDefault(); send(); }}
            className="rounded-2xl border border-gray-700/80 bg-gray-900/80 focus-within:border-emerald-500/50 transition-all"
          >
            <textarea
              value={input}
              onChange={(e) => setInput(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); send(); }
              }}
              rows={3}
              disabled={loading}
              placeholder="Message Sentinel..."
              className="w-full px-4 pt-3 pb-2 bg-transparent border-0 text-sm text-white placeholder-gray-500 focus:outline-none disabled:opacity-50 resize-y min-h-[3rem] max-h-56"
            />
            <div className="flex items-center justify-end px-3 pb-2">
              <button type="submit" disabled={loading || !input.trim()} className={btnPrimary}>
                {loading ? 'Working…' : 'Send'}
              </button>
            </div>
          </form>
        </div>
      </div>

      {deleteConvId && (
        <Dialog title="Delete conversation" onClose={() => setDeleteConvId(null)}>
          <p className="text-xs text-gray-400">Delete this conversation? This cannot be undone.</p>
          <div className="flex items-center justify-end gap-2 pt-1">
            <button onClick={() => setDeleteConvId(null)} className={btnGhost}>Cancel</button>
            <button onClick={deleteConv} className="px-3 py-1.5 text-xs font-medium bg-rose-600 hover:bg-rose-500 text-white rounded-md transition-all">
              Delete
            </button>
          </div>
        </Dialog>
      )}
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
    <div className="space-y-3 max-w-full border rounded-xl p-4 bg-gray-900/35 border-gray-800">
      {r.error && (
        <div className="p-3 bg-rose-500/10 border border-rose-500/20 rounded-lg text-xs text-rose-400 font-mono whitespace-pre-wrap">{r.error}</div>
      )}

      {r.explanation && <Markdown text={r.explanation} />}

      {(r.summary || r.riskLevel || r.findings?.length > 0 || r.recommendedActions?.length > 0) && (
        <div className="border border-gray-800 rounded-lg p-3 bg-gray-900/40 space-y-2">
          {r.summary && <Markdown text={r.summary} className="text-gray-200" />}
          {r.riskLevel && (
            <div className="text-xs">
              <span className={riskClasses(r.riskLevel)}>{r.riskLevel}</span>
            </div>
          )}
          {r.findings?.length > 0 && (
            <div>
              <div className="text-[10px] text-gray-500 uppercase tracking-wider mb-1">Findings</div>
              {r.findings.map((fi, i) => (
                <div key={i} className="text-xs text-gray-300">• {fi}</div>
              ))}
            </div>
          )}
          {r.recommendedActions?.length > 0 && (
            <div>
              <div className="text-[10px] text-gray-500 uppercase tracking-wider mb-1">Recommended Actions</div>
              {r.recommendedActions.map((a, i) => (
                <div key={i} className="text-xs text-gray-300">• {a}</div>
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

      <div className="flex items-center gap-3 text-[10px] text-gray-600">
        {tokens > 0 && <span>{tokens.toLocaleString()} tokens</span>}
        {r.reportSent && <span className="text-emerald-500">✓ Report emailed</span>}
      </div>

      {debugMode && (r.sql || multiResults.some((q) => q.sql)) && (
        <details className="border border-gray-800/60 rounded-lg overflow-hidden">
          <summary className="px-3 py-2 text-xs text-gray-400 cursor-pointer hover:text-gray-300 select-none bg-gray-900/40">
            SQL{multiResults.length > 1 ? ` · ${multiResults.length} queries` : ''}
          </summary>
          {multiResults.length > 1 ? (
            <div className="divide-y divide-gray-800/40">
              {multiResults.filter((q) => q.sql).map((q, i) => (
                <div key={i}>
                  <div className="px-3 py-1 text-[10px] text-gray-500 bg-gray-900/50">{q.label || `Query ${i + 1}`}</div>
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
          <summary className="px-3 py-2 text-xs text-gray-400 cursor-pointer hover:text-gray-300 select-none bg-gray-900/40">Reasoning</summary>
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
    <div className="border border-gray-800/60 rounded-lg overflow-hidden bg-gray-950/30">
      <div className="px-3 py-2 border-b border-gray-800/60 flex items-center justify-between">
        <span className="text-xs font-medium text-gray-300">{qr.label || 'Result'}</span>
        <div className="flex items-center gap-2">
          <span className="text-[10px] text-gray-600">{qr.rowCount} rows</span>
          <button
            onClick={() => downloadCsv(`${(qr.label || 'result').replace(/[^\w-]+/g, '-')}.csv`, qr.columns, qr.rows)}
            className="text-[10px] text-gray-500 hover:text-emerald-400 transition-colors flex items-center gap-0.5 print:hidden"
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
          <div className="flex items-center gap-1.5 flex-wrap mb-2 print:hidden">
            {types.map((t) => (
              <button
                key={t}
                onClick={() => onChartTypeChange(blockKey, t)}
                className={`px-2 py-0.5 text-[10px] rounded border transition-colors ${
                  effective === t
                    ? 'bg-emerald-600/20 border-emerald-500/40 text-emerald-300'
                    : 'border-gray-700 text-gray-500 hover:text-gray-300 hover:border-gray-600'
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
  const base = 'inline-flex px-1.5 py-0.5 rounded text-[10px] font-medium border ';
  switch (level.toLowerCase()) {
    case 'critical': return base + 'bg-rose-500/10 text-rose-400 border-rose-500/20';
    case 'high': return base + 'bg-orange-500/10 text-orange-400 border-orange-500/20';
    case 'medium': return base + 'bg-amber-500/10 text-amber-400 border-amber-500/20';
    default: return base + 'bg-blue-500/10 text-blue-400 border-blue-500/20';
  }
}
