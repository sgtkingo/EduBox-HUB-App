using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NewGUI;

internal static class VscpProtocolTests
{
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
    }

    private static async Task Run()
    {
        var endpoint = new VscpPingEndpoint();
        var sent = new List<string>();
        Check(endpoint.HandleFrame("?seq=42&side=server&type=ping", sent.Add), "Reordered request");
        Check(sent.Count == 1 && sent[0] == "?type=PING&side=client&seq=42&status=1", "Server-initiated PING");
        foreach (var frame in new[] {
            "?type=PING&side=server&seq=0", "?type=PING&side=server&seq=01",
            "?type=PING&side=server&seq=4294967296", "?type=PING&side=server&seq=-1",
            "?type=PING&side=server&seq=1.0", "?type=PING&side=client&seq=1",
            "?type=PING&seq=1", "?type=PING&side=server&seq=1&status=1",
            "?type=PING&side=server&seq=1&status=0", "?type=PING&side=server&seq=1&status=" })
            Check(endpoint.HandleFrame(frame, sent.Add), "Consume invalid or unsolicited frame");
        Check(sent.Count == 1, "Never answer invalid requests or responses");
        Check(!endpoint.HandleFrame("?id=S01&status=1&temperature=23.5", sent.Add), "Ordinary data passthrough");

        var ping = endpoint.PingAsync(sent.Add, 500);
        var request = SerialParser.ParseQuery(sent[sent.Count - 1]);
        var seq = request["seq"];
        Check(!ping.IsCompleted, "Wait for acknowledgment");
        endpoint.HandleFrame("?type=PING&side=client&seq=" + seq + "&status=1", sent.Add);
        endpoint.HandleFrame("?type=PING&side=server&seq=999&status=1", sent.Add);
        endpoint.HandleFrame("?type=PING&side=server&seq=" + seq + "&status=0", sent.Add);
        Check(!ping.IsCompleted, "Ignore wrong role, sequence and failure");
        endpoint.HandleFrame("?type=PING&side=server&seq=77", sent.Add);
        Check(!ping.IsCompleted && sent[sent.Count - 1].Contains("seq=77&status=1"), "Service simultaneous request");
        endpoint.HandleFrame("?status=1&seq=" + seq + "&side=server&type=PING", sent.Add);
        Check(await ping, "Matching acknowledgment");
        Check(!await endpoint.PingAsync(sent.Add, 20), "Timeout");
        var expiredSeq = SerialParser.ParseQuery(sent[sent.Count - 1])["seq"];
        ping = endpoint.PingAsync(sent.Add, 500);
        endpoint.HandleFrame("?type=PING&side=server&seq=" + expiredSeq + "&status=1", sent.Add);
        Check(!ping.IsCompleted, "Late acknowledgment cannot satisfy next probe");
        endpoint.Reset();
        Check(!await ping, "Close cancels pending probe");
        try { await endpoint.PingAsync(_ => { throw new Exception("write failure"); }); }
        catch (Exception e) { Check(e.Message == "write failure", "Propagate write failure"); }
        ping = endpoint.PingAsync(sent.Add, 500);
        endpoint.Reset();
        Check(!await ping, "Write failure releases pending slot");

        Check(RequestBuilder.BuildRequest("INIT", null, null, null, null, null, null) == "?type=INIT&api=1.5", "INIT version");
        var simulator = new VirtualDeviceSimulator();
        Check(simulator.ProcessCommand("?seq=4294967295&type=PING&side=client") ==
            "?type=PING&side=server&seq=4294967295&status=1", "Simulator PING before INIT");
        Check(simulator.ProcessCommand("?type=PING&side=client&seq=1&status=1") == "", "Simulator ignores responses");
        Check(simulator.ProcessCommand("?type=INIT&api=1.4").Contains("status=0"), "Simulator rejects obsolete API");
        Check(simulator.ProcessCommand(VscpProtocol.InitRequest).Contains("api=1.5&status=1"), "Simulator INIT 1.5");
        var manager = new SerialManager();
        manager.ConfigurePort("SIMULATOR");
        manager.Open();
        var dataCount = 0;
        manager.LinesReceived += (sender, args) => dataCount += args.Lines.Length;
        Check(await manager.PingAsync(), "Simulated serial round trip");
        Check(dataCount == 0, "PING does not reach data listeners");
        manager.Close();
        Console.WriteLine("VSCP 1.5 protocol checks passed.");
    }

    public static int Main()
    {
        try { Run().GetAwaiter().GetResult(); return 0; }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
