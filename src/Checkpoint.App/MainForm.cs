using System.Runtime.InteropServices;
using Checkpoint.Core;

namespace Checkpoint.App;

public sealed class MainForm : Form
{
    private readonly LinkLabel updateLink=new() { AutoSize=true, Visible=false, LinkColor=Theme.Gold, ActiveLinkColor=Theme.Gold, VisitedLinkColor=Theme.Gold,
        LinkBehavior=LinkBehavior.HoverUnderline, Font=new Font("Segoe UI Semibold",9), Margin=new Padding(10,0,0,0) };
    private ReleaseInfo? availableUpdate;
    private readonly string buildVersion;
    private readonly string? dataDirectoryArgument;
    private GlobalShortcuts? shortcuts;
    private DateTime lastShortcutRetry;
    private readonly Label shortcutHint=new() { Dock=DockStyle.Fill,AutoEllipsis=true,ForeColor=Theme.Muted };
    private readonly TextBox executable=Field(), saves=Field(), library=Field(), aliases=Field(), launch=Field(), arguments=Field(), working=Field();
    private readonly TextBox name=new() { Width=270, MaxLength=120, BackColor=Theme.Input, ForeColor=Theme.Text, BorderStyle=BorderStyle.FixedSingle };
    private readonly TextBox search=new() { Width=270, BackColor=Theme.Input, ForeColor=Theme.Text, BorderStyle=BorderStyle.FixedSingle };
    private readonly CheckBox emergencies=Check("library.emergencies"), restart=Check("settings.restart"), closeWindow=Check("settings.close"), live=Check("settings.live");
    private readonly CheckBox external=Check("external.option");
    private readonly ComboBox connectionMode=new() { DropDownStyle=ComboBoxStyle.DropDownList,DrawMode=DrawMode.OwnerDrawFixed,ItemHeight=24,FlatStyle=FlatStyle.Flat,Dock=DockStyle.Fill,BackColor=Theme.Input,ForeColor=Theme.Text };
    private readonly Button recoverButton=Theme.Button("");
    private readonly Label recoveryState=new() { AutoSize=true,ForeColor=Theme.Gold,MaximumSize=new Size(760,0) };
    private bool changingConnectionMode;
    private readonly ComboBox portPicker=new() { DropDownStyle=ComboBoxStyle.DropDownList,FlatStyle=FlatStyle.Flat,Dock=DockStyle.Fill,BackColor=Theme.Input,ForeColor=Theme.Text };
    private PortProfiles portProfiles=new();
    private string selectedPort=Ports.Supermedo;
    private bool changingPort;
    private bool? pendingRecovery;
    private CheckpointPicker? picker;
    private Button? renameButton,restoreButton,deleteButton;
    private readonly ListView list=new() { Dock=DockStyle.Fill, View=View.Details, FullRowSelect=true, MultiSelect=true, HideSelection=false, BorderStyle=BorderStyle.None };
    private readonly Label status=new() { Dock=DockStyle.Fill, AutoEllipsis=true, ForeColor=Theme.Muted, Padding=new Padding(0,12,10,0) };
    private readonly Label summary=new() { Dock=DockStyle.Fill, ForeColor=Theme.Muted }, selection=new() { Dock=DockStyle.Fill, ForeColor=Theme.Gold, AutoEllipsis=true };
    private readonly TabControl tabs=new DarkTabs { Dock=DockStyle.Fill, DrawMode=TabDrawMode.OwnerDrawFixed, SizeMode=TabSizeMode.Fixed, ItemSize=new Size(195,40), Padding=new Point(16,8) };
    private readonly ComboBox languagePicker=new() { DropDownStyle=ComboBoxStyle.DropDownList, DrawMode=DrawMode.OwnerDrawFixed, ItemHeight=24, FlatStyle=FlatStyle.Flat, Width=174, BackColor=Theme.Input, ForeColor=Theme.Text };
    private readonly Label mode=new() { AutoSize=true, ForeColor=Theme.Gold, Font=new Font("Segoe UI Semibold",9) };
    private readonly ImageList rowHeight=new() { ImageSize=new Size(32,38),ColorDepth=ColorDepth.Depth32Bit };
    private readonly Bitmap rowImage=AppAssets.RowImage();
    private readonly ProgressBar progressBar=new() { Dock=DockStyle.Top, Height=3, Visible=false, Style=ProgressBarStyle.Marquee };
    private readonly Button cancel=Theme.Button(TextCatalog.T("ui.cancel"));
    private readonly string appData;
    private readonly bool preview;
    private readonly System.Windows.Forms.Timer watcher=new() { Interval=100 };
    private bool checkingExternal;
    private int watchedPid;
    private string lastConnectionDetails="";
    private DateTime lastFullDiscovery=DateTime.MinValue;
    private bool busy, prompting, changingLanguage;
    private CancellationTokenSource? cancellation;
    private Manifest[] manifests=[];
    private string? nextSelection;
    private TableLayoutPanel? libraryLayout;
    private TableLayoutPanel? footer;
    private FlowLayoutPanel? libraryActions;
    private static TextBox Field()=>new() { Dock=DockStyle.Fill, BackColor=Theme.Input, ForeColor=Theme.Text, BorderStyle=BorderStyle.FixedSingle };
    private static CheckBox Check(string key)=>new() { Tag=key, Text=TextCatalog.T(key), AutoSize=true, ForeColor=Theme.Text, Margin=new Padding(0,8,20,8), FlatStyle=FlatStyle.Flat };
    private static Label Caption(string key,bool heading=false)=>new() { Tag=key, Text=TextCatalog.T(key), AutoSize=true, ForeColor=heading ? Theme.Text : Theme.Muted,
        Font=new Font(heading ? "Segoe UI Semibold" : "Segoe UI",heading ? 17 : 10) };

