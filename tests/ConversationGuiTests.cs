using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using CodexPet;
public static class ConversationGuiTests
{
    static Control[] Controls(Control root) { return root.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Controls(c))).ToArray(); }
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); Console.WriteLine("PASS: " + message); }
    [STAThread]
    public static int Main()
    {
        var folder = Path.Combine(Path.GetTempPath(), "pet-gui-fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            var store = new ProjectStore(Path.Combine(folder,"progress.json")); long now = ProjectStore.NowMs();
            store.Data.Projects.Add(new ProjectEntry { SessionId="550e8400-e29b-41d4-a716-446655440000", Name="对话一", Path=@"C:\shared", UpdatedMs=now, Tasks=new List<ProjectTask> { new ProjectTask {Id="a:t",SessionId="550e8400-e29b-41d4-a716-446655440000",State="waiting",UpdatedMs=now} } });
            store.Data.Projects.Add(new ProjectEntry { SessionId="550e8400-e29b-41d4-a716-446655440001", Name="对话二", Path=@"C:\shared", UpdatedMs=now-1, Tasks=new List<ProjectTask> { new ProjectTask {Id="b:t",SessionId="550e8400-e29b-41d4-a716-446655440001",State="completed",UpdatedMs=now-1} } });
            var opened = new List<string>();
            using(var panel = new ProjectPanel(store, delegate(string sid) { opened.Add(sid); }))
            {
                panel.Show(); panel.PerformLayout(); Application.DoEvents();
                var all=Controls(panel); var titles=all.OfType<LinkLabel>().Where(c=>c.Text=="对话一"||c.Text=="对话二").ToArray();
                Check(titles.Length==2 && panel.Width==210 && !all.OfType<ProgressBar>().Any(),"compact panel keeps two independent conversation titles without progress bars");
                var lamps=all.OfType<StatusLamp>().ToArray();
                Check(lamps.Length==2 && lamps.Any(c=>(string)c.Tag=="待审批") && lamps.Any(c=>(string)c.Tag=="已完成"),"same-folder conversations display their independent approval and completed lamps");
                foreach(var title in titles) typeof(LinkLabel).GetMethod("OnLinkClicked",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(title,new object[] {new LinkLabelLinkClickedEventArgs(title.Links[0])});
                Check(opened.SequenceEqual(store.Data.Projects.Select(p=>p.SessionId)),"clicking each displayed title opens that exact conversation ID");
                all.OfType<Button>().Single(c=>c.Name=="hide-conversation" && (string)c.Tag==opened[0]).PerformClick();
                Check(Controls(panel).OfType<Button>().Count(c=>c.Name=="hide-conversation")==1 && !store.Data.Hidden.Contains(opened[1]),"the real hide button removes only its selected conversation");
                store.Restore(opened[0]); panel.RefreshProjects();
                Check(Controls(panel).OfType<LinkLabel>().Count(c=>c.Text=="对话一"||c.Text=="对话二")==2,"restoring the conversation rebuilds its card");
            }
            return 0;
        }
        catch(Exception e) { Console.Error.WriteLine(e.ToString());return 1; }
        finally { Directory.Delete(folder,true); }
    }
}
