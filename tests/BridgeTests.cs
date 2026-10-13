using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CodexPet;

public static class BridgeTests
{
    private static int passed;
    private static string Approval(string session)
    { return Json.Write(new { hook_event_name = "PermissionRequest", session_id = session, tool_name = "Bash", tool_input = new { command = "TEST ONLY - NEVER EXECUTED" } }); }
    private static void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); passed++; Console.WriteLine("PASS: " + name); }
    private static string Behavior(string json) { return Json.Text(Json.Object(Json.Object(Json.Read(json), "hookSpecificOutput"), "decision"), "behavior"); }
    private static string Send(Bridge bridge, string payload) { return HookClient.Send(bridge.Port, bridge.Token, payload, 6000, "/hook"); }
    private static void Await(Func<bool> done) { for (int i = 0; i < 80; i++) { if (done()) return; Thread.Sleep(25); } throw new Exception("Wait failed"); }
    public static int Main()
    {
        try
        {
            int received = 0;
            using (var bridge = new Bridge(delegate(Pending p) { Interlocked.Increment(ref received); p.Resolve(Json.Text(p.Payload, "session_id") == "allow" ? "allow" : "deny"); }, delegate { }, 3000))
            {
                Check(Behavior(Send(bridge, Approval("allow"))) == "allow", "explicit allow returns official hook payload");
                Check(Behavior(Send(bridge, Approval("deny"))) == "deny", "explicit deny returns official hook payload");
                var tasks = new List<Task<string>>();
                for (int i = 0; i < 12; i++) { string sid = i % 2 == 0 ? "allow" : "deny"; tasks.Add(Task.Factory.StartNew(delegate { return Send(bridge, Approval(sid)); })); }
                Task.WaitAll(tasks.ToArray()); bool isolated = true;
                for (int i = 0; i < tasks.Count; i++) isolated &= Behavior(tasks[i].Result) == (i % 2 == 0 ? "allow" : "deny");
                Check(isolated, "12 concurrent requests keep their decisions isolated");
                int before = received; bool rejected = false;
                try { HookClient.Send(bridge.Port, "incorrect-token", Approval("allow"), 1500, "/hook"); }
                catch (WebException ex) { var response = ex.Response as HttpWebResponse; rejected = response != null && response.StatusCode == HttpStatusCode.Forbidden; if (!rejected) Console.WriteLine(ex.ToString()); }
                Check(rejected && received == before, "wrong capability rejected before reaching approval UI");
                string runtime = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtime-test.json");
                File.WriteAllText(runtime, Json.Write(new { port = bridge.Port, token = bridge.Token }));
                try
                {
                    Check(Behavior(HookClient.Run(Approval("allow"), runtime)) == "allow", "hook client verifies liveness and carries a real decision");
                    Check(HookClient.Run("not json", runtime) == "{}", "malformed input falls back without approval");
                    File.WriteAllText(runtime, Json.Write(new { port = bridge.Port, token = new string('x', 44) }));
                    Check(HookClient.Run(Approval("allow"), runtime) == "{}", "stale runtime token falls back without approval");
                }
                finally { File.Delete(runtime); }
                Check(Send(bridge, Json.Write(new { hook_event_name = "PreToolUse", session_id = "allow" })) == "{}" && received == before + 1, "state hooks never grant permission");
            }
            using (var bridge = new Bridge(delegate { }, delegate { }, 180))
                Check(Send(bridge, Approval("timeout")) == "{}", "unanswered timeout falls back without approval");
            Pending held = null;
            using (var bridge = new Bridge(delegate(Pending p) { held = p; }, delegate { }, 3000))
            {
                var task = Task.Factory.StartNew(delegate { return Send(bridge, Approval("cancelled-session")); });
                Await(delegate { return held != null; });
                Send(bridge, Json.Write(new { hook_event_name = "Interrupt", session_id = "another-session" }));
                Check(!held.Done, "other session interruption cannot cancel this approval");
                Send(bridge, Json.Write(new { hook_event_name = "Interrupt", session_id = "cancelled-session" }));
                Check(task.Result == "{}" && !held.Resolve("allow"), "interrupted session discards approval and rejects late clicks");
            }
            held = null;
            using (var bridge = new Bridge(delegate(Pending p) { held = p; }, delegate { }, 3000))
            {
                var socket = new TcpClient(); socket.Connect(IPAddress.Loopback, bridge.Port);
                var payload = Encoding.UTF8.GetBytes(Approval("disconnect"));
                var header = Encoding.ASCII.GetBytes("POST /hook HTTP/1.1\r\nAuthorization: Bearer " + bridge.Token + "\r\nContent-Length: " + payload.Length + "\r\n\r\n");
                var stream = socket.GetStream(); stream.Write(header, 0, header.Length); stream.Write(payload, 0, payload.Length);
                Await(delegate { return held != null; }); socket.Close(); Await(delegate { return held.Done; });
                Check(held.Result == "{}" && !held.Resolve("allow"), "disconnected hook discards pending approval");
            }
            held = null;
            var shutdown = new Bridge(delegate(Pending p) { held = p; }, delegate { }, 3000);
            var shutdownTask = Task.Factory.StartNew(delegate { return Send(shutdown, Approval("shutdown")); });
            Await(delegate { return held != null; }); shutdown.Dispose();
            Check(shutdownTask.Result == "{}", "closing desktop pet releases pending hooks without approval");
            Check(HookClient.Run(Approval("offline"), "missing-runtime-file.json") == "{}", "pet not running falls back without approval");
            Console.WriteLine(passed + " transport checks passed."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.ToString()); return 1; }
    }
}
