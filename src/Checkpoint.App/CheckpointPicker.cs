using Checkpoint.Core;

namespace Checkpoint.App;

/// <summary>A desktop floating picker. Restoration is delegated to the manager's existing guarded workflow.</summary>
public sealed class CheckpointPicker : Form
{
    private readonly Func<Manifest,Task<bool>> restore;
    private readonly Func<Task> refresh;
    private readonly Action openManager;
    private readonly TextBox search=new() { Dock=DockStyle.Fill,BackColor=Theme.Input,ForeColor=Theme.Text,BorderStyle=BorderStyle.FixedSingle };
    private readonly ListBox list=new() { Dock=DockStyle.Fill,DrawMode=DrawMode.OwnerDrawFixed,ItemHeight=54,IntegralHeight=false,BorderStyle=BorderStyle.None,BackColor=Theme.Surface,ForeColor=Theme.Text,DisplayMember=nameof(Manifest.Name) };
    private readonly CheckBox emergencies=new() { AutoSize=true,ForeColor=Theme.Muted,FlatStyle=FlatStyle.Flat };
    private readonly Label heading=new() { AutoSize=true,Font=new Font("Georgia",19),ForeColor=Theme.Gold };
    private readonly Label hint=new() { AutoSize=true,ForeColor=Theme.Muted };
    private readonly Label summary=new() { Dock=DockStyle.Fill,ForeColor=Theme.Muted };
    private readonly Label selection=new() { Dock=DockStyle.Fill,ForeColor=Theme.Gold,AutoEllipsis=true };
    private readonly Label status=new() { Dock=DockStyle.Fill,ForeColor=Theme.Text,AutoEllipsis=true };
    private readonly Button restoreButton=Theme.Button("",primary:true), refreshButton=Theme.Button(""), managerButton=Theme.Button("");
    private Manifest[] checkpoints=[];
    private bool busy, invoking;
    public Manifest? SelectedCheckpoint=>list.SelectedItem as Manifest;

