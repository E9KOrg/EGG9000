function initVirtueStatsChart(suffix, snapData) {
    var METRICS = {
        curiosity:  { label: 'Curiosity',  axisGroup: 'egg', dataKey: 'curiosity',  tooltipKey: 'curiosityStr' },
        integrity:  { label: 'Integrity',  axisGroup: 'egg', dataKey: 'integrity',  tooltipKey: 'integrityStr' },
        humility:   { label: 'Humility',   axisGroup: 'egg', dataKey: 'humility',   tooltipKey: 'humilityStr' },
        resilience: { label: 'Resilience', axisGroup: 'egg', dataKey: 'resilience', tooltipKey: 'resilienceStr' },
        kindness:   { label: 'Kindness',   axisGroup: 'egg', dataKey: 'kindness',   tooltipKey: 'kindnessStr' },
        teTotal:    { label: 'TE Total',   axisGroup: 'te',  dataKey: 'teTotal',    tooltipKey: null },
        teEarned:   { label: 'TE Earned',  axisGroup: 'te',  dataKey: 'teEarned',   tooltipKey: null },
        tePending:  { label: 'TE Pending', axisGroup: 'te',  dataKey: 'tePending',  tooltipKey: null },
        shifts:     { label: 'Shifts',     axisGroup: 'te',  dataKey: 'shifts',     tooltipKey: null },
        resets:     { label: 'Resets',     axisGroup: 'te',  dataKey: 'resets',     tooltipKey: null }
    };
    var METRIC_KEYS = ['curiosity', 'integrity', 'humility', 'resilience', 'kindness', 'teTotal', 'teEarned', 'tePending', 'shifts', 'resets'];

    var EGG_SUFFIXES = [
        [0, ''], [3, 'K'], [6, 'M'], [9, 'B'], [12, 'T'], [15, 'q'], [18, 'Q'], [21, 's'], [24, 'S'],
        [27, 'o'], [30, 'N'], [33, 'd'], [36, 'U'], [39, 'D'], [42, 'Td'], [45, 'qd'], [48, 'Qd'],
        [51, 'sd'], [54, 'Sd'], [57, 'Od'], [60, 'Nd'], [63, 'V'], [66, 'uV'], [69, 'dV'], [72, 'tV'],
        [75, 'qV'], [78, 'QV']
    ];

    function formatEggAxis(val) {
        if (typeof val !== 'number' || !isFinite(val)) return val;
        var neg = val < 0;
        if (neg) val = -val;
        if (val === 0) return '0';
        var oom = Math.floor(Math.log10(val) / 3) * 3;
        var entry = EGG_SUFFIXES.filter(function(e) { return e[0] === oom; })[0] || EGG_SUFFIXES[EGG_SUFFIXES.length - 1];
        var scaled = val / Math.pow(10, entry[0]);
        return (neg ? '-' : '') + scaled.toFixed(1) + entry[1];
    }

    function formatTeAxis(val) {
        return typeof val === 'number' ? Math.round(val).toString() : val;
    }

    var state = {
        activeMetrics: new Set(['teTotal']),
        chart: null,
        dual: false
    };

    function buildSeries() {
        return METRIC_KEYS
            .filter(function(k) { return state.activeMetrics.has(k); })
            .map(function(k) {
                var m = METRICS[k];
                var data = snapData.map(function(p) {
                    var val = p[m.dataKey];
                    return [new Date(p.date + 'T00:00:00').getTime(), (typeof val === 'number' && isFinite(val)) ? val : null];
                });
                return { name: m.label, data: data };
            })
            .filter(function(s) {
                return s.data.some(function(pt) { return pt[1] !== null; });
            });
    }

    function axisBounds(axisGroup) {
        var vals = [];
        METRIC_KEYS.forEach(function(k) {
            if (!state.activeMetrics.has(k) || METRICS[k].axisGroup !== axisGroup) return;
            var key = METRICS[k].dataKey;
            snapData.forEach(function(p) {
                var v = p[key];
                if (typeof v === 'number' && isFinite(v)) vals.push(v);
            });
        });
        if (vals.length === 0) return { min: 0, max: 1 };
        var mn = Math.min.apply(null, vals);
        var mx = Math.max.apply(null, vals);
        if (mn === mx) { mn -= 1; mx += 1; }
        return { min: mn, max: mx };
    }

    function buildYAxis() {
        var activeKeys = METRIC_KEYS.filter(function(k) { return state.activeMetrics.has(k); });
        var hasEgg = activeKeys.some(function(k) { return METRICS[k].axisGroup === 'egg'; });
        var hasTe  = activeKeys.some(function(k) { return METRICS[k].axisGroup === 'te'; });
        var dual = hasEgg && hasTe;

        var safeAxis = { forceNiceScale: false, tickAmount: 5 };

        if (!dual) {
            var bounds = axisBounds(hasEgg ? 'egg' : 'te');
            return [Object.assign({ _group: hasEgg ? 'egg' : 'te', title: { text: hasEgg ? 'Delivered' : 'TE / Shifts / Resets', style: { fontSize: '11px' } } }, safeAxis, bounds)];
        }

        var eggBounds = axisBounds('egg');
        var teBounds  = axisBounds('te');
        return activeKeys.map(function(k) {
            var isEgg = METRICS[k].axisGroup === 'egg';
            var isFirstOfType = activeKeys.filter(function(j) {
                return METRICS[j].axisGroup === METRICS[k].axisGroup;
            })[0] === k;
            return Object.assign({
                _group: isEgg ? 'egg' : 'te',
                show: isFirstOfType,
                opposite: !isEgg,
                title: {
                    text: isEgg ? 'Delivered' : 'TE / Shifts / Resets',
                    style: { fontSize: '11px' }
                }
            }, safeAxis, isEgg ? eggBounds : teBounds);
        });
    }

    function buildOptions() {
        var isDark = document.documentElement.getAttribute('data-bs-theme') === 'dark';
        var textColor  = isDark ? '#e9ecef' : '#373d3f';
        var gridColor  = isDark ? '#444'    : '#e0e0e0';
        return {
            chart: {
                type: 'line',
                height: 300,
                background: 'transparent',
                animations: { enabled: false },
                zoom: { type: 'x', enabled: true, autoScaleYaxis: true },
                toolbar: { autoSelected: 'zoom' }
            },
            theme: { mode: isDark ? 'dark' : 'light' },
            series: buildSeries(),
            xaxis: {
                type: 'datetime',
                labels: { datetimeUTC: false, style: { colors: textColor } }
            },
            grid: { borderColor: gridColor },
            yaxis: buildYAxis().map(function(ax) {
                var entry = Object.assign({}, ax);
                entry.labels = { formatter: entry._group === 'egg' ? formatEggAxis : formatTeAxis, style: { colors: textColor } };
                if (entry.title) entry.title.style = Object.assign({}, entry.title.style, { color: textColor });
                return entry;
            }),
            tooltip: {
                theme: isDark ? 'dark' : 'light',
                x: { format: 'MMM dd yyyy' },
                y: {
                    formatter: function(val, opts) {
                        var sIdx = opts.seriesIndex;
                        var dp   = opts.dataPointIndex;
                        var sName = opts.w.config.series[sIdx].name;
                        var metricKey = METRIC_KEYS.filter(function(k) {
                            return METRICS[k].label === sName;
                        })[0];
                        if (metricKey && METRICS[metricKey].tooltipKey && snapData[dp]) {
                            return snapData[dp][METRICS[metricKey].tooltipKey];
                        }
                        if (typeof val !== 'number') return val;
                        return Math.round(val).toString();
                    }
                }
            },
            stroke: { curve: 'smooth', width: 2 },
            dataLabels: { enabled: false },
            legend: { show: true },
            markers: { size: 0, hover: { size: 5 } }
        };
    }

    function renderChart(containerId) {
        if (state.chart) {
            state.chart.destroy();
            state.chart = null;
        }
        var el = document.getElementById(containerId);
        if (!el) return;
        state.chart = new ApexCharts(el, buildOptions());
        state.chart.render();
    }

    function syncButtonStyles() {
        METRIC_KEYS.forEach(function(k) {
            var btn = document.getElementById('virtueStatsBtn-' + k + '-' + suffix);
            if (!btn) return;
            if (state.activeMetrics.has(k)) {
                btn.classList.remove('btn-outline-secondary');
                btn.classList.add('btn-primary');
            } else {
                btn.classList.remove('btn-primary');
                btn.classList.add('btn-outline-secondary');
            }
        });
    }

    function updateChart() {
        if (!state.chart) return;
        var activeKeys = METRIC_KEYS.filter(function(k) { return state.activeMetrics.has(k); });
        var nowDual = activeKeys.some(function(k) { return METRICS[k].axisGroup === 'egg'; })
                   && activeKeys.some(function(k) { return METRICS[k].axisGroup === 'te'; });
        var prevDual = state.dual;
        state.dual = nowDual;
        if (nowDual || prevDual) {
            renderChart('virtueStatsChartNarrow-' + suffix);
        } else {
            var isDarkU = document.documentElement.getAttribute('data-bs-theme') === 'dark';
            var textColorU = isDarkU ? '#e9ecef' : '#373d3f';
            state.chart.updateOptions({
                series: buildSeries(),
                yaxis: buildYAxis().map(function(ax) {
                    var entry = Object.assign({}, ax);
                    entry.labels = { formatter: entry._group === 'egg' ? formatEggAxis : formatTeAxis, style: { colors: textColorU } };
                    if (entry.title) entry.title.style = Object.assign({}, entry.title.style, { color: textColorU });
                    return entry;
                })
            }, false, false);
        }
    }

    function toggle(key) {
        if (state.activeMetrics.has(key)) {
            if (state.activeMetrics.size === 1) return;
            state.activeMetrics.delete(key);
        } else {
            state.activeMetrics.add(key);
        }
        syncButtonStyles();
        updateChart();
    }

    window['virtueStatsToggle_' + suffix] = toggle;

    window.addEventListener('load', function() {
        var chartEl = document.getElementById('virtueStatsChartNarrow-' + suffix);
        if (!chartEl) return;
        var pane = chartEl.closest('.tab-pane');
        if (!pane) return;

        if (pane.classList.contains('active')) {
            renderChart('virtueStatsChartNarrow-' + suffix);
        }

        document.addEventListener('shown.bs.tab', function(e) {
            if (state.chart) return;
            var href = e.target.getAttribute('data-bs-target') || e.target.getAttribute('href');
            if (!href) return;
            var shownEl = document.querySelector(href);
            if (shownEl && (shownEl === pane || shownEl.contains(pane)) && pane.classList.contains('active')) {
                renderChart('virtueStatsChartNarrow-' + suffix);
            }
        });

        document.addEventListener('hidden.bs.tab', function(e) {
            if (!state.chart) return;
            var href = e.target.getAttribute('data-bs-target') || e.target.getAttribute('href');
            if (!href) return;
            var hiddenEl = document.querySelector(href);
            if (hiddenEl && (hiddenEl === pane || hiddenEl.contains(pane))) {
                state.chart.destroy();
                state.chart = null;
            }
        });
    });
}
