using System.Reflection;

namespace Checkpoint.App;

internal static class AppAssets
{
    public static readonly Bitmap CheckpointImage=LoadImage();
    public static readonly Icon ApplicationIcon=LoadIcon();
    private static Bitmap LoadImage()
    {
        using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("BCM.checkpoint.png")!;
        using var image=Image.FromStream(stream); return new Bitmap(image);
    }
    private static Icon LoadIcon()
    {
        using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("BCM.checkpoint.ico")!;
        using var icon=new Icon(stream,32,32); return (Icon)icon.Clone();
    }
    public static Bitmap RowImage()
    {
        var row=new Bitmap(32,38);
        using var graphics=Graphics.FromImage(row);
        graphics.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.DrawImage(CheckpointImage,new Rectangle(0,3,32,32)); return row;
    }
}
