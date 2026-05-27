// Chart.js interop for Blazor Server
window.chartInterop = {
    charts: {},

    renderChart: function (canvasId, chartType, labels, datasets, options) {
        const canvas = document.getElementById(canvasId);
        if (!canvas) return;

        if (this.charts[canvasId]) {
            this.charts[canvasId].destroy();
        }

        const colors = [
            'rgba(16, 185, 129, 0.8)', 'rgba(59, 130, 246, 0.8)',
            'rgba(245, 158, 11, 0.8)', 'rgba(239, 68, 68, 0.8)',
            'rgba(139, 92, 246, 0.8)', 'rgba(236, 72, 153, 0.8)',
            'rgba(20, 184, 166, 0.8)', 'rgba(249, 115, 22, 0.8)',
        ];
        const borderColors = colors.map(c => c.replace('0.8', '1'));

        const chartDatasets = datasets.map((ds, i) => ({
            label: ds.label,
            data: ds.data,
            backgroundColor: chartType === 'doughnut'
                ? ds.data.map((_, j) => colors[j % colors.length])
                : colors[i % colors.length],
            borderColor: chartType === 'doughnut'
                ? ds.data.map((_, j) => borderColors[j % borderColors.length])
                : borderColors[i % borderColors.length],
            borderWidth: 1,
            tension: 0.3,
            fill: chartType === 'line'
        }));

        this.charts[canvasId] = new Chart(canvas, {
            type: chartType,
            data: { labels, datasets: chartDatasets },
            options: {
                responsive: true,
                maintainAspectRatio: true,
                plugins: {
                    legend: {
                        display: chartDatasets.length > 1 || chartType === 'doughnut',
                        labels: { color: '#9ca3af', font: { size: 11 } }
                    }
                },
                scales: chartType === 'doughnut' ? {} : {
                    x: { ticks: { color: '#6b7280', font: { size: 10 } }, grid: { color: 'rgba(55, 65, 81, 0.3)' } },
                    y: { ticks: { color: '#6b7280', font: { size: 10 } }, grid: { color: 'rgba(55, 65, 81, 0.3)' } }
                }
            }
        });
    },

    destroyChart: function (canvasId) {
        if (this.charts[canvasId]) {
            this.charts[canvasId].destroy();
            delete this.charts[canvasId];
        }
    },

    destroyAllCharts: function () {
        Object.values(this.charts).forEach(c => c.destroy());
        this.charts = {};
    },

    scrollToBottom: function (element) {
        if (element) {
            element.scrollTop = element.scrollHeight;
        }
    }
};
