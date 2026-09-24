using System.Runtime.InteropServices;
using SimpleWeather.Core;

namespace SimpleWeather.Interop;

internal enum TaskbarEdge { Left, Top, Right, Bottom }

/// <summary>A read-only snapshot of the taskbar. All rects are in screen pixels.</summary>
internal sealed record TaskbarInfo(
    IntPtr Handle,
    Win32.RECT TaskbarRect,
    Win32.RECT MonitorRect,
    Win32.RECT WorkArea,
    Win32.RECT RebarRect,
    Win32.RECT NotifyRect,
    Win32.RECT ClockRect,
    TaskbarEdge Edge,
    int Dpi,
    bool IsAutoHidden)
{
    public bool IsVertical => Edge is TaskbarEdge.Left or TaskbarEdge.Right;

    /// <summary>Changes when the shell re-lays-out, so a stale placement can be detected cheaply.</summary>
    public string Signature =>
        $"{Handle}|{TaskbarRect}|{RebarRect}|{NotifyRect}|{Dpi}|{Edge}|{IsAutoHidden}";
}

/// <summary>
/// Reserves a strip of taskbar space for the widget by shrinking ReBarWindow32 (the running
/// applications band), exactly as the earlier child-window build did. The difference now is
/// that the widget itself is a separate translucent popup window parked in the freed strip,
/// which is what makes true per-pixel transparency possible.
///
/// The unmodified size of ReBarWindow32 is *derived*, never remembered: the button band by
/// definition ends where TrayNotifyWnd begins.
/// </summary>
internal sealed class TaskbarBand : IDisposable
{
    private const string PrimaryTaskbarClass = "Shell_TrayWnd";
    private const string SecondaryTaskbarClass = "Shell_SecondaryTrayWnd";
    private const string RebarClass = "ReBarWindow32";
    private const string NotifyClass = "TrayNotifyWnd";
    private const string ClockClass = "TrayClockWClass";

    private const uint AttachFlags = Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE;

    /// <summary>
    /// Flags for the one call that actually resizes ReBarWindow32.
    ///
    /// <c>SWP_NOCOPYBITS</c> is the important addition: without it the shell bit-blits the old
    /// client bits into the resized window, which is what makes the taskbar buttons visibly smear
    /// and flash while the band is being reclaimed. A clean repaint is cheaper to look at than a
    /// stale copy, and it costs nothing here because the band is only ever touched when its
    /// geometry really differs from the target.
    /// </summary>
    private const uint ResizeFlags = AttachFlags | Win32.SWP_NOCOPYBITS;

    private IntPtr _taskbar;
    private IntPtr _rebar;
    private Win32.RECT _originalClient;
    private Win32.RECT _shrunkClient;
    private bool _reserved;
    private bool _disposed;

    /// <summary>
    /// Tick at which the band was first seen broken, or 0 when it was intact at the last look.
    /// Used to debounce the guard; see <see cref="IsBandIntact"/>.
    /// </summary>
    private long _bandBrokenSince;

    /// <summary>
    /// How long the band must stay broken before we act on it.
    ///
    /// The shell does not restore ReBarWindow32 in one step — it re-lays the whole taskbar out,
    /// and the guard's 100 ms tick can easily land inside that window and see a transient value.
    /// Acting on the first broken reading meant issuing a resize *during* the shell's own
    /// re-layout, which made explorer start over: the loop that showed up in the log as identical
    /// "Reserve:" lines repeating every 0.6-1.5 s. Requiring the break to survive one more tick
    /// rides out the transient instead of fighting it.
    /// </summary>
    private const int BandBreakGraceMs = 35;

    /// <summary>Where the widget should be placed, in screen pixels. Valid after a successful Reserve.</summary>
    public Win32.RECT StripScreenRect { get; private set; }

    public TaskbarInfo? Current { get; private set; }

    public bool IsReserved => _reserved;

    // ------------------------------------------------------------- probing

