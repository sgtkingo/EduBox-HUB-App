using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace NewGUI
{
    /// <summary>
    /// VirtualDeviceSimulator: Poskytuje autonomní simulaci reálného chování ESP desky
    /// pro VSCP protokol (INIT, CONNECT, DISCONNECT, CONFIG, RESET, CONTROL, UPDATE).
    /// </summary>
    public class VirtualDeviceSimulator
    {
        private static readonly Lazy<VirtualDeviceSimulator> _instance =
            new Lazy<VirtualDeviceSimulator>(() => new VirtualDeviceSimulator());

        public static VirtualDeviceSimulator Instance => _instance.Value;

        private readonly Random _rnd = new Random();
        private int _step = 0;
        private List<Komponenty> _sensors = new List<Komponenty>();
        private readonly HashSet<string> _connectedSensors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public VirtualDeviceSimulator()
        {
            LoadSensors();
        }

        private void LoadSensors()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string jsonPath = Path.Combine(baseDir, "Senzory.json");
                if (!File.Exists(jsonPath) && !string.IsNullOrEmpty(Application.StartupPath))
                {
                    jsonPath = Path.Combine(Application.StartupPath, "Senzory.json");
                }

                if (File.Exists(jsonPath))
                {
                    string text = File.ReadAllText(jsonPath);
                    var data = JsonSerializer.Deserialize<List<Komponenty>>(text,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (data != null)
                    {
                        _sensors = data;
                    }
                }
            }
            catch
            {
                _sensors = new List<Komponenty>();
            }
        }

        /// <summary>
        /// Zpracuje VSCP požadavek a vrátí odpovídající VSCP odpověď.
        /// </summary>
        public string ProcessCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return string.Empty;

            string s = command.Trim();
            if (s.StartsWith("?")) s = s.Substring(1);

            var query = SerialParser.ParseQuery(s);
            query.TryGetValue("type", out string type);
            query.TryGetValue("id", out string id);

            type = type?.Trim().ToUpperInvariant() ?? string.Empty;
            id = id?.Trim() ?? string.Empty;

            if (type == "PING")
            {
                if (query.ContainsKey("status") ||
                    !VscpProtocol.TryReadPing(command, out var side, out var sequence) || side != "client")
                    return string.Empty;
                return VscpProtocol.PingFrame("server", sequence, true);
            }

            // 1. INIT handshake
            if (type == "INIT")
            {
                if (!query.TryGetValue("api", out var api) || api != VscpProtocol.ApiVersion)
                    return "?type=INIT&status=0&error=API mismatch";
                _step = 0;
                var sensorTokens = new List<string>();
                if (_sensors != null && _sensors.Count > 0)
                {
                    foreach (var k in _sensors)
                    {
                        string sId = $"S{k.Id:D2}";
                        string sName = !string.IsNullOrWhiteSpace(k.Znaceni) ? k.Znaceni : k.Nazev ?? "Sensor";
                        sensorTokens.Add($"{sId}:{sName}");
                    }
                }
                else
                {
                    sensorTokens.AddRange(new[] { "S00:DS18B20", "S01:DHT11", "S02:Dhall", "S06:HCSR04", "S09:BMP280", "S14:Antc", "S15:PHresistance", "S30:Heartbeat", "S31:Btn" });
                }

                string payload = string.Join(",", sensorTokens.Take(12));
                return $"?type=INIT&api={VscpProtocol.ApiVersion}&status=1&{payload}";
            }

            // 2. CONNECT
            if (type == "CONNECT")
            {
                if (!string.IsNullOrEmpty(id)) _connectedSensors.Add(id);
                return $"?id={id}&status=1";
            }

            // 3. DISCONNECT
            if (type == "DISCONNECT")
            {
                if (!string.IsNullOrEmpty(id)) _connectedSensors.Remove(id);
                return $"?id={id}&status=1";
            }

            // 4. CONFIG
            if (type == "CONFIG")
            {
                return $"?id={id}&status=1";
            }

            // 5. RESET
            if (type == "RESET")
            {
                _step = 0;
                return $"?id={id}&status=1";
            }

            // 6. CONTROL (Aktuátory)
            if (type == "CONTROL")
            {
                return $"?id={id}&status=1";
            }

            // 7. UPDATE (Čtení dat senzoru)
            if (type == "UPDATE")
            {
                return GenerateSensorUpdate(id);
            }

            // Default fallback
            if (!string.IsNullOrEmpty(id))
            {
                return $"?id={id}&status=1";
            }

            return "?status=1";
        }

        public void ResetState()
        {
            _step = 0;
            _connectedSensors.Clear();
        }

        private string GenerateSensorUpdate(string id)
        {
            _step++;
            var sb = new StringBuilder();
            sb.Append("?id=").Append(id).Append("&status=1");

            // Najít metadata senzoru v Senzory.json
            Komponenty sensor = null;
            if (_sensors != null)
            {
                sensor = _sensors.FirstOrDefault(k =>
                    string.Equals($"S{k.Id:D2}", id, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals($"S{k.Id}", id, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(k.Znaceni, id, StringComparison.OrdinalIgnoreCase));
            }

            if (sensor != null && sensor.Keywords_values != null && sensor.Keywords_values.Count > 0)
            {
                foreach (var kv in sensor.Keywords_values)
                {
                    string key = kv.Key;
                    string valType = kv.Value;
                    string val = GenerateValue(key, valType);
                    sb.Append("&").Append(key).Append("=").Append(val);
                }
            }
            else
            {
                if (id.IndexOf("S01", StringComparison.OrdinalIgnoreCase) >= 0 || id.IndexOf("DHT", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    double rise = 4.0 * (1.0 - Math.Exp(-_step * 0.025));
                    double t = 22.0 + rise + ((_rnd.NextDouble() - 0.5) * 0.06);
                    sb.Append("&temp=").Append(t.ToString("F1", CultureInfo.InvariantCulture));
                    sb.Append("&humi=48");
                }
                else if (id.IndexOf("S00", StringComparison.OrdinalIgnoreCase) >= 0 || id.IndexOf("DS18", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    double rise = 3.5 * (1.0 - Math.Exp(-_step * 0.02));
                    double t = 22.0 + rise + ((_rnd.NextDouble() - 0.5) * 0.04);
                    sb.Append("&temp=").Append(t.ToString("F2", CultureInfo.InvariantCulture));
                }
                else
                {
                    double v = 50.0 + Math.Sin(_step * 0.2) * 20.0 + (_rnd.NextDouble() - 0.5) * 2.0;
                    sb.Append("&val=").Append(v.ToString("F1", CultureInfo.InvariantCulture));
                }
            }

            return sb.ToString();
        }

        private string GenerateValue(string key, string valType)
        {
            string k = (key ?? string.Empty).ToLowerInvariant();
            string t = (valType ?? string.Empty).ToLowerInvariant();

            if (k.Contains("temp"))
            {
                // Temperature starts around 22.0 and creeps slightly and smoothly upwards
                double rise = 4.0 * (1.0 - Math.Exp(-_step * 0.025));
                double val = 22.0 + rise + ((_rnd.NextDouble() - 0.5) * 0.06);
                return val.ToString("F1", CultureInfo.InvariantCulture);
            }
            if (k.Contains("hum"))
            {
                // Humidity stays steady (realistic indoor humidity 48%)
                return "48";
            }
            if (k.Contains("dist"))
            {
                double val = 30.0 + Math.Sin(_step * 0.2) * 10.0 + (_rnd.NextDouble() - 0.5) * 0.5;
                return Math.Max(2.0, val).ToString("F1", CultureInfo.InvariantCulture);
            }
            if (k.Contains("press"))
            {
                double val = 1013.2 + Math.Sin(_step * 0.05) * 1.5 + (_rnd.NextDouble() - 0.5) * 0.2;
                return val.ToString("F1", CultureInfo.InvariantCulture);
            }
            if (k.Contains("alt"))
            {
                double val = 245.0 + Math.Sin(_step * 0.05) * 3.0;
                return val.ToString("F1", CultureInfo.InvariantCulture);
            }
            if (k.Contains("volt"))
            {
                double val = 2.5 + Math.Sin(_step * 0.2) * 0.8;
                return val.ToString("F2", CultureInfo.InvariantCulture);
            }
            if (k.Contains("bpm"))
            {
                int val = (int)(75 + Math.Sin(_step * 0.1) * 8 + _rnd.Next(-1, 2));
                return val.ToString();
            }
            if (k.Contains("intensity") || k.Contains("pos") || k.Contains("r") || k.Contains("g") || k.Contains("b"))
            {
                int val = (int)(500 + Math.Sin(_step * 0.15) * 200 + _rnd.Next(-10, 11));
                return Math.Max(0, val).ToString();
            }
            if (k.Contains("state") || t.Contains("bool"))
            {
                return (_step % 6 < 3) ? "1" : "0";
            }
            if (t.Contains("float") || t.Contains("double"))
            {
                double val = 20.0 + Math.Sin(_step * 0.1) * 10.0 + _rnd.NextDouble() * 2.0;
                return val.ToString("F2", CultureInfo.InvariantCulture);
            }
            if (t.Contains("int"))
            {
                int val = (int)(50 + Math.Sin(_step * 0.1) * 30 + _rnd.Next(-2, 3));
                return Math.Max(0, val).ToString();
            }
            if (t.Contains("string"))
            {
                return "OK";
            }

            return "1";
        }
    }
}