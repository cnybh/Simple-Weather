using SimpleWeather.Interop;

namespace SimpleWeather.Ui;

/// <summary>
/// A <see cref="SynchronizationContext"/> backed by the widget's own message loop.
///
/// Without one, every <c>await</c> started on the UI thread resumes on a thread-pool thread, and
/// the continuation then calls window APIs on windows owned by the UI thread. Measured on the
/// reference machine: the settings window's city search resumed on worker thread 5 and ran
/// <c>ListAdd</c>, <c>MoveWindow</c>, <c>SetWindowPos</c>, <c>EnableWindow</c> and
/// <c>SetWindowText</c> against UI-thread windows from there. Windows marshals some of those
/// messages and the call then blocks the worker until the UI thread pumps, so a UI thread that is
/// momentarily inside a shell call (taskbar re-reserve, layered-window update) makes the whole
/// window feel stuck. Resuming on the UI thread removes the whole class of problem.
///
/// Install once, on the UI thread, before the message loop runs; <see cref="AttachSink"/> must be
/// given a window created by that same thread.
/// </summary>
internal sealed class UiSynchronizationContext : SynchronizationContext
{
    /// <summary>Posted to the sink window to drain <see cref="_queue"/> on the UI thread.</summary>
    public const uint WmInvoke = 0x8000 + 11;   // WM_APP + 11

    private readonly Queue<Entry> _queue = new();
    private readonly object _gate = new();

    private IntPtr _sink;

    /// <summary>The instance installed on the widget's UI thread, if any.</summary>
    public static UiSynchronizationContext? Installed { get; private set; }

    /// <summary>Installs the context for the calling (UI) thread. Idempotent.</summary>
    public static void Install()
    {
        if (Installed is not null) return;

        var context = new UiSynchronizationContext();
        Installed = context;
        SetSynchronizationContext(context);
    }

    /// <summary>Names the window whose message loop drains the queue.</summary>
    public void AttachSink(IntPtr hwnd) => _sink = hwnd;

    public override void Post(SendOrPostCallback d, object? state)
    {
        lock (_gate) _queue.Enqueue(new Entry(d, state));

        IntPtr sink = _sink;
        if (sink != IntPtr.Zero)
        {
            Win32.PostMessage(sink, WmInvoke, IntPtr.Zero, IntPtr.Zero);
        }
        else
        {
            // Before the sink exists there is no way back to the UI thread; the widget's startup
            // path is single-threaded at that point, so running inline is the best available.
            d(state);
        }
    }

    public override void Send(SendOrPostCallback d, object? state)
    {
        // Nothing in the widget sends; async/await only posts. Running inline keeps a blocking
        // send from deadlocking against a UI thread that is busy in a shell call.
        d(state);
    }

    /// <summary>Runs every queued continuation. Must be called on the UI thread.</summary>
    public void Drain()
    {
        while (true)
        {
            Entry entry;
            lock (_gate)
            {
                if (_queue.Count == 0) return;
                entry = _queue.Dequeue();
            }

            entry.Callback(entry.State);
        }
    }

    private readonly record struct Entry(SendOrPostCallback Callback, object? State);
}
