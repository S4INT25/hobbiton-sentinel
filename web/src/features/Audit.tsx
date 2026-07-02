import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { api } from '../api';
import { PageHeader, Spinner, fmtDateFull, tableWrap, thCls, tdCls } from '../components/ui';

const PAGE_SIZE = 50;

export default function Audit() {
  const [page, setPage] = useState(1);
  const { data: logs = [], isLoading } = useQuery({
    queryKey: ['audit', page],
    queryFn: () => api.listAudit(PAGE_SIZE, (page - 1) * PAGE_SIZE),
  });

  return (
    <div className="space-y-4" data-stagger>
      <PageHeader title="Audit Log" subtitle="Every admin action, login, and change — newest first" />

      <div className={tableWrap}>
        <div className="overflow-x-auto">
          <table className="w-full text-sm min-w-[720px]">
            <thead className="bg-gray-900/60">
              <tr className="border-b border-gray-800">
                <th className={thCls}>Time</th>
                <th className={thCls}>User</th>
                <th className={thCls}>Action</th>
                <th className={thCls}>Resource</th>
                <th className={thCls}>Details</th>
                <th className={thCls}>IP</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-800/50">
              {logs.map((l) => (
                <tr key={l.id} className="hover:bg-emerald-500/[0.03] transition-colors">
                  <td className={`${tdCls} font-mono text-gray-500 whitespace-nowrap py-2`}>{fmtDateFull(l.timestamp)}</td>
                  <td className={`${tdCls} py-2`}>{l.username}</td>
                  <td className="px-4 py-2">
                    <span className="px-1.5 py-0.5 text-[10px] rounded bg-gray-800/80 border border-gray-700/50 text-gray-300 font-mono uppercase tracking-wide">{l.action}</span>
                  </td>
                  <td className={`${tdCls} font-mono text-gray-400 whitespace-nowrap py-2`}>
                    {l.resourceType}{l.resourceId ? `/${l.resourceId.slice(0, 10)}` : ''}
                  </td>
                  <td className={`${tdCls} text-gray-500 max-w-md truncate py-2`}>{l.details}</td>
                  <td className={`${tdCls} font-mono text-gray-600 py-2`}>{l.ipAddress}</td>
                </tr>
              ))}
              {!isLoading && logs.length === 0 && (
                <tr><td colSpan={6} className="px-4 py-8 text-center text-gray-600 text-xs">No audit entries</td></tr>
              )}
              {isLoading && (
                <tr><td colSpan={6} className="px-4 py-8"><div className="flex justify-center"><Spinner /></div></td></tr>
              )}
            </tbody>
          </table>
        </div>
      </div>

      <div className="flex items-center justify-between text-xs">
        <span className="font-mono text-[11px] text-gray-500 tnum">Page {page}</span>
        <div className="flex items-center gap-2">
          <button onClick={() => setPage((p) => Math.max(1, p - 1))} disabled={page <= 1}
                  className="px-2.5 py-1 border border-gray-800 rounded-md text-gray-300 disabled:opacity-40 hover:border-gray-700 hover:bg-gray-900/60 transition-colors">
            Previous
          </button>
          <button onClick={() => setPage((p) => p + 1)} disabled={logs.length < PAGE_SIZE}
                  className="px-2.5 py-1 border border-gray-800 rounded-md text-gray-300 disabled:opacity-40 hover:border-gray-700 hover:bg-gray-900/60 transition-colors">
            Next
          </button>
        </div>
      </div>
    </div>
  );
}