    public MainForm(string? dataDirectory=null,bool preview=false)
    {
        this.preview=preview; dataDirectoryArgument=dataDirectory;
        appData=dataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BloodborneCheckpointManager");
        Directory.CreateDirectory(appData); UiPreferences.Initialize(appData);
        Text=TextCatalog.T("app.name"); Icon=AppAssets.ApplicationIcon; Size=new Size(1160,850); MinimumSize=new Size(980,740);
        Font=new Font("Segoe UI",10); ForeColor=Theme.Text; BackColor=Theme.Background; StartPosition=FormStartPosition.CenterScreen;
        var root=new TableLayoutPanel { Dock=DockStyle.Fill, ColumnCount=1, RowCount=3, Padding=new Padding(26) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,108)); root.RowStyles.Add(new RowStyle(SizeType.Percent,100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute,64)); Controls.Add(root);
        var header=new TableLayoutPanel { Dock=DockStyle.Fill, ColumnCount=2, RowCount=1 };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,190));
        var brand=new FlowLayoutPanel { Dock=DockStyle.Fill, FlowDirection=FlowDirection.TopDown, WrapContents=false };
        brand.Controls.Add(new Label { Text="BLOODBORNE", Font=new Font("Georgia",25), ForeColor=Theme.Gold, AutoSize=true, Margin=new Padding(0) });
        brand.Controls.Add(new Label { Text="CHECKPOINT MANAGER", Font=new Font("Segoe UI Semibold",11), AutoSize=true, Margin=new Padding(1,3,0,11) });
        buildVersion=System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(MainForm).Assembly)?.InformationalVersion.Split('+')[0]
            ?? typeof(MainForm).Assembly.GetName().Version?.ToString(3) ?? "";
        var versionRow=new FlowLayoutPanel { AutoSize=true, WrapContents=false, Margin=new Padding(0) };
        versionRow.Controls.Add(new Label { Text="v"+buildVersion, Font=new Font("Segoe UI",9), ForeColor=Theme.Muted, AutoSize=true, Margin=new Padding(1,0,0,0) });
        versionRow.Controls.Add(updateLink); updateLink.LinkClicked+=async(_,_)=>await InstallUpdate();
        brand.Controls.Add(versionRow);
        header.Controls.Add(brand,0,0);
        var preferences=new FlowLayoutPanel { Dock=DockStyle.Fill, FlowDirection=FlowDirection.TopDown, WrapContents=false, Padding=new Padding(8,0,0,0) };
        preferences.Controls.Add(Caption("language.label")); preferences.Controls.Add(languagePicker); mode.Margin=new Padding(0,14,0,0); preferences.Controls.Add(mode); header.Controls.Add(preferences,1,0);
        languagePicker.DrawItem+=(_,e)=>
        {
            using var fill=new SolidBrush(Theme.Input); e.Graphics.FillRectangle(fill,e.Bounds);
            var text=e.Index>=0 ? languagePicker.Items[e.Index]?.ToString() : languagePicker.SelectedItem?.ToString();
            TextRenderer.DrawText(e.Graphics,text,Font,Rectangle.Inflate(e.Bounds,-6,0),Theme.Text,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
        };
        connectionMode.DrawItem+=(_,e)=>
        {
            using var fill=new SolidBrush(Theme.Input); e.Graphics.FillRectangle(fill,e.Bounds);
            var text=e.Index>=0 ? connectionMode.Items[e.Index]?.ToString() : connectionMode.SelectedItem?.ToString();
            TextRenderer.DrawText(e.Graphics,text,Font,Rectangle.Inflate(e.Bounds,-6,0),Theme.Text,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
        };
        root.Controls.Add(header,0,0); root.Controls.Add(tabs,0,1);
        tabs.DrawItem+=(_,e)=>
        {
            using var brush=new SolidBrush(e.Index==tabs.SelectedIndex ? Theme.Surface : Theme.Background); e.Graphics.FillRectangle(brush,e.Bounds);
            TextRenderer.DrawText(e.Graphics,tabs.TabPages[e.Index].Text,Font,e.Bounds,e.Index==tabs.SelectedIndex ? Theme.Gold : Theme.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
            if (e.Index==tabs.SelectedIndex) { using var line=new Pen(Theme.Gold,2); e.Graphics.DrawLine(line,e.Bounds.Left+15,e.Bounds.Bottom-3,e.Bounds.Right-15,e.Bounds.Bottom-3); }
        };
        BuildLibrary(); BuildSettings(); BuildRecovery();
        watcher.Tick+=async(_,_)=>{ RetryShortcuts(); await WatchExternal(); };
        if (!preview) watcher.Start();
        FormClosed+=(_,_)=>watcher.Dispose();
        footer=new TableLayoutPanel { Dock=DockStyle.Fill, ColumnCount=2, RowCount=2, Padding=new Padding(0,10,0,0) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,0)); footer.RowStyles.Add(new RowStyle(SizeType.Absolute,4)); footer.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        cancel.Tag="ui.cancel"; cancel.Visible=false; cancel.Enabled=false; cancel.Click+=(_,_)=>{ cancellation?.Cancel(); SetWaiting(false); status.Text=TextCatalog.T("ui.cancelling"); };
        footer.Controls.Add(progressBar,0,0); footer.SetColumnSpan(progressBar,2); footer.Controls.Add(status,0,1); footer.Controls.Add(cancel,1,1); root.Controls.Add(footer,0,2);
        Theme.StyleList(list); rowHeight.Images.Add(rowImage); list.SmallImageList=rowHeight;
        status.TextChanged+=(_,_)=>picker?.SetOperationState(busy,status.Text);
        list.SizeChanged+=(_,_)=>ResizeColumns();
        list.KeyDown+=async(_,e)=>
        {
            if(busy || prompting) return;
            if(e.Control && e.KeyCode==Keys.A) { e.Handled=e.SuppressKeyPress=true; foreach(ListViewItem item in list.Items) item.Selected=true; }
            else if(e.KeyCode==Keys.Delete) { e.Handled=e.SuppressKeyPress=true; await DeleteSelected(); }
        };
        list.SelectedIndexChanged+=(_,_)=>{ if (Selected() is { } m) name.Text=m.Name; UpdateSelection(); };
        search.TextChanged+=(_,_)=>RenderList(); emergencies.CheckedChanged+=(_,_)=>RenderList(); live.CheckedChanged+=(_,_)=>UpdateMode(); external.CheckedChanged+=(_,_)=>UpdateMode();
        library.Text=Path.Combine(appData,"Checkpoints"); status.Text=TextCatalog.T("ui.configure");
        try
        {
            var path=Path.Combine(appData,"settings.json");
            if (File.Exists(path))
            {
                portProfiles=PortProfiles.Load(appData);
                LoadProfile(JsonDisk.Read<Profile>(path).ForSupportedManagerMode());
            }
        }
        catch(Exception ex) { status.Text=TextCatalog.T("ui.settings_load",ex.Message); }
        changingPort=true; portPicker.SelectedIndex=Array.IndexOf(Ports.All,selectedPort); changingPort=false;
        portPicker.SelectedIndexChanged+=async(_,_)=>
        {
            if(changingPort || portPicker.SelectedIndex<0) return;
            portProfiles.Profiles[selectedPort]=Current();
            LoadProfile(portProfiles.Get(Ports.All[portPicker.SelectedIndex],appData));
            pendingRecovery=null; manifests=[]; nextSelection=null; name.Clear(); watchedPid=0; RenderList(); UpdateRecovery(); ApplyLanguage();
            if(!preview && Directory.Exists(saves.Text)) await RefreshLibrary();
            else status.Text=TextCatalog.T("ui.configure");
        };
        changingConnectionMode=true; connectionMode.SelectedIndex=live.Checked ? 1 : 0; changingConnectionMode=false;
        connectionMode.SelectedIndexChanged+=(_,_)=>
        {
            if(changingConnectionMode) return;
            live.Checked=connectionMode.SelectedIndex!=0; external.Checked=connectionMode.SelectedIndex==1; watchedPid=0;
        };
        foreach(var field in new[]{saves,library}) field.TextChanged+=(_,_)=>
        {
            pendingRecovery=null; manifests=[]; RenderList(); UpdateRecovery(); watchedPid=0;
        };
        changingLanguage=true;
        foreach(var choice in TextCatalog.Discover(UiPreferences.LanguagesDirectory)) languagePicker.Items.Add(choice);
        languagePicker.SelectedItem=languagePicker.Items.Cast<LanguageChoice>().FirstOrDefault(p=>p.Code==TextCatalog.Current.Language) ?? languagePicker.Items[0];
        changingLanguage=false;
        languagePicker.SelectedIndexChanged+=(_,_)=>{ if (!changingLanguage && languagePicker.SelectedItem is LanguageChoice c) ChangeLanguage(c.Code); };
        ApplyLanguage();
        Shown+=async(_,_)=>{ if (!preview && saves.Text.Length>0) await RefreshLibrary(); if (!preview) await CheckForUpdates(manual:false); };
        FormClosing+=(_,e)=>{ if(busy) { e.Cancel=true; status.Text=TextCatalog.T("ui.close_busy"); } };
        Disposed+=(_,_)=>{ picker?.Dispose(); rowHeight.Dispose(); rowImage.Dispose(); };
    }
    public void ChangeLanguage(string code)
    {
        if(busy || prompting) return;
        var candidate=TextCatalog.Load(UiPreferences.LanguagesDirectory,code);
        TextCatalog.Current=candidate;
        var editedName=name.Text; ApplyLanguage(); name.Text=editedName;
        changingLanguage=true; languagePicker.SelectedItem=languagePicker.Items.Cast<LanguageChoice>().FirstOrDefault(p=>p.Code==candidate.Language); changingLanguage=false;
        if (!preview)
        {
            try { JsonDisk.Write(Path.Combine(appData,"ui-settings.json"),new UiPreferences(candidate.Language)); }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException)
            { Log(ex.ToString()); AppDialog.Show(this,TextCatalog.T("ui.stopped_title"),TextCatalog.T("language.save_error",ex.Message)); }
        }
        status.Text=TextCatalog.T(candidate.LoadWarning==null ? "language.saved" : "language.fallback");
    }
    private void ApplyLanguage()
    {
        void Walk(Control c) { if(c.Tag is string key) c.Text=TextCatalog.T(key); foreach(Control child in c.Controls) Walk(child); }
        Walk(this); Text=TextCatalog.T("app.name"); search.PlaceholderText=TextCatalog.T("library.search"); name.PlaceholderText=TextCatalog.T("library.name_hint");
        var keys=new[]{"column.name","column.created","column.type","column.size"}; for(int i=0;i<keys.Length;i++) list.Columns[i].Text=TextCatalog.T(keys[i]);
        UpdateMode(); RenderList(); LayoutLibraryActions(); tabs.Invalidate();
        if(availableUpdate!=null) updateLink.Text=TextCatalog.T("update.available_link",availableUpdate.Version);
        var modeIndex=connectionMode.SelectedIndex;
        changingConnectionMode=true; connectionMode.Items.Clear();
        foreach(var key in new[]{"settings.mode_offline",selectedPort==Ports.Awaken ? "settings.mode_awaken" : "settings.mode_external"}) connectionMode.Items.Add(TextCatalog.T(key));
        connectionMode.SelectedIndex=Math.Clamp(modeIndex,0,1); changingConnectionMode=false;
        recoverButton.Text=TextCatalog.T("recovery.button"); UpdateRecovery(); picker?.ApplyLanguage(); UpdateShortcutHint();
    }
    private void LoadProfile(Profile p)
    {
        Ports.Validate(p.Port); selectedPort=p.Port;
        executable.Text=p.Executable; saves.Text=p.Saves; library.Text=p.Library; aliases.Text=p.ExtraProcessNames;
        launch.Text=p.LaunchPath; arguments.Text=p.LaunchArguments; working.Text=p.WorkingDirectory;
        restart.Checked=p.RestartAfterRestore; closeWindow.Checked=p.RequestWindowClose; live.Checked=p.LiveCheckpoints; external.Checked=p.ExternalBridge;
        changingConnectionMode=true; connectionMode.SelectedIndex=p.LiveCheckpoints ? 1 : 0; changingConnectionMode=false;
    }
    private void UpdateMode()=>mode.Text=TextCatalog.T(!live.Checked ? "mode.offline" : selectedPort==Ports.Awaken ? "mode.awaken" : "mode.external");
    private void UpdateSelection()
    {
        int count=list.SelectedItems.Count;
        selection.Text=count>1 ? TextCatalog.T("library.selected_count",count) : Selected() is { } m ? TextCatalog.T("library.selected",m.Name) : TextCatalog.T("library.no_selection");
        if(renameButton!=null) renameButton.Enabled=count==1;
        if(restoreButton!=null) restoreButton.Enabled=count==1;
        if(deleteButton!=null) { deleteButton.Enabled=count>0; deleteButton.Text=count>1 ? TextCatalog.T("library.delete_count",count) : TextCatalog.T("library.delete"); }
    }
    private TabPage Page(string key)
    {
        var page=new TabPage(TextCatalog.T(key)) { Tag=key, BackColor=Theme.Surface, ForeColor=Theme.Text, Padding=new Padding(18), AutoScroll=true }; tabs.TabPages.Add(page); return page;
    }
    private static Panel PageHeading(string title,string subtitle)
    {
        var panel=new FlowLayoutPanel { Dock=DockStyle.Fill, FlowDirection=FlowDirection.TopDown, WrapContents=false };
        panel.Controls.Add(Caption(title,true)); panel.Controls.Add(Caption(subtitle)); return panel;
    }
    private void BuildLibrary()
    {
        var page=Page("nav.checkpoints"); var layout=new TableLayoutPanel { Dock=DockStyle.Fill, ColumnCount=1, RowCount=7 };
        libraryLayout=layout;
        foreach(var height in new[]{60,46,28}) layout.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,28)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,92)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,28)); page.Controls.Add(layout);
        layout.Controls.Add(PageHeading("library.title","library.subtitle"),0,0);
        var filters=new FlowLayoutPanel { Dock=DockStyle.Fill, WrapContents=false }; search.Margin=new Padding(0,7,18,0); filters.Controls.Add(search); filters.Controls.Add(emergencies); filters.Controls.Add(Button("library.refresh",RefreshLibrary)); layout.Controls.Add(filters,0,1); layout.Controls.Add(summary,0,2);
        foreach(var width in new[]{380,190,110,95}) list.Columns.Add("",width); layout.Controls.Add(list,0,3); selection.Padding=new Padding(0,9,0,0); layout.Controls.Add(selection,0,4);
        var actions=new FlowLayoutPanel { Dock=DockStyle.Fill, Padding=new Padding(0,12,0,0), WrapContents=true };
        libraryActions=actions; actions.SizeChanged+=(_,_)=>LayoutLibraryActions();
        name.Margin=new Padding(0,5,12,8); actions.Controls.Add(name); actions.Controls.Add(Button("library.create",Create)); renameButton=Button("library.rename",Rename); restoreButton=Button("library.restore",RestoreSelected,true); deleteButton=Button("library.delete",DeleteSelected,danger:true); actions.Controls.Add(renameButton); actions.Controls.Add(restoreButton); actions.Controls.Add(deleteButton); actions.Controls.Add(Button("picker.open",TogglePicker)); layout.Controls.Add(actions,0,5);
        layout.Controls.Add(shortcutHint,0,6); UpdateShortcutHint();
    }
    private void BuildSettings()
    {
        var page=Page("nav.settings"); var layout=new TableLayoutPanel { Dock=DockStyle.Top, AutoSize=true, ColumnCount=1, RowCount=2 };
        var heading=PageHeading("settings.title","settings.subtitle"); heading.Height=62; layout.Controls.Add(heading,0,0); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,62));
        var fields=new TableLayoutPanel { Dock=DockStyle.Top, AutoSize=true, ColumnCount=3, RowCount=8 };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,170)); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,122));
        fields.Controls.Add(Caption("settings.port"),0,0); portPicker.Margin=new Padding(0,8,14,8); portPicker.Items.AddRange(["Supermedo","Awaken · BloodBorneButPc"]); fields.Controls.Add(portPicker,1,0); fields.SetColumnSpan(portPicker,2);
        AddField(fields,1,"settings.executable",executable,"exe"); AddField(fields,2,"settings.saves",saves,"folder"); AddField(fields,3,"settings.library",library,"folder");
        var note=Caption("settings.paths_note"); note.MaximumSize=new Size(740,0); note.Margin=new Padding(0,8,0,8); fields.Controls.Add(note,0,4); fields.SetColumnSpan(note,3);
        fields.Controls.Add(Caption("settings.connection"),0,5); connectionMode.Margin=new Padding(0,0,14,8); fields.Controls.Add(connectionMode,1,5); fields.SetColumnSpan(connectionMode,2);
        foreach(var key in new[]{"settings.mode_offline","settings.mode_external"}) connectionMode.Items.Add(TextCatalog.T(key)); connectionMode.SelectedIndex=0;
        var startNote=Caption("settings.start_note"); startNote.MaximumSize=new Size(740,0); startNote.Margin=new Padding(0,8,0,8); fields.Controls.Add(startNote,0,6); fields.SetColumnSpan(startNote,3);
        var actions=new FlowLayoutPanel { AutoSize=true, Dock=DockStyle.Fill, WrapContents=false };
        actions.Controls.Add(Button("settings.save",SaveSettings,true)); actions.Controls.Add(Button("settings.check_updates",()=>CheckForUpdates(manual:true)));
        fields.Controls.Add(actions,1,7); fields.SetColumnSpan(actions,2);
        layout.Controls.Add(fields,0,1); page.Controls.Add(layout);
    }
    private void BuildRecovery()
    {
        var page=Page("nav.recovery"); var panel=new FlowLayoutPanel { Dock=DockStyle.Fill, FlowDirection=FlowDirection.TopDown, WrapContents=false };
        panel.Controls.Add(Caption("recovery.title",true)); panel.Controls.Add(Caption("recovery.subtitle"));
        var instructions=Caption("recovery.instructions"); instructions.ForeColor=Theme.Text; instructions.MaximumSize=new Size(780,0); instructions.Margin=new Padding(0,30,0,26); panel.Controls.Add(instructions);
        panel.Controls.Add(recoveryState); recoverButton.Tag="recovery.button"; recoverButton.Click+=async(_,_)=>await Recover(); recoverButton.Enabled=false; panel.Controls.Add(recoverButton);
        var note=Caption("recovery.note"); note.MaximumSize=new Size(780,0); note.ForeColor=Theme.Gold; note.Margin=new Padding(0,32,0,0); panel.Controls.Add(note); page.Controls.Add(panel);
    }
    private void AddField(TableLayoutPanel panel,int row,string key,TextBox box,string kind)
    {
        var label=Caption(key); label.ForeColor=Theme.Text; label.Margin=new Padding(0,11,5,9); panel.Controls.Add(label,0,row); box.Margin=new Padding(0,9,14,9); panel.Controls.Add(box,1,row);
        if(kind=="none") return;
        panel.Controls.Add(Button("settings.browse",()=>
        {
            var directory=BrowseDirectory(box.Text);
            if(kind!="folder")
            {
                using var dialog=new OpenFileDialog { Title=TextCatalog.T(key), Filter=TextCatalog.T(kind=="exe" ? "settings.exe_filter" : "settings.launch_filter"), InitialDirectory=directory, RestoreDirectory=true };
                if(File.Exists(box.Text.Trim())) dialog.FileName=box.Text.Trim();
                if(dialog.ShowDialog(this)==DialogResult.OK) { box.Text=dialog.FileName; if(kind=="launch" && string.IsNullOrWhiteSpace(working.Text)) working.Text=Path.GetDirectoryName(dialog.FileName); }
            }
            else { using var dialog=new FolderBrowserDialog { Description=TextCatalog.T(key), UseDescriptionForTitle=true, InitialDirectory=directory, SelectedPath=directory }; if(dialog.ShowDialog(this)==DialogResult.OK) box.Text=dialog.SelectedPath; }
            return Task.CompletedTask;
        }),2,row);
    }
    private static string BrowseDirectory(string value)
    {
        var path=value.Trim();
        try
        {
            if(Path.IsPathFullyQualified(path))
                while(!string.IsNullOrEmpty(path))
                {
                    if(Directory.Exists(path)) return path;
                    path=Path.GetDirectoryName(path);
                }
        }
        catch(Exception ex) when(ex is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException) { }
        var documents=Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Directory.Exists(documents) ? documents : AppContext.BaseDirectory;
    }
    private Button Button(string key,Func<Task> action,bool primary=false,bool danger=false)
    {
        var button=Theme.Button(TextCatalog.T(key),primary,danger); button.Tag=key; button.Click+=async(_,_)=>await action(); return button;
    }
    private Profile Current() => new(executable.Text.Trim(), saves.Text.Trim(), library.Text.Trim(), aliases.Text.Trim(), launch.Text.Trim(), arguments.Text, working.Text.Trim(), restart.Checked, closeWindow.Checked, live.Checked, external.Checked,selectedPort);
    private async Task TogglePicker()
    {
        if(picker?.Visible==true) { picker.Hide(); return; }
        picker ??=new CheckpointPicker(m=>RestoreCheckpoint(m,inlineErrors:true),
            ()=>Run((_,_,_)=>Task.FromResult(TextCatalog.T("ui.refresh_done")),inlineErrors:true),
            ()=>{ WindowState=FormWindowState.Normal; Show(); Activate(); });
        picker.ApplyLanguage(); picker.SetCheckpoints(manifests); picker.SetOperationState(busy,status.Text); picker.ShowFloating();
        if(!busy && !prompting) await Run((_,_,_)=>Task.FromResult(TextCatalog.T("ui.refresh_done")),inlineErrors:true);
    }
    private void UpdateRecovery()
    {
        recoveryState.Text=TextCatalog.T(pendingRecovery==true ? "recovery.pending" : pendingRecovery==false ? "recovery.none" : "recovery.unknown");
        recoverButton.Enabled=pendingRecovery==true && !busy;
    }
    private string ExternalStatus(System.Text.Json.JsonElement hello)
    {
        var status=ExternalStatusCore(hello);
        return hello.TryGetProperty("hostSha256",out var host) && host.GetString() is { } hash && ExternalBridge.IsUnreviewed(hash)
            ? status+" "+TextCatalog.T("external.structural_note") : status;
    }
    private string ExternalStatusCore(System.Text.Json.JsonElement hello) =>
        selectedPort==Ports.Awaken && hello.TryGetProperty("stage",out var state) && state.GetString()=="observer" && hello.TryGetProperty("reason",out var reason) && !string.IsNullOrWhiteSpace(reason.GetString())
            ? TextCatalog.T("external.connection_failed",reason.GetString()!) : TextCatalog.T(
        hello.TryGetProperty("stage",out var stage) && stage.GetString()=="startup" ? "external.initializing" :
        hello.GetProperty("capture").GetBoolean() ? selectedPort==Ports.Awaken ? "external.awaken_connected" : "external.live_connected" : "external.connected");
    private async Task WatchExternal()
    {
        if (preview || busy || checkingExternal || portPicker.DroppedDown || !external.Checked || !live.Checked) return;
        checkingExternal=true;
        try
        {
            var full=DateTime.UtcNow-lastFullDiscovery>TimeSpan.FromSeconds(2);
            if (full) lastFullDiscovery=DateTime.UtcNow;
            var hints=new[] { "bb-probe", "bbport", Path.GetFileNameWithoutExtension(executable.Text.Trim()) };
            var profile=Current();
            var games=await Task.Run(()=>ExternalBridge.Discover(full ? null : hints,profile.Port));
            // Discovery runs in the background without changing the selector's state.
            // Discard results if the user edited settings or opened the dropdown meanwhile.
            if(IsDisposed || busy || prompting || portPicker.DroppedDown || profile!=Current()) return;
            if (games.Length!=1) { if (games.Length==0) watchedPid=0; return; }
            if (games[0].Pid==watchedPid) return;
            // Lock port changes only during an actual attachment, never on every timer tick.
            portPicker.Enabled=false;
            try
            {
                using var bridge=await LiveBridgeClient.ConnectAsync(profile,CancellationToken.None,observationOnly:true);
                if (!bridge.Greeting.TryGetProperty("stage",out var stage) || stage.GetString()!="startup") watchedPid=games[0].Pid;
                var details=bridge.Greeting.GetRawText();
                var diagnostic=games[0].Pid+":"+bridge.Greeting.GetProperty("capture").GetBoolean()+":"+
                    (bridge.Greeting.TryGetProperty("reason",out var why) ? why.GetString() : "");
                if(diagnostic!=lastConnectionDetails) { lastConnectionDetails=diagnostic; Log("Game connection details: "+details); }
                if (!busy && !IsDisposed) { var message=ExternalStatus(bridge.Greeting); if (status.Text!=message) { status.Text=message; Log(message);  } }
            }
            finally { if(!IsDisposed) portPicker.Enabled=true; }
        }
        catch (Exception ex)
        {
            // Source configuration may not be ready during startup. A timed-out
            // remote call must never be retried automatically on that instance.
            if (ex is ExternalAttachException { Code: -1 or -2 })
            {
                var games=ExternalBridge.Discover(port:selectedPort);
                if (games.Length==1) watchedPid=games[0].Pid;
            }
            if (!busy && !IsDisposed) status.Text=TextCatalog.T("external.watch_error",ex.Message);
        }
        finally { checkingExternal=false; }
    }
    private static CheckpointStore Store(Profile p) => new(p, new ProcessGuard());
    private Manifest[] SelectedCheckpoints()=>list.SelectedItems.Cast<ListViewItem>().Select(item=>(Manifest)item.Tag!).ToArray();
    private Manifest? Selected() => list.SelectedItems.Count == 1 ? list.SelectedItems[0].Tag as Manifest : null;
    private void RenderList()
    {
        var selected = nextSelection!=null ? new HashSet<string>([nextSelection],StringComparer.Ordinal) : SelectedCheckpoints().Select(m=>m.Id).ToHashSet(StringComparer.Ordinal); list.BeginUpdate(); list.Items.Clear();
        foreach (var m in manifests.Where(m => (emergencies.Checked || !m.Emergency) && m.Name.Contains(search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase)))
        {
            var item = new ListViewItem(m.Name,0) { Tag = m }; item.SubItems.Add(m.Created.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")); item.SubItems.Add(TextCatalog.T(m.Emergency ? "type.emergency" : "type.manual")); item.SubItems.Add(TextCatalog.T("library.size_mb",m.Files.Sum(f=>f.Size)/1048576d)); list.Items.Add(item);
            if (selected.Contains(m.Id)) { item.Selected = true; item.EnsureVisible(); }
        }
        list.EndUpdate(); nextSelection = null;
        summary.Text = manifests.Length == 0 ? TextCatalog.T("library.empty") : TextCatalog.T("library.counts",list.Items.Count,manifests.Count(m=>!m.Emergency),manifests.Count(m=>m.Emergency));
        UpdateSelection(); ResizeColumns();
        picker?.SetCheckpoints(manifests);
    }
    private void ResizeColumns()
    {
        if(list.Columns.Count!=4) return;
        // Use bounds: native printing temporarily changes ClientSize while
        // hiding scrollbars, which must not resize the columns again.
        var scrollbar=list.Items.Count*38+28>list.Height ? SystemInformation.VerticalScrollBarWidth : 0;
        list.Columns[0].Width=Math.Max(230,list.Width-190-110-95-scrollbar-4);
    }
    private void LayoutLibraryActions()
    {
        if(libraryActions==null || libraryLayout==null || libraryActions.ClientSize.Width<=0) return;
        var height=Math.Max(64,libraryActions.GetPreferredSize(new Size(libraryActions.ClientSize.Width,0)).Height);
        if(libraryLayout.RowStyles[5].Height!=height) libraryLayout.RowStyles[5].Height=height;
    }
    private Task RefreshLibrary() => Execute(_ => TextCatalog.T("ui.refresh_done"));
    private Task SaveSettings() => Execute(p =>
    {
        _ = Store(p); if (!File.Exists(p.Executable) || !Path.GetExtension(p.Executable).Equals(".exe", StringComparison.OrdinalIgnoreCase)) throw new IOException(TextCatalog.T("ui.executable_missing"));
        if (!Directory.Exists(p.Saves)) throw new IOException(TextCatalog.T("ui.saves_missing"));
        // A port's user folder holds savedata\ alongside caches and settings; the bridge compares against savedata itself.
        if (!string.Equals(Path.GetFileName(Path.TrimEndingDirectorySeparator(p.Saves)),"savedata",StringComparison.OrdinalIgnoreCase) && Directory.Exists(Path.Combine(p.Saves,"savedata")))
            throw new IOException(TextCatalog.T("ui.saves_parent",Path.Combine(p.Saves,"savedata")));
        if (!p.LiveCheckpoints && p.RestartAfterRestore) new WindowsGameLifecycle().ValidateLaunch(p);
        var reviewed=!p.LiveCheckpoints || ExternalBridge.ResolveRuntime(p.Executable,p.Port).Reviewed;
        portProfiles.Save(appData,p); return TextCatalog.T(reviewed ? "ui.settings_saved" : "ui.settings_saved_structural");
    });
    private Task Create()
    {
        var n = string.IsNullOrWhiteSpace(name.Text) ? "Checkpoint " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") : name.Text;
        return Run(async (p, progress, token) =>
        {
            Manifest m;
            if (p.LiveCheckpoints && GameRunning(p))
            {
                using var bridge = await WhileWaiting(() => LiveBridgeClient.ConnectAsync(p, token));
                await WhileWaiting(() => bridge.AcquireAsync(false, progress, token));
                token.ThrowIfCancellationRequested();
                m = await Task.Run(() => new CheckpointStore(p, bridge).Create(n));
                bridge.Complete();
            }
            else m = await Task.Run(() => Store(p).Create(n), token);
            nextSelection = m.Id; return TextCatalog.T("ui.created_prefix") + m.Name;
        });
    }
    private static bool GameRunning(Profile p)
    {
        try { new ProcessGuard().EnsureClosed(p); return false; }
        catch (GameRunningException) { return true; }
    }
    private Task Rename()
    {
        if (Selected() is not { } m) { status.Text = TextCatalog.T("ui.select_checkpoint"); return Task.CompletedTask; }
        var n = name.Text; return Execute(p => { Store(p).Rename(m.Id, n); return TextCatalog.T("ui.renamed"); });
    }
    private Task DeleteSelected()
    {
        if(busy || prompting) return Task.CompletedTask;
        var selected=SelectedCheckpoints();
        if(selected.Length==0) { status.Text=TextCatalog.T("ui.select_delete"); return Task.CompletedTask; }
        string message;
        if(selected.Length==1) message=TextCatalog.T("confirm.delete",selected[0].Name,selected[0].Created.ToLocalTime());
        else
        {
            var names=string.Join(Environment.NewLine,selected.Take(12).Select(m=>"• "+m.Name));
            if(selected.Length>12) names+=Environment.NewLine+TextCatalog.T("confirm.delete_more",selected.Length-12);
            message=TextCatalog.T("confirm.delete_many",selected.Length,names);
        }
        if(!Confirm(message,TextCatalog.T(selected.Length==1 ? "ui.delete_title" : "ui.delete_many_title"))) return Task.CompletedTask;
        return Execute(p=>
        {
            var result=Store(p).DeleteMany(selected.Select(m=>m.Id));
            if(result.Error!=null) return TextCatalog.T("ui.delete_partial",result.Deleted,selected.Length,result.Error);
            if(result.Residuals.Length>0)
            {
                Log("Delete cleanup pending: "+string.Join("; ",result.Residuals));
                return TextCatalog.T("ui.delete_cleanup",result.Deleted,result.Residuals.Length);
            }
            return selected.Length==1 ? TextCatalog.T("ui.deleted_prefix")+selected[0].Name : TextCatalog.T("ui.deleted_many",result.Deleted);
        });
    }
    private Task RestoreSelected()
    {
        if (Selected() is not { } m) { status.Text = TextCatalog.T("ui.select_restore"); return Task.CompletedTask; }
        return RestoreCheckpoint(m);
    }
    private Task RestoreLatest()
    {
        var m = manifests.FirstOrDefault(cp => !cp.Emergency);
        if (m == null) { status.Text = TextCatalog.T("ui.no_checkpoint"); return Task.CompletedTask; }
        return RestoreCheckpoint(m);
    }
    private async Task<bool> RestoreCheckpoint(Manifest m,bool inlineErrors=false)
    {
        if (busy || prompting) return false;
        var p = Current();
        if (p.LiveCheckpoints)
        {
            return await Run(async (profile, progress, token) =>
            {
                if (!GameRunning(profile))
                {
                    await Task.Run(() => Store(profile).Restore(m.Id), token);
                    return TextCatalog.T("ui.restored_prefix") + m.Name + TextCatalog.T("ui.open_game_suffix");
                }
                using var bridge = await WhileWaiting(() => LiveBridgeClient.ConnectAsync(profile, token));
                await Task.Run(() => Store(profile).ValidateRestore(m.Id), token);
                await WhileWaiting(() => bridge.AcquireAsync(true, progress, token));
                token.ThrowIfCancellationRequested();
                var store = new CheckpointStore(profile, bridge, phase => { if (phase == "Prepared") bridge.BeginMutation(); });
                progress.Report(TextCatalog.T("ui.restoring"));
                var original = await Task.Run(() => store.Restore(m.Id));
                bridge.Complete(); Log(TextCatalog.T("ui.original_prefix") + original);
                return TextCatalog.T("ui.restored_prefix") + m.Name + TextCatalog.T("ui.continue_suffix");
            },inlineErrors);
        }
        var close = p.RequestWindowClose ? TextCatalog.T("ui.close_warning") : TextCatalog.T("ui.close_wait");
        var reopen = p.RestartAfterRestore ? TextCatalog.T("ui.reopen",p.LaunchPath) : TextCatalog.T("ui.remain_closed");
        if (!Confirm(TextCatalog.T("confirm.offline",m.Name,m.Created.ToLocalTime(),p.Saves,close,reopen))) return false;
        return await Run(async (profile, progress, token) =>
        {
            var store = Store(profile);
            var result = await new RestoreWorkflow(new WindowsGameLifecycle()).RunAsync(profile, () => store.ValidateRestore(m.Id), () => store.Restore(m.Id), progress, token, SetWaiting);
            Log(TextCatalog.T("ui.original_prefix") + result.Original); return result.Message;
        },inlineErrors);
    }
    private Task Recover()
    {
        if (busy || prompting || !Confirm(TextCatalog.T("ui.recover_confirm"))) return Task.CompletedTask;
        return Execute(p => { Store(p).Recover(); return TextCatalog.T("ui.recovered"); });
    }
    private async Task CheckForUpdates(bool manual)
    {
        if(preview || busy) return;
        try
        {
            if(manual) status.Text=TextCatalog.T("update.checking");
            using var http=Updater.CreateClient(buildVersion);
            availableUpdate=await Updater.CheckAsync(http,buildVersion,CancellationToken.None);
            updateLink.Visible=availableUpdate!=null;
            if(availableUpdate!=null)
            {
                updateLink.Text=TextCatalog.T("update.available_link",availableUpdate.Version);
                Log($"Update available: {availableUpdate.Version} ({availableUpdate.PageUrl})");
            }
            if(!manual) return;
            if(availableUpdate!=null) await InstallUpdate(); else status.Text=TextCatalog.T("update.none",buildVersion);
        }
        catch(Exception ex) when(ex is HttpRequestException or IOException or TaskCanceledException or System.Text.Json.JsonException or ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            Log("Update check failed: "+ex.Message);
            if(manual) status.Text=TextCatalog.T("update.failed",ex.Message);
        }
    }
    private async Task InstallUpdate()
    {
        var release=availableUpdate;
        if(release==null || busy || prompting) return;
        var notes=release.Notes.Length>1500 ? release.Notes[..1500]+"…" : release.Notes;
        if(!Confirm(TextCatalog.T("update.confirm",release.Version,buildVersion,notes),TextCatalog.T("update.title"))) return;
        // The game keeps the bridge DLL from this folder loaded; it cannot be replaced while the game runs.
        var games=System.Diagnostics.Process.GetProcessesByName("bb-probe");
        var gameRunning=games.Length>0; foreach(var game in games) game.Dispose();
        if(gameRunning) { AppDialog.Show(this,TextCatalog.T("update.title"),TextCatalog.T("update.game_running")); return; }
        var install=AppContext.BaseDirectory;
        if(!Updater.IsWritable(install))
        {
            AppDialog.Show(this,TextCatalog.T("update.title"),TextCatalog.T("update.not_writable",install)); OpenReleasePage(release.PageUrl); return;
        }
        busy=true; tabs.Enabled=false; languagePicker.Enabled=false; progressBar.Visible=true;
        var applying=false;
        try
        {
            var updates=Path.Combine(appData,"updates");
            using var http=Updater.CreateClient(buildVersion);
            var progress=new Progress<int>(percent=>status.Text=TextCatalog.T("update.downloading",percent));
            var staging=await Updater.DownloadAsync(http,release,updates,progress,CancellationToken.None);
            status.Text=TextCatalog.T("update.restarting"); Log($"Installing update {release.Version} from {staging}");
            Updater.StartApply(staging,install,updates,Path.Combine(appData,"manager.log"),dataDirectoryArgument);
            applying=true;
        }
        catch(UpdateException ex)
        {
            status.Text=TextCatalog.T(ex.Key); Log("Update not installed: "+ex.Key);
            AppDialog.Show(this,TextCatalog.T("update.title"),TextCatalog.T(ex.Key)); OpenReleasePage(release.PageUrl);
        }
        catch(Exception ex) when(ex is HttpRequestException or IOException or TaskCanceledException or UnauthorizedAccessException or InvalidDataException or System.ComponentModel.Win32Exception)
        {
            status.Text=TextCatalog.T("update.failed",ex.Message); Log("Update failed: "+ex);
            AppDialog.Show(this,TextCatalog.T("update.title"),TextCatalog.T("update.failed",ex.Message));
        }
        finally { busy=false; tabs.Enabled=true; languagePicker.Enabled=true; progressBar.Visible=false; }
        if(applying) Application.Exit(); // The applier waits for this process to exit, then restarts the manager.
    }
    private static void OpenReleasePage(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute=true })?.Dispose(); }
        catch(Exception ex) when(ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }
    private bool Confirm(string message, string? title = null)
    {
        var pickerVisible=picker?.Visible==true;
        if(pickerVisible) picker!.Hide();
        prompting = true;
        try { if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal; Activate(); return AppDialog.Show(this,title ?? TextCatalog.T("ui.restore_title"),message,confirm:true); }
        finally { prompting = false; if(pickerVisible && !IsDisposed) picker!.ShowFloating(); }
    }
    private Task Execute(Func<Profile, string> action) => Run((p, _, token) => Task.Run(() => action(p), token));
    private void SetWaiting(bool waiting)
    {
        var visible=waiting && busy && cancellation?.IsCancellationRequested==false;
        cancel.Visible=visible; cancel.Enabled=visible;
        if(footer!=null) footer.ColumnStyles[1].Width=visible ? 180 : 0;
    }
    private async Task<T> WhileWaiting<T>(Func<Task<T>> action)
    {
        SetWaiting(true);
        try { return await action(); }
        finally { SetWaiting(false); }
    }
    private Task WhileWaiting(Func<Task> action) => WhileWaiting(async () => { await action(); return true; });
    private async Task<bool> Run(Func<Profile, IProgress<string>, CancellationToken, Task<string>> action,bool inlineErrors=false)
    {
        if (busy || prompting) return false;
        var profile = Current(); busy = true; tabs.Enabled = false; languagePicker.Enabled=false; SetWaiting(false); progressBar.Visible = true;
        cancellation = new CancellationTokenSource(); status.Text = TextCatalog.T("ui.working");
        var progress = new Progress<string>(message => status.Text = message);
        try
        {
            var message = await action(profile, progress, cancellation.Token);
            var result = await Task.Run(() => { var store = Store(profile); return (items: store.List(), pending: store.HasPendingRestore); });
            manifests = result.items; pendingRecovery=result.pending; RenderList(); UpdateRecovery(); status.Text = result.pending ? TextCatalog.T("ui.pending") : message; Log(message);
            return !result.pending;
        }
        catch (OperationCanceledException) { status.Text = TextCatalog.T("ui.cancelled"); }
        catch (Exception ex) { status.Text = TextCatalog.T("ui.stopped_prefix") + ex.Message; Log(ex.ToString()); if(!inlineErrors) AppDialog.Show(this,TextCatalog.T("ui.stopped_title"),ex.Message); }
        finally
        {
            SetWaiting(false); cancellation.Dispose(); cancellation = null; busy = false; tabs.Enabled = true; languagePicker.Enabled=true; progressBar.Visible = false;
            try { pendingRecovery=Store(profile).HasPendingRestore; } catch (Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException) { pendingRecovery=null; }
            picker?.SetOperationState(false,status.Text); UpdateRecovery();
        }
        return false;
    }
    private void Log(string message)
    {
        try { File.AppendAllText(Path.Combine(appData, "manager.log"), $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}"); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    private void UpdateShortcutHint()
    {
        var occupied=shortcuts is { AllRegistered:false };
        shortcutHint.ForeColor=occupied ? Theme.Gold : Theme.Muted;
        shortcutHint.Text=occupied ? TextCatalog.T("ui.hotkeys_three",
            TextCatalog.T(shortcuts!.Registered(Keys.F5) ? "ui.active" : "ui.occupied"),
            TextCatalog.T(shortcuts.Registered(Keys.F9) ? "ui.active" : "ui.occupied"),
            TextCatalog.T(shortcuts.Registered(Keys.F11) ? "ui.active" : "ui.occupied")) : TextCatalog.T("library.hint");
    }
    private void RetryShortcuts()
    {
        if(shortcuts==null || shortcuts.AllRegistered || DateTime.UtcNow-lastShortcutRetry<TimeSpan.FromSeconds(1)) return;
        lastShortcutRetry=DateTime.UtcNow; shortcuts.Retry(); UpdateShortcutHint();
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e); if(preview) return;
        shortcuts=new GlobalShortcuts(Handle); shortcuts.Retry(); UpdateShortcutHint();
    }
    protected override void OnHandleDestroyed(EventArgs e)
    { shortcuts?.Dispose(); shortcuts=null; base.OnHandleDestroyed(e); }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0312 && !prompting)
        {
            if(m.WParam.ToInt32()==11) _=TogglePicker();
            else if(!busy) { if (m.WParam.ToInt32() == 5) _ = Create(); if (m.WParam.ToInt32() == 9) _ = RestoreLatest(); }
        }
        base.WndProc(ref m);
    }
}
