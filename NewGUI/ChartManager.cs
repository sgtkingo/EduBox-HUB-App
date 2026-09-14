using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;

namespace NewGUI
{
    public class ChartManager : IDisposable
    {
        private readonly Chart _chart;
        private readonly ValueDisplayManager _valueMgr;
        private readonly Action<string> _log;
        private readonly ConcurrentQueue<FrameData> _queue = new ConcurrentQueue<FrameData>();
        private readonly Timer _timer;
        private readonly int _maxPoints;

        private static readonly Color[] SeriesPalette = new[]
        {
            Color.FromArgb(15, 108, 189),   // Vibrant Blue
            Color.FromArgb(230, 81, 0),     // Vibrant Deep Orange
            Color.FromArgb(46, 125, 50),    // Forest Green
            Color.FromArgb(142, 36, 170),   // Vivid Purple
            Color.FromArgb(198, 40, 40),    // Crimson Red
            Color.FromArgb(0, 131, 143),    // Cyan / Teal
            Color.FromArgb(216, 27, 96),    // Deep Pink
            Color.FromArgb(55, 71, 79)      // Dark Slate
        };

        private int _sampleCount = 0;
        private bool _disposed;

        private readonly int _maxSamples;
        private bool _limitReached;

        // index -> (seriesName -> value)
        private readonly SortedDictionary<int, Dictionary<string, double>> _history
            = new SortedDictionary<int, Dictionary<string, double>>();
        private readonly object _historyLock = new object();

        public event EventHandler MaxSamplesReached;

        public ChartManager(Chart chart, ValueDisplayManager valueMgr, Action<string> log, int intervalMs = 100, int maxPoints = 50, int maxSamples = 10000)
        {
            _chart = chart ?? throw new ArgumentNullException(nameof(chart));
            _valueMgr = valueMgr;
            _log = log ?? (_ => { });
            _maxPoints = Math.Max(10, maxPoints);
            _maxSamples = Math.Max(1, maxSamples);

            _timer = new Timer();
            _timer.Interval = Math.Max(10, intervalMs);
            _timer.Tick += Timer_Tick;
            _timer.Start();

            EnsureChartArea();
        }

        public void SetInterval(int ms)
        {
            if (_disposed) return;
            if (ms < 10) ms = 100;
            _timer.Interval = ms;
        }

        public void Start() { if (!_disposed) _timer.Start(); }
        public void Stop() { if (!_disposed) _timer.Stop(); }

        /// <summary>
        /// Vynuluje graf pro nové měření: smaže pending data, řady, body a počítadlo vzorků.
        /// </summary>
        public void Reset()
        {
            if (_disposed) return;

            // vyprázdnit čekající rámce
            while (_queue.TryDequeue(out _)) { }

            // vynulovat počítadlo vzorků
            System.Threading.Interlocked.Exchange(ref _sampleCount, 0);
            _limitReached = false;

            lock (_historyLock)
            {
                _history.Clear();
            }

            // vyčistit zobrazení hodnot
            try { _valueMgr?.UpdateValueText(string.Empty); } catch { }

            void ClearChart()
            {
                try
                {
                    _chart.Series.Clear();
                    if (_chart.ChartAreas.Count > 0)
                    {
                        var ca = _chart.ChartAreas[0];
                        ca.AxisX.Minimum = 0;
                        ca.AxisX.Maximum = 10;
                        ca.AxisX.Interval = 1;
                        ca.AxisY.Minimum = 0;
                        ca.AxisY.Maximum = 100;
                        ca.AxisY.Interval = 20;
                    }
                    _chart.Invalidate();
                    _chart.Update();
                }
                catch { }
            }

            try
            {
                if (_chart.InvokeRequired)
                {
                    _chart.BeginInvoke((Action)ClearChart);
                }
                else
                {
                    ClearChart();
                }
            }
            catch { }
        }

        public void Enqueue(FrameData frame)
        {
            if (frame == null) return;
            _queue.Enqueue(frame);
        }

