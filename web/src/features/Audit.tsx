import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { api } from '../api';
import { PageHeader, Spinner, fmtDateFull } from '../components/ui';

const PAGE_SIZE = 50;

export default function Audit() {
  const [page, setPage] = useState(1);
  const { data: logs = [], isLoading } = useQuery({
    queryKey: ['audit', page],
    queryFn: () => api.listAudit(PAGE_SIZE, (page - 1) * PAGE_SIZE),
  });

  return (
    <div className="space-y-4">
      <PageHeader title="Audit Log" subtitle="Every admin action, login, and change — newest first" />

      <div className="border border-gray-800 rounded-lg overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full text-sm min-w-[720px]">
            <thead className="bg-gray-900/50">
              <tr className="text-xs text-gray-500 border-b border-gray-800">
                <th className="text-left px-4 py-2 font-medium">Time</th>
                <th className="text-left px-4 py-2 font-medium">User</th>
                <th className="text-left px-4 py-2 font-medium">Action</th>
                <th className="text-left px-4 py-2 font-medium">Resource</th>
                <th className="text-left px-4 py-2 font-medium">Details</th>
                <th className="text-left px-4 py-2 font-medium">IP</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-800/50">
              {logs.map((l) => (
                <tr key={l.id} className="hover:bg-gray-900/30 transition-colors">
                  <td className="px-4 py-2 text-xs text-gray-500 whitespace-nowrap">{fmtDateFull(l.timestamp)}</td>
                  <td className="px-4 py-2 text-xs text-gray-300">{l.username}</td>
                  <td className="px-4 py-2">
                    <span className="px-1.5 py-0.5 text-[10px] rounded bg-gray-800 text-gray-300 font-mono">{l.action}</span>
                  </td>
                  <td className="px-4 py-2 text-xs text-gray-400 font-mono whitespace-nowrap">
                    {l.resourceType}{l.resourceId ? `/${l.resourceId.slice(0, 10)}` : ''}
                  </td>
                  <td className="px-4 py-2 text-xs text-gray-500 max-w-md truncate">{l.details}</td>
                  <td className="px-4 py-2 text-xs text-gray-600 font-mono">{l.ipAddress}</td>
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
        <span className="text-gray-500">Page {page}</span>
        <div className="flex items-center gap-2">
          <button onClick={() => setPage((p) => Math.max(1, p - 1))} disabled={page <= 1}
                  className="px-2.5 py-1 border border-gray-800 rounded text-gray-300 disabled:opacity-40 hover:border-gray-700 transition-colors">
            Previous
          </button>
          <button onClick={() => setPage((p) => p + 1)} disabled={logs.length < PAGE_SIZE}
                  className="px-2.5 py-1 border border-gray-800 rounded text-gray-300 disabled:opacity-40 hover:border-gray-700 transition-colors">
            Next
          </button>
        </div>
      </div>
    </div>
  );
}
