using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Windows.Foundation;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace ChurchPresenter.Adapters.Media;

/// <summary>
/// Warms the Windows media decode pipeline for a list of media items by cycling a background
/// <see cref="MediaPlayer"/> through each one. After a file has been opened once, the OS page
/// cache and the media-framework decoder state remain warm so that subsequent real loads from
/// <see cref="ChurchPresenter.Controls.OutputMediaSlotView"/> complete in ~20 ms instead of ~200 ms.
/// </summary>
public sealed class MediaCachePrimerService : IDisposable
{
    private CancellationTokenSource? _cts;

    /// <summary>
    /// Starts (or restarts) a background priming pass over <paramref name="items"/>.
    /// Any in-progress pass is cancelled before the new one begins.
    /// Safe to call from any thread.
    /// </summary>
    public void PrimeItems(IReadOnlyList<(Uri uri, bool loop)> items)
    {
        if (items == null || items.Count == 0)
            return;

        var prev = Interlocked.Exchange(ref _cts, new CancellationTokenSource());
        prev?.Cancel();
        prev?.Dispose();

        var ct = _cts!.Token;
        _ = Task.Run(() => RunAsync(items, ct), ct);
    }

    private static async Task RunAsync(IReadOnlyList<(Uri uri, bool loop)> items, CancellationToken ct)
    {
        foreach (var (uri, loop) in items)
        {
            if (ct.IsCancellationRequested)
                return;

            try
            {
                await PrimeOneAsync(uri, loop, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // Swallow per-item errors; a single bad file should not stop the rest.
            }
        }
    }

    /// <summary>
    /// Opens a temporary <see cref="MediaPlayer"/> for the given URI, waits for
    /// <see cref="MediaPlayer.MediaOpened"/> (first frame decoded), then disposes the player.
    /// The OS file cache and media-framework state remain warm for the next real load.
    /// </summary>
    private static Task PrimeOneAsync(Uri uri, bool loop, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var player = new MediaPlayer { AutoPlay = false, RealTimePlayback = true };

        TypedEventHandler<MediaPlayer, object>? openedHandler = null;
        TypedEventHandler<MediaPlayer, MediaPlayerFailedEventArgs>? failedHandler = null;
        CancellationTokenRegistration reg = default;

        void Cleanup()
        {
            if (openedHandler != null) player.MediaOpened -= openedHandler;
            if (failedHandler != null) player.MediaFailed -= failedHandler;
            reg.Unregister();
            try { player.Source = null; } catch { }
            try { player.Dispose(); } catch { }
        }

        openedHandler = new TypedEventHandler<MediaPlayer, object>((_, _) => { Cleanup(); tcs.TrySetResult(true); });
        failedHandler = new TypedEventHandler<MediaPlayer, MediaPlayerFailedEventArgs>((_, _) => { Cleanup(); tcs.TrySetResult(false); });

        player.MediaOpened += openedHandler;
        player.MediaFailed += failedHandler;
        reg = ct.Register(() => { Cleanup(); tcs.TrySetCanceled(ct); });

        player.Source = CreateSource(uri, loop);
        return tcs.Task;
    }

    private static IMediaPlaybackSource CreateSource(Uri uri, bool loop)
    {
        if (!loop)
            return MediaSource.CreateFromUri(uri);

        var item = new MediaPlaybackItem(MediaSource.CreateFromUri(uri));
        var list = new MediaPlaybackList { AutoRepeatEnabled = true, MaxPlayedItemsToKeepOpen = 1 };
        list.Items.Add(item);
        return list;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        var cts = Interlocked.Exchange(ref _cts, null);
        cts?.Cancel();
        cts?.Dispose();
    }
}
