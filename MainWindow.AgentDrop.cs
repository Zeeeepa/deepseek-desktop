using System.Windows;
using DeepSeekBrowser.Services;

namespace DeepSeekBrowser;

public partial class MainWindow
{
    private int _agentFileDragDepth;

    private void WireAgentFileDragRouting()
    {
        AllowDrop = true;
        PreviewDragEnter += OnWindowAgentFileDragEnter;
        PreviewDragOver += OnWindowAgentFileDragOver;
        PreviewDragLeave += OnWindowAgentFileDragLeave;
        PreviewDrop += OnWindowAgentFilePreviewDrop;
    }

    private static bool IsFileDrag(System.Windows.DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop);

    private bool AgentAcceptsFileDrag() =>
        _webViewReady && _webHost is { IsAgentVisible: true };

    private void OnWindowAgentFileDragEnter(object sender, DragEventArgs e)
    {
        if (!AgentAcceptsFileDrag() || !IsFileDrag(e))
            return;

        _agentFileDragDepth++;
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
        PostAgentDragHover(true);
    }

    private void OnWindowAgentFileDragOver(object sender, DragEventArgs e)
    {
        if (!AgentAcceptsFileDrag() || !IsFileDrag(e))
            return;

        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
        PostAgentDragHover(true);
    }

    private void OnWindowAgentFileDragLeave(object sender, DragEventArgs e)
    {
        if (!IsFileDrag(e))
            return;

        _agentFileDragDepth = Math.Max(0, _agentFileDragDepth - 1);
        if (_agentFileDragDepth == 0)
            PostAgentDragHover(false);
    }

    private void OnWindowAgentFilePreviewDrop(object sender, DragEventArgs e)
    {
        _agentFileDragDepth = 0;
        PostAgentDragHover(false);

        if (!AgentAcceptsFileDrag() || !IsFileDrag(e))
            return;

        if (e.Data.GetData(DataFormats.FileDrop) is not string[] raw || raw.Length == 0)
            return;

        e.Effects = DragDropEffects.Copy;
        e.Handled = true;

        var paths = raw
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim().Trim('"'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (paths.Length == 0)
            return;

        System.Diagnostics.Trace.WriteLine("[DeepSeek] WPF Drop: " + string.Join("; ", paths));
        DeliverAgentDroppedPaths(paths);
    }

    private void PostAgentDragHover(bool active)
    {
        if (_webHost is null || !AgentAcceptsFileDrag())
            return;
        _ = _webHost.Agent.PostToPageAsync(new { type = "agentDragHover", active });
    }

    private void DeliverAgentDroppedPaths(IReadOnlyList<string> paths)
    {
        if (_webHost is null)
            return;
        _ = _webHost.Agent.PostToPageAsync(new { type = "agentDroppedPaths", paths });
    }

    private void RefreshAgentNativeDropTargets()
    {
        try
        {
            _nativeFileDrop?.RefreshAgentDropTargets(AgentWebView, WebSurfaceGrid);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine("[DeepSeek] RefreshAgentNativeDropTargets: " + ex.Message);
        }
    }
}
