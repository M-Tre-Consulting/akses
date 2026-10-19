using Akses.Core.Engines;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace Akses.App;

/// <summary>
/// Documents → PDF: pandoc writes HTML, and this prints it with WebView2 (the Edge engine that is
/// already part of Windows 10/11) on an invisible WebView2 in the main window. Scripts are off:
/// the page is only laid out and printed. One document at a time.
/// </summary>
internal sealed class WebViewPdfPrinter(WebView2 view, DispatcherQueue dispatcher) : IHtmlToPdf
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task PrintAsync(string htmlPath, string pdfOutput, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await OnUiAsync(async () =>
            {
                await view.EnsureCoreWebView2Async();
                CoreWebView2 core = view.CoreWebView2;
                core.Settings.IsScriptEnabled = false;

                var loaded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                void OnCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args) => loaded.TrySetResult(args.IsSuccess);
                core.NavigationCompleted += OnCompleted;
                try
                {
                    core.Navigate(new Uri(Path.GetFullPath(htmlPath)).AbsoluteUri);
                    Task finished = await Task.WhenAny(loaded.Task, Task.Delay(TimeSpan.FromSeconds(60), ct));
                    ct.ThrowIfCancellationRequested();
                    if (finished != loaded.Task || !loaded.Task.Result) throw new IOException("the document could not be laid out");
                }
                finally
                {
                    core.NavigationCompleted -= OnCompleted;
                }

                CoreWebView2PrintSettings settings = core.Environment.CreatePrintSettings();
                settings.ShouldPrintHeaderAndFooter = false;
                settings.ShouldPrintBackgrounds = true;
                settings.PageWidth = 8.27;   // A4, inches
                settings.PageHeight = 11.69;
                settings.MarginTop = settings.MarginBottom = 0.8;
                settings.MarginLeft = settings.MarginRight = 0.75;
                if (!await core.PrintToPdfAsync(Path.GetFullPath(pdfOutput), settings))
                    throw new IOException("printing to PDF failed");
                core.Navigate("about:blank");
            }).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private Task OnUiAsync(Func<Task> action)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool queued = dispatcher.TryEnqueue(async () =>
        {
            try { await action(); tcs.SetResult(); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        if (!queued) tcs.SetException(new InvalidOperationException("the window is closing"));
        return tcs.Task;
    }
}
