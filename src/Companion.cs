using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace CodexPet
{
    public sealed class CompanionConfig
    {
        public bool Enabled;
        public string Root;
        public string PetdexHome;
        public string Python;
        public string CodexHome;
        public string CodeExecutable;
        public string ExtensionVersion;
        public bool VscodeOnly;
        public bool ThreadRouteSupported;
        public static CompanionConfig Load()
        {
            var root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
            var result = new CompanionConfig { Root = root, PetdexHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".petdex"), CodexHome = Environment.GetEnvironmentVariable("CODEX_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex") };
            try
            {
                var json = Json.Read(File.ReadAllText(Path.Combine(root, "petdex-integration.json"), Encoding.UTF8));
                result.Enabled = Json.Text(json, "enabled") == "True";
                if (Json.Text(json, "petdexHome").Length > 0) result.PetdexHome = Json.Text(json, "petdexHome");
                result.Python = Json.Text(json, "python");
                result.VscodeOnly = Json.Text(json, "vscodeOnly") == "True";
                if (Json.Text(json, "codexHome").Length > 0) result.CodexHome = Json.Text(json, "codexHome");
                result.CodeExecutable = Json.Text(json, "codeExecutable");
                result.ExtensionVersion = Json.Text(json, "extensionVersion");
                result.ThreadRouteSupported = Json.Text(json, "threadRouteSupported") == "True";
            }
            catch { }
            return result;
        }
    }

    public static class VscodeTarget
    {
        public static string SessionUri(string sid)
        {
            Guid id;
            return Guid.TryParse(sid, out id) ? "vscode://openai.chatgpt/local/" + id.ToString() : "vscode://openai.chatgpt/";
        }
        public static ProcessStartInfo EditorStart(string executable, string uri)
        {
            var start = new ProcessStartInfo(executable, "--open-url -- \"" + uri + "\"") { UseShellExecute = false };
            // The extension host inherits the CLI flag; its editor child needs
            // the normal editor entry point rather than Electron's Node mode.
            start.EnvironmentVariables.Remove("ELECTRON_RUN_AS_NODE");
            return start;
        }
        public static void Open(CompanionConfig config, string sid)
        {
            if (sid.StartsWith("[SELFTEST]") || sid.StartsWith("演示")) return;
            try
            {
                string uri = SessionUri(config.ThreadRouteSupported ? sid : "");
                if (!string.IsNullOrEmpty(config.CodeExecutable) && File.Exists(config.CodeExecutable))
                {
                    Process.Start(EditorStart(config.CodeExecutable, uri));
                }
                else Process.Start(uri);
            }
            catch { MessageBox.Show("未能打开 VS Code，请从编辑器中打开 Codex 侧栏。", "JokeBear", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        }
    }

    public static class NativeAnchor
    {
        public struct Rect { public int Left, Top, Right, Bottom; }
        private delegate bool Callback(IntPtr window, IntPtr data);
        [DllImport("user32.dll")] private static extern bool EnumWindows(Callback callback, IntPtr data);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int count);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
        [DllImport("user32.dll")] private static extern IntPtr GetMenu(IntPtr window);
        [DllImport("user32.dll")] private static extern bool SetMenu(IntPtr window, IntPtr menu);
        [DllImport("user32.dll")] private static extern bool DrawMenuBar(IntPtr window);
        public static IntPtr PetWindow;
        public static Rectangle Bounds;
        private static readonly List<IntPtr> hiddenBubbles = new List<IntPtr>();
        private static readonly Dictionary<IntPtr, IntPtr> petMenus = new Dictionary<IntPtr, IntPtr>();
        private static readonly Dictionary<IntPtr, int> petMenuPids = new Dictionary<IntPtr, int>();
        public static void HidePetMenu(int pid)
        {
            if (PetWindow == IntPtr.Zero) return;
            IntPtr menu = GetMenu(PetWindow); if (menu == IntPtr.Zero) return;
            if (SetMenu(PetWindow, IntPtr.Zero)) { petMenus[PetWindow] = menu; petMenuPids[PetWindow] = pid; DrawMenuBar(PetWindow); }
        }
        public static void RestorePetMenus()
        {
            foreach (var item in petMenus) { uint pid; GetWindowThreadProcessId(item.Key, out pid); if (pid == petMenuPids[item.Key]) { SetMenu(item.Key, item.Value); DrawMenuBar(item.Key); } }
            petMenus.Clear(); petMenuPids.Clear();
        }
        public static bool Refresh(int pid)
        {
            IntPtr found = IntPtr.Zero; Rectangle bounds = Rectangle.Empty;
            EnumWindows(delegate(IntPtr window, IntPtr ignored)
            {
                uint id; GetWindowThreadProcessId(window, out id);
                if (id != pid || !IsWindowVisible(window)) return true;
                var title = new StringBuilder(256); GetWindowText(window, title, title.Capacity);
                if (title.ToString() == "Petdex")
                {
                    Rect r; if (GetWindowRect(window, out r)) { found = window; bounds = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom); }
                }
                return true;
            }, IntPtr.Zero);
            PetWindow = found; Bounds = bounds; return found != IntPtr.Zero;
        }
        public static void Attach(Form form) { if (PetWindow != IntPtr.Zero) SetWindowLongPtr(form.Handle, -8, PetWindow); }
        public static void HoldOriginalBubble(int pid, bool hold)
        {
            if (!hold) { foreach (var window in hiddenBubbles) ShowWindow(window, 4); hiddenBubbles.Clear(); return; }
            EnumWindows(delegate(IntPtr window, IntPtr ignored)
            {
                uint id; GetWindowThreadProcessId(window, out id);
                if (id != pid || !IsWindowVisible(window)) return true;
                var title = new StringBuilder(256); GetWindowText(window, title, title.Capacity);
                if (title.ToString() == "petdex-desktop-native") { if (!hiddenBubbles.Contains(window)) hiddenBubbles.Add(window); ShowWindow(window, 0); }
                return true;
            }, IntPtr.Zero);
        }
        public static Point BalloonPosition(Size size, Rectangle pet)
        {
            var area = Screen.FromRectangle(pet).WorkingArea;
            int x = pet.Right - size.Width + 8;
            int y = pet.Top - size.Height + 14;
            if (y < area.Top + 8) y = pet.Bottom + 8;
            return new Point(Math.Max(area.Left + 8, Math.Min(x, area.Right - size.Width - 8)), Math.Max(area.Top + 8, Math.Min(y, area.Bottom - size.Height - 8)));
        }
        public static Point PanelPosition(Size size, Rectangle pet)
        {
            var area = Screen.FromRectangle(pet).WorkingArea;
            int x = pet.Right + 12; if (x + size.Width > area.Right - 8) x = pet.Left - size.Width - 12;
            return new Point(Math.Max(area.Left + 8, x), Math.Max(area.Top + 8, Math.Min(pet.Top, area.Bottom - size.Height - 8)));
        }
    }

    public sealed class PetdexFeed
    {
        private readonly CompanionConfig config;
        public int Pid;
        public PetdexFeed(CompanionConfig config) { this.config = config; }
        public bool Probe()
        {
            try { var result = Get("/whoami"); Pid = Convert.ToInt32(result["pid"]); return Json.Text(result, "ok") == "True"; }
            catch { Pid = 0; return false; }
        }
        private static Dictionary<string, object> Get(string path)
        {
            var request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:7777" + path); request.Proxy = null; request.Timeout = 700;
            using (var response = request.GetResponse()) using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8)) return Json.Read(reader.ReadToEnd());
        }
        public void Post(string state)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    string token = File.ReadAllText(Path.Combine(config.PetdexHome, "runtime", "update-token"), Encoding.UTF8).Trim();
                    var request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:7777/state"); request.Proxy = null; request.Method = "POST"; request.Timeout = 700; request.ReadWriteTimeout = 700;
                    request.ContentType = "application/json"; request.Headers["x-petdex-update-token"] = token;
                    byte[] bytes = Encoding.UTF8.GetBytes(Json.Write(new { state = state, duration = 0, agent_source = "codex" })); request.ContentLength = bytes.Length;
                    using (var stream = request.GetRequestStream()) stream.Write(bytes, 0, bytes.Length);
                    using (var response = request.GetResponse()) { }
                }
                catch { }
            });
        }
    }

    public sealed class ProjectTask
    {
        public string Id { get; set; }
        public string SessionId { get; set; }
        public string Title { get; set; }
        public string State { get; set; }
        public long UpdatedMs { get; set; }
        public long StartedMs { get; set; }
    }
    public sealed class ProjectEntry
    {
        public string SessionId { get; set; }
        public string Path { get; set; }
        public string Name { get; set; }
        public long UpdatedMs { get; set; }
        public List<ProjectTask> Tasks { get; set; }
        public int PlanDone { get; set; }
        public int PlanTotal { get; set; }
        public string PlanTaskId { get; set; }
        public ProjectEntry() { Tasks = new List<ProjectTask>(); }
        public ProjectTask Latest { get { return Tasks.AsEnumerable().Reverse().OrderByDescending(t => t.UpdatedMs).ThenByDescending(t => t.StartedMs).FirstOrDefault(); } }
        public string Status
        {
            get
            {
                var task = Latest;
                return task == null ? "有新活动" : task.State == "waiting" ? "待审批" : task.State == "running" ? "进行中" : task.State == "completed" ? "已完成" : task.State == "failed" ? "任务出错" : task.State == "interrupted" ? "已暂停" : "待继续";
            }
        }
        public int Completed { get { return Tasks.Count(t => t.State == "completed"); } }
    }
    public sealed class ProjectData
    {
        public string Source { get; set; }
        public string Unit { get; set; }
        public List<string> SessionIds { get; set; }
        public List<ProjectEntry> Projects { get; set; }
        public List<string> Hidden { get; set; }
        public ProjectData() { Projects = new List<ProjectEntry>(); Hidden = new List<string>(); SessionIds = new List<string>(); }
    }
    public sealed class ProjectStore
    {
        private readonly string path;
        public ProjectData Data;
        public int Revision;
        public static long NowMs() { return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds; }
        public ProjectStore(string path)
        {
            this.path = path;
            try { Data = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<ProjectData>(File.ReadAllText(path, Encoding.UTF8)); }
            catch { Data = new ProjectData(); }
            if (Data == null) Data = new ProjectData();
            if (Data.Projects == null) Data.Projects = new List<ProjectEntry>(); if (Data.Hidden == null) Data.Hidden = new List<string>();
            bool migrated = Data.Unit != "conversation" || Data.Projects.Any(p => p.Path != Canonical(p.Path));
            NormalizeConversations(Data);
            if (migrated) Save();
        }
        private static void NormalizeConversations(ProjectData data)
        {
            bool legacy = data.Unit != "conversation";
            var oldHidden = data.Hidden ?? new List<string>();
            var hidden = legacy ? new List<string>() : oldHidden.ToList();
            var combined = new List<ProjectEntry>();
            foreach (var project in data.Projects ?? new List<ProjectEntry>())
            {
                project.Path = Canonical(project.Path);
                if (project.Tasks == null) project.Tasks = new List<ProjectTask>();
                var ids = string.IsNullOrEmpty(project.SessionId) ? project.Tasks.Select(t => t.SessionId).Where(id => !string.IsNullOrEmpty(id)).Distinct().ToArray() : new[] { project.SessionId };
                foreach (string sid in ids)
                {
                    var tasks = project.Tasks.Where(t => SameId(t.SessionId, sid)).ToList();
                    var latest = tasks.AsEnumerable().Reverse().OrderByDescending(t => t.UpdatedMs).FirstOrDefault();
                    var conversation = new ProjectEntry { SessionId = sid, Path = project.Path, Name = legacy && string.IsNullOrEmpty(project.SessionId) ? (latest == null || string.IsNullOrEmpty(latest.Title) ? "Codex 对话" : latest.Title) : project.Name, UpdatedMs = string.IsNullOrEmpty(project.SessionId) && latest != null ? latest.UpdatedMs : project.UpdatedMs, Tasks = tasks };
                    if (tasks.Any(t => t.Id == project.PlanTaskId)) { conversation.PlanDone = project.PlanDone; conversation.PlanTotal = project.PlanTotal; conversation.PlanTaskId = project.PlanTaskId; }
                    var existing = combined.FirstOrDefault(p => SameId(p.SessionId, sid));
                    if (existing == null) combined.Add(conversation);
                    else
                    {
                        existing.Tasks = existing.Tasks.Concat(tasks).GroupBy(t => t.Id).Select(g => g.OrderByDescending(t => t.UpdatedMs).First()).ToList();
                        if (conversation.UpdatedMs >= existing.UpdatedMs) { existing.Name = conversation.Name; existing.Path = conversation.Path; existing.PlanDone = conversation.PlanDone; existing.PlanTotal = conversation.PlanTotal; existing.PlanTaskId = conversation.PlanTaskId; }
                        existing.UpdatedMs = Math.Max(existing.UpdatedMs, conversation.UpdatedMs);
                    }
                    if (legacy && oldHidden.Any(h => Same(h, project.Path) || SameId(h, sid))) hidden.Add(sid);
                }
            }
            data.Projects = combined; data.Hidden = hidden.Distinct(StringComparer.OrdinalIgnoreCase).ToList(); data.Unit = "conversation";
        }
        public static string Canonical(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) value = @"\\" + value.Substring(8);
            else if (value.StartsWith(@"\\?\")) value = value.Substring(4);
            return value.Replace('/', '\\').TrimEnd('\\');
        }
        private static bool Same(string a, string b) { return string.Equals(Canonical(a), Canonical(b), StringComparison.OrdinalIgnoreCase); }
        private static bool SameId(string a, string b) { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
        public List<ProjectEntry> Recent(long now)
        { return Data.Projects.Where(p => p.UpdatedMs >= now - 5L * 24 * 3600000 && !Data.Hidden.Any(h => SameId(h, p.SessionId))).OrderByDescending(p => p.UpdatedMs).ToList(); }
        public void Hide(string sid)
        { if (!Data.Hidden.Any(h => SameId(h, sid))) Data.Hidden.Add(sid); Save(); }
        public void Restore(string sid)
        { Data.Hidden.RemoveAll(h => SameId(h, sid)); Save(); }
        private ProjectEntry Entry(string sid, string project)
        {
            project = Canonical(project);
            var entry = Data.Projects.FirstOrDefault(p => SameId(p.SessionId, sid));
            if (entry != null) { if (project.Length > 0) entry.Path = project; return entry; }
            entry = new ProjectEntry { SessionId = sid, Path = project, Name = "Codex 对话" }; Data.Projects.Add(entry); return entry;
        }
        public bool IsVscodeSession(Dictionary<string, object> payload)
        {
            string sid = Json.Text(payload, "session_id");
            if (sid.StartsWith("[SELFTEST]") || sid.StartsWith("演示")) return true;
            if (Data.SessionIds != null && Data.SessionIds.Contains(sid)) return true;
            if (Json.Text(payload, "source") == "vscode") return true;
            try
            {
                string transcript = Json.Text(payload, "transcript_path");
                using (var stream = new FileStream(transcript, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    var line = Json.Read(reader.ReadLine()); var meta = Json.Object(line, "payload");
                    return Json.Text(line, "type") == "session_meta" && Json.Text(meta, "source") == "vscode" && (Json.Text(meta, "id") == sid || Json.Text(meta, "session_id") == sid);
                }
            }
            catch { return false; }
        }
        public void Update(Dictionary<string, object> payload, string explicitState)
        {
            string cwd = Json.Text(payload, "cwd"); string sid = Json.Text(payload, "session_id");
            if (sid.Length == 0 || sid.StartsWith("[SELFTEST]") || sid.StartsWith("演示")) return;
            try { if (cwd.Length > 0) cwd = System.IO.Path.GetFullPath(cwd).TrimEnd('\\', '/'); } catch { return; }
            string ev = Json.Text(payload, "hook_event_name"); string turn = Json.Text(payload, "turn_id");
            long now = NowMs(); var project = Entry(sid, cwd); project.UpdatedMs = now;
            if (ev == "SessionStart") { Save(); return; }
            if (ev == "SessionEnd") { foreach (var open in project.Tasks.Where(t => t.SessionId == sid && (t.State == "running" || t.State == "waiting"))) { open.State = "interrupted"; open.UpdatedMs = now; } Save(); return; }
            string id = turn.Length > 0 ? sid + ":" + turn : "";
            var task = id.Length > 0 ? project.Tasks.FirstOrDefault(t => t.Id == id) : null;
            if (task == null && ev != "UserPromptSubmit") task = project.Tasks.LastOrDefault(t => t.SessionId == sid && (t.State == "running" || t.State == "waiting"));
            if (task == null) { task = new ProjectTask { Id = id.Length == 0 ? sid + ":hook:" + now : id, SessionId = sid, Title = "Codex 任务", StartedMs = now }; project.Tasks.Add(task); }
            else if (id.Length > 0 && task.Id.Contains(":hook:")) { if (project.PlanTaskId == task.Id) project.PlanTaskId = id; task.Id = id; }
            string prompt = Json.Text(payload, "prompt"); if (prompt.Length > 0) task.Title = prompt.Length > 100 ? prompt.Substring(0, 100) : prompt;
            if (project.Name == "Codex 对话" && prompt.Length > 0) project.Name = task.Title;
            task.State = explicitState ?? (ev == "Stop" ? "completed" : ev == "Interrupt" ? "interrupted" : "running"); task.UpdatedMs = now;
            var input = Json.Object(payload, "tool_input"); object plan;
            if (input != null && input.TryGetValue("plan", out plan) && plan is System.Collections.IEnumerable && !(plan is string))
            {
                var steps = ((System.Collections.IEnumerable)plan).OfType<Dictionary<string, object>>().ToArray();
                if (steps.Length > 0) { project.PlanTotal = steps.Length; project.PlanDone = steps.Count(s => Json.Text(s, "status") == "completed"); project.PlanTaskId = task.Id; }
            }
            if (project.Tasks.Count > 80) project.Tasks = project.Tasks.OrderByDescending(t => t.UpdatedMs).Take(80).OrderBy(t => t.UpdatedMs).ToList();
            Save();
        }
        public void ApprovalResolved(Dictionary<string, object> payload, string behavior)
        {
            string sid = Json.Text(payload, "session_id"); string turn = Json.Text(payload, "turn_id");
            var project = Data.Projects.FirstOrDefault(p => SameId(p.SessionId, sid));
            if (project == null) return;
            var task = turn.Length > 0 ? project.Tasks.FirstOrDefault(t => t.Id == sid + ":" + turn) : project.Tasks.LastOrDefault(t => t.SessionId == sid && t.State == "waiting");
            if (task == null || task.State != "waiting") return;
            task.State = behavior == "allow" ? "running" : behavior == "deny" ? "interrupted" : "waiting";
            task.UpdatedMs = project.UpdatedMs = NowMs(); Save();
        }
        public void MergeSeed(string seedPath)
        {
            ProjectData seed;
            try { seed = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<ProjectData>(File.ReadAllText(seedPath, Encoding.UTF8)); } catch { return; }
            if (seed == null || seed.Projects == null) return;
            NormalizeConversations(seed);
            bool changed = false;
            if (seed.Source == "vscode")
            {
                Data.Source = "vscode"; Data.SessionIds = seed.SessionIds ?? new List<string>();
                if (Data.Projects.RemoveAll(p => !Data.SessionIds.Contains(p.SessionId)) > 0) changed = true;
                changed = true;
            }
            foreach (var incoming in seed.Projects)
            {
                if (string.IsNullOrEmpty(incoming.SessionId) || incoming.UpdatedMs < NowMs() - 5L * 24 * 3600000) continue;
                var project = Entry(incoming.SessionId, incoming.Path);
                if (!string.IsNullOrEmpty(incoming.Name) && project.Name != incoming.Name) { project.Name = incoming.Name; changed = true; }
                if (incoming.UpdatedMs > project.UpdatedMs) { project.UpdatedMs = incoming.UpdatedMs; changed = true; }
                foreach (var item in incoming.Tasks ?? new List<ProjectTask>())
                {
                    if (seed.Source == "vscode" && !Data.SessionIds.Contains(item.SessionId)) continue;
                    var old = project.Tasks.FirstOrDefault(t => t.Id == item.Id);
                    if (old == null && item.StartedMs > 0)
                    {
                        old = project.Tasks.FirstOrDefault(t => t.SessionId == item.SessionId && t.Id.Contains(":hook:") && Math.Abs(t.StartedMs - item.StartedMs) < 60000);
                        if (old != null) { if (project.PlanTaskId == old.Id) project.PlanTaskId = item.Id; old.Id = item.Id; changed = true; }
                    }
                    if (old == null) { project.Tasks.Add(item); changed = true; }
                    else if (item.UpdatedMs > old.UpdatedMs) { old.State = item.State; old.Title = item.Title; old.UpdatedMs = item.UpdatedMs; changed = true; }
                }
                if (incoming.PlanTotal > 0 && incoming.UpdatedMs >= project.UpdatedMs) { project.PlanDone = incoming.PlanDone; project.PlanTotal = incoming.PlanTotal; project.PlanTaskId = incoming.PlanTaskId; changed = true; }
                project.Tasks = project.Tasks.OrderByDescending(t => t.UpdatedMs).Take(80).OrderBy(t => t.UpdatedMs).ToList();
            }
            if (changed) Save();
        }
        private void Save()
        {
            long cutoff = NowMs() - 5L * 24 * 3600000;
            Data.Projects.RemoveAll(p => p.UpdatedMs < cutoff);
            foreach (var project in Data.Projects) project.Tasks.RemoveAll(t => t.UpdatedMs < cutoff);
            Revision++;
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                string temporary = path + ".tmp";
                File.WriteAllText(temporary, Json.Write(Data), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
            catch { }
        }
    }

    public sealed class ProjectPanel : Form
    {
        private readonly ProjectStore store;
        private readonly FlowLayoutPanel rows;
        private readonly Label heading;
        private int revision = -1;
        private int drawnMinute = -1;
        private readonly ToolTip tip = new ToolTip();
        private readonly Action<string> openSession;
        public ProjectPanel(ProjectStore store) : this(store, null) { }
        public ProjectPanel(ProjectStore store, Action<string> openSession)
        {
            this.store = store; this.openSession = openSession; Text = "JokeBear · 最近对话"; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
            BackColor = Color.FromArgb(250, 249, 245); Font = new Font("Microsoft YaHei UI", 9); Size = new Size(210, 142); Padding = new Padding(10, 6, 10, 6);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22)); Controls.Add(layout);
            heading = new Label { Dock = DockStyle.Fill, Font = new Font(Font.FontFamily, 9, FontStyle.Bold), ForeColor = Color.FromArgb(54, 52, 47) }; layout.Controls.Add(heading);
            tip.SetToolTip(heading, "每行是一条独立的 Codex 对话\n黄灯：进行中 / 待审批\n绿灯：本轮回复已完成\n红灯：出错 / 中断\n点击标题，在 VS Code 中打开该对话");
            rows = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true }; layout.Controls.Add(rows);
            var restore = new LinkLabel { Text = "恢复隐藏对话", Dock = DockStyle.Fill, Font = new Font(Font.FontFamily, 8), LinkColor = Color.FromArgb(129, 108, 78) }; restore.LinkClicked += delegate { RestoreMenu(restore); }; layout.Controls.Add(restore);
            Resize += delegate { SetRoundedRegion(); }; SetRoundedRegion(); RefreshProjects();
            FormClosed += delegate { tip.Dispose(); };
        }
        private void SetRoundedRegion() { using (var path = UiStyle.Round(new Rectangle(0, 0, Width, Height), 18)) { var old = Region; Region = new Region(path); if (old != null) old.Dispose(); } }
        public void RefreshProjects()
        {
            int minute = DateTime.Now.Minute;
            if (revision == store.Revision && minute == drawnMinute) return; revision = store.Revision; drawnMinute = minute;
            var recent = store.Recent(ProjectStore.NowMs()); heading.Text = "Codex 对话 · 近 5 天";
            rows.SuspendLayout(); foreach (Control old in rows.Controls.Cast<Control>().ToArray()) old.Dispose(); rows.Controls.Clear();
            Height = 64 + Math.Max(1, Math.Min(5, recent.Count)) * 30;
            if (recent.Count == 0) rows.Controls.Add(new Label { Text = "暂无近期对话", Size = new Size(172, 27), ForeColor = Color.Gray, Padding = new Padding(0, 3, 0, 0) });
            foreach (var project in recent)
            {
                var row = new Panel { Size = new Size(171, 28), Margin = new Padding(0, 0, 0, 2), BackColor = Color.FromArgb(242, 239, 231) };
                using (var shape = UiStyle.Round(new Rectangle(0, 0, row.Width, row.Height), 7)) row.Region = new Region(shape);
                string state = project.Status;
                var lamp = new StatusLamp { Name = "project-status-lamp", Tag = state, Location = new Point(7, 9), Size = new Size(10, 10), LampColor = state == "已完成" ? Color.FromArgb(65, 167, 112) : state == "任务出错" || state == "已暂停" ? Color.FromArgb(216, 92, 85) : Color.FromArgb(227, 174, 53) }; row.Controls.Add(lamp);
                var name = new LinkLabel { Text = project.Name, Location = new Point(24, 4), Size = new Size(123, 21), Font = new Font(Font.FontFamily, 9), AutoEllipsis = true, LinkBehavior = LinkBehavior.NeverUnderline, LinkColor = Color.FromArgb(64, 58, 49), ActiveLinkColor = Color.FromArgb(134, 101, 65) };
                name.LinkClicked += delegate { if (this.openSession != null) this.openSession(project.SessionId); }; row.Controls.Add(name);
                var remove = new Button { Text = "×", Name = "hide-conversation", Tag = project.SessionId, FlatStyle = FlatStyle.Flat, Size = new Size(21, 21), Location = new Point(148, 3), ForeColor = Color.FromArgb(147, 135, 117), TabStop = false }; remove.FlatAppearance.BorderSize = 0;
                remove.Click += delegate { store.Hide(project.SessionId); RefreshProjects(); }; tip.SetToolTip(remove, "仅隐藏这条对话，保留聊天记录"); row.Controls.Add(remove);
                bool plan = project.PlanTotal > 0 && project.Latest != null && project.Latest.Id == project.PlanTaskId;
                int done = plan ? project.PlanDone : project.Completed, total = plan ? project.PlanTotal : project.Tasks.Count;
                string label = total == 0 ? "等待任务记录" : plan ? "计划步骤 " + done + " / " + total : "已完成 " + done + " / " + total + " 次任务";
                string details = project.Name + "\n" + state + " · " + label + (string.IsNullOrEmpty(project.Path) ? "" : "\n" + project.Path);
                tip.SetToolTip(name, details); tip.SetToolTip(lamp, details); tip.SetToolTip(row, details); rows.Controls.Add(row);
            }
            rows.ResumeLayout();
        }
        private void RestoreMenu(Control parent)
        {
            var menu = new ContextMenuStrip();
            foreach (var sid in store.Data.Hidden.ToArray()) { string captured = sid; var conversation = store.Data.Projects.FirstOrDefault(p => p.SessionId == sid); string title = conversation == null ? "已超出 5 天的对话 " + sid.Substring(0, Math.Min(8, sid.Length)) : conversation.Name; menu.Items.Add(title + " · 恢复", null, delegate { store.Restore(captured); RefreshProjects(); }); }
            if (menu.Items.Count == 0) menu.Items.Add("没有隐藏对话").Enabled = false;
            menu.Closed += delegate { menu.Dispose(); }; menu.Show(parent, new Point(0, parent.Height));
        }
    }

    public sealed class StatusLamp : Control
    {
        public Color LampColor;
        public StatusLamp() { DoubleBuffered = true; }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new SolidBrush(LampColor)) e.Graphics.FillEllipse(brush, 1, 1, Width - 2, Height - 2);
        }
    }

    public static class UiStyle
    {
        public static GraphicsPath Round(Rectangle r, int radius)
        {
            var path = new GraphicsPath(); int d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90); path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90); path.CloseFigure(); return path;
        }
    }
}