    public static TaskbarInfo? Probe()
    {
        IntPtr taskbar = Win32.FindWindow(PrimaryTaskbarClass, null);
        if (taskbar == IntPtr.Zero || !Win32.IsWindow(taskbar))
            taskbar = FindSecondaryTaskbar();

        if (taskbar == IntPtr.Zero || !Win32.IsWindow(taskbar))
        {
            Log.Write("Probe: no taskbar window found");
            return null;
        }

        if (!Win32.TryGetRect(taskbar, out var barRect) || barRect.IsEmpty)
        {
            Log.Write($"Probe: bad taskbar rect for 0x{taskbar.ToInt64():X}");
            return null;
        }

        IntPtr rebar = Win32.FindChild(taskbar, RebarClass);
        IntPtr notify = Win32.FindChild(taskbar, NotifyClass);
        Win32.TryGetRect(rebar, out var rebarRect);
        Win32.TryGetRect(notify, out var notifyRect);

        // TrayClockWClass is not always reachable through a single FindWindowEx hop, so walk
        // the notification area's direct children instead.
        Win32.RECT clockRect = default;
        if (notify != Win32.Zero)
        {
            IntPtr child = Win32.GetWindow(notify, Win32.GW_CHILD);
            while (child != IntPtr.Zero)
            {
                if (Win32.ClassNameOf(child) == ClockClass)
                {
                    Win32.TryGetRect(child, out clockRect);
                    break;
                }
                child = Win32.GetWindow(child, Win32.GW_HWNDNEXT);
            }
        }

        IntPtr monitor = Win32.MonitorFromWindow(taskbar, Win32.MONITOR_DEFAULTTONEAREST);
        var mi = new Win32.MONITORINFOEX
        {
            cbSize = (uint)Marshal.SizeOf<Win32.MONITORINFOEX>(),
            szDevice = string.Empty,
        };
        if (!Win32.GetMonitorInfo(monitor, ref mi))
        {
            Log.Write("Probe: GetMonitorInfo failed");
            return null;
        }

        uint dpi = Win32.GetDpiForWindow(taskbar);
        if (dpi == 0) dpi = 96;

        var info = new TaskbarInfo(
            Handle: taskbar,
            TaskbarRect: barRect,
            MonitorRect: mi.rcMonitor,
            WorkArea: mi.rcWork,
            RebarRect: rebarRect,
            NotifyRect: notifyRect,
            ClockRect: clockRect,
            Edge: DeriveEdge(barRect, mi.rcMonitor),
            Dpi: (int)dpi,
            IsAutoHidden: Win32.IsTaskbarAutoHide());

        Log.Verbose($"Probe: taskbar={barRect} rebar={rebarRect} notify={notifyRect} clock={clockRect} "
                    + $"edge={info.Edge} dpi={dpi} autohide={info.IsAutoHidden}");
        return info;
    }

    private static IntPtr FindSecondaryTaskbar()
    {
        // Only the primary bar is targeted today; secondaries share the same class name shape.
        _ = SecondaryTaskbarClass;
        return IntPtr.Zero;
    }

    private static TaskbarEdge DeriveEdge(Win32.RECT bar, Win32.RECT monitor)
    {
        if (bar.Width >= bar.Height)
        {
            return (bar.Top - monitor.Top) <= (monitor.Bottom - bar.Bottom)
                ? TaskbarEdge.Top
                : TaskbarEdge.Bottom;
        }

        return (bar.Left - monitor.Left) <= (monitor.Right - bar.Right)
            ? TaskbarEdge.Left
            : TaskbarEdge.Right;
    }

    /// <summary>The geometry ReBarWindow32 has when nothing is reserved.</summary>
    public static Win32.RECT OriginalRebarClient(TaskbarInfo info)
    {
        var rebar = ToClient(info.Handle, info.RebarRect);
        var notify = ToClient(info.Handle, info.NotifyRect);

        if (info.IsVertical)
        {
            if (notify.Top > rebar.Top && notify.Top <= info.TaskbarRect.Bottom)
                rebar.Bottom = notify.Top;
        }
        else if (notify.Left > rebar.Left && notify.Left <= info.TaskbarRect.Right)
        {
            rebar.Right = notify.Left;
        }

        return rebar;
    }

