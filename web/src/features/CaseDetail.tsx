import { useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { api } from '../api';
import { Spinner, inputCls, SeverityBadge, ConfidenceBadge } from '../components/ui';

const label = 'block font-mono text-[10px] text-gray-500 uppercase tracking-wider mb-1';

export default function CaseDetail() {
  const { id = '' } = useParams();
  const navigate = useNavigate();
  const qc = useQueryClient();

  const { data: c, isLoading } = useQuery({
    queryKey: ['case', id],
    queryFn: () => api.getCase(id),
  });

  const [showFpForm, setShowFpForm] = useState(false);
  const [fpReason, setFpReason] = useState('');
  const [createRule, setCreateRule] = useState(true);
  const [fpRuleType, setFpRuleType] = useState('ip');
  const [fpMatchValue, setFpMatchValue] = useState('');

  const feedback = useMutation({
    mutationFn: ({ action, reason, rule }: { action: string; reason?: string; rule?: { ruleType: string; matchValue: string; action: string; reason: string } }) =>
      api.caseFeedback(id, action, reason, rule),
    onSuccess: (_, vars) => {
      qc.invalidateQueries({ queryKey: ['case', id] });
      qc.invalidateQueries({ queryKey: ['cases'] });
      if (vars.action === 'false_positive') navigate('/cases');
    },
  });

  if (isLoading) return <div className="flex justify-center py-12"><Spinner /></div>;

  if (!c) {
    return (
      <div className="space-y-4">
        <Link to="/cases" className="text-gray-500 hover:text-gray-300 text-sm">← Cases</Link>
        <div className="panel p-8 text-center text-gray-600 text-sm">Case not found</div>
      </div>
    );
  }

  const confirmFp = () =>
    feedback.mutate({
      action: 'false_positive',
      reason: fpReason,
      rule:
        createRule && fpRuleType && fpMatchValue
          ? { ruleType: fpRuleType, matchValue: fpMatchValue, action: 'suppress', reason: fpReason }
          : undefined,
    });

  // The suppression rule is only created if fpMatchValue is non-empty — without a default,
  // "create rule" silently no-ops when the analyst doesn't also hand-fill a separate field.
  // Pre-fill from the case itself so the checkbox being checked actually does something.
  const openFpForm = () => {
    if (!showFpForm && !fpMatchValue) {
      const merchantMatch = c.title.match(/^([^(]+?)\s*\(\d+\)/);
      const guess = merchantMatch?.[1]?.trim() || c.affectedEntities[0] || '';
      if (guess) {
        setFpMatchValue(guess);
        setFpRuleType(/^\d{1,3}(\.\d{1,3}){3}$/.test(guess) ? 'ip' : 'keyword');
      }
    }
    setShowFpForm((v) => !v);
  };

  return (
    <div className="space-y-4" data-stagger>
      <div className="flex items-center gap-3 min-w-0">
        <Link to="/cases" className="text-gray-500 hover:text-gray-300 text-sm shrink-0">← Cases</Link>
        <h1 className="font-display text-lg font-semibold text-white truncate">{c.title}</h1>
      </div>

      <div className="grid grid-cols-2 md:grid-cols-5 gap-3 text-xs">
        <div className="panel p-3">
          <div className="kicker mb-1.5">Severity</div>
          <SeverityBadge severity={c.severity} />
        </div>
        <div className="panel p-3">
          <div className="kicker mb-1.5">Status</div>
          <div className="text-white font-medium">{c.status}</div>
        </div>
        <div className="panel p-3">
          <div className="kicker mb-1.5">Confidence</div>
          <ConfidenceBadge confidence={c.confidence} />
        </div>
        <div className="panel p-3">
          <div className="kicker mb-1.5">Created</div>
          <div className="text-white font-mono">{c.firstSeen?.slice(0, 16).replace('T', ' ')}</div>
        </div>
        <div className="panel p-3">
          <div className="kicker mb-1.5">Last Seen</div>
          <div className="text-white font-mono">{c.lastSeen?.slice(0, 16).replace('T', ' ')}</div>
        </div>
      </div>

      {c.affectedEntities.length > 0 && (
        <div className="panel p-4">
          <div className="kicker mb-2">Affected Entities</div>
          <div className="flex flex-wrap gap-1.5">
            {c.affectedEntities.map((e) => (
              <span key={e} className="px-2 py-0.5 text-xs font-mono bg-gray-800/80 border border-gray-700/60 text-gray-300 rounded">{e}</span>
            ))}
          </div>
        </div>
      )}

      <div className="panel p-4">
        <div className="kicker mb-2">Notes</div>
        <pre className="text-xs text-gray-300 font-mono whitespace-pre-wrap">{c.notes}</pre>
      </div>

      {c.followUpQueries.length > 0 && (
        <div className="panel p-4">
          <div className="kicker mb-2">Follow-Up Queries</div>
          {c.followUpQueries.map((q, i) => (
            <pre key={i} className="text-xs text-emerald-300/80 font-mono whitespace-pre-wrap mb-2 bg-gray-950/80 border border-gray-800/60 rounded-md p-2.5">{q}</pre>
          ))}
        </div>
      )}

      {c.evidence.length > 0 && (
        <div className="panel p-4">
          <div className="kicker mb-2.5">Evidence ({c.evidence.length})</div>
          <div className="relative pl-4 space-y-2.5">
            <span aria-hidden className="absolute left-[5px] top-2 bottom-2 w-px bg-gray-800" />
            {c.evidence.map((ev, i) => (
              <div key={i} className="relative">
                <span aria-hidden className="absolute -left-4 top-3 h-2 w-2 rounded-full border-2 border-gray-700 bg-gray-950" />
                <div className="border border-gray-800/60 rounded-lg p-2.5 ml-1 bg-gray-950/40">
                  <div className="font-mono text-[10px] text-gray-600 mb-1">
                    {ev.timestamp?.slice(0, 16).replace('T', ' ')} · run {ev.runId?.slice(0, 8)}
                  </div>
                  <div className="text-xs text-gray-300">{ev.summary}</div>
                </div>
              </div>
            ))}
          </div>
        </div>
      )}

      <div className="panel p-4 space-y-3">
        <div className="kicker">Actions</div>
        <div className="flex gap-2 flex-wrap">
          <button
            onClick={openFpForm}
            className="px-3 py-1.5 text-xs bg-gray-800/80 hover:bg-gray-700 text-gray-300 rounded-md border border-gray-700 transition-colors"
          >
            Mark False Positive
          </button>
          <button
            onClick={() => feedback.mutate({ action: 'escalate' })}
            disabled={feedback.isPending}
            className="px-3 py-1.5 text-xs bg-rose-600/15 hover:bg-rose-600/25 text-rose-400 rounded-md border border-rose-500/30 transition-colors disabled:opacity-50"
          >
            Escalate
          </button>
          <button
            onClick={() => feedback.mutate({ action: 'resolve', reason: 'Manually resolved by analyst' })}
            disabled={feedback.isPending}
            className="px-3 py-1.5 text-xs bg-emerald-600/15 hover:bg-emerald-600/25 text-emerald-400 rounded-md border border-emerald-500/30 transition-colors disabled:opacity-50"
          >
            Resolve
          </button>
        </div>

        {showFpForm && (
          <div className="rise border border-gray-800 rounded-lg p-4 bg-gray-950/60 space-y-3 mt-3">
            <div className="text-xs text-gray-400">
              Mark as false positive and optionally create a suppression rule so this doesn't get flagged again.
            </div>
            <div>
              <label className={label}>Reason</label>
              <input
                value={fpReason}
                onChange={(e) => setFpReason(e.target.value)}
                className={inputCls}
                placeholder="Why this is not fraud..."
              />
            </div>
            <div>
              <label className="flex items-center gap-2 cursor-pointer">
                <input
                  type="checkbox"
                  checked={createRule}
                  onChange={(e) => setCreateRule(e.target.checked)}
                  className="accent-emerald-500"
                />
                <span className="text-xs text-gray-300">Create suppression rule to prevent future flags</span>
              </label>
              {createRule && (
                <div className="grid grid-cols-2 gap-3 mt-3">
                  <div>
                    <label className={label}>Rule Type</label>
                    <select value={fpRuleType} onChange={(e) => setFpRuleType(e.target.value)} className={inputCls}>
                      <option value="ip">IP Address</option>
                      <option value="cidr">CIDR Range</option>
                      <option value="asn">ASN</option>
                      <option value="pattern_id">Pattern ID</option>
                      <option value="keyword">Keyword</option>
                      <option value="recipient">Recipient</option>
                    </select>
                  </div>
                  <div>
                    <label className={label}>Match Value</label>
                    <input
                      value={fpMatchValue}
                      onChange={(e) => setFpMatchValue(e.target.value)}
                      className={inputCls}
                      placeholder="e.g. 159.89.x.x or AS14061"
                    />
                  </div>
                </div>
              )}
            </div>
            <div className="flex gap-2">
              <button
                onClick={confirmFp}
                disabled={feedback.isPending || !fpReason}
                className="px-3 py-1.5 text-xs font-medium bg-gray-700 hover:bg-gray-600 text-white rounded-md transition-colors disabled:opacity-50"
              >
                Confirm False Positive
              </button>
              <button onClick={() => setShowFpForm(false)} className="px-3 py-1.5 text-xs text-gray-500 hover:text-gray-300">
                Cancel
              </button>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
