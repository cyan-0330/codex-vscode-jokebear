using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace CodexPet
{
    public static class Json
    {
        public static string Write(object value) { return new JavaScriptSerializer { MaxJsonLength = 2097152 }.Serialize(value); }
        public static Dictionary<string, object> Read(string value) { return new JavaScriptSerializer { MaxJsonLength = 2097152 }.Deserialize<Dictionary<string, object>>(value); }
        public static string Text(Dictionary<string, object> value, string key)
        { object result; return value != null && value.TryGetValue(key, out result) && result != null ? Convert.ToString(result) : ""; }
        public static Dictionary<string, object> Object(Dictionary<string, object> value, string key)
        { object result; return value != null && value.TryGetValue(key, out result) ? result as Dictionary<string, object> : null; }
        public static string Decision(string behavior)
        {
            if (behavior != "allow" && behavior != "deny") return "{}";
            var decision = new Dictionary<string, object> { { "behavior", behavior } };
            if (behavior == "deny") decision["message"] = "用户在 Codex 桌宠中拒绝了此次请求。";
            return Write(new { hookSpecificOutput = new { hookEventName = "PermissionRequest", decision = decision } });
        }
    }

    public sealed class Pending
    {
        public readonly Dictionary<string, object> Payload;
        public readonly DateTime Expires;
        private readonly object gate = new object();
        private string result;
        public Pending(Dictionary<string, object> payload, int timeoutMs) { Payload = payload; Expires = DateTime.UtcNow.AddMilliseconds(timeoutMs); }
        public bool Done { get { lock (gate) { return result != null; } } }
        public string Result { get { lock (gate) { return result ?? "{}"; } } }
        public bool Resolve(string behavior)
        {
            lock (gate)
            {
                if (result != null) return false;
                result = DateTime.UtcNow >= Expires ? "{}" : Json.Decision(behavior);
                return true;
            }
        }
    }

    // HTTP is bound to IPv4 loopback only. Each GUI launch gets a new capability token.
    public sealed class Bridge : IDisposable
    {
        private readonly TcpListener listener;
        private readonly Action<Pending> approval;
        private readonly Action<Dictionary<string, object>> state;
        private readonly List<Pending> pending = new List<Pending>();
        private readonly int timeoutMs;
        private volatile bool stopped;
        public readonly int Port;
        public readonly string Token;
        public Bridge(Action<Pending> approval, Action<Dictionary<string, object>> state, int timeoutMs)
        {
            this.approval = approval; this.state = state; this.timeoutMs = timeoutMs;
            byte[] secret = new byte[32]; using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(secret);
            Token = Convert.ToBase64String(secret);
            listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(32);
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var thread = new Thread(Accept) { IsBackground = true }; thread.Start();
        }
        private void Accept()
        {
            while (!stopped)
            {
                try { var client = listener.AcceptTcpClient(); ThreadPool.QueueUserWorkItem(delegate { Handle(client); }); }
                catch (SocketException) { if (stopped) return; }
                catch (ObjectDisposedException) { return; }
            }
        }
        private static string HeaderLine(NetworkStream stream)
        {
            var bytes = new List<byte>();
            while (bytes.Count < 8192)
            {
                int next = stream.ReadByte(); if (next < 0) throw new IOException("Incomplete header");
                if (next == 10) return Encoding.ASCII.GetString(bytes.ToArray()).TrimEnd('\r');
                bytes.Add((byte)next);
            }
            throw new IOException("Header too large");
        }
        private static void Respond(NetworkStream stream, string body, int code)
        {
            byte[] content = Encoding.UTF8.GetBytes(body);
            byte[] headers = Encoding.ASCII.GetBytes("HTTP/1.1 " + code + (code == 200 ? " OK" : " Error") + "\r\nContent-Type: application/json; charset=utf-8\r\nCache-Control: no-store\r\nConnection: close\r\nContent-Length: " + content.Length + "\r\n\r\n");
            stream.Write(headers, 0, headers.Length); stream.Write(content, 0, content.Length);
        }
        private void Handle(TcpClient client)
        {
            Pending request = null;
            using (client)
            {
                try
                {
                    client.ReceiveTimeout = 5000; client.SendTimeout = 5000;
                    var stream = client.GetStream(); string line = HeaderLine(stream);
                    string[] first = line.Split(' ');
                    int length = -1; string auth = "";
                    int total = 0;
                    while ((line = HeaderLine(stream)).Length > 0)
                    {
                        total += line.Length; if (total > 16384) return;
                        int colon = line.IndexOf(':'); if (colon < 0) return;
                        string key = line.Substring(0, colon); string value = line.Substring(colon + 1).Trim();
                        if (key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) { if (!int.TryParse(value, out length)) return; }
                        if (key.Equals("Authorization", StringComparison.OrdinalIgnoreCase)) auth = value;
                    }
                    if (first.Length != 3 || first[0] != "POST" || (first[1] != "/hook" && first[1] != "/ping")) { Respond(stream, "{}", 404); return; }
                    if (length < 0 || length > 2097152) { Respond(stream, "{}", 413); return; }
                    byte[] body = new byte[length]; int offset = 0;
                    while (offset < length) { int count = stream.Read(body, offset, length - offset); if (count == 0) return; offset += count; }
                    if (auth != "Bearer " + Token) { Respond(stream, "{}", 403); return; }
                    if (first[1] == "/ping") { Respond(stream, "{\"ok\":true}", 200); return; }
                    var payload = Json.Read(Encoding.UTF8.GetString(body));
                    if (Json.Text(payload, "hook_event_name") == "PermissionRequest")
                    {
                        request = new Pending(payload, timeoutMs);
                        lock (pending) { if (stopped) return; pending.Add(request); }
                        approval(request);
                        while (!request.Done)
                        {
                            if (stopped || DateTime.UtcNow >= request.Expires || (client.Client.Poll(0, SelectMode.SelectRead) && client.Client.Available == 0))
                            { request.Resolve(null); break; }
                            Thread.Sleep(80);
                        }
                        Respond(stream, request.Result, 200);
                    }
                    else
                    {
                        string ev = Json.Text(payload, "hook_event_name");
                        string sid = Json.Text(payload, "session_id");
                        if (sid.Length > 0 && (ev == "Interrupt" || ev == "SessionEnd" || ev == "Stop"))
                            lock (pending) foreach (var item in pending)
                                if (Json.Text(item.Payload, "session_id") == sid) item.Resolve(null);
                        state(payload); Respond(stream, "{}", 200);
                    }
                }
                catch (Exception ex) { if (!(ex is IOException || ex is SocketException || ex is ArgumentException || ex is InvalidOperationException || ex is ObjectDisposedException)) System.Diagnostics.Debug.WriteLine(ex.Message); }
                finally { if (request != null) { request.Resolve(null); lock (pending) pending.Remove(request); } }
            }
        }
        public void Dispose()
        {
            stopped = true; listener.Stop();
            lock (pending) foreach (var item in pending) item.Resolve(null);
        }
    }

    public static class HookClient
    {
        public static string Send(int port, string token, string payload, int timeoutMs, string path)
        {
            var request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + port + path);
            request.Proxy = null; request.Method = "POST"; request.KeepAlive = false;
            request.Timeout = timeoutMs; request.ReadWriteTimeout = timeoutMs;
            request.Headers["Authorization"] = "Bearer " + token;
            request.ContentType = "application/json; charset=utf-8";
            byte[] bytes = Encoding.UTF8.GetBytes(payload); request.ContentLength = bytes.Length;
            using (var stream = request.GetRequestStream()) stream.Write(bytes, 0, bytes.Length);
            using (var response = request.GetResponse())
            using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8)) return reader.ReadToEnd();
        }
        public static string Run(string input, string runtime)
        {
            try
            {
                var payload = Json.Read(input);
                bool isApproval = Json.Text(payload, "hook_event_name") == "PermissionRequest";
                var endpoint = Json.Read(File.ReadAllText(runtime, Encoding.UTF8));
                int port = Convert.ToInt32(endpoint["port"]);
                if (port < 1 || port > 65535) return "{}";
                string token = Json.Text(endpoint, "token"); if (token.Length < 32) return "{}";
                // Fast liveness check avoids a ten-minute delay when a stale port is reused.
                var ping = Json.Read(Send(port, token, "{}", 1500, "/ping"));
                if (Json.Text(ping, "ok") != "True") return "{}";
                string output = Send(port, token, input, isApproval ? 570000 : 1500, "/hook");
                if (!isApproval) return "{}";
                var result = Json.Object(Json.Read(output), "hookSpecificOutput");
                if (Json.Text(result, "hookEventName") != "PermissionRequest") return "{}";
                return Json.Decision(Json.Text(Json.Object(result, "decision"), "behavior"));
            }
            catch { return "{}"; }
        }
    }

    public sealed class ApprovalDialog : Form
    {
        public readonly Pending Request;
        private readonly Label timerLabel;
        private readonly System.Windows.Forms.Timer timer;
        private readonly Button allow;
        private readonly TableLayoutPanel layout;
        public int TailX = 410;
        public bool BelowPet;
        public Action ReturnToEditor;
        public ApprovalDialog(Pending request)
        {
            Request = request;
            Text = "Codex 桌宠 · 需要你审批"; Size = new Size(480, 370); FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual; TopMost = true; ShowInTaskbar = false; DoubleBuffered = true;
            BackColor = Color.FromArgb(253, 251, 245); Font = new Font("Microsoft YaHei UI", 9);
            layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20, 17, 20, 34), ColumnCount = 1, RowCount = 5 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 63));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
            Controls.Add(layout);
            var payload = request.Payload;
            string tool = Json.Text(payload, "tool_name");
            var input = Json.Object(payload, "tool_input");
            string reason = Json.Text(input, "description");
            if (reason.Length == 0) reason = Json.Text(payload, "reason");
            layout.Controls.Add(new Label { Text = "JokeBear 想请你确认  ·  " + tool, Font = new Font(Font.FontFamily, 12, FontStyle.Bold), Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = Color.FromArgb(87, 63, 44) });
            string sid = Json.Text(payload, "session_id");
            layout.Controls.Add(new Label { Text = "目录：" + Json.Text(payload, "cwd") + "\n会话：" + sid + "\n原因：" + (reason.Length == 0 ? "Codex 请求额外权限" : reason), Dock = DockStyle.Fill, AutoEllipsis = true });
            string command = Json.Text(input, "command");
            string details = command.Length > 0 ? command : Json.Write(payload.ContainsKey("tool_input") ? payload["tool_input"] : payload);
            details += "\r\n\r\n──────── 完整请求 ────────\r\n" + Json.Write(payload);
            layout.Controls.Add(new TextBox { Text = details, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, BackColor = Color.White, Font = new Font("Consolas", 10) });
            timerLabel = new Label { Dock = DockStyle.Fill, ForeColor = Color.DimGray }; layout.Controls.Add(timerLabel);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0) };
            allow = MakeButton("允许这一次", Color.FromArgb(134, 101, 65), Color.White, delegate { Finish("allow"); });
            buttons.Controls.Add(allow);
            buttons.Controls.Add(MakeButton("拒绝", Color.FromArgb(255, 227, 230), Color.FromArgb(151, 45, 62), delegate { Finish("deny"); }));
            var fallback = MakeButton("回 VS Code 处理", Color.White, Color.DimGray, delegate { Finish(null); if (ReturnToEditor != null) ReturnToEditor(); }); buttons.Controls.Add(fallback);
            layout.Controls.Add(buttons);
            // Enter deliberately has no default approval action.
            CancelButton = fallback;
            FormClosing += delegate { request.Resolve(null); };
            timer = new System.Windows.Forms.Timer { Interval = 250 }; timer.Tick += delegate
            {
                if (request.Done) { Close(); return; }
                int seconds = (int)Math.Ceiling((request.Expires - DateTime.UtcNow).TotalSeconds);
                if (seconds <= 0) { Finish(null); return; }
                timerLabel.Text = "等待你的决定 · " + seconds / 60 + ":" + (seconds % 60).ToString("00") + " 后交回 VS Code";
            };
            Shown += delegate { fallback.Focus(); timer.Start(); };
            FormClosed += delegate { timer.Dispose(); };
            Resize += delegate { UpdateShape(); }; UpdateShape();
        }
        public void UpdateShape()
        {
            layout.Padding = BelowPet ? new Padding(20, 34, 20, 17) : new Padding(20, 17, 20, 34);
            using (var shape = UiStyle.Round(new Rectangle(0, BelowPet ? 18 : 0, Width - 1, Height - 19), 20))
            {
                int x = Math.Max(28, Math.Min(TailX, Width - 28));
                int edge = BelowPet ? 20 : Height - 21;
                shape.AddPolygon(new[] { new Point(x - 12, edge), new Point(x + 12, edge), new Point(x, BelowPet ? 0 : Height - 1) });
                var old = Region; Region = new Region(shape); if (old != null) old.Dispose();
            }
        }
        private Button MakeButton(string text, Color background, Color foreground, Action clicked)
        {
            var button = new Button { Text = text, AutoSize = true, Height = 35, Padding = new Padding(8, 0, 8, 0), FlatStyle = FlatStyle.Flat, BackColor = background, ForeColor = foreground };
            button.FlatAppearance.BorderSize = 0; button.Click += delegate { clicked(); }; return button;
        }
        private void Finish(string decision) { allow.Enabled = false; Request.Resolve(decision); Close(); }
    }

    public sealed class PetForm : Form
    {
        private readonly Queue<Pending> waiting = new Queue<Pending>();
        private readonly Dictionary<string, string> sessions = new Dictionary<string, string>();
        private readonly Bridge bridge;
        private readonly string runtime;
        private readonly NotifyIcon tray;
        private readonly System.Windows.Forms.Timer animation;
        private readonly ToolTip tip = new ToolTip();
        private ApprovalDialog dialog;
        private string status = "等你开工";
        private bool ready;
        private int frame;
        private Point dragOrigin;
        private bool dragging;
        private DateTime lastHook;
        private readonly CompanionConfig companion;
        private readonly PetdexFeed feed;
        private readonly ProjectStore projects;
        private readonly ProjectPanel projectPanel;
        private DateTime nextProbe;
        private DateTime nextSeed;
        private bool seedBusy;
        private bool panelWanted = true;
        public PetForm()
        {
            companion = CompanionConfig.Load();
            if (companion.Enabled)
            {
                feed = new PetdexFeed(companion);
                projects = new ProjectStore(Path.Combine(companion.Root, "data", "progress.json"));
                projects.MergeSeed(Path.Combine(companion.Root, "data", "recent-projects-seed.json"));
                projectPanel = new ProjectPanel(projects, delegate(string sid) { VscodeTarget.Open(companion, sid); });
            }
            Text = "Codex 小猫"; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
            Size = new Size(184, 178); BackColor = Color.Magenta; TransparencyKey = Color.Magenta; DoubleBuffered = true;
            var area = Screen.PrimaryScreen.WorkingArea; Location = new Point(area.Right - Width - 35, area.Bottom - Height - 20);
            var menu = new ContextMenuStrip();
            menu.Items.Add("测试审批气泡（不会执行命令）", null, delegate { Demo(); });
            if (companion.Enabled) menu.Items.Add("显示 / 隐藏对话面板", null, delegate { panelWanted = !panelWanted; projectPanel.Visible = panelWanted && NativeAnchor.PetWindow != IntPtr.Zero; });
            if (companion.Enabled) menu.Items.Add("打开 VS Code Codex", null, delegate { VscodeTarget.Open(companion, ""); });
            menu.Items.Add("查看连接与会话", null, delegate { ShowStatus(); });
            menu.Items.Add("首次审阅和信任 hooks", null, delegate { System.Diagnostics.Process.Start(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "review-hooks.cmd")); });
            menu.Items.Add("使用说明", null, delegate { System.Diagnostics.Process.Start(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "README.md")); });
            if (!companion.Enabled) menu.Items.Add("显示 / 隐藏", null, delegate { Visible = !Visible; });
            menu.Items.Add("退出桌宠", null, delegate { Close(); }); ContextMenuStrip = menu;
            tray = new NotifyIcon { Icon = SystemIcons.Information, Text = companion.Enabled ? "JokeBear · 审批与对话状态" : "Codex 小猫 · 等待钩子连接", ContextMenuStrip = menu, Visible = true };
            tray.DoubleClick += delegate { Show(); Activate(); };
            tip.SetToolTip(this, "拖动移动 · 双击查看状态 · 右键菜单");
            MouseDown += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { dragging = true; dragOrigin = e.Location; } };
            MouseMove += delegate(object sender, MouseEventArgs e) { if (dragging) Location = new Point(Location.X + e.X - dragOrigin.X, Location.Y + e.Y - dragOrigin.Y); };
            MouseUp += delegate { dragging = false; }; DoubleClick += delegate { ShowStatus(); };
            // Create the GUI handle before worker threads can dispatch events.
            var handle = Handle;
            bridge = new Bridge(QueueApproval, OnState, 540000);
            runtime = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtime.json");
            File.WriteAllText(runtime, Json.Write(new { port = bridge.Port, token = bridge.Token, pid = System.Diagnostics.Process.GetCurrentProcess().Id }), new UTF8Encoding(false));
            ready = true;
            animation = new System.Windows.Forms.Timer { Interval = 140 }; animation.Tick += delegate { frame++; NextDialog(); if (companion.Enabled) FollowPetdex(); else Invalidate(); }; animation.Start();
            FormClosed += delegate
            {
                ready = false; animation.Dispose();
                if (dialog != null) { dialog.Request.Resolve(null); dialog.Close(); }
                foreach (var item in waiting) item.Resolve(null);
                bridge.Dispose(); tray.Dispose(); tip.Dispose();
                if (projectPanel != null) projectPanel.Close();
                if (feed != null) { NativeAnchor.HoldOriginalBubble(feed.Pid, false); NativeAnchor.RestorePetMenus(); }
                try { File.Delete(runtime); } catch { }
            };
        }
        protected override void SetVisibleCore(bool value) { base.SetVisibleCore(companion != null && companion.Enabled ? false : value); }
        private void FollowPetdex()
        {
            if (DateTime.UtcNow >= nextProbe) { nextProbe = DateTime.UtcNow.AddSeconds(3); ThreadPool.QueueUserWorkItem(delegate { feed.Probe(); }); }
            if (feed.Pid > 0 && NativeAnchor.Refresh(feed.Pid))
            {
                var pet = NativeAnchor.Bounds;
                NativeAnchor.HidePetMenu(feed.Pid);
                if (!projectPanel.IsDisposed)
                {
                    projectPanel.RefreshProjects(); if (!projectPanel.Visible && panelWanted) projectPanel.Show();
                    NativeAnchor.Attach(projectPanel); projectPanel.Location = NativeAnchor.PanelPosition(projectPanel.Size, pet);
                }
                if (dialog != null)
                {
                    NativeAnchor.Attach(dialog); dialog.Location = NativeAnchor.BalloonPosition(dialog.Size, pet);
                    int tail = pet.Left + pet.Width / 2 - dialog.Left; bool below = dialog.Top >= pet.Bottom;
                    if (tail != dialog.TailX || below != dialog.BelowPet) { dialog.TailX = tail; dialog.BelowPet = below; dialog.UpdateShape(); }
                }
                NativeAnchor.HoldOriginalBubble(feed.Pid, true);
            }
            else
            {
                projectPanel.Hide();
                if (dialog != null) { dialog.Request.Resolve(null); dialog.Close(); }
            }
            if (DateTime.UtcNow >= nextSeed && !seedBusy)
            {
                nextSeed = DateTime.UtcNow.AddSeconds(5); SeedProjects();
            }
        }
        private void SeedProjects()
        {
            string seed = Path.Combine(companion.Root, "data", "recent-projects-seed.json");
            if (!string.IsNullOrEmpty(companion.Python) && File.Exists(companion.Python))
            {
                seedBusy = true;
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try
                    {
                        string script = Path.Combine(companion.Root, "scripts", "seed-projects.py");
                        string codexHome = companion.CodexHome;
                        var start = new System.Diagnostics.ProcessStartInfo(companion.Python, "\"" + script + "\" --codex-dir \"" + codexHome + "\" --output \"" + seed + "\"") { UseShellExecute = false, CreateNoWindow = true };
                        using (var process = System.Diagnostics.Process.Start(start)) { if (!process.WaitForExit(5000)) process.Kill(); }
                        if (ready) BeginInvoke((Action)delegate {
                            projects.MergeSeed(seed);
                            if (dialog != null && !dialog.Request.Done) projects.Update(dialog.Request.Payload, "waiting");
                            else { var current = projects.Recent(ProjectStore.NowMs()); feed.Post(current.Exists(p => p.Status == "待审批") ? "waiting" : current.Exists(p => p.Status == "进行中") ? "running" : "idle"); }
                        });
                    }
                    catch { }
                    finally { seedBusy = false; }
                });
            }
            else projects.MergeSeed(seed);
        }
        private void QueueApproval(Pending request)
        {
            if (!ready) { request.Resolve(null); return; }
            try { BeginInvoke((Action)delegate { if (!ready || (companion.VscodeOnly && !projects.IsVscodeSession(request.Payload))) { request.Resolve(null); return; } lastHook = DateTime.Now; waiting.Enqueue(request); NextDialog(); }); }
            catch (InvalidOperationException) { request.Resolve(null); }
        }
        private void NextDialog()
        {
            if (dialog != null) return;
            while (waiting.Count > 0)
            {
                var next = waiting.Dequeue(); if (next.Done) continue;
                if (companion.Enabled && (feed.Pid <= 0 || !NativeAnchor.Refresh(feed.Pid))) { next.Resolve(null); continue; }
                status = "请你审批";
                if (projects != null) { projects.Update(next.Payload, "waiting"); feed.Post("waiting"); }
                dialog = new ApprovalDialog(next);
                if (companion.Enabled) dialog.ReturnToEditor = delegate { VscodeTarget.Open(companion, Json.Text(next.Payload, "session_id")); };
                Rectangle pet = companion.Enabled && NativeAnchor.PetWindow != IntPtr.Zero ? NativeAnchor.Bounds : Bounds;
                dialog.Location = NativeAnchor.BalloonPosition(dialog.Size, pet);
                dialog.TailX = pet.Left + pet.Width / 2 - dialog.Left; dialog.BelowPet = dialog.Top >= pet.Bottom; dialog.UpdateShape();
                if (companion.Enabled) { NativeAnchor.Attach(dialog); NativeAnchor.HoldOriginalBubble(feed.Pid, true); }
                dialog.FormClosed += delegate
                {
                    if (projects != null)
                    {
                        string behavior = Json.Text(Json.Object(Json.Object(Json.Read(next.Result), "hookSpecificOutput"), "decision"), "behavior");
                        projects.ApprovalResolved(next.Payload, behavior);
                        if (!projects.Data.Projects.Exists(p => p.Tasks.Exists(t => t.SessionId == Json.Text(next.Payload, "session_id") && (t.State == "completed" || t.State == "interrupted"))))
                            feed.Post(behavior == "deny" ? "idle" : behavior == "allow" ? "running" : "waiting");
                        NativeAnchor.HoldOriginalBubble(feed.Pid, true);
                    }
                    dialog = null; status = "等待后续消息"; tray.Text = companion.Enabled ? "JokeBear · 审批与对话状态" : "Codex 小猫 · 等待后续消息";
                };
                tray.Text = companion.Enabled ? "JokeBear · 等待审批" : "Codex 小猫 · 等待审批";
                dialog.Show(); dialog.Activate(); System.Media.SystemSounds.Exclamation.Play(); break;
            }
        }
        private void OnState(Dictionary<string, object> payload)
        {
            if (!ready) return;
            try { BeginInvoke((Action)delegate
            {
                lastHook = DateTime.Now;
                if (companion.VscodeOnly && !projects.IsVscodeSession(payload)) return;
                string ev = Json.Text(payload, "hook_event_name"); string sid = Json.Text(payload, "session_id");
                string message = ev == "Stop" ? "完成啦" : ev == "Interrupt" ? "已暂停" : ev == "SessionEnd" ? "会话结束" : ev == "SessionStart" ? "已连接" : ev == "UserPromptSubmit" ? "思考中" : "工作中";
                if (sid.Length > 0) { if (sessions.Count >= 100 && !sessions.ContainsKey(sid)) sessions.Clear(); sessions[sid] = message; }
                if (projects != null) { projects.Update(payload, null); if (dialog == null) feed.Post(ev == "Stop" ? "waving" : ev == "Interrupt" || ev == "SessionEnd" ? "idle" : "running"); }
                if (dialog == null) status = message;
                tray.Text = (companion.Enabled ? "JokeBear · " : "Codex 小猫 · ") + status;
            }); } catch (InvalidOperationException) { }
        }
        private void ShowStatus()
        {
            if (companion.Enabled)
            {
                var vscodeText = new StringBuilder("接入：VS Code Codex 扩展 " + companion.ExtensionVersion + "\n会话目录：" + companion.CodexHome + "\n对话状态：每条 Codex 对话单独显示，每 5 秒刷新\n审批：" + (lastHook == default(DateTime) ? "接收通道就绪，尚未收到真实消息" : "最近收到消息 " + lastHook.ToString("HH:mm:ss")) + "\n\n");
                foreach (var entry in sessions) vscodeText.AppendLine(entry.Key + "  " + entry.Value);
                MessageBox.Show(vscodeText.ToString(), "JokeBear · VS Code 连接", MessageBoxButtons.OK, MessageBoxIcon.Information); return;
            }
            var text = new StringBuilder("桌宠正在运行，接收通道已就绪。\n最近 Codex 消息：" + (lastHook == default(DateTime)
                ? "尚未收到\n\n等待 Codex 发来任务或审批消息。\n仅启用审批处理器时，小猫会在需要审批时收到消息；\n要显示工作状态，还需信任并启用其余状态处理器。"
                : lastHook.ToString("HH:mm:ss")) + "\n\n");
            foreach (var entry in sessions) text.AppendLine(entry.Key + "  " + entry.Value);
            MessageBox.Show(text.ToString(), "Codex 小猫 · 连接状态", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        private void Demo()
        {
            QueueApproval(new Pending(new Dictionary<string, object> {
                { "hook_event_name", "PermissionRequest" }, { "session_id", "演示 · 不会执行任何操作" }, { "cwd", Environment.CurrentDirectory }, { "tool_name", "Bash" },
                { "tool_input", new Dictionary<string, object> { { "command", "git status" }, { "description", "测试审批界面，不会执行显示的命令。" } } }
            }, 540000));
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            int bob = (int)(Math.Sin(frame / 6.0) * 3);
            using (var shadow = new SolidBrush(Color.FromArgb(198, 192, 215))) g.FillEllipse(shadow, 46, 148, 97, 12);
            using (var bubble = new SolidBrush(Color.FromArgb(250, 248, 255)))
            using (var ink = new SolidBrush(Color.FromArgb(65, 47, 96)))
            using (var fur = new SolidBrush(Color.FromArgb(186, 165, 236)))
            using (var line = new Pen(Color.FromArgb(65, 47, 96), 3))
            using (var pink = new SolidBrush(Color.FromArgb(245, 177, 201)))
            {
                g.FillRoundedRectangle(bubble, new Rectangle(10, 6, 165, 35), 12);
                using (var font = new Font("Microsoft YaHei UI", 10, FontStyle.Bold))
                using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(status + (waiting.Count > 0 ? " +" + waiting.Count : ""), font, ink, new RectangleF(10, 6, 165, 35), format);
                g.FillEllipse(fur, 58, 102 + bob, 70, 49); g.DrawEllipse(line, 58, 102 + bob, 70, 49);
                using (var tail = new Pen(Color.FromArgb(186, 165, 236), 10)) g.DrawArc(tail, 121, 103 + bob, 30, 32, 275, 185);
                Point[] left = { new Point(52, 82 + bob), new Point(51, 49 + bob), new Point(81, 69 + bob) };
                Point[] right = { new Point(106, 68 + bob), new Point(134, 49 + bob), new Point(137, 85 + bob) };
                g.FillPolygon(fur, left); g.DrawPolygon(line, left); g.FillPolygon(fur, right); g.DrawPolygon(line, right);
                g.FillEllipse(fur, 47, 66 + bob, 94, 67); g.DrawEllipse(line, 47, 66 + bob, 94, 67);
                g.FillEllipse(pink, 58, 103 + bob, 18, 8); g.FillEllipse(pink, 113, 103 + bob, 18, 8);
                bool blink = frame % 48 >= 45;
                if (blink) { g.DrawLine(line, 70, 95 + bob, 82, 95 + bob); g.DrawLine(line, 106, 95 + bob, 118, 95 + bob); }
                else { g.FillEllipse(ink, 74, 89 + bob, 7, 12); g.FillEllipse(ink, 108, 89 + bob, 7, 12); }
                g.FillEllipse(pink, 90, 103 + bob, 9, 6); g.DrawArc(line, 82, 104 + bob, 13, 11, 0, 150); g.DrawArc(line, 94, 104 + bob, 13, 11, 30, 150);
                if (dialog != null) using (var alert = new SolidBrush(Color.FromArgb(247, 157, 48))) g.FillEllipse(alert, 135, 58 + bob, 18, 18);
            }
        }
    }
    public static class Drawing
    {
        public static void FillRoundedRectangle(this Graphics g, Brush brush, Rectangle r, int radius)
        {
            using (var path = new GraphicsPath())
            { int d = radius * 2; path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90); path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90); path.CloseFigure(); g.FillPath(brush, path); }
        }
    }
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--hook")
            {
                Console.InputEncoding = new UTF8Encoding(false); Console.OutputEncoding = new UTF8Encoding(false);
                string result = HookClient.Run(Console.In.ReadToEnd(), Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtime.json"));
                Console.WriteLine(result); return 0;
            }
            if (args.Length > 0 && args[0] == "--ping")
            {
                try { var endpoint = Json.Read(File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtime.json"))); Console.WriteLine(HookClient.Send(Convert.ToInt32(endpoint["port"]), Json.Text(endpoint, "token"), "{}", 1500, "/ping")); return 0; }
                catch { Console.Error.WriteLine("桌宠未连接"); return 1; }
            }
            using (var mutex = new Mutex(false, "Local\\CodexDesktopPet-" + Environment.UserName))
            {
                bool acquired; try { acquired = mutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) return 0;
                try { Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Application.Run(new PetForm()); }
                catch (Exception ex) { MessageBox.Show(ex.Message, "Codex 桌宠启动失败"); return 1; }
                finally { mutex.ReleaseMutex(); }
            }
            return 0;
        }
    }
}
