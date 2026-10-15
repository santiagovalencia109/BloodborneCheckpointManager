using Checkpoint.App;
using System.Drawing.Imaging;
using System.Reflection;
using Checkpoint.Core;
using System.Diagnostics;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if(args.Length==1 && args[0]=="--hotkeys-only")
        {
            ApplicationConfiguration.Initialize();
            using var one=new Form { ShowInTaskbar=false }; using var two=new Form { ShowInTaskbar=false };
            var first=new GlobalShortcuts(one.Handle,Keys.F21,Keys.F22,Keys.F23);
            using var second=new GlobalShortcuts(two.Handle,Keys.F21,Keys.F22,Keys.F23);
            try
            {
                first.Retry(); if(!first.AllRegistered) throw new Exception("Test shortcut is already occupied.");
                second.Retry(); if(second.AllRegistered || second.Registered(Keys.F21)) throw new Exception("Competing registration was accepted.");
                second.Retry(); if(second.Registered(Keys.F21)) throw new Exception("Retry stole an occupied key.");
                first.Dispose(); second.Retry();
                if(!second.AllRegistered) throw new Exception("Freed shortcuts were not reclaimed.");
                Console.WriteLine("PASS: real Windows shortcut conflicts are preserved; freed keys are reclaimed on retry.");
            }
            finally { first.Dispose(); }
            return;
        }
        if (args.Length == 1 && args[0] == "--fake-game")
        {
            Application.Run(new Form { Text = "BCM isolated lifecycle fixture", Opacity = 0, ShowInTaskbar = true });
            return;
        }
        if (args.Length != 1) throw new ArgumentException("Pass preview output directory.");
        var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        using var form = new MainForm(Path.Combine(output, "isolated-settings"), preview: true);
        form.ShowInTaskbar = false; form.Opacity = 0;
        form.Show(); Application.DoEvents(); form.PerformLayout();
        typeof(MainForm).GetField("manifests", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(form, new[]
        {
            new Manifest(1, Guid.NewGuid().ToString("N"), "Before the Cleric Beast", DateTimeOffset.Parse("2026-10-12T18:20:00-05:00"), "demo", false, [new Entry("demo", 15 * 1048576, "demo")]),
            new Manifest(1, Guid.NewGuid().ToString("N"), "Father Gascoigne", DateTimeOffset.Parse("2026-10-12T17:05:00-05:00"), "demo", false, [new Entry("demo", 15 * 1048576, "demo")])
        });
        typeof(MainForm).GetMethod("RenderList", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, null);
        var tabs = Descendants(form).OfType<TabControl>().Single();
        var name=(TextBox)typeof(MainForm).GetField("name",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(form)!;
        var search=(TextBox)typeof(MainForm).GetField("search",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(form)!;
        var list=Descendants(form).OfType<ListView>().Single();
        search.Text="Cleric"; list.Items[0].Selected=true; name.Text="Unsaved edit";
        var id=((Manifest)list.SelectedItems[0].Tag!).Id;
        form.ChangeLanguage("es");
        if(name.Text!="Unsaved edit" || search.Text!="Cleric" || ((Manifest)list.SelectedItems[0].Tag!).Id!=id || tabs.TabPages[1].Text!="Ajustes") throw new Exception("Language switch lost state.");
        search.Text=""; name.Text="Before the Cleric Beast";
        foreach(var language in new[]{"en","es"})
        {
        form.ChangeLanguage(language);
        foreach (var width in new[] { 1120, 980 })
        {
            form.Size = new Size(width, width == 1120 ? 820 : 740);
            for (var i = 0; i < tabs.TabCount; i++)
            {
                tabs.SelectedIndex = i;
                foreach (var control in Descendants(form)) control.CreateControl();
                form.PerformLayout();
                Application.DoEvents();
                using var bitmap = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                bitmap.Save(Path.Combine(output, $"{language}-page-{i}-{width}.png"), ImageFormat.Png);
            }
        }
        }
        Console.WriteLine("PASS: English/Spanish switch preserves selection, search and unsaved name; 12 page previews at two sizes; no game actions.");
        // The picker must dispatch the exact selected checkpoint and remain usable
        // when the main manager is minimized. A failed restore keeps the panel visible.
        string? requested=null; int requests=0; var allowSuccess=false;
        var release=new TaskCompletionSource<bool>();
        using var picker=new CheckpointPicker(async cp=>
        {
            requested=cp.Id; ++requests;
            if(!allowSuccess) return false;
            return await release.Task;
        },()=>Task.CompletedTask,()=>{});
        var earlier=new Manifest(1,Guid.NewGuid().ToString("N"),"Older boss checkpoint",DateTimeOffset.Now.AddMinutes(-5),"demo",false,[new Entry("demo",1024,"demo")]);
        var latest=new Manifest(1,Guid.NewGuid().ToString("N"),"Latest checkpoint",DateTimeOffset.Now,"demo",false,[new Entry("demo",1024,"demo")]);
        var emergency=latest with { Id=Guid.NewGuid().ToString("N"),Name="Emergency backup",Emergency=true };
        form.WindowState=FormWindowState.Minimized; picker.SetCheckpoints([latest,earlier,emergency]); picker.ShowFloating(); Application.DoEvents();
        if(!picker.Visible || !picker.TopMost || picker.ShowInTaskbar || picker.Owner!=null) throw new Exception("Picker depends on the minimized main window.");
        var pickerList=Descendants(picker).OfType<ListBox>().Single();
        var pickerSearch=Descendants(picker).OfType<TextBox>().Single();
        if(pickerList.Items.Count!=2 || picker.Icon==null || Descendants(picker).OfType<PictureBox>().Single().Image==null) throw new Exception("Picker rows/icons are missing.");
        pickerSearch.Text="Older"; Application.DoEvents();
        picker.RestoreSelectedAsync().GetAwaiter().GetResult();
        if(requested!=earlier.Id || requests!=1 || !picker.Visible) throw new Exception("Picker chose latest instead of selected, or hid after failure.");
        TextCatalog.Current=TextCatalog.Load(Path.Combine(AppContext.BaseDirectory,"Languages"),"es"); picker.ApplyLanguage();
        if(pickerSearch.Text!="Older" || picker.SelectedCheckpoint?.Id!=earlier.Id) throw new Exception("Picker language switch lost selection/search.");
        pickerSearch.Text="does not exist"; picker.RestoreSelectedAsync().GetAwaiter().GetResult();
        if(requests!=1 || picker.SelectedCheckpoint!=null) throw new Exception("Empty search dispatched a restore.");
        pickerSearch.Text="Older"; picker.SetOperationState(true,"busy"); picker.RestoreSelectedAsync().GetAwaiter().GetResult();
        if(requests!=1) throw new Exception("Busy picker dispatched a restore.");
        picker.SetOperationState(false,"ready");
        foreach(var lang in new[]{"en","es"})
        {
            TextCatalog.Current=TextCatalog.Load(Path.Combine(AppContext.BaseDirectory,"Languages"),lang); picker.ApplyLanguage();
            foreach(var size in new[]{new Size(540,620),new Size(460,520)})
            {
                picker.Size=size; picker.PerformLayout(); Application.DoEvents();
                using var bitmap=new Bitmap(size.Width,size.Height); picker.DrawToBitmap(bitmap,new Rectangle(Point.Empty,size));
                bitmap.Save(Path.Combine(output,$"{lang}-picker-{size.Width}.png"),ImageFormat.Png);
            }
        }
        allowSuccess=true; var operation=picker.RestoreSelectedAsync(); picker.RestoreSelectedAsync().GetAwaiter().GetResult();
        if(requests!=2) throw new Exception("Repeated input dispatched concurrent restores.");
        release.SetResult(true); while(!operation.IsCompleted) Application.DoEvents(); operation.GetAwaiter().GetResult();
        if(picker.Visible) throw new Exception("Picker did not close after successful restoration.");
        picker.Dispose(); form.WindowState=FormWindowState.Normal;
        Console.WriteLine("PASS: exact selected checkpoint, failure/empty/busy/repeated-input guards, minimized-manager floating window, icon, language preservation and success close; 4 picker previews.");
        Task.Run(async () =>
        {
            var exe = Path.ChangeExtension(typeof(Program).Assembly.Location, ".exe");
            var p = new Profile(exe, Path.Combine(output, "unused-saves"), output, LaunchPath: exe,
                LaunchArguments: "--fake-game", WorkingDirectory: output, RequestWindowClose: true);
            try { new ProcessGuard().EnsureClosed(p); }
            catch (GameRunningException) { Console.WriteLine("SKIP lifecycle fixture: another matching game/test process is running."); return; }
            var lifecycle = new WindowsGameLifecycle();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await lifecycle.LaunchAndWaitAsync(p, null, timeout.Token);
            var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe));
            try
            {
                var fixture = processes.Single();
                while (fixture.MainWindowHandle == IntPtr.Zero) { await Task.Delay(100, timeout.Token); fixture.Refresh(); }
                await lifecycle.StopAndWaitAsync(p, null, timeout.Token);
                if (!fixture.HasExited) throw new Exception("Fixture did not close.");
                Console.WriteLine("PASS: real Windows launch, exact-path detection, window-close request, exit and quiet-period checks on isolated fixture.");
            }
            finally
            {
                foreach (var process in processes)
                {
                    // Only the synthetic child apphost created above, never a game process.
                    if (!process.HasExited && process.MainModule?.FileName == exe) process.Kill();
                    process.Dispose();
                }
            }
        }).GetAwaiter().GetResult();
    }
    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control c in root.Controls) { yield return c; foreach (var nested in Descendants(c)) yield return nested; }
    }
}