        public void ParseAndEnqueue(string data)
        {
            if (string.IsNullOrWhiteSpace(data)) return;
            if (_limitReached) return;

            string s = data.Trim();
            s = s.TrimStart('\uFEFF');
            if (s.StartsWith("?")) s = s.Substring(1);

            // Bezpečné parsování dvojic klíč=hodnota bez pádů na duplicitních klíčích
            var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var parts = s.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var kv = part.Split(new[] { '=' }, 2);
                if (kv.Length == 2)
                {
                    string k = kv[0].Trim();
                    string v = kv[1].Trim();
                    parameters[k] = v;
                }
            }

            // Klíče, které se do grafu nekreslí (meta-informace)
            var skipKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "type", "id", "pin", "pins", "app", "version", "dbversion", "api", "status", "code", "error" };

            var dataForGraph = parameters
                .Where(kvp => !skipKeys.Contains(kvp.Key))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            var numericPairs = new List<string>();
            var numericValues = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in dataForGraph)
            {
                string variableName = kvp.Key;
                string raw = kvp.Value ?? string.Empty;

                string normalized = raw;
                if (normalized.IndexOf(',') >= 0 && normalized.IndexOf('.') < 0)
                    normalized = normalized.Replace(',', '.');

                var m = System.Text.RegularExpressions.Regex.Match(
                            normalized, @"[-+]?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?");

                double numericValue = 0.0;
                bool hasNumber = m.Success && double.TryParse(
                    m.Value,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out numericValue);

                if (hasNumber)
                {
                    numericPairs.Add($"{variableName}={numericValue.ToString("G", System.Globalization.CultureInfo.InvariantCulture)}");
                    numericValues[variableName] = numericValue;
                }
                else
                {
                    _log?.Invoke($"{variableName}: {raw}");
                }
            }

            if (numericValues.Count > 0)
            {
                var text = string.Join(", ", numericPairs);
                int idx = System.Threading.Interlocked.Increment(ref _sampleCount);
                var frame = new FrameData(idx, numericValues, text);
                _queue.Enqueue(frame);
            }
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            if (_disposed) return;
            if (_limitReached) return;

            bool any = false;
            FrameData last = null;

            while (_queue.TryDequeue(out var frame))
            {
                last = frame;
                any = true;

                lock (_historyLock)
                {
                    if (!_history.TryGetValue(frame.Index, out var row))
                    {
                        row = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                        _history[frame.Index] = row;
                    }
                    foreach (var kv in frame.Values)
                        row[kv.Key] = kv.Value;
                }

                foreach (var kv in frame.Values)
                {
                    var variableName = kv.Key;
                    var numericValue = kv.Value;

                    if (_chart.Series.IsUniqueName(variableName))
                    {
                        int colorIdx = _chart.Series.Count % SeriesPalette.Length;
                        var color = SeriesPalette[colorIdx];

                        var s = new Series(variableName)
                        {
                            ChartType = SeriesChartType.Line,
                            BorderWidth = 2,
                            Color = color,
                            MarkerStyle = MarkerStyle.Circle,
                            MarkerSize = 6,
                            MarkerColor = Color.White,
                            MarkerBorderColor = color,
                            MarkerBorderWidth = 2,
                            ChartArea = _chart.ChartAreas.Count > 0 ? _chart.ChartAreas[0].Name : "ChartArea1",
                            Legend = _chart.Legends.Count > 0 ? _chart.Legends[0].Name : "Legend1",
                            IsVisibleInLegend = true
                        };
                        _chart.Series.Add(s);
                    }

                    var series = _chart.Series[variableName];

                    if (series.Points.Count > _maxPoints) series.Points.RemoveAt(0);

                    series.Points.AddXY(frame.Index, numericValue);
                }

                if (frame.Index >= _maxSamples)
                {
                    _limitReached = true;
                    try { _timer.Stop(); } catch { }
                    try { MaxSamplesReached?.Invoke(this, EventArgs.Empty); } catch { }
                    break;
                }
            }

            if (any)
            {
                EnsureChartArea();
                var ca = _chart.ChartAreas[0];

                // Popisky os bez uvozovek
                ca.AxisX.Title = "Počet vzorků";
                ca.AxisX.TitleFont = new Font("Segoe UI Variable Text", 9F, FontStyle.Regular);
                ca.AxisX.TitleForeColor = Color.FromArgb(90, 90, 95);
                ca.AxisY.Title = "Hodnota";
                ca.AxisY.TitleFont = new Font("Segoe UI Variable Text", 9F, FontStyle.Regular);
                ca.AxisY.TitleForeColor = Color.FromArgb(90, 90, 95);

                // Plynulé posouvání okna X osy
                int lastIndex = (last != null) ? last.Index : _sampleCount;
                if (lastIndex <= 10)
                {
                    ca.AxisX.Minimum = 0;
                    ca.AxisX.Maximum = 10;
                    ca.AxisX.Interval = 1;
                }
                else
                {
                    ca.AxisX.Minimum = lastIndex - 10;
                    ca.AxisX.Maximum = lastIndex;
                    ca.AxisX.Interval = 2;
                }

                // Bezpečný přepočet Y osy (dynamický rozsah dle dat)
                ca.AxisY.Minimum = double.NaN;
                ca.AxisY.Maximum = double.NaN;
                ca.RecalculateAxesScale();
                if (!double.IsNaN(ca.AxisY.Minimum) && !double.IsNaN(ca.AxisY.Maximum))
                {
                    if (Math.Abs(ca.AxisY.Maximum - ca.AxisY.Minimum) < 0.0001)
                    {
                        double mid = ca.AxisY.Minimum;
                        ca.AxisY.Minimum = mid - 1.0;
                        ca.AxisY.Maximum = mid + 1.0;
                    }
                }

                if (last != null && _valueMgr != null)
                {
                    _valueMgr.UpdateValueText(last.ValueText);
                }

                _chart.Invalidate();
                _chart.Update();
            }
        }

        private void EnsureChartArea()
        {
            if (_chart.ChartAreas.Count == 0)
            {
                var ca = new ChartArea("ChartArea1");
                ca.BackColor = Color.White;
                ca.BorderWidth = 0;
                ca.AxisX.Title = "Počet vzorků";
                ca.AxisY.Title = "Hodnota";
                _chart.ChartAreas.Add(ca);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _timer.Tick -= Timer_Tick; } catch { }
            try { _timer.Stop(); } catch { }
            try { _timer.Dispose(); } catch { }
        }

        public string ExportCsv(char separator = ';', bool forceText = false, bool decimalComma = true)
        {
            if (_disposed) return string.Empty;

            SortedDictionary<int, Dictionary<string, double>> snapshot;
            HashSet<string> allSeries;
            lock (_historyLock)
            {
                if (_history.Count == 0) return string.Empty;

                snapshot = new SortedDictionary<int, Dictionary<string, double>>();
                allSeries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var kv in _history)
                {
                    var rowCopy = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                    foreach (var v in kv.Value)
                    {
                        rowCopy[v.Key] = v.Value;
                        allSeries.Add(v.Key);
                    }
                    snapshot[kv.Key] = rowCopy;
                }
            }

            var seriesList = allSeries.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();

            var sb = new StringBuilder();
            sb.Append("Sample");
            foreach (var s in seriesList)
                sb.Append(separator).Append(EscapeCsv(s, separator));
            sb.AppendLine();

            foreach (var row in snapshot)
            {
                string xText = row.Key.ToString(System.Globalization.CultureInfo.InvariantCulture);
                sb.Append(forceText ? ToExcelText(xText) : xText);

                for (int i = 0; i < seriesList.Count; i++)
                {
                    sb.Append(separator);
                    if (row.Value.TryGetValue(seriesList[i], out var y))
                    {
                        string yText = y.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        if (decimalComma) yText = yText.Replace('.', ',');
                        sb.Append(forceText ? ToExcelText(yText) : yText);
                    }
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static string ToExcelText(string value)
        {
            return value ?? string.Empty;
        }

        private static string EscapeCsv(string value, char separator)
        {
            if (value == null) return string.Empty;
            bool mustQuote = value.IndexOf(separator) >= 0 || value.IndexOf('"') >= 0 || value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0;
            if (!mustQuote) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}