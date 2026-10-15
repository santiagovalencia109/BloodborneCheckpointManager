using System.Runtime.InteropServices;

namespace Checkpoint.App;

/// <summary>Own successful registrations only; failed keys can be retried after another app exits.</summary>
public sealed class GlobalShortcuts : IDisposable
{
    [DllImport("user32.dll",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)] private static extern bool RegisterHotKey(IntPtr window,int id,uint modifiers,uint key);
    [DllImport("user32.dll")]
    [return:MarshalAs(UnmanagedType.Bool)] private static extern bool UnregisterHotKey(IntPtr window,int id);
    private readonly IntPtr window;
    private readonly Dictionary<Keys,bool> keys;
    private bool disposed;
    public GlobalShortcuts(IntPtr window,params Keys[] keys)
    { this.window=window; this.keys=(keys.Length==0 ? new[]{Keys.F5,Keys.F9,Keys.F11} : keys).Distinct().ToDictionary(k=>k,_=>false); }
    private static int Id(Keys key)=>key switch { Keys.F5=>5,Keys.F9=>9,Keys.F11=>11,_=>(int)key };
    public bool Registered(Keys key)=>keys.TryGetValue(key,out var registered) && registered;
    public bool AllRegistered=>keys.Values.All(value=>value);
    public void Retry()
    {
        if(disposed) return;
        foreach(var key in keys.Keys.ToArray()) if(!keys[key]) keys[key]=RegisterHotKey(window,Id(key),0x4000,(uint)key);
    }
    public void Dispose()
    {
        if(disposed) return; disposed=true;
        foreach(var key in keys.Keys) if(keys[key]) UnregisterHotKey(window,Id(key));
    }
}
