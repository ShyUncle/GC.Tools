using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using m3u8downloader.ViewModels;

namespace m3u8downloader.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void OnBrowseFolder(object? sender, RoutedEventArgs e)
    {
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "选择保存目录",
                AllowMultiple = false
            });

            if (folders.Count > 0 && DataContext is MainViewModel viewModel)
                viewModel.SaveDirectory = folders[0].Path.LocalPath;
        }
        catch
        {
            // 用户取消或平台不支持文件夹选择器
        }
    }
}
