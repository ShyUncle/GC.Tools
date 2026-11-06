using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using m3u8downloader.Services;

namespace m3u8downloader.ViewModels;

/// <summary>单个下载任务的展示模型，所有数值由主界面的定时器统一刷新。</summary>
public partial class DownloadItemViewModel : ObservableObject
{
    private readonly DownloadJob _job;
    private readonly Action<DownloadItemViewModel> _onRemove;
    private long _lastBytes;
    private double _speed;

    public DownloadItemViewModel(DownloadJob job, Action<DownloadItemViewModel> onRemove)
    {
        _job = job;
        _onRemove = onRemove;
    }

    public DownloadJob Job => _job;
    public string FileName => _job.FileName;
    public string DisplayFileName => _job.DisplayFileName;
    public string Url => _job.Url;
    public string OutputPath => _job.OutputPath;
    public bool HasVisibleFile => _job.HasVisibleFile;

    /// <summary>在卡片上直接告诉用户去哪个文件夹找文件，避免「下载中却看不到文件」的困惑。</summary>
    public string LocationText
    {
        get
        {
            if (State == DownloadState.Completed) return "已保存到：" + _job.OutputPath;
            if (_job.HasVisibleFile) return "边下边写：" + _job.VisiblePath;
            return "输出到：" + _job.OutputPath + "（全部分片完成后生成）";
        }
    }
    public DownloadState State => _job.State;
    public bool IsLive => _job.IsLive;
    public bool IsLiveActive => IsLive && State is DownloadState.Running or DownloadState.Merging;
    public bool IsIndeterminate => IsLiveActive;
    public bool CanFinishRecording => IsLive && State is not (DownloadState.Completed or DownloadState.Canceled);

    public string StateText => (State, IsLive) switch
    {
        (DownloadState.Running, true) => "直播录制中",
        (DownloadState.Paused, true) => "直播已暂停",
        (DownloadState.Merging, true) => "保存录制",
        (DownloadState.Pending, _) => "等待中",
        (DownloadState.Running, _) => "下载中",
        (DownloadState.Paused, _) => "已暂停",
        (DownloadState.Merging, _) => "合并中",
        (DownloadState.Completed, _) => "已完成",
        (DownloadState.Failed, _) => "失败",
        (DownloadState.Canceled, _) => "已取消",
        _ => "准备中"
    };

    public bool IsRunning => State is DownloadState.Running or DownloadState.Merging;
    public bool IsPaused => State is DownloadState.Paused or DownloadState.Pending;
    public bool IsFailed => State == DownloadState.Failed;
    public bool IsCompleted => State == DownloadState.Completed;
    public bool CanPauseResume => State is DownloadState.Running or DownloadState.Paused or DownloadState.Failed;
    public string PauseResumeText => State == DownloadState.Running ? "暂停" : "继续";

    public double ProgressPercent =>
        IsLiveActive ? 0 :
        _job.TotalSegments == 0
            ? (State == DownloadState.Completed ? 100 : 0)
            : Math.Min(100, _job.CompletedSegments * 100.0 / _job.TotalSegments);

    public string InfoText
    {
        get
        {
            if (IsLive && State is not (DownloadState.Completed or DownloadState.Failed or DownloadState.Canceled))
                return $"直播中 · 已录制 {_job.CompletedSegments} 段 · {FormatBytes(_job.TotalBytes)}" +
                       (IsRunning ? $" · {FormatBytes((long)_speed)}/s" : "");

            var counts = _job.TotalSegments > 0
                ? $"{_job.CompletedSegments}/{_job.TotalSegments} 段 · {FormatBytes(_job.TotalBytes)}"
                : "";
            var speed = IsRunning ? $" · {FormatBytes((long)_speed)}/s" : "";
            if (State is DownloadState.Failed or DownloadState.Completed or DownloadState.Canceled)
                return counts.Length > 0 ? $"{counts} · {_job.Message}" : _job.Message;
            if (State == DownloadState.Merging) return _job.Message;
            if (counts.Length > 0) return counts + speed;
            return _job.Message;
        }
    }

    public void Refresh()
    {
        var now = _job.TotalBytes;
        _speed = (now - _lastBytes) * 2.0; // 500ms 刷新一次
        _lastBytes = now;

        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(IsLive));
        OnPropertyChanged(nameof(IsLiveActive));
        OnPropertyChanged(nameof(IsIndeterminate));
        OnPropertyChanged(nameof(CanFinishRecording));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(IsFailed));
        OnPropertyChanged(nameof(IsCompleted));
        OnPropertyChanged(nameof(CanPauseResume));
        OnPropertyChanged(nameof(PauseResumeText));
        OnPropertyChanged(nameof(ProgressPercent));
        OnPropertyChanged(nameof(InfoText));
        OnPropertyChanged(nameof(OutputPath));
        OnPropertyChanged(nameof(DisplayFileName));
        OnPropertyChanged(nameof(HasVisibleFile));
        OnPropertyChanged(nameof(LocationText));
        PauseResumeCommand.NotifyCanExecuteChanged();
        FinishRecordingCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanPauseResume))]
    private void PauseResume()
    {
        switch (State)
        {
            case DownloadState.Running or DownloadState.Merging:
                _job.Pause();
                break;
            case DownloadState.Paused:
            case DownloadState.Failed:
            case DownloadState.Pending:
                _job.Start();
                break;
        }
        Refresh();
    }

    [RelayCommand(CanExecute = nameof(CanFinishRecording))]
    private void FinishRecording()
    {
        _job.FinishRecording();
        Refresh();
    }

    [RelayCommand]
    private void OpenFolder()
    {
        try
        {
            var directory = _job.TaskDirectory;
            Directory.CreateDirectory(directory);
            var file = State == DownloadState.Completed ? _job.OutputPath
                       : _job.HasVisibleFile ? _job.VisiblePath : null;
            // Windows 下直接选中目标文件，一眼就能看到
            if (file is not null && File.Exists(file) && OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{file}\"",
                    UseShellExecute = true
                });
                return;
            }
            Process.Start(new ProcessStartInfo { FileName = directory, UseShellExecute = true });
        }
        catch { /* 无文件管理器时忽略 */ }
    }

    [RelayCommand]
    private void OpenCache()
    {
        try
        {
            Directory.CreateDirectory(_job.TempDirectory);
            Process.Start(new ProcessStartInfo { FileName = _job.TempDirectory, UseShellExecute = true });
        }
        catch { /* 无文件管理器时忽略 */ }
    }

    public void CleanupCache() => _job.CleanupCache();

    [RelayCommand]
    private void Remove()
    {
        _job.Stop();
        _onRemove(this);
    }

    public void Dispose() => _job.Dispose();

    private static string FormatBytes(long bytes)
    {
        var value = (double)bytes;
        if (value < 1024) return $"{value:0} B";
        if (value < 1024 * 1024) return $"{value / 1024:0.0} KB";
        if (value < 1024L * 1024 * 1024) return $"{value / (1024 * 1024):0.0} MB";
        return $"{value / (1024L * 1024 * 1024):0.00} GB";
    }
}