    public static Win32.RECT ToClient(IntPtr anchor, Win32.RECT screen)
    {
        var tl = new Win32.POINT(screen.Left, screen.Top);
        var br = new Win32.POINT(screen.Right, screen.Bottom);
        if (!Win32.ScreenToClient(anchor, ref tl) || !Win32.ScreenToClient(anchor, ref br))
            return screen;
        return new Win32.RECT { Left = tl.X, Top = tl.Y, Right = br.X, Bottom = br.Y };
    }

    public static Win32.RECT ToScreen(IntPtr anchor, Win32.RECT client)
    {
        var tl = new Win32.POINT(client.Left, client.Top);
        var br = new Win32.POINT(client.Right, client.Bottom);
        if (!Win32.ClientToScreen(anchor, ref tl) || !Win32.ClientToScreen(anchor, ref br))
            return client;
        return new Win32.RECT { Left = tl.X, Top = tl.Y, Right = br.X, Bottom = br.Y };
    }

    // ------------------------------------------------------------ reserving

    /// <summary>
    /// Shrinks the button band and computes where the widget should sit.
    ///
    /// The strip's size along the taskbar's length differs per orientation: a vertical bar uses
    /// <paramref name="verticalLength"/> as the height, a horizontal bar uses
    /// <paramref name="horizontalLength"/> as the width. Using one value for both was the reason
    /// the strip was clipped to a 40px sliver on a bottom taskbar — the content is laid out
    /// horizontally and needs ~85px.
    /// </summary>
    public bool Reserve(int verticalLength, int horizontalLength, int gapPx)
    {
        var info = Probe();
        if (info is null) return false;

        var rebar = Win32.FindChild(info.Handle, RebarClass);
        if (rebar == IntPtr.Zero)
        {
            Log.Write("Reserve: ReBarWindow32 not found");
            return false;
        }

        var original = OriginalRebarClient(info);
        if (original.IsEmpty)
        {
            Log.Write($"Reserve: degenerate rebar geometry {original}");
            return false;
        }

        _taskbar = info.Handle;
        _rebar = rebar;
        _originalClient = original;

        var computed = ComputeLayout(info, original, verticalLength, horizontalLength, gapPx);
        if (computed is null)
        {
            Log.Write("Reserve: layout computation failed");
            return false;
        }

        var (shrunk, strip) = computed.Value;

        // Publish the freshly derived numbers first: the geometry below is what IsBandIntact()
        // compares against, so it has to be current even on the no-op path.
        var previous = _shrunkClient;
        _shrunkClient = shrunk;
        _reserved = true;
        Current = info;
        StripScreenRect = ToScreen(info.Handle, strip);

        // Already at the target geometry: do not touch the taskbar at all.
        //
        // The 100 ms guard calls this several times a second, and the shell re-derives
        // ReBarWindow32 ("it always ends where TrayNotifyWnd begins") on its own schedule, so the
        // naive version issued a SetWindowPos on *every* tick even when the window already
        // measured exactly what we wanted. Each of those resize messages makes explorer re-lay the
        // taskbar out and repaint it, and passing the *original* size as the first step made it a
        // two-step shrink-then-restore dance. Measured in the log as a steady stream of identical
        // "Reserve ok: rebar (46,0)-(1643,40) -> (46,0)-(1537,40)" lines several times a second,
        // and visible as the strip flashing whenever the notification area was used. Comparing
        // against the target instead makes the steady state completely silent.
        var current = ToClient(info.Handle, info.RebarRect);
        if (Same(current, shrunk))
        {
            _bandBrokenSince = 0;
            Log.Verbose($"Reserve: band already {shrunk}; not touching the taskbar");
            return true;
        }

        if (!Same(current, previous))
            Log.Write($"Reserve: rebar {current} -> {shrunk} (original {original})");

        if (!Win32.SetWindowPos(rebar, IntPtr.Zero,
                shrunk.Left, shrunk.Top, shrunk.Width, shrunk.Height, ResizeFlags))
        {
            Log.Write($"Reserve: shrinking rebar to {shrunk} failed err={Marshal.GetLastPInvokeError()}");
            return false;
        }

        Log.Write($"Reserve ok: rebar {original} -> {shrunk}; strip screen {StripScreenRect}");
        _bandBrokenSince = 0;
        return true;
    }

    /// <summary>Client-space rectangle equality; the four edges are all that is ever set.</summary>
    private static bool Same(Win32.RECT a, Win32.RECT b)
        => a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom;

    /// <summary>
    /// Pure layout maths, split out so the horizontal branch can be exercised on a machine whose
    /// taskbar is docked vertically (see <see cref="SelfTestHorizontal"/>).
    /// </summary>
    internal static (Win32.RECT Shrunk, Win32.RECT Strip)? ComputeLayout(
        TaskbarInfo info, Win32.RECT original, int verticalLength, int horizontalLength, int gapPx)
    {
        if (original.IsEmpty) return null;

        int gap = Math.Clamp(gapPx, 0, 40);
        // Length runs along the bar; thickness is whatever the bar itself measures.
        int length = info.IsVertical ? verticalLength : horizontalLength;

        Win32.RECT shrunk, strip;

        if (info.IsVertical)
        {
            // Keep at least 60px of button band so the task list stays usable.
            int band = Math.Clamp(length, 24, Math.Max(24, original.Height - 60 - gap));
            int stripBottom = original.Bottom - gap;

            shrunk = new Win32.RECT
            {
                Left = original.Left,
                Top = original.Top,
                Right = original.Right,
                Bottom = stripBottom - band,
            };
            strip = new Win32.RECT
            {
                Left = original.Left,
                Top = stripBottom - band,
                Right = original.Right,
                Bottom = stripBottom,
            };
        }
        else
        {
            // A horizontal bar is long, so the strip needs a real width; 60px is the floor.
            int band = Math.Clamp(length, 60, Math.Max(60, original.Width - 60 - gap));
            int stripRight = original.Right - gap;

            shrunk = new Win32.RECT
            {
                Left = original.Left,
                Top = original.Top,
                Right = stripRight - band,
                Bottom = original.Bottom,
            };
            strip = new Win32.RECT
            {
                Left = stripRight - band,
                Top = original.Top,
                Right = stripRight,
                Bottom = original.Bottom,
            };
        }

        return (shrunk, strip);
    }