    public CheckpointPicker(Func<Manifest,Task<bool>> restore,Func<Task> refresh,Action openManager)
    {
        this.restore=restore; this.refresh=refresh; this.openManager=openManager;
        Icon=AppAssets.ApplicationIcon; TopMost=true; ShowInTaskbar=false; KeyPreview=true;
        FormBorderStyle=FormBorderStyle.SizableToolWindow; StartPosition=FormStartPosition.Manual;
        Size=new Size(540,620); MinimumSize=new Size(460,520); Font=new Font("Segoe UI",10);
        BackColor=Theme.Surface; ForeColor=Theme.Text;
        var root=new TableLayoutPanel { Dock=DockStyle.Fill,Padding=new Padding(20),ColumnCount=1,RowCount=8 };
        foreach(var height in new[]{80,38,36,24}) root.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        foreach(var height in new[]{32,52,74}) root.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
        Controls.Add(root);
        var header=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=2,RowCount=1 };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,68)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        header.Controls.Add(new PictureBox { Image=AppAssets.CheckpointImage,SizeMode=PictureBoxSizeMode.Zoom,Dock=DockStyle.Fill,Margin=new Padding(0,0,12,12) },0,0);
        var titles=new FlowLayoutPanel { Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false };
        titles.Controls.Add(heading); titles.Controls.Add(hint); header.Controls.Add(titles,1,0); root.Controls.Add(header,0,0);
        root.Controls.Add(search,0,1);
        var filters=new FlowLayoutPanel { Dock=DockStyle.Fill,WrapContents=false,Padding=new Padding(0,8,0,0) };
        filters.Controls.Add(emergencies); root.Controls.Add(filters,0,2); root.Controls.Add(summary,0,3);
        list.DrawItem+=(_,e)=>
        {
            if(e.Index<0) return;
            var cp=(Manifest)list.Items[e.Index]; var chosen=e.Index==list.SelectedIndex;
            using var fill=new SolidBrush(chosen ? Theme.Selected : Theme.Surface); e.Graphics.FillRectangle(fill,e.Bounds);
            e.Graphics.DrawImage(AppAssets.CheckpointImage,new Rectangle(e.Bounds.Left+8,e.Bounds.Top+11,32,32));
            var titleBounds=new Rectangle(e.Bounds.Left+52,e.Bounds.Top+5,e.Bounds.Width-60,23);
            TextRenderer.DrawText(e.Graphics,cp.Name,Font,titleBounds,chosen ? Theme.Gold : Theme.Text,TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis);
            var detail=cp.Created.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")+(cp.Emergency ? " · "+TextCatalog.T("type.emergency") : "");
            TextRenderer.DrawText(e.Graphics,detail,Font,new Rectangle(titleBounds.X,e.Bounds.Top+28,titleBounds.Width,20),Theme.Muted,TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis);
        };
        root.Controls.Add(list,0,4); root.Controls.Add(selection,0,5);
        var actions=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=3,RowCount=1 };
        foreach(var width in new[]{48f,24f,28f}) actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,width));
        foreach(var button in new[]{restoreButton,refreshButton,managerButton}) { button.AutoSize=false; button.MinimumSize=Size.Empty; button.Dock=DockStyle.Fill; }
        managerButton.Margin=new Padding(0,0,0,8);
        actions.Controls.Add(restoreButton,0,0); actions.Controls.Add(refreshButton,1,0); actions.Controls.Add(managerButton,2,0); root.Controls.Add(actions,0,6);
        root.Controls.Add(status,0,7);
        search.TextChanged+=(_,_)=>Render(); emergencies.CheckedChanged+=(_,_)=>Render();
        list.SelectedIndexChanged+=(_,_)=>UpdateSelection(); list.DoubleClick+=async(_,_)=>await RestoreSelectedAsync();
        restoreButton.Click+=async(_,_)=>await RestoreSelectedAsync(); refreshButton.Click+=async(_,_)=>await this.refresh();
        managerButton.Click+=(_,_)=>{ Hide(); this.openManager(); };
        KeyDown+=async(_,e)=>
        {
            if(e.KeyCode is Keys.Escape or Keys.F11) { e.Handled=e.SuppressKeyPress=true; Hide(); }
            else if(e.KeyCode==Keys.Enter && (list.Focused || search.Focused)) { e.Handled=e.SuppressKeyPress=true; await RestoreSelectedAsync(); }
            else if(e.KeyCode==Keys.Down && search.Focused && list.Items.Count>0) { e.Handled=e.SuppressKeyPress=true; list.Focus(); }
        };
        ApplyLanguage();
    }
    public void ApplyLanguage()
    {
        Text=TextCatalog.T("picker.title"); heading.Text=TextCatalog.T("picker.heading"); hint.Text=TextCatalog.T("picker.keys");
        search.PlaceholderText=TextCatalog.T("library.search"); emergencies.Text=TextCatalog.T("picker.emergencies");
        restoreButton.Text=TextCatalog.T("picker.restore"); refreshButton.Text=TextCatalog.T("library.refresh"); managerButton.Text=TextCatalog.T("picker.manager");
        Render(); list.Invalidate();
    }
    public void SetCheckpoints(IEnumerable<Manifest> items)
    {
        checkpoints=items.OrderByDescending(m=>m.Created).ThenBy(m=>m.Id,StringComparer.Ordinal).ToArray(); Render();
    }
    public void SetOperationState(bool working,string message)
    {
        busy=working; status.Text=message; search.Enabled=emergencies.Enabled=list.Enabled=refreshButton.Enabled=!busy;
        UpdateSelection();
    }
    public void ShowFloating()
    {
        var area=Screen.FromPoint(Cursor.Position).WorkingArea;
        Width=Math.Min(Width,area.Width); Height=Math.Min(Height,area.Height);
        Location=new Point(area.Left+(area.Width-Width)/2,area.Top+(area.Height-Height)/2);
        Show(); BringToFront(); Activate(); list.Focus();
    }
    private void Render()
    {
        var selected=SelectedCheckpoint?.Id;
        list.BeginUpdate(); list.Items.Clear();
        foreach(var cp in checkpoints.Where(m=>(emergencies.Checked || !m.Emergency) && m.Name.Contains(search.Text.Trim(),StringComparison.CurrentCultureIgnoreCase)))
        {
            var index=list.Items.Add(cp);
            if(cp.Id==selected) list.SelectedIndex=index;
        }
        if(list.SelectedIndex<0 && list.Items.Count>0) list.SelectedIndex=0;
        list.EndUpdate();
        summary.Text=TextCatalog.T(list.Items.Count==0 ? checkpoints.Length==0 ? "picker.empty" : "picker.no_matches" : "picker.count",list.Items.Count);
        UpdateSelection(); list.Invalidate();
    }
    private void UpdateSelection()
    {
        selection.Text=SelectedCheckpoint is { } cp ? TextCatalog.T("library.selected",cp.Name) : TextCatalog.T("ui.select_restore");
        restoreButton.Enabled=!busy && !invoking && SelectedCheckpoint!=null;
    }
    public async Task RestoreSelectedAsync()
    {
        if(busy || invoking || SelectedCheckpoint is not { } selected) return;
        invoking=true; UpdateSelection();
        try { if(await restore(selected)) Hide(); }
        catch(Exception error) { status.Text=TextCatalog.T("ui.stopped_prefix")+error.Message; }
        finally { invoking=false; UpdateSelection(); }
    }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if(e.CloseReason==CloseReason.UserClosing) { e.Cancel=true; Hide(); }
        base.OnFormClosing(e);
    }
}
