using System;
using System.Collections.Generic;
using System.Linq;

namespace NewGUI
{
    public static class RequestBuilder
    {
        public static string BuildRequest(string mode, string sensorLabel, IDictionary<string, string> sensorIdMap, Komponenty item, string pin1, string pin2, string pin3)
        {
            if (string.IsNullOrWhiteSpace(mode)) return null;
            string m = mode.Trim();

            string formattedId = null;
            if (!string.IsNullOrWhiteSpace(sensorLabel))
            {
                string sensorId;
                if (!sensorIdMap.TryGetValue(sensorLabel.Trim(), out sensorId) || string.IsNullOrWhiteSpace(sensorId))
                    sensorId = sensorLabel.Trim();
                formattedId = FormatSensorId(sensorId);
            }

            if (m.Equals("INIT", StringComparison.OrdinalIgnoreCase))
            {
                return VscpProtocol.InitRequest;
            }

            if (m.Equals("CONNECT", StringComparison.OrdinalIgnoreCase) || m.Equals("DISCONNECT", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(formattedId)) return null;
                var pinExpr = BuildPinExpr(item, pin1, pin2);
                if (string.IsNullOrWhiteSpace(pinExpr))
                {
                    bool needTwo = item != null && !string.IsNullOrWhiteSpace(item.PIN2);
                    string keyWhenEmpty = needTwo ? "pins" : "pin";
                    return $"?type={m}&id={formattedId}&{keyWhenEmpty}=";
                }
                bool multiple = pinExpr.Contains(",");
                string key = multiple ? "pins" : "pin";
                return $"?type={m}&id={formattedId}&{key}={pinExpr}";
            }

            if (m.Equals("CONFIG", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(formattedId)) return null;
                var cfgQuery = BuildConfigQuery(item, pin1, pin2, pin3);
                return string.IsNullOrEmpty(cfgQuery) ? $"?type={m}&id={formattedId}" : $"?type={m}&id={formattedId}&{cfgQuery}";
            }

            if (m.Equals("RESET", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(formattedId)) return null;
                return $"?type={m}&id={formattedId}";
            }

            // default for other modes
            if (!string.IsNullOrWhiteSpace(formattedId))
                return $"?type={m}&id={formattedId}";

            return null;
        }

        public static string BuildConfigQuery(Komponenty item, string p1, string p2, string p3)
        {
            if (item == null) return string.Empty;
            var cfgs = GetConfigNames(item);
            var values = new[] { p1?.Trim(), p2?.Trim(), p3?.Trim() };

            var parts = new List<string>();
            for (int i = 0; i < Math.Min(3, cfgs.Count); i++)
            {
                string key = ConfigKey(cfgs[i]);
                string val = values[i];

                if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(val))
                {
                    parts.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(val)}");
                }
            }

            return string.Join("&", parts);
        }

        private static string BuildPinExpr(Komponenty item, string pin1, string pin2)
        {
            var p1 = NormalizePinInput(pin1);
            var hasSecond = item != null && !string.IsNullOrWhiteSpace(item.PIN2);
            var p2 = NormalizePinInput(pin2);

            if (hasSecond)
            {
                if (string.IsNullOrWhiteSpace(p1) || string.IsNullOrWhiteSpace(p2))
                    return null;
                return $"{p1},{p2}";
            }
            else
            {
                if (string.IsNullOrWhiteSpace(p1)) return null;
                return p1;
            }
        }

        public static string NormalizePinInput(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            input = input.Trim();
            var digits = new string(input.Where(char.IsDigit).ToArray());
            return string.IsNullOrEmpty(digits) ? input : digits;
        }

        public static List<string> GetConfigNames(Komponenty item)
        {
            var result = new List<string>();
            if (item == null) return result;

            var propArr = item.GetType().GetProperty("Configs");
            if (propArr != null)
            {
                var val = propArr.GetValue(item) as System.Collections.IEnumerable;
                if (val != null)
                {
                    foreach (var it in val)
                    {
                        var s = (it ?? "").ToString().Trim();
                        if (!string.IsNullOrWhiteSpace(s)) result.Add(s);
                    }
                }
                if (result.Count > 0) return result;
            }

            string[] names = { "Config1", "Config2", "Config3", "CONFIG1", "CONFIG2", "CONFIG3" };
            foreach (var n in names)
            {
                var p = item.GetType().GetProperty(n);
                if (p != null)
                {
                    var s = (p.GetValue(item)?.ToString() ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(s)) result.Add(s);
                }
            }
            return result;
        }

        private static string CleanConfigLabel(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw;
            var s = raw.Trim();
            int colon = s.IndexOf(':');
            if (colon >= 0) s = s.Substring(0, colon);
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\s*\(.*?\)\s*$", "");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ");
            return s + ":";
        }

        private static string ConfigKey(string raw)
        {
            var s = CleanConfigLabel(raw);
            return s?.TrimEnd(':').Trim();
        }

        private static string FormatSensorId(string rawId)
        {
            if (string.IsNullOrWhiteSpace(rawId)) return rawId;

            string t = rawId.Trim();

            if (t.StartsWith("S", StringComparison.OrdinalIgnoreCase))
                return "S" + t.Substring(1);

            if (int.TryParse(t, out int n) && n >= 0)
                return "S" + n.ToString("D2");

            var digits = new string(t.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out n) && n >= 0)
                return "S" + n.ToString("D2");

            return "S" + t;
        }
    }
}
