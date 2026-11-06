using CommunityToolkit.Mvvm.Input;
using m3u8downloader.Services;

namespace m3u8downloader.ViewModels;

/// <summary>「探测页面」捕获到的一个播放列表候选。</summary>
public sealed class ProbeResultViewModel
{
    public ProbeResultViewModel(MediaCandidate candidate, Action<MediaCandidate> use)
    {
        Candidate = candidate;
        UseCommand = new RelayCommand(() => use(candidate));
    }

    public MediaCandidate Candidate { get; }
    public string Url => Candidate.Url;

    public string DisplayUrl => Url.Length > 66 ? Url[..66] + "…" : Url;

    public string DetailText =>
        $"{Candidate.Source} · 匹配度 {Candidate.Score}" +
        (Candidate.Headers.ContainsKey("Cookie") ? " · 携带 Cookie" : "") +
        (string.IsNullOrEmpty(Candidate.MimeType) ? "" : " · " + Candidate.MimeType);

    public IRelayCommand UseCommand { get; }
}
