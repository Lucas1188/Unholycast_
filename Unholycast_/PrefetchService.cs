using System.Collections.Concurrent;
using System.Net.Http.Json;

namespace Unholycast_;

public sealed class PrefetchService
{
    private readonly PlaybackStore _store;
    private readonly string _dbDir;
    private readonly string _resolverUrl;
    private readonly SemaphoreSlim _worker = new(1, 1);
    private readonly ConcurrentQueue<StreamMetadataRequest> _queue = new();
    private readonly TimeSpan _ttl = TimeSpan.FromMinutes(45);
    private const long MaxCacheBytes = 250L * 1024 * 1024;
    private const int MaxPrefetchCount = 3;

    public PrefetchService(PlaybackStore store, string dbDir, string resolverUrl)
    {
        _store = store;
        _dbDir = dbDir;
        _resolverUrl = resolverUrl.TrimEnd('/');
    }

    public void Enqueue(StreamMetadataRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.VideoId))
            return;

        if (_store.Store.TryGetValue(request.VideoId, out var existing) &&
            existing.fileStatus == PlaybackInfo.Status.Complete &&
            File.Exists(Path.Join(_dbDir, $"{request.VideoId}.mp3")))
        {
            Console.WriteLine($"[PREFETCH] Cache hit for {request.VideoId}; skipping");
            return;
        }

        if (_queue.Any(x => x.VideoId == request.VideoId))
            return;

        _queue.Enqueue(request);
        _ = Task.Run(ProcessQueueAsync);
    }

    private async Task ProcessQueueAsync()
    {
        await _worker.WaitAsync();
        try
        {
            while (_queue.TryDequeue(out var request))
            {
                await EvictAsync(request.VideoId);

                var targetFile = Path.Join(_dbDir, $"{request.VideoId}.mp3");
                if (_store.Store.TryGetValue(request.VideoId, out var existing))
                {
                    if (existing.fileStatus == PlaybackInfo.Status.Complete &&
                        File.Exists(targetFile) &&
                        new FileInfo(targetFile).Length > 0)
                        continue;

                    if (existing.fileStatus == PlaybackInfo.Status.Filing &&
                        File.Exists(targetFile) &&
                        new FileInfo(targetFile).Length > 0 &&
                        DateTime.UtcNow - File.GetLastWriteTimeUtc(targetFile) < TimeSpan.FromMinutes(2))
                        continue;

                    if (existing.fileStatus == PlaybackInfo.Status.Complete ||
                        existing.fileStatus == PlaybackInfo.Status.Filing)
                    {
                        Console.WriteLine($"[PREFETCH] Repairing stale {existing.fileStatus} record for {request.VideoId}");
                        _store.SetStatus(request.VideoId, PlaybackInfo.Status.Incomplete);
                        _store.UpdateFileMeta(request.VideoId, 0, PlaybackInfo.Status.Incomplete);
                    }
                }

                var resolved = await ResolveAsync(request);
                if (resolved == null)
                    continue;

                _store.AddRecord(new PlaybackInfo
                {
                    VideoId = resolved.VideoId,
                    Title = string.IsNullOrWhiteSpace(resolved.Title) ? request.Title : resolved.Title,
                    duration = resolved.Duration > 0 ? resolved.Duration : request.Duration,
                    Source = string.IsNullOrWhiteSpace(resolved.Src) ? request.Src : resolved.Src,
                    Channel = string.IsNullOrWhiteSpace(resolved.Channel) ? request.Channel : resolved.Channel,
                    Artist = string.IsNullOrWhiteSpace(resolved.Artist) ? request.Artist : resolved.Artist,
                    Album = string.IsNullOrWhiteSpace(resolved.Album) ? request.Album : resolved.Album
                });

                var finalTitle = string.IsNullOrWhiteSpace(resolved.Title) ? request.Title : resolved.Title;
                var finalDuration = resolved.Duration > 0 ? resolved.Duration : request.Duration;
                var finalSrc = string.IsNullOrWhiteSpace(resolved.Src) ? request.Src : resolved.Src;
                var finalChannel = string.IsNullOrWhiteSpace(resolved.Channel) ? request.Channel : resolved.Channel;
                var finalArtist = string.IsNullOrWhiteSpace(resolved.Artist) ? request.Artist : resolved.Artist;
                var finalAlbum = string.IsNullOrWhiteSpace(resolved.Album) ? request.Album : resolved.Album;

                FfmpegService.StartFfmpegStream(
                    resolved.VideoId,
                    resolved.StreamUrl,
                    0,
                    finalDuration,
                    finalTitle,
                    finalSrc,
                    finalChannel,
                    finalArtist,
                    finalAlbum,
                    _store);

                var file = Path.Join(_dbDir, $"{resolved.VideoId}.mp3");
                var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(10);
                while (DateTime.UtcNow < deadline)
                {
                    if (_store.Store.TryGetValue(resolved.VideoId, out var meta) &&
                        meta.fileStatus == PlaybackInfo.Status.Complete)
                        break;
                    await Task.Delay(500);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PREFETCH] Queue error: {ex.Message}");
        }
        finally
        {
            _worker.Release();
        }
    }

    private async Task<StreamMetadataRequest?> ResolveAsync(StreamMetadataRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.StreamUrl) &&
            request.StreamUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return request;

        if (string.IsNullOrWhiteSpace(_resolverUrl))
        {
            Console.WriteLine($"[PREFETCH] No stream URL and no resolver configured for {request.VideoId}");
            return null;
        }

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            var response = await client.PostAsJsonAsync(_resolverUrl, new ResolveRequest
            {
                VideoId = request.VideoId,
                PlaylistId = request.PlaylistId,
                Params = request.Params,
                Index = request.Index,
                Ctt = request.Ctt,
                Client = request.Src == "ytmusic" ? "ytmusic" : "yt"
            });
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[PREFETCH] Resolve failed for {request.VideoId}: HTTP {(int)response.StatusCode}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var resolved = System.Text.Json.JsonSerializer.Deserialize<StreamMetadataRequest>(json, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (resolved == null || string.IsNullOrWhiteSpace(resolved.StreamUrl))
                return null;

            return resolved with
            {
                PlaylistId = request.PlaylistId,
                Params = request.Params,
                Index = request.Index,
                Ctt = request.Ctt
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PREFETCH] Resolve error for {request.VideoId}: {ex.Message}");
            return null;
        }
    }

    private async Task EvictAsync(string? protectedVideoId = null)
    {
        var candidates = _store.Store
            .Where(x => x.Value.fileStatus == PlaybackInfo.Status.Complete)
            .Select(x => new
            {
                x.Key,
                x.Value,
                Path = Path.Join(_dbDir, $"{x.Key}.mp3")
            })
            .Where(x => File.Exists(x.Path))
            .ToList();

        long total = candidates.Sum(x => new FileInfo(x.Path).Length);
        var now = DateTime.UtcNow;

        foreach (var candidate in candidates
            .Where(x => now - File.GetLastWriteTimeUtc(x.Path) > _ttl)
            .OrderBy(x => File.GetLastWriteTimeUtc(x.Path))
            .ToList())
        {
            var size = new FileInfo(candidate.Path).Length;
            Delete(candidate.Path, candidate.Key);
            total -= size;
        }

        foreach (var candidate in candidates
            .Where(x => File.Exists(x.Path))
            .OrderBy(x => File.GetLastWriteTimeUtc(x.Path))
            .ToList())
        {
            if (total <= MaxCacheBytes && CountCompleted() <= MaxPrefetchCount)
                break;

            if (candidate.Key == protectedVideoId)
                continue;
            var size = new FileInfo(candidate.Path).Length;
            Delete(candidate.Path, candidate.Key);
            total -= size;
        }

        await _store.SaveDB();
    }

    private int CountCompleted()
    {
        return _store.Store.Count(x => x.Value.fileStatus == PlaybackInfo.Status.Complete &&
            File.Exists(Path.Join(_dbDir, $"{x.Key}.mp3")));
    }

    private void Delete(string path, string videoId)
    {
        try
        {
            File.Delete(path);
            _store.Store.TryRemove(videoId, out _);
            Console.WriteLine($"[PREFETCH] Evicted {videoId}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PREFETCH] Eviction failed for {videoId}: {ex.Message}");
        }
    }
}
