using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace SolDesktop.Services.Status;

/// <summary>Now-playing info and transport controls via the system media session (SMTC).</summary>
public sealed class MediaService : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;

    public bool HasSession => _session is not null;
    public string? Title { get; private set; }
    public string? Artist { get; private set; }
    public bool IsPlaying { get; private set; }
    public ImageSource? Artwork { get; private set; }

    public event EventHandler? Changed;

    public MediaService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public async Task InitializeAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.CurrentSessionChanged += OnCurrentSessionChanged;
            await AttachAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
    }

    public async Task TogglePlayPauseAsync()
    {
        if (_session is null) return;
        try { await _session.TryTogglePlayPauseAsync(); } catch (Exception ex) { Log.Error(ex); }
    }

    public async Task NextAsync()
    {
        if (_session is null) return;
        try { await _session.TrySkipNextAsync(); } catch (Exception ex) { Log.Error(ex); }
    }

    public async Task PreviousAsync()
    {
        if (_session is null) return;
        try { await _session.TrySkipPreviousAsync(); } catch (Exception ex) { Log.Error(ex); }
    }

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args) =>
        _dispatcher.InvokeAsync(AttachAsync);

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) =>
        _dispatcher.InvokeAsync(RefreshAsync);

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) =>
        _dispatcher.InvokeAsync(RefreshAsync);

    private async Task AttachAsync()
    {
        if (_session is not null)
        {
            _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        }

        try
        {
            _session = _manager?.GetCurrentSession();
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            _session = null;
        }

        if (_session is not null)
        {
            _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged += OnPlaybackInfoChanged;
        }

        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        GlobalSystemMediaTransportControlsSession? session = _session;
        if (session is null)
        {
            Title = Artist = null;
            IsPlaying = false;
            Artwork = null;
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        try
        {
            IsPlaying = session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var props = await session.TryGetMediaPropertiesAsync();
            if (session != _session) return;

            Title = string.IsNullOrWhiteSpace(props.Title) ? null : props.Title;
            Artist = string.IsNullOrWhiteSpace(props.Artist) ? props.AlbumTitle : props.Artist;
            Artwork = await LoadArtworkAsync(props.Thumbnail);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static async Task<ImageSource?> LoadArtworkAsync(IRandomAccessStreamReference? thumbnail)
    {
        if (thumbnail is null) return null;
        try
        {
            using IRandomAccessStreamWithContentType stream = await thumbnail.OpenReadAsync();
            using Stream managed = stream.AsStreamForRead();
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 128;
            bitmap.StreamSource = managed;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            return null;
        }
    }

    public void Dispose()
    {
        if (_manager is not null) _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
        if (_session is not null)
        {
            _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        }
    }
}
