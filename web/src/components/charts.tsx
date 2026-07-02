import ReactApexChart from 'react-apexcharts';
import type { ApexOptions } from 'apexcharts';

// Chart-shape detection ported from the Blazor chat page: first column is the
// label axis, remaining numeric columns become series.

const num = (v: string | undefined) => {
  if (v == null) return NaN;
  const n = parseFloat(v.replace(/,/g, ''));
  return Number.isFinite(n) ? n : NaN;
};

export function numericColumns(columns: string[], rows: Record<string, string>[]): string[] {
  if (columns.length < 2 || rows.length === 0) return [];
  return columns.slice(1).filter((c) => rows.every((r) => !Number.isNaN(num(r[c]))));
}

export function applicableChartTypes(columns: string[], rows: Record<string, string>[]): string[] {
  const numeric = numericColumns(columns, rows);
  if (numeric.length === 0 || rows.length === 0) return [];
  const types = ['bar', 'line', 'area'];
  if (numeric.length === 1 && rows.length <= 12) types.push('pie', 'donut');
  types.push('table');
  return types;
}

export function chartSeries(columns: string[], rows: Record<string, string>[]) {
  const numeric = numericColumns(columns, rows);
  const labelCol = columns[0];
  return {
    categories: rows.map((r) => r[labelCol] ?? ''),
    series: numeric.map((c) => ({
      name: c,
      data: rows.map((r) => {
        const n = num(r[c]);
        return Number.isNaN(n) ? 0 : n;
      }),
    })),
  };
}

const PALETTE = ['#3b82f6', '#10b981', '#f97316', '#8b5cf6', '#ec4899', '#f59e0b', '#06b6d4', '#f43f5e'];

function baseOptions(categories: string[]): ApexOptions {
  return {
    chart: {
      background: 'transparent',
      toolbar: { show: false },
      animations: { enabled: false },
      foreColor: '#9ca3af',
    },
    theme: { mode: 'dark' },
    colors: PALETTE,
    grid: { borderColor: '#1f2937', strokeDashArray: 3 },
    xaxis: {
      categories,
      labels: { style: { fontSize: '10px' }, rotate: -35, trim: true },
      axisBorder: { color: '#374151' },
      axisTicks: { color: '#374151' },
    },
    yaxis: { labels: { style: { fontSize: '10px' } } },
    dataLabels: { enabled: false },
    legend: { position: 'bottom', fontSize: '11px' },
    tooltip: { theme: 'dark' },
    stroke: { width: 2, curve: 'smooth' },
  };
}

export function DataChart({
  chartType,
  columns,
  rows,
  height = 320,
}: {
  chartType: string;
  columns: string[];
  rows: Record<string, string>[];
  height?: number;
}) {
  if (chartType === 'pie' || chartType === 'donut' || chartType === 'doughnut') {
    const { categories, series } = chartSeries(columns, rows);
    if (series.length === 0) return null;
    const options: ApexOptions = {
      ...baseOptions(categories),
      labels: categories,
      stroke: { width: 1, colors: ['#111827'] },
    };
    return (
      <ReactApexChart
        type={chartType === 'pie' ? 'pie' : 'donut'}
        options={options}
        series={series[0].data}
        height={height}
        width="100%"
      />
    );
  }

  const apexType = chartType === 'area' ? 'area' : chartType === 'line' ? 'line' : 'bar';
  const { categories, series } = chartSeries(columns, rows);
  if (series.length === 0) return null;
  const options: ApexOptions = {
    ...baseOptions(categories),
    ...(apexType === 'area' ? { fill: { type: 'gradient', gradient: { opacityFrom: 0.35, opacityTo: 0.05 } } } : {}),
    ...(apexType === 'bar' ? { plotOptions: { bar: { borderRadius: 3, columnWidth: '60%' } } } : {}),
  };
  return <ReactApexChart type={apexType} options={options} series={series} height={height} width="100%" />;
}

export function DataTable({ columns, rows, maxRows = 50 }: { columns: string[]; rows: Record<string, string>[]; maxRows?: number }) {
  const shown = rows.slice(0, maxRows);
  return (
    <div className="overflow-x-auto border border-gray-800/60 rounded-lg">
      <table className="w-full text-xs">
        <thead className="bg-gray-900/50">
          <tr className="text-gray-500 border-b border-gray-800">
            {columns.map((c) => (
              <th key={c} className="text-left px-3 py-2 font-medium whitespace-nowrap">{c}</th>
            ))}
          </tr>
        </thead>
        <tbody className="divide-y divide-gray-800/40">
          {shown.map((r, i) => (
            <tr key={i} className="hover:bg-gray-900/30">
              {columns.map((c) => (
                <td key={c} className="px-3 py-1.5 text-gray-300 whitespace-nowrap">{r[c]}</td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
      {rows.length > maxRows && (
        <div className="px-3 py-1.5 text-[10px] text-gray-600 border-t border-gray-800/40">
          Showing {maxRows} of {rows.length} rows — download CSV for the full set
        </div>
      )}
    </div>
  );
}
