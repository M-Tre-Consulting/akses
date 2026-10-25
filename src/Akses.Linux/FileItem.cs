using System.ComponentModel;
using System.Runtime.CompilerServices;
using Akses.Core;

namespace Akses.App;

/// <summary>One file in a list (results, PDF tools), with the state of its last job.</summary>
public sealed class FileItem : INotifyPropertyChanged
{
    private bool _isBusy, _isIndeterminate;
    private double _progress;
    private string _status = "", _error = "", _details;
    private string? _output;

    public FileItem(string path)
    {
        Path = path;
        Kind = FormatCatalog.KindOf(path);
        long size = 0;
        try { size = new FileInfo(path).Length; } catch { }
        _details = $"{Strings.KindLabel(Kind)} · {Strings.FormatSize(size)}";
        if (Kind == FileKind.Unknown) SetError(Strings.UnsupportedFormat);
    }

    public string Path { get; }
    public string Name => System.IO.Path.GetFileName(Path);
    public FileKind Kind { get; }
    public string Details { get => _details; set => Set(ref _details, value); }

    public CancellationTokenSource? Cancellation { get; set; }

    public bool IsBusy { get => _isBusy; private set => Set(ref _isBusy, value); }
    public double Progress { get => _progress; private set => Set(ref _progress, value); }
    public bool IsIndeterminate { get => _isIndeterminate; private set => Set(ref _isIndeterminate, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string Error { get => _error; private set => Set(ref _error, value); }
    /// <summary>The last result (file or folder), for "Apri" and "Mostra nella cartella".</summary>
    public string? Output { get => _output; private set => Set(ref _output, value); }

    public bool HasResult => Output is not null && !IsBusy;

    public void SetQueued(string status)
    {
        Error = "";
        Status = status;
        Progress = 0;
        IsIndeterminate = true;
        IsBusy = true;
    }

    public void SetProgress(double fraction)
    {
        IsIndeterminate = false;
        Progress = Math.Clamp(fraction, 0, 1) * 100;
    }

    public void SetRunning(string status) => Status = status;

    public void SetDone(string output)
    {
        Output = output;
        Status = Strings.DoneSaved(System.IO.Path.GetFileName(output));
        IsBusy = false;
    }

    public void SetError(string message)
    {
        Status = "";
        Error = message;
        IsBusy = false;
    }

    public void SetCancelled()
    {
        Status = Strings.Cancelled;
        IsBusy = false;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