    /// <summary>
    /// Exercises the horizontal (top/bottom docked) layout maths with a synthetic taskbar.
    ///
    /// This machine's taskbar is docked vertically and the shell offers no API to relocate it
    /// (ABM_SETPOS is ignored for Shell_TrayWnd), so the horizontal branch cannot be reached by
    /// simply running the app. The maths is pure: feeding a fake TaskbarInfo through the same
    /// helper proves the strip gets a usable width and ends up flush against the notification
    /// area. It does NOT prove the on-screen result — that still needs a real bottom-docked bar.
    /// </summary>
    internal static void SelfTestHorizontal(int verticalLength, int horizontalLength, int gapPx)
    {
        var fake = new TaskbarInfo(
            Handle: IntPtr.Zero,   // no window, so coordinate conversion falls back to identity
            TaskbarRect: new Win32.RECT { Left = 0, Top = 1040, Right = 1920, Bottom = 1080 },
            MonitorRect: new Win32.RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 },
            WorkArea: new Win32.RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1040 },
            RebarRect: new Win32.RECT { Left = 0, Top = 1040, Right = 1600, Bottom = 1080 },
            NotifyRect: new Win32.RECT { Left = 1600, Top = 1040, Right = 1920, Bottom = 1080 },
            ClockRect: new Win32.RECT { Left = 1700, Top = 1040, Right = 1800, Bottom = 1080 },
            Edge: TaskbarEdge.Bottom,
            Dpi: 96,
            IsAutoHidden: false);

        var original = OriginalRebarClient(fake);
        var computed = ComputeLayout(fake, original, verticalLength, horizontalLength, gapPx);
        if (computed is null)
        {
            Log.Write("[selftest] horizontal layout returned null");
            return;
        }

        var (shrunk, strip) = computed.Value;
        bool flushAgainstNotify = strip.Right == fake.NotifyRect.Left - gapPx;

        Log.Write($"[selftest] horizontal(bottom): original={original} shrunk={shrunk} "
                  + $"strip={strip} {strip.Width}x{strip.Height} "
                  + $"flushAgainstNotify={flushAgainstNotify} widthOk={strip.Width >= 80}");
    }

    /// <summary>
    /// True when ReBarWindow32 still measures what we set it to.
    ///
    /// A single broken reading is not enough: see <see cref="BandBreakGraceMs"/>. The first broken
    /// sighting only arms the debounce and still reports intact, so the caller keeps its hands off
    /// the taskbar while the shell is mid-re-layout; only a break that survives the grace period
    /// is reported as lost.
    /// </summary>
    public bool IsBandIntact()
    {
        if (!_reserved || _rebar == IntPtr.Zero || !Win32.IsWindow(_rebar))
        {
            _bandBrokenSince = 0;
            return false;
        }

        if (!Win32.TryGetRect(_rebar, out var screen))
        {
            _bandBrokenSince = 0;
            return false;
        }

        var client = ToClient(_taskbar, screen);
        bool intact = Same(client, _shrunkClient);

        if (intact)
        {
            _bandBrokenSince = 0;
            return true;
        }

        long now = Environment.TickCount64;
        if (_bandBrokenSince == 0)
        {
            _bandBrokenSince = now;
            Log.Verbose($"IsBandIntact: band reads {client}, expected {_shrunkClient}; arming debounce");
            return true;
        }

        if (now - _bandBrokenSince < BandBreakGraceMs)
        {
            Log.Verbose($"IsBandIntact: band still {client} after {now - _bandBrokenSince}ms; waiting");
            return true;
        }

        Log.Verbose($"IsBandIntact: band lost, {client} != {_shrunkClient}");
        return false;
    }

    /// <summary>Puts ReBarWindow32 back to its unreserved size.</summary>
    public void Release()
    {
        if (_rebar != IntPtr.Zero && Win32.IsWindow(_rebar) && !_originalClient.IsEmpty)
        {
            _ = Win32.SetWindowPos(_rebar, IntPtr.Zero,
                _originalClient.Left, _originalClient.Top,
                _originalClient.Width, _originalClient.Height, ResizeFlags);
        }

        _rebar = IntPtr.Zero;
        _originalClient = default;
        _shrunkClient = default;
        _reserved = false;
        _bandBrokenSince = 0;
    }

    /// <summary>
    /// Startup repair: if a previous run was killed mid-flight the band stays shrunk.
    /// Deriving the correct size makes this unconditionally correct.
    /// </summary>
    public static bool RestoreTaskbarBand()
    {
        var info = Probe();
        if (info is null) return false;

        var rebar = Win32.FindChild(info.Handle, RebarClass);
        if (rebar == IntPtr.Zero) return false;

        var target = OriginalRebarClient(info);
        var current = ToClient(info.Handle, info.RebarRect);

        if (current.Left == target.Left && current.Top == target.Top
            && current.Right == target.Right && current.Bottom == target.Bottom)
            return true;

        bool ok = Win32.SetWindowPos(rebar, IntPtr.Zero,
            target.Left, target.Top, target.Width, target.Height, ResizeFlags);

        Log.Write($"RestoreTaskbarBand: {current} -> {target} ok={ok}");
        return ok;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Release();
    }
}
