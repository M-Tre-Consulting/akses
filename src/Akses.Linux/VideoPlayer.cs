using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using LibVLCSharp.Shared;

namespace Akses.App;

/// <summary>
/// The trim preview's player (what MediaPlayer is in the Windows app): the system's libvlc, which
/// plays every format ffmpeg reads, MKV included. VLC draws each frame into our own buffer, copied
/// into a <see cref="WriteableBitmap"/> shown by an ordinary Image, so the video sits inside the
/// window's layout (rounded corners, overlays) with no native child window.
/// Every event arrives on the UI thread. <see cref="Available"/> is false without libvlc: the page
/// then shows ffmpeg's still frames and says audio can't be played.
/// </summary>
internal sealed class VideoPlayer : IDisposable
{
    private const int MaxWidth = 1280;

    private static LibVLC? _libVlc;
    private static bool _initTried;

    private readonly MediaPlayer? _player;
    private readonly object _gate = new();
    private IntPtr _buffer;
    private int _width, _height, _pitch;
    private WriteableBitmap? _bitmap;
    private bool _frameQueued;
    private long? _pendingSeekMs;

    // The delegates handed to native code must stay alive as long as the player.
    private readonly MediaPlayer.LibVLCVideoFormatCb _format;
    private readonly MediaPlayer.LibVLCVideoCleanupCb _cleanup;
    private readonly MediaPlayer.LibVLCVideoLockCb _lock;
    private readonly MediaPlayer.LibVLCVideoDisplayCb _display;

    public bool Available => _player is not null;
    public bool IsPlaying => _player?.IsPlaying == true;

    /// <summary>A new frame is in <see cref="Frame"/> (it may be a new bitmap after a size change).</summary>
    public event Action? FrameReady;
    public event Action<double>? PositionChanged;
    public event Action? StateChanged;
    public event Action<string>? Failed;

    public WriteableBitmap? Frame => _bitmap;

    public VideoPlayer()
    {
        _format = OnFormat;
        _cleanup = OnCleanup;
        _lock = OnLock;
        _display = OnDisplay;

        LibVLC? vlc = Shared();
        if (vlc is null) return;
        try
        {
            _player = new MediaPlayer(vlc);
            _player.SetVideoFormatCallbacks(_format, _cleanup);
            _player.SetVideoCallbacks(_lock, null, _display);
            _player.TimeChanged += (_, e) => Post(() => PositionChanged?.Invoke(e.Time / 1000.0));
            _player.Playing += (_, _) => Post(() =>
            {
                if (_pendingSeekMs is long ms) { _player.Time = ms; _pendingSeekMs = null; }
                StateChanged?.Invoke();
            });
            _player.Paused += (_, _) => Post(() => StateChanged?.Invoke());
            _player.Stopped += (_, _) => Post(() => StateChanged?.Invoke());
            _player.EndReached += (_, _) => Post(() => StateChanged?.Invoke());
            _player.EncounteredError += (_, _) => Post(() => Failed?.Invoke("libvlc error"));
        }
        catch (Exception ex)
        {
            AppLog.Write($"Lettore non disponibile: {ex.Message}");
            _player = null;
        }
    }

    private static LibVLC? Shared()
    {
        if (_initTried) return _libVlc;
        _initTried = true;
        try
        {
            // LibVLCSharp asks for "libvlc", i.e. libvlc.so: on Mint/Ubuntu that name only comes with
            // libvlc-dev, the runtime package (libvlc5) has libvlc.so.5. Point it there.
            NativeLibrary.SetDllImportResolver(typeof(LibVLC).Assembly, (name, assembly, path) =>
                name == "libvlc" && OperatingSystem.IsLinux() && NativeLibrary.TryLoad("libvlc.so.5", out IntPtr handle) ? handle : IntPtr.Zero);
            LibVLCSharp.Shared.Core.Initialize();
            _libVlc = new LibVLC("--quiet", "--no-video-title-show", "--no-sub-autodetect-file");
        }
        catch (Exception ex)
        {
            AppLog.Write($"libvlc non trovata, anteprima senza riproduzione: {ex.Message}");
        }
        return _libVlc;
    }

