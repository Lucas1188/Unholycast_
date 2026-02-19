using Python.Runtime;
using System.Text.Json.Nodes;
using Unholycast_;
using static Unholycast_.PlaybackInfo;

internal class Program
{
    private static async Task Main(string[] args)
    {
        AppConfig config;

        try
        {
            config = ArgParser.Parse(args);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            return;
        }

        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.UseKestrel(serverOptions =>
        {
            serverOptions.ListenAnyIP(config.Port);
        });
        // Add services to the container.
        // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
        builder.Services.AddOpenApi();
#if DEBUG
        Environment.SetEnvironmentVariable("PYTHONNET_PYDLL", builder.Configuration["PythonNet:Pythondll"]);
#endif
        string DB_DIRABS = Path.Join(Directory.GetCurrentDirectory(), "files");
        if (!Directory.Exists(DB_DIRABS))
        {
            Directory.CreateDirectory(DB_DIRABS);
        }
        GlobalStore.DbDirAbs = DB_DIRABS;

        //Python Runtime
        PythonEngine.Initialize();
        PythonEngine.BeginAllowThreads();
        SocoRuntime socoruntime = new SocoRuntime();

        var _store = new PlaybackStore(Path.Join(Directory.GetCurrentDirectory(), "db.db"));

        var _localip = IPUtils.GetLocalIp();
        
        dynamic dev = socoruntime.GetDevice(socoruntime.FindDevice(config.Leader));

        var sonospoller = new SonosPoller(socoruntime, dev);
#if DEBUG
        //sonospoller.OnPolledState += (s,p,v) => { Console.WriteLine($"[SPOLLER] Debug got state: {s} || {p} || {v}"); };
#endif

        var app = builder.Build();
        ShutdownToken.Token = app.Lifetime.ApplicationStopping;

        var _poller = sonospoller.Begin();
        var _manager = new UnholySonosManager(socoruntime, dev, sonospoller);


        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }
        app.MapGet("/poll", () => {
            var _cts = _manager.cTransportState;
            return Results.Ok( new
            {
                status = new
                {
                    _cts.current_transport_status,
                    _cts.current_transport_state,
                    current_speed = _cts.current_transport_speed
                }
            } );
        });
        app.MapGet("/pause", () =>
        {
            try
            {
                _manager.PauseSonos();
                // Python dict returned through pythonnet
                return Results.Ok(new { msg = "Player paused" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[INFO] [HTTP] Could not pause player: {ex}");
                return Results.BadRequest(new { msg = "Pause failed" });
            }
        });


        app.MapGet("/duration", () =>
        {
            try
            {
                // Python dict returned through pythonnet
                //Console.WriteLine($"[INFO] [HTTP] Returning duration of {_manager._currentPlaying?._playbackInfo.Title} - ");
                int dur = 0;
                if (_manager._currentPlaying != null)
                {
                    dur = _manager._currentPlaying._playbackInfo.duration;
                }
                return Results.Ok(new
                {
                    duration = dur
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[INFO] [HTTP] Position get failed: {ex}");
                return Results.Ok(new { msg = "Get duration from soco failed", duration = 0 });
            }
        });

        app.MapGet("/position", () =>
        {
            try
            {
                // Python dict returned through pythonnet
                return Results.Ok(new
                {
                    _manager.cTrackInfo.position,
                    
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[INFO] [HTTP] Position get failed: {ex}");
                return Results.BadRequest(new { msg = "Get position from soco failed" });
            }
        });

        app.MapGet("/seek", async (HttpContext ctx) =>
        {
            try
            {
                var q = ctx.Request.Query;

                string? seekstr = q["pos"].FirstOrDefault();
                if (seekstr == null) return Results.BadRequest(new { msg = "Bad seek params" });

                if (await _manager.TrySeek(int.Parse(seekstr)))
                {
                    var _cts = _manager.cTransportState;

                    return Results.Ok(new
                    {
                        msg = "Seek requested",
                        status = new
                        {
                            _cts.current_transport_status,
                            _cts.current_transport_state,
                            current_speed = _cts.current_transport_speed
                        }
                    });
                }
                else
                {
                    return Results.BadRequest(new { msg = "Seek failed" });
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine($"[INFO] [HTTP] Position get failed: {ex}");
                return Results.BadRequest(new { msg = "Cannot seek" });
            }
        });

        app.MapGet("/resume", async (HttpContext ctx) =>
        {
            try
            {
                _manager.ResumeSonos();
                Console.WriteLine($"[INFO] [HTTP] Resuming Playback");
                return Results.Ok(new { msg = "Resume ok" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[INFO] [HTTP] Resume failed: {ex}");
                return Results.BadRequest(new { msg = "Cannot resume" });

            }

        });

        app.MapGet("/volume", () =>
        {
            return Results.Ok(new { level = _manager.cVolume.value, _manager.cVolume.muted });
        });

        app.MapPost("/volume", async (HttpContext ctx) =>
        {
            try
            {
                // Parse JSON body
                var body = await ctx.Request.ReadFromJsonAsync<JsonObject>();

                if (body == null || !body.ContainsKey("level") || !body.ContainsKey("muted")) return Results.BadRequest(body);

                int level = (int)body["level"]!;
                bool muted = (bool)body["muted"]!;

                Console.WriteLine($"[INFO] [HTTP] Setting volume: {level}, muted={muted}");

                // Apply to leader (your SoCo / pythonnet object)
                _manager.SetVolume(new() { value = level, muted = muted });

                return Results.Ok(new { msg = "Volume updated" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] [HTTP] Volume update failed: {ex}");
                return Results.BadRequest(new { err = "Volume update failed" });
            }
        });

        app.MapGet("/start", async (HttpContext ctx) =>
        {
            try
            {
                var q = ctx.Request.Query;

                string? uri = q["streamUrl"].FirstOrDefault();
                string? videoId = q["videoId"].FirstOrDefault() ?? "";
                string? title = q["title"].FirstOrDefault() ?? "Live Cast";
                string? durationS = q["duration"].FirstOrDefault() ?? "0";
                string? posS = q["position"].FirstOrDefault() ?? "0";
                string? source = q["src"].FirstOrDefault() ?? "yt";
                string? artist = q["artist"].FirstOrDefault() ?? "";
                string? album = q["album"].FirstOrDefault() ?? "";
                string? channel = q["channel"].FirstOrDefault() ?? "";


                if (string.IsNullOrWhiteSpace(uri))
                {
                    ctx.Response.StatusCode = 400;
                    Console.WriteLine("[ERROR] [HTTP] Missin uri parameter");
                    return Results.BadRequest(new { msg = "Missing uri parameter" });
                }

                // Convert numeric params
                int seek = 0;
                int duration = 0;

                _ = int.TryParse(posS, out seek);
                _ = int.TryParse(durationS, out duration);

                Console.WriteLine($"[INFO] [HTTP] /start called with {videoId} - {title} : {duration} @ {seek}");
                try
                {
                    _manager.StopSonos();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WARNING] [HTTP] Sonos transition state: {ex.Message}");
                }
                var fState = _store.AddRecord(new()
                {
                    VideoId = videoId,
                    Title = title,
                    duration = duration,
                });

                _ = Task.Run(_store.SaveDB);

                if (fState == PlaybackInfo.Status.Incomplete)
                {
                    Console.WriteLine($"[PROGRAM] Starting FFMPEG process for {videoId}");
                    FfmpegService.StartFfmpegStream(videoId, uri, seek, duration, title, source, channel, artist, album, _store);
                    await Task.Delay(500);
                }
                var success = await _manager.PlaySonos(_store.Store[videoId], _localip, (int)ctx.Request.Host.Port!);
                if (!success)
                {
                    Console.WriteLine($"[MANAGER] State is NOT PLAYING");
                }
                else
                {
                    if (seek > 0)
                    {
                        if (!await _manager.TrySeek(seek))
                        {
                            Console.WriteLine($"[Manager] Seeking failed during play");
                        }
                    }
                }

                return success ? Results.Ok(new { msg = "Stream started!" }) : Results.BadRequest(new { msg = "Could not set player to start playing" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PROGRAM] Start attempted but failed with exception:\n{ex.Message}");
                return Results.BadRequest(new { msg = "Could not start player" });
            }
        });
        app.MapGet("/store", () =>
        {
            return Results.Ok(_store.Store);
        });

        app.MapGet("/{**path:regex(.*\\.mp3$)}", async (HttpContext ctx, string path) =>
        {
            Console.WriteLine($"[STREAMER] Got request from {ctx.Connection.RemoteIpAddress}");
            int BITRATE = 320_000; // bits per second
            string vName = path[0..^4];
            string fname = Path.Join(DB_DIRABS, path[0..]);

            int guessedSize;
            long resumeAt = 0;

            // DB lookup
            if (_store == null || !_store.Store.ContainsKey(vName))
            {
                ctx.Response.StatusCode = 404;
                Console.WriteLine($"[STREAMER] Tried {vName} | {path} but does not exist as key in store");
                return;
            }
            var meta = _store.Store[vName];

            if (meta.fileStatus == PlaybackInfo.Status.Complete)
            {
                guessedSize = meta.bytes;
            }
            else
            {
                guessedSize = (int)(BITRATE / 8.0 * meta.duration);
                Console.WriteLine($"[STREAMER] Guessing file size = {guessedSize}");
                meta.bytes = guessedSize;
            }

            // Wait up to 2s for file
            var start = DateTime.UtcNow;
            while (!File.Exists(fname) && (DateTime.UtcNow - start).TotalSeconds < 2)
                await Task.Delay(500);

            // Process Range
            if (ctx.Request.Headers.TryGetValue("Range", out var rangeHeader))
            {
                // Expect "bytes=1234-"
                var r = rangeHeader.ToString();
                var num = r[6..].TrimEnd('-');
                resumeAt = long.TryParse(num, out var v) ? v : 0;
                Console.WriteLine($"[STREAMER] Resume at byte {resumeAt}/{guessedSize}");
            }

            if (resumeAt >= guessedSize)
            {
                ctx.Response.StatusCode = 416;
                return;
            }

            string contentRange = $"bytes {resumeAt}-/{guessedSize}";
            long contentLength = guessedSize - resumeAt;

            ctx.Response.StatusCode = resumeAt == 0 ? 200 : 206;
            ctx.Response.ContentType = "application/mp3";
            ctx.Response.Headers["Accept-Ranges"] = "bytes";
            if (resumeAt > 0)
                ctx.Response.Headers["Content-Range"] = contentRange;

            ctx.Response.ContentLength = contentLength;
            var cTime = DateTime.Now;
            Console.WriteLine($"[STREAMER] Streaming {fname} (resume={resumeAt}, CL={contentLength}) --- {DateTime.Now}");

            try
            {
                await using var fs = new FileStream(
                    fname, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite
                );

                if (resumeAt > 0)
                    fs.Seek(resumeAt, SeekOrigin.Begin);

                long written = 0;
                byte[] buffer = new byte[8192];
                var token = _manager.GetToken();

                while (!token.IsCancellationRequested && written < contentLength)
                {
                    // Re-fetch metadata if file growing
                    if (meta.fileStatus == PlaybackInfo.Status.Incomplete)
                        meta = _store.Store[vName];

                    int toRead = (int)Math.Min(buffer.Length, contentLength - written);
                    int readBytes = await fs.ReadAsync(buffer, 0, toRead, token);

                    if (readBytes == 0)
                    {
                        // File still growing
                        if (meta.fileStatus == PlaybackInfo.Status.Filing)
                        {
                            await Task.Delay(100);
                            continue;
                        }

                        // File complete but short — pad the rest
                        long pad = contentLength - written;
                        await ctx.Response.Body.WriteAsync(new byte[pad], token);
                        written += readBytes;
                        Console.WriteLine($"[STREAMER] Wrote {pad} pad bytes");
                        break;
                    }

                    await ctx.Response.Body.WriteAsync(buffer.AsMemory(0, readBytes), token);
                    written += readBytes;
                    //Console.WriteLine($"[STREAMER] Writing TRANSFER - {vName} | {written} / {contentLength} --- {DateTime.Now}");
                    await Task.Delay(50, token);
                }
                Console.WriteLine($"[STREAMER] DONE TRANSFER - {vName} | {written} / {contentLength} --- {(DateTime.Now - cTime).TotalSeconds}s");

                if (token.IsCancellationRequested)
                    Console.WriteLine("[STREAMER] Cancelled by token - maybe a fault has occurred");
            }
            catch (IOException)
            {
                Console.WriteLine("[ERROR] Client disconnected.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Internal error: {ex}");
            }
        });


        var _apptask = app.RunAsync();

        await Task.WhenAll([_poller, _apptask]);

        _manager.StopSonos();
    }
}
