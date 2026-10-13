using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using CodexPet;

public static class GuiTests
{
    private static Process client;
    private static int stage;
    private static DateTime deadline;
    private static int exitCode;
    private static string hook;
    private static string runtime;
    private static Control[] Flatten(Control control)
    { return control.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Flatten(c))).ToArray(); }
    private static void Begin()
    {
        var start = new ProcessStartInfo(hook, "--hook") { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, StandardOutputEncoding = new UTF8Encoding(false) };
        client = Process.Start(start);
        byte[] input = Encoding.UTF8.GetBytes(Json.Write(new { hook_event_name = "PermissionRequest", session_id = "[SELFTEST] " + stage, cwd = "中文目录 · GUI test", tool_name = "Bash", tool_input = new { command = "TEST ONLY - THIS COMMAND IS NEVER EXECUTED", description = "只测试按钮回传，不执行命令" } }));
        client.StandardInput.BaseStream.Write(input, 0, input.Length); client.StandardInput.Close();
        deadline = DateTime.UtcNow.AddSeconds(12);
    }
    [STAThread]
    public static int Main(string[] args)
    {
        hook = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CodexPet.Hook.exe");
        runtime = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtime.json");
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        var pet = new PetForm();
        var timer = new System.Windows.Forms.Timer { Interval = 200 };
        pet.Shown += delegate { Begin(); timer.Start(); };
        timer.Tick += delegate
        {
            try
            {
                if (DateTime.UtcNow > deadline) throw new Exception("GUI test timed out");
                var dialog = Application.OpenForms.OfType<ApprovalDialog>().FirstOrDefault();
                if (dialog != null && Json.Text(dialog.Request.Payload, "session_id").StartsWith("[SELFTEST]"))
                {
                    var controls = Flatten(dialog);
                    if (!controls.OfType<TextBox>().Any(t => t.Text.Contains("TEST ONLY"))) throw new Exception("Missing command preview");
                    if (!controls.OfType<Label>().Any(t => t.Text.Contains("中文目录"))) throw new Exception("UTF-8 path corrupted");
                    if (stage == 0)
                    {
                        using (var bitmap = new Bitmap(dialog.Width, dialog.Height))
                        { dialog.DrawToBitmap(bitmap, new Rectangle(0, 0, dialog.Width, dialog.Height)); bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "approval-preview.png")); }
                        using (var bitmap = new Bitmap(pet.Width, pet.Height))
                        { pet.DrawToBitmap(bitmap, new Rectangle(0, 0, pet.Width, pet.Height)); bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pet-preview.png")); }
                    }
                    string text = stage == 0 ? "允许这一次" : stage == 1 ? "拒绝" : "回 VS Code 处理";
                    controls.OfType<Button>().Single(b => b.Text == text).PerformClick();
                }
                if (client.HasExited)
                {
                    string output = client.StandardOutput.ReadToEnd().Trim();
                    string behavior = Json.Text(Json.Object(Json.Object(Json.Read(output), "hookSpecificOutput"), "decision"), "behavior");
                    string expected = stage == 0 ? "allow" : stage == 1 ? "deny" : "";
                    if (behavior != expected || (stage == 2 && output != "{}")) throw new Exception("Wrong GUI decision: " + output);
                    Console.WriteLine("PASS: actual GUI button + native stdin/stdout hook: " + (stage == 2 ? "fallback" : expected));
                    client.Dispose(); client = null; stage++;
                    if (stage == 3) { timer.Stop(); pet.Close(); }
                    else Begin();
                }
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); exitCode = 1; timer.Stop(); pet.Close(); }
        };
        try { Application.Run(pet); }
        finally { timer.Dispose(); if (client != null) { if (!client.HasExited) client.Kill(); client.Dispose(); } if (File.Exists(runtime)) File.Delete(runtime); }
        return exitCode;
    }
}
