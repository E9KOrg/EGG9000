function initEbStatsChart(suffix, snapData) {
    var METRICS = {
        eb:  { label: 'EB',  scaleType: 'log',    dataKey: 'logEb', tooltipKey: 'ebStr' },
        se:  { label: 'SE',  scaleType: 'log',    dataKey: 'logSe', tooltipKey: 'seStr' },
        pe:  { label: 'PE',  scaleType: 'linear', dataKey: 'pe',    tooltipKey: null },
        eot: { label: 'EoT', scaleType: 'linear', dataKey: 'eot',   tooltipKey: null },
        mer: { label: 'MER', scaleType: 'linear', dataKey: 'mer',   tooltipKey: null }
    };
    var METRIC_KEYS = ['eb', 'se', 'pe', 'eot', 'mer'];

    var state = {
        activeMetrics: new Set(['eb']),
        chart: null,
        expanded: false,
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

    function axisBounds(scaleType) {
        var vals = [];
        METRIC_KEYS.forEach(function(k) {
            if (!state.activeMetrics.has(k) || METRICS[k].scaleType !== scaleType) return;
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

    function seriesBounds(key) {
        var vals = snapData
            .map(function(p) { return p[METRICS[key].dataKey]; })
            .filter(function(v) { return typeof v === 'number' && isFinite(v); });
        if (vals.length === 0) return { min: 0, max: 1 };
        var mn = Math.min.apply(null, vals);
        var mx = Math.max.apply(null, vals);
        if (mn === mx) { mn -= 1; mx += 1; }
        return { min: mn, max: mx };
    }

    function buildYAxis() {
        var activeKeys = METRIC_KEYS.filter(function(k) { return state.activeMetrics.has(k); });
        var hasLog    = activeKeys.some(function(k) { return METRICS[k].scaleType === 'log'; });
        var hasLinear = activeKeys.some(function(k) { return METRICS[k].scaleType === 'linear'; });
        var dual = hasLog && hasLinear;

        var safeAxis = { forceNiceScale: false, tickAmount: 5 };

        if (!dual) {
            var bounds = axisBounds(hasLog ? 'log' : 'linear');
            return [Object.assign({ title: { text: hasLog ? 'log₁₀(value)' : 'value', style: { fontSize: '11px' } } }, safeAxis, bounds)];
        }

        var logBounds    = axisBounds('log');
        var linearBounds = axisBounds('linear');
        return activeKeys.map(function(k) {
            var isLog = METRICS[k].scaleType === 'log';
            var isFirstOfType = activeKeys.filter(function(j) {
                return METRICS[j].scaleType === METRICS[k].scaleType;
            })[0] === k;
            return Object.assign({
                show: isFirstOfType,
                opposite: !isLog,
                title: {
                    text: isLog ? 'log₁₀(EB/SE)' : 'linear',
                    style: { fontSize: '11px' }
                }
            }, safeAxis, isLog ? logBounds : linearBounds);
        });
    }

    function buildOptions() {
        var isDark = (typeof getCookie === 'function' && getCookie('Egg9000Theme') === 'bootstrap-dark')
                  || document.body.classList.contains('bootstrap-dark')
                  || document.documentElement.classList.contains('bootstrap-dark');
        var textColor  = isDark ? '#e9ecef' : '#373d3f';
        var gridColor  = isDark ? '#444'    : '#e0e0e0';
        var axisLabelFmt = function(val) { return typeof val === 'number' ? val.toFixed(2) : val; };
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
                entry.labels = { formatter: axisLabelFmt, style: { colors: textColor } };
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
                            var str = snapData[dp][METRICS[metricKey].tooltipKey];
                            var logVal = snapData[dp][METRICS[metricKey].dataKey];
                            if (typeof logVal === 'number' && isFinite(logVal)) {
                                str += ' (' + logVal.toFixed(2) + ')';
                            }
                            return str;
                        }
                        if (typeof val !== 'number') return val;
                        if (metricKey === 'pe' || metricKey === 'eot') return Math.round(val).toString();
                        return val.toFixed(2);
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
        var sets = ['', '-full'];
        METRIC_KEYS.forEach(function(k) {
            sets.forEach(function(set) {
                var btnId = 'ebStatsBtn-' + k + set + '-' + suffix;
                var btn = document.getElementById(btnId);
                if (!btn) return;
                if (state.activeMetrics.has(k)) {
                    btn.classList.remove('btn-outline-secondary');
                    btn.classList.add('btn-primary');
                } else {
                    btn.classList.remove('btn-primary');
                    btn.classList.add('btn-outline-secondary');
                }
            });
        });
    }

    function updateChart() {
        if (!state.chart) return;
        var activeKeys = METRIC_KEYS.filter(function(k) { return state.activeMetrics.has(k); });
        var nowDual = activeKeys.some(function(k) { return METRICS[k].scaleType === 'log'; })
                   && activeKeys.some(function(k) { return METRICS[k].scaleType === 'linear'; });
        var prevDual = state.dual;
        state.dual = nowDual;
        if (nowDual || prevDual) {
            // yaxis array length changes in any dual-axis scenario; updateOptions unreliable
            var containerId = state.expanded ? 'ebStatsChartFull-' + suffix : 'ebStatsChartNarrow-' + suffix;
            renderChart(containerId);
        } else {
            var isDarkU = document.body.classList.contains('bootstrap-dark') || document.documentElement.classList.contains('bootstrap-dark');
            var textColorU = isDarkU ? '#e9ecef' : '#373d3f';
            var fmtU = function(val) { return typeof val === 'number' ? val.toFixed(2) : val; };
            state.chart.updateOptions({
                series: buildSeries(),
                yaxis: buildYAxis().map(function(ax) {
                    var entry = Object.assign({}, ax);
                    entry.labels = { formatter: fmtU, style: { colors: textColorU } };
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

    window['ebStatsToggle_' + suffix] = toggle;

    window.addEventListener('load', function() {
        var chartEl = document.getElementById('ebStatsChartNarrow-' + suffix);
        if (!chartEl) return;
        var pane = chartEl.closest('.tab-pane');
        if (!pane) return;

        function activeContainerId() {
            return state.expanded ? 'ebStatsChartFull-' + suffix : 'ebStatsChartNarrow-' + suffix;
        }

        if (pane.classList.contains('active')) {
            renderChart(activeContainerId());
        }

        $('[data-toggle="tab"]').on('shown.bs.tab.ebstats' + suffix, function(e) {
            if (state.chart) return;
            var href = $(e.target).attr('href');
            if (!href) return;
            var shownEl = document.querySelector(href);
            if (shownEl && (shownEl === pane || shownEl.contains(pane)) && pane.classList.contains('active')) {
                renderChart(activeContainerId());
            }
        });

        $('[data-toggle="tab"]').on('hidden.bs.tab.ebstats' + suffix, function(e) {
            if (!state.chart) return;
            var href = $(e.target).attr('href');
            if (!href) return;
            var hiddenEl = document.querySelector(href);
            if (hiddenEl && (hiddenEl === pane || hiddenEl.contains(pane))) {
                state.chart.destroy();
                state.chart = null;
            }
        });
    });

    function expand() {
        state.expanded = true;
        document.getElementById('ebStatsNarrowCard-' + suffix).style.display = 'none';
        document.getElementById('ebStatsFullRow-' + suffix).style.display = '';
        syncButtonStyles();
        renderChart('ebStatsChartFull-' + suffix);
    }

    function collapse() {
        state.expanded = false;
        document.getElementById('ebStatsFullRow-' + suffix).style.display = 'none';
        document.getElementById('ebStatsNarrowCard-' + suffix).style.display = '';
        syncButtonStyles();
        renderChart('ebStatsChartNarrow-' + suffix);
    }

    window['ebStatsExpand_' + suffix]   = expand;
    window['ebStatsCollapse_' + suffix] = collapse;
}
