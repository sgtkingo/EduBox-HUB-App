using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;

namespace NewGUI
{
    public static class VscpProtocol
    {
        public const string ApiVersion = "1.7";
        public const string LibraryVersion = "2.3.0";
        public const string ByeRequest = "?type=BYE&side=client";
        public static bool IsBye(string line, string expectedSide)
        {
            if (string.IsNullOrWhiteSpace(line) || !line.Trim().StartsWith("?")) return false;
            var fields = SerialParser.ParseQuery(line.Trim());
            return fields.TryGetValue("type", out var type) && type.Equals("BYE", StringComparison.OrdinalIgnoreCase) &&
                fields.TryGetValue("side", out var side) && side == expectedSide && !fields.ContainsKey("status");
        }
        public const string InitRequest = "?type=INIT&api=" + ApiVersion + "&hold=0";
        public static string BuildInitRequest() => InitRequest;
        public static bool TryReadPing(string line, out string side, out uint sequence)
        {
            side = null;
            sequence = 0;
            if (string.IsNullOrWhiteSpace(line) || !line.TrimStart().StartsWith("?")) return false;
            var fields = SerialParser.ParseQuery(line.Trim());
            bool typed = fields.TryGetValue("type", out var type);
            if (typed ? !type.Equals("PING", StringComparison.OrdinalIgnoreCase) || fields.ContainsKey("status")
                      : !fields.ContainsKey("status") || !fields.ContainsKey("seq")) return false;
            fields.TryGetValue("side", out side);
            return fields.TryGetValue("seq", out var seq) &&
                uint.TryParse(seq, NumberStyles.None, CultureInfo.InvariantCulture, out sequence) && sequence != 0 &&
                seq == sequence.ToString(CultureInfo.InvariantCulture) &&
                (side == null || side == "client" || side == "server");
        }
        public static string PingFrame(string side, uint sequence, bool response = false)
        {
            return (response ? "?" : "?type=PING&") + (side == null ? "" : "side=" + side + "&") +
                "seq=" + sequence.ToString(CultureInfo.InvariantCulture) +
                (response ? "&status=1" : string.Empty);
        }
    }

    public sealed class VscpPingEndpoint
    {
        private readonly object _sync = new object();
        private uint _sequence;
        private TaskCompletionSource<bool> _pending;
        private long _deadline;
        public async Task<bool> PingAsync(Action<string> send, int timeoutMs = 500)
        {
            if (timeoutMs <= 0) throw new ArgumentOutOfRangeException(nameof(timeoutMs));
            TaskCompletionSource<bool> pending;
            uint sequence;
            lock (_sync)
            {
                if (_pending != null) throw new InvalidOperationException("A PING is already pending.");
                sequence = _sequence = _sequence == uint.MaxValue ? 1 : _sequence + 1;
                pending = _pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _deadline = Stopwatch.GetTimestamp() + (long)(timeoutMs * (double)Stopwatch.Frequency / 1000);
            }
            try
            {
                send(VscpProtocol.PingFrame(null, sequence));
                var completed = await Task.WhenAny(pending.Task, Task.Delay(timeoutMs)).ConfigureAwait(false);
                return completed == pending.Task && await pending.Task.ConfigureAwait(false);
            }
            finally { lock (_sync) { if (_pending == pending) _pending = null; } }
        }
        public bool HandleFrame(string line, Action<string> send)
        {
            var fields = SerialParser.ParseQuery(line);
            bool typed = fields.TryGetValue("type", out var type);
            bool request = typed && type.Equals("PING", StringComparison.OrdinalIgnoreCase);
            bool sidedResponse = !typed && fields.ContainsKey("side") && fields.ContainsKey("seq") && fields.ContainsKey("status");
            bool boardResponse = false;
            if (!typed && fields.Count == 2 && fields.ContainsKey("seq") && fields.ContainsKey("status") &&
                VscpProtocol.TryReadPing(line, out _, out var responseSequence))
            {
                lock (_sync) boardResponse = _pending != null && responseSequence == _sequence;
            }
            bool response = sidedResponse || boardResponse;
            if (!request && !response) return false;
            // Keep every PING frame out of ordinary device transactions.
            if (!VscpProtocol.TryReadPing(line, out var side, out var sequence) || side == "client") return true;
            if (request) send(VscpProtocol.PingFrame(side == null ? null : "client", sequence, true));
            else if (fields.TryGetValue("status", out var status) && status == "1")
            {
                lock (_sync)
                {
                    if (_pending != null && sequence == _sequence && Stopwatch.GetTimestamp() < _deadline)
                        _pending.TrySetResult(true);
                }
            }
            return true;
        }
        public void Reset()
        {
            lock (_sync) { _pending?.TrySetResult(false); _pending = null; }
        }
    }
}
