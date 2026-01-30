using System.Collections.ObjectModel;
using CommunityToolkit.Maui.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gallery.Application.Interfaces;
using Gallery.Domain.Enums;
using Gallery.Domain.Models;
using Gallery.Infrastructure.Services;

namespace Gallery.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ILibraryStore _libraryStore;
    private readonly IMediaItemStore _itemStore;
    private readonly IItemIndexService _indexService;
    private readonly ThumbWorker _thumbWorker;

    [ObservableProperty]
    private ObservableCollection<LibraryFolder> _folders = [];

    [ObservableProperty]
    private ObservableCollection<MediaItem> _items = [];

    [ObservableProperty]
    private MediaItem? _selectedItem;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _scanStatus = string.Empty;

    [ObservableProperty]
    private int _itemCount;

    [ObservableProperty]
    private int _pendingThumbs;

    public MainViewModel(
        ILibraryStore libraryStore,
        IMediaItemStore itemStore,
        IItemIndexService indexService,
        ThumbWorker thumbWorker)
    {
        _libraryStore = libraryStore;
        _itemStore = itemStore;
        _indexService = indexService;
        _thumbWorker = thumbWorker;

        _thumbWorker.ThumbGenerated += OnThumbGenerated;
    }

    public async Task InitializeAsync()
    {
        await LoadFoldersAsync();
        await LoadItemsAsync();
        _thumbWorker.Start();
    }

    [RelayCommand]
    private async Task AddFolderAsync()
    {
        var result = await FolderPicker.Default.PickAsync(CancellationToken.None);
        if (result.IsSuccessful && !string.IsNullOrEmpty(result.Folder?.Path))
        {
            var folder = await _libraryStore.AddAsync(result.Folder.Path);
            Folders.Add(folder);
            await ScanFolderAsync(folder);
        }
    }

    [RelayCommand]
    private async Task RemoveFolderAsync(LibraryFolder folder)
    {
        await _libraryStore.RemoveAsync(folder.Id);
        Folders.Remove(folder);
    }

    [RelayCommand]
    private async Task ScanAllAsync()
    {
        if (IsScanning) return;

        IsScanning = true;
        ScanStatus = "Starting scan...";

        try
        {
            var progress = new Progress<ScanProgress>(p =>
            {
                ScanStatus = $"Scanning: {Path.GetFileName(p.CurrentFile)} ({p.FilesScanned}/{p.FilesTotal})";
            });

            await _indexService.ScanAllAsync(progress);
            await LoadItemsAsync();
            ScanStatus = $"Scan complete. {ItemCount} items indexed.";
        }
        finally
        {
            IsScanning = false;
        }
    }

    private async Task ScanFolderAsync(LibraryFolder folder)
    {
        if (IsScanning) return;

        IsScanning = true;
        ScanStatus = $"Scanning {folder.Path}...";

        try
        {
            var progress = new Progress<ScanProgress>(p =>
            {
                ScanStatus = $"Scanning: {Path.GetFileName(p.CurrentFile)} ({p.FilesScanned}/{p.FilesTotal})";
            });

            await _indexService.ScanFolderAsync(folder, progress);
            await LoadItemsAsync();
            ScanStatus = $"Scan complete. {ItemCount} items indexed.";
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync()
    {
        if (SelectedItem is null) return;

        var newValue = !SelectedItem.IsFavorite;
        await _itemStore.SetFavoriteAsync(SelectedItem.Id, newValue);
        SelectedItem = SelectedItem with { IsFavorite = newValue };

        // Update in collection
        var index = Items.IndexOf(Items.FirstOrDefault(i => i.Id == SelectedItem.Id)!);
        if (index >= 0)
        {
            Items[index] = SelectedItem;
        }
    }

    [RelayCommand]
    private async Task SetRatingAsync(int rating)
    {
        if (SelectedItem is null) return;

        await _itemStore.SetRatingAsync(SelectedItem.Id, rating);
        SelectedItem = SelectedItem with { Rating = rating };
    }

    [RelayCommand]
    private async Task OpenInExplorerAsync()
    {
        if (SelectedItem is null) return;

        var directory = Path.GetDirectoryName(SelectedItem.Path);
        if (!string.IsNullOrEmpty(directory))
        {
            await Launcher.Default.OpenAsync(new OpenFileRequest
            {
                File = new ReadOnlyFile(SelectedItem.Path)
            });
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadItemsAsync();
    }

    private async Task LoadFoldersAsync()
    {
        var folders = await _libraryStore.GetAllAsync();
        Folders = new ObservableCollection<LibraryFolder>(folders);
    }

    private async Task LoadItemsAsync()
    {
        var items = await _itemStore.GetAllAsync(limit: 5000);
        Items = new ObservableCollection<MediaItem>(items);
        ItemCount = Items.Count;
    }

    private void OnThumbGenerated(object? sender, ThumbGeneratedEventArgs e)
    {
        // Update the item in the collection on UI thread
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var item = Items.FirstOrDefault(i => i.Id == e.ItemId);
            if (item is not null)
            {
                var index = Items.IndexOf(item);
                var updated = e.Size == ThumbSize.Small
                    ? item with { ThumbSmallPath = e.ThumbPath }
                    : item with { ThumbLargePath = e.ThumbPath };
                Items[index] = updated;

                // Update selected item if it's the one that changed
                if (SelectedItem?.Id == e.ItemId)
                {
                    SelectedItem = updated;
                }
            }
        });
    }
}