    public void Open(string path)
    {
        if (_player is null) return;
        _player.Stop();
        _pendingSeekMs = null;
        using var media = new Media(_libVlc!, new Uri(path));
        _player.Media = media;
    }

    /// <summary>Plays from <paramref name="seconds"/> (null = from where it is).</summary>
    public void Play(double? seconds)
    {
        if (_player is null || _player.Media is null) return;
        VLCState state = _player.State;
        if (state == VLCState.Paused)
        {
            if (seconds is double s) _player.Time = (long)(s * 1000);
            _player.SetPause(false);
            return;
        }
        if (state is VLCState.Ended or VLCState.Stopped or VLCState.Error) _player.Stop();
        _pendingSeekMs = seconds is double from ? (long)(from * 1000) : null;
        _player.Play();
    }

    public void Pause()
    {
        if (_player?.IsPlaying == true) _player.SetPause(true);
    }

    /// <summary>Moves the playhead; only meaningful while playing or paused.</summary>
    public void Seek(double seconds)
    {
        if (_player is null) return;
        if (_player.State is VLCState.Playing or VLCState.Paused) _player.Time = (long)(seconds * 1000);
        else _pendingSeekMs = (long)(seconds * 1000);
    }

    public void Stop() => _player?.Stop();

    private static void Post(Action action) => Dispatcher.UIThread.Post(action);

    // ---- frames: VLC's threads -----------------------------------------------------------------

    private uint OnFormat(ref IntPtr opaque, IntPtr chroma, ref uint width, ref uint height, ref uint pitches, ref uint lines)
    {
        // BGRA, scaled down to at most 1280 px wide: plenty for the preview, light to copy.
        Marshal.Copy("RV32"u8.ToArray(), 0, chroma, 4);
        double scale = Math.Min(1, MaxWidth / (double)Math.Max(1, width));
        uint w = (uint)Math.Max(2, Math.Round(width * scale)), h = (uint)Math.Max(2, Math.Round(height * scale));
        width = w;
        height = h;
        pitches = w * 4;
        lines = h;
        lock (_gate)
        {
            if (_buffer != IntPtr.Zero) Marshal.FreeHGlobal(_buffer);
            _width = (int)w;
            _height = (int)h;
            _pitch = (int)w * 4;
            _buffer = Marshal.AllocHGlobal(_pitch * _height);
        }
        return 1;
    }

    private void OnCleanup(ref IntPtr opaque)
    {
        lock (_gate)
        {
            if (_buffer != IntPtr.Zero) Marshal.FreeHGlobal(_buffer);
            _buffer = IntPtr.Zero;
        }
    }

    // VLC decodes into the one buffer (lock) and later says it's time to show it (display), possibly
    // from different threads: _gate only guards the buffer against being reallocated mid-copy.
    private IntPtr OnLock(IntPtr opaque, IntPtr planes)
    {
        Marshal.WriteIntPtr(planes, _buffer);
        return IntPtr.Zero;
    }

    private void OnDisplay(IntPtr opaque, IntPtr picture)
    {
        lock (_gate)
        {
            if (_buffer == IntPtr.Zero) return;
            if (_bitmap is null || _bitmap.PixelSize.Width != _width || _bitmap.PixelSize.Height != _height)
                _bitmap = new WriteableBitmap(new PixelSize(_width, _height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            using (ILockedFramebuffer fb = _bitmap.Lock())
            {
                unsafe
                {
                    long rowBytes = Math.Min(_pitch, fb.RowBytes);
                    for (int y = 0; y < _height; y++)
                        Buffer.MemoryCopy((byte*)_buffer + (long)y * _pitch, (byte*)fb.Address + (long)y * fb.RowBytes, fb.RowBytes, rowBytes);
                }
            }
        }
        // One pending repaint at a time: a slow UI never queues up stale frames.
        if (_frameQueued) return;
        _frameQueued = true;
        Post(() => { _frameQueued = false; FrameReady?.Invoke(); });
    }

    public void Dispose()
    {
        _player?.Stop();
        _player?.Dispose();
    }
}
