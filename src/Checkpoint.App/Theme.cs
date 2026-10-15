using System.Drawing.Drawing2D;
using Checkpoint.Core;

namespace Checkpoint.App;

internal static class Theme
{
    public static readonly Color Background=Color.FromArgb(18,21,26), Surface=Color.FromArgb(26,30,36),
        Input=Color.FromArgb(34,39,46), Hover=Color.FromArgb(44,49,56), Border=Color.FromArgb(59,64,71),
        Text=Color.FromArgb(235,232,224), Muted=Color.FromArgb(158,165,174), Gold=Color.FromArgb(204,171,112),
        Selected=Color.FromArgb(57,51,40), Danger=Color.FromArgb(215,139,130);
    public static Button Button(string text, bool primary=false, bool danger=false)
    {
        var b=new ThemedButton { Text=text, AutoSize=true, MinimumSize=new Size(90,40), FlatStyle=FlatStyle.Flat,
            Padding=new Padding(12,5,12,5), Margin=new Padding(0,0,8,8), Cursor=Cursors.Hand,
            BackColor=primary ? Gold : Input, ForeColor=primary ? Background : danger ? Danger : Text };
        b.FlatAppearance.BorderColor=primary ? Gold : Border;
        b.FlatAppearance.MouseOverBackColor=primary ? Color.FromArgb(223,190,133) : Hover;
        b.FlatAppearance.MouseDownBackColor=primary ? Color.FromArgb(182,146,88) : Selected;
        return b;
    }
    public static void StyleList(ListView list)
    {
        list.BackColor=Surface; list.ForeColor=Text; list.OwnerDraw=true;
        list.DrawColumnHeader+=(_,e)=>
        {
            using var brush=new SolidBrush(Input); e.Graphics.FillRectangle(brush,e.Bounds);
            TextRenderer.DrawText(e.Graphics,e.Header!.Text,list.Font,Rectangle.Inflate(e.Bounds,-12,0),Muted,
                TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine);
        };
        list.DrawItem+=(_,_)=>{};
        list.DrawSubItem+=(_,e)=>
        {
            var selected=e.Item!.Selected;
            using var brush=new SolidBrush(selected ? Selected : e.ItemIndex%2==0 ? Surface : Color.FromArgb(29,33,39));
            e.Graphics.FillRectangle(brush,e.Bounds);
            if (selected && e.ColumnIndex==0) { using var mark=new SolidBrush(Gold); e.Graphics.FillRectangle(mark,e.Bounds.X,e.Bounds.Y,3,e.Bounds.Height); }
            var textBounds=Rectangle.Inflate(e.Bounds,-12,0);
            if(e.ColumnIndex==0 && e.Item.ImageIndex>=0 && list.SmallImageList is { } images && e.Item.ImageIndex<images.Images.Count)
            {
                images.Draw(e.Graphics,e.Bounds.Left+12,e.Bounds.Top+(e.Bounds.Height-images.ImageSize.Height)/2,e.Item.ImageIndex);
                textBounds.X+=images.ImageSize.Width+10; textBounds.Width-=images.ImageSize.Width+10;
            }
            TextRenderer.DrawText(e.Graphics,e.SubItem!.Text,list.Font,textBounds,
                selected ? Gold : e.ColumnIndex==0 ? Text : Muted,
                TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine);
        };
    }
}

