using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using CodexPet;
public static class ProjectTests
{
    static int checks;
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; Console.WriteLine("PASS: " + message); }
    static Dictionary<string, object> Event(string ev, string turn, string sid = "conversation-a")
    { return Json.Read(Json.Write(new { cwd = Path.GetFullPath("."), session_id = sid, turn_id = turn, hook_event_name = ev })); }
    static void Seed(string path, ProjectEntry entry, ProjectTask task)
    { File.WriteAllText(path, Json.Write(new ProjectData { Unit = "conversation", Projects = new List<ProjectEntry> { new ProjectEntry { SessionId = entry.SessionId, Path = entry.Path, Name = entry.Name, UpdatedMs = task.UpdatedMs, Tasks = new List<ProjectTask> { task } } } })); }
    public static int Main()
    {
        string folder = Path.Combine(Path.GetTempPath(), "codex-conversation-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "progress.json"), seed = Path.Combine(folder, "seed.json");
            var store = new ProjectStore(path); long now = ProjectStore.NowMs();
            store.Data.Projects.Add(new ProjectEntry { SessionId = "old", UpdatedMs = now - 432000001 });
            store.Data.Projects.Add(new ProjectEntry { SessionId = "edge", UpdatedMs = now - 432000000 });
            Check(store.Recent(now).Count == 1, "five-day cutoff includes the boundary only");
            store.Update(Event("UserPromptSubmit", "turn-1"), null);
            var a = store.Data.Projects.Single(p => p.SessionId == "conversation-a");
            store.Update(Event("UserPromptSubmit", "turn-b", "conversation-b"), null);
            var b = store.Data.Projects.Single(p => p.SessionId == "conversation-b");
            Check(a.Path == b.Path && a != b && a.Tasks.Count == 1 && b.Tasks.Count == 1, "same folder has independent conversation cards");
            store.Update(Event("Stop", "turn-b", b.SessionId), null); store.Update(Event("PermissionRequest", "turn-1"), "waiting");
            Check(a.Status == "待审批" && b.Status == "已完成", "approval changes only the requesting conversation lamp");
            store.ApprovalResolved(Event("PermissionRequest", "turn-1"), "deny");
            Check(a.Status == "已暂停" && b.Status == "已完成", "denial does not interrupt another conversation");
            store.Update(Event("Stop", "turn-1"), null); store.ApprovalResolved(Event("PermissionRequest", "turn-1"), "");
            Check(a.Tasks.Single().State == "completed", "stale approval cannot revive completed turn");
            store.Update(Event("UserPromptSubmit", "turn-2"), null);
            var plan = Event("PreToolUse", "turn-2"); plan["tool_input"] = Json.Read("{\"plan\":[{\"status\":\"completed\"},{\"status\":\"in_progress\"}]}"); store.Update(plan, null);
            Check(a.PlanDone == 1 && a.PlanTotal == 2 && b.PlanTotal == 0, "plan belongs to one conversation");
            store.Hide(a.SessionId);
            Check(!store.Recent(ProjectStore.NowMs()).Contains(a) && store.Recent(ProjectStore.NowMs()).Contains(b), "hiding one conversation leaves its shared-folder neighbor visible");
            store = new ProjectStore(path); store.Update(Event("PreToolUse", "turn-2"), null);
            Check(!store.Recent(ProjectStore.NowMs()).Any(p => p.SessionId == a.SessionId), "hiding survives restart and new activity");
            store.Restore(a.SessionId); a = store.Data.Projects.Single(p => p.SessionId == a.SessionId);
            Check(store.Recent(ProjectStore.NowMs()).Contains(a), "hidden conversation can be restored");
            var task = a.Tasks.Single(t => t.Id.EndsWith("turn-2"));
            Seed(seed, a, new ProjectTask { Id = task.Id, SessionId = a.SessionId, State = "completed", UpdatedMs = task.UpdatedMs - 1 }); store.MergeSeed(seed);
            Check(task.State == "running", "stale SQLite snapshot cannot overwrite live state");
            store.Update(Event("SessionEnd", ""), null);
            Check(task.State == "interrupted" && store.Data.Projects.Single(p => p.SessionId == b.SessionId).Status == "已完成", "session end affects only that conversation");
            store.Update(Event("UserPromptSubmit", "", "new-session"), null);
            var newer = store.Data.Projects.Single(p => p.SessionId == "new-session"); task = newer.Tasks.Last();
            Seed(seed, newer, new ProjectTask { Id = "new-session:real-turn", SessionId = newer.SessionId, State = "completed", StartedMs = task.StartedMs, UpdatedMs = task.UpdatedMs + 1 }); store.MergeSeed(seed);
            Check(newer.Tasks.Count == 1 && task.Id == "new-session:real-turn" && task.State == "completed", "SQLite reconciles temporary hook turns without double counting");
            newer.Name = "真实对话标题";
            File.WriteAllText(seed, Json.Write(new ProjectData { Source = "vscode", Unit = "conversation", SessionIds = new List<string> { newer.SessionId }, Projects = new List<ProjectEntry> { newer } })); store.MergeSeed(seed);
            Check(store.Data.Projects.Count == 1 && store.Data.Projects[0].Name == "真实对话标题", "VS Code import uses conversation identity and title");
            var candidate = Event("PermissionRequest", "", "new-session");
            Check(store.IsVscodeSession(candidate), "known VS Code conversation retains approvals"); candidate["session_id"] = "unrelated-cli";
            Check(!store.IsVscodeSession(candidate), "unknown CLI conversation is excluded");
            Check(VscodeTarget.SessionUri("550e8400-e29b-41d4-a716-446655440000").EndsWith("/local/550e8400-e29b-41d4-a716-446655440000") && VscodeTarget.SessionUri("invalid / thread") == "vscode://openai.chatgpt/", "VS Code links use exact conversation IDs");
            Check(!VscodeTarget.EditorStart("Code.exe", VscodeTarget.SessionUri("")).EnvironmentVariables.ContainsKey("ELECTRON_RUN_AS_NODE"), "editor launch strips inherited Node mode");
            newer.Tasks.Add(new ProjectTask { Id = "stale", SessionId = newer.SessionId, State = "waiting", UpdatedMs = task.UpdatedMs - 1000 });
            Check(newer.Status == "已完成", "old waiting turn cannot override latest reply");
            string legacyPath = Path.Combine(folder, "legacy.json");
            File.WriteAllText(legacyPath, Json.Write(new ProjectData { Projects = new List<ProjectEntry> { new ProjectEntry { Path = @"C:\shared", Name = "shared", UpdatedMs = now, Tasks = new List<ProjectTask> { new ProjectTask { Id = "one:t", SessionId = "one", Title = "对话一", State = "running", UpdatedMs = now }, new ProjectTask { Id = "two:t", SessionId = "two", Title = "对话二", State = "completed", UpdatedMs = now - 1 } }, PlanTotal = 2, PlanDone = 1, PlanTaskId = "one:t" } }, Hidden = new List<string> { @"C:\shared" } }));
            var legacy = new ProjectStore(legacyPath);
            Check(legacy.Data.Unit == "conversation" && legacy.Data.Projects.Count == 2 && legacy.Data.Projects.Select(p => p.Name).Distinct().Count() == 2, "legacy folder records split into titled conversations");
            Check(legacy.Data.Projects.Single(p => p.SessionId == "one").PlanTotal == 2 && legacy.Data.Projects.Single(p => p.SessionId == "two").PlanTotal == 0, "legacy plan migrates to its correct conversation");
            legacy.Restore("one"); Check(legacy.Recent(now).Count == 1 && legacy.Data.Hidden.Contains("two"), "legacy hiding becomes separately restorable conversations");
            var fresh = Event("SessionStart", "", "three"); fresh["cwd"] = @"C:\shared"; legacy.Update(fresh, null);
            Check(legacy.Recent(ProjectStore.NowMs()).Any(p => p.SessionId == "three"), "legacy folder hiding does not hide future conversations");
            Console.WriteLine(checks + " conversation checks passed."); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error.ToString()); return 1; }
        finally { Directory.Delete(folder, true); }
    }
}