internal sealed class ThemedButton : Button
{
    private bool hover, down;
    protected override void OnMouseEnter(EventArgs e) { hover=true; base.OnMouseEnter(e); Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { hover=down=false; base.OnMouseLeave(e); Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { down=true; base.OnMouseDown(e); Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { down=false; base.OnMouseUp(e); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var color=Enabled ? down ? FlatAppearance.MouseDownBackColor : hover ? FlatAppearance.MouseOverBackColor : BackColor : Theme.Surface;
        using var fill=new SolidBrush(color); e.Graphics.FillRectangle(fill,ClientRectangle);
        using var border=new Pen(Enabled ? FlatAppearance.BorderColor : Theme.Border); e.Graphics.DrawRectangle(border,0,0,Width-1,Height-1);
        TextRenderer.DrawText(e.Graphics,Text,Font,Rectangle.Inflate(ClientRectangle,-8,-4),Enabled ? ForeColor : Theme.Muted,
            TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.WordBreak);
        if(Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(ClientRectangle,-4,-4),ForeColor,color);
    }
}

internal sealed class DarkTabs : TabControl
{
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if ((m.Msg!=0xF && m.Msg!=0x317 && m.Msg!=0x318) || TabCount==0) return;
        using var g=m.Msg==0xF ? CreateGraphics() : Graphics.FromHdc(m.WParam);
        var body=DisplayRectangle;
        using var background=new SolidBrush(Theme.Background); g.FillRectangle(background,0,0,Width,body.Top);
        using var surface=new SolidBrush(Theme.Surface);
        g.FillRectangle(surface,0,body.Top-2,body.Left,Height-body.Top+2);
        g.FillRectangle(surface,body.Right,body.Top-2,Width-body.Right,Height-body.Top+2);
        g.FillRectangle(surface,0,body.Bottom,Width,Height-body.Bottom);
        for(int i=0;i<TabCount;i++)
        {
            var r=GetTabRect(i); if(i==SelectedIndex) g.FillRectangle(surface,r);
            TextRenderer.DrawText(g,TabPages[i].Text,Font,r,i==SelectedIndex ? Theme.Gold : Theme.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
            if(i==SelectedIndex) { using var line=new Pen(Theme.Gold,2); g.DrawLine(line,r.Left+15,r.Bottom-3,r.Right-15,r.Bottom-3); }
        }
    }
}

internal static class AppDialog
{
    public static bool Show(IWin32Window? owner,string title,string message,bool confirm=false)
    {
        using var dialog=new Form { Text=title, BackColor=Theme.Surface, ForeColor=Theme.Text,
            Font=new Font("Segoe UI",10), Size=new Size(590,400), MinimumSize=new Size(500,330),
            StartPosition=owner==null ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent,
            ShowInTaskbar=false, MinimizeBox=false, MaximizeBox=false };
        var grid=new TableLayoutPanel { Dock=DockStyle.Fill, Padding=new Padding(24), ColumnCount=1, RowCount=3 };
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute,45)); grid.RowStyles.Add(new RowStyle(SizeType.Percent,100)); grid.RowStyles.Add(new RowStyle(SizeType.Absolute,58));
        grid.Controls.Add(new Label { Text=title, Dock=DockStyle.Fill, Font=new Font("Segoe UI Semibold",15), ForeColor=Theme.Gold },0,0);
        grid.Controls.Add(new TextBox { Text=message, Multiline=true, ReadOnly=true, BorderStyle=BorderStyle.None,
            Dock=DockStyle.Fill, BackColor=Theme.Surface, ForeColor=Theme.Text, ScrollBars=ScrollBars.Vertical, TabStop=false },0,1);
        var buttons=new FlowLayoutPanel { Dock=DockStyle.Fill, FlowDirection=FlowDirection.RightToLeft, Padding=new Padding(0,10,0,0) };
        var accept=Theme.Button(TextCatalog.T(confirm ? "dialog.yes" : "dialog.ok"),primary:true); accept.DialogResult=DialogResult.Yes;
        if (confirm) { var no=Theme.Button(TextCatalog.T("dialog.no")); no.DialogResult=DialogResult.No; buttons.Controls.Add(no); dialog.CancelButton=no; dialog.AcceptButton=no; }
        else { dialog.CancelButton=accept; dialog.AcceptButton=accept; }
        buttons.Controls.Add(accept); grid.Controls.Add(buttons,0,2); dialog.Controls.Add(grid);
        return dialog.ShowDialog(owner)==DialogResult.Yes;
    }
}
