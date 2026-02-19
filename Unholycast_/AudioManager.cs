using Microsoft.AspNetCore.Http.Metadata;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Net.Sockets;

namespace Unholycast_
{
    public static class IPUtils{
        public static string GetLocalIp()
        {
            string localIp = "127.0.0.1";
            try
            {
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    // Doesn't actually send packets, just used to determine outbound interface
                    socket.Connect("8.8.8.8", 80);
                    if (socket.LocalEndPoint is IPEndPoint endPoint)
                    {
                        localIp = endPoint.Address.ToString();
                    }
                }
            }
            catch
            {
                // fallback to loopback
                localIp = "127.0.0.1";
            }

            return localIp;

        }

    }
    public class AudioJob
    {
        public string Status { get; set; } = "pending";
        public long FileSize { get; set; }
        public string VideoId { get; set; } = "";
    }
    public static class GlobalStore
    {
        public static readonly ConcurrentDictionary<string, AudioJob> Db = new();
        public static readonly ConcurrentDictionary<string, Process> FfmpegProcesses = new();

        public static string DbDirAbs = "/data/db";   // whatever your directory is
    }
    public static class FfmpegService
    {
        public static Process StartFfmpegStream(
            string videoId,
            string url,
            int seek,
            double duration,
            string title,
            string source,
            string channel,
            string artist, string album,PlaybackStore playbackStore)
        {
            //Directory.CreateDirectory(GlobalStore.DbDirAbs);

            var outputPath = Path.Join(GlobalStore.DbDirAbs, $"{videoId}.mp3");
            playbackStore.SetStatus(videoId, PlaybackInfo.Status.Filing);
            var args = new List<string>
            {
                "-y","-hide_banner","-nostats","-loglevel","error",

                "-i", $"\"{url}\"",

                "-vn","-acodec","libmp3lame","-b:a","320k",
                "-f","mp3",

                "-metadata", $"title=\"{title}\"",
                "-metadata", $"artist=\"{artist}\"",
                "-metadata", $"duration=\"{duration}\"",
                "-metadata", $"channel=\"{channel}\"",
                "-metadata", $"source=\"{source}\"",

                $"\"{outputPath}\""
            };

            Console.WriteLine($"[FFMPEG] Launching for {videoId}");//: ffmpeg {string.Join(" ", args)}");

            var psi = new ProcessStartInfo("ffmpeg", string.Join(" ", args))
            {
                RedirectStandardError = false,
                RedirectStandardOutput = false,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var proc = Process.Start(psi)!;

            GlobalStore.FfmpegProcesses[videoId] = proc;
            GlobalStore.Db[videoId] = new AudioJob
            {
                VideoId = videoId,
                Status = "pending"
            };

            _ = Task.Run(() => MonitorFfmpegProcess(videoId, proc,playbackStore));

            return proc;
        }
        private static async Task MonitorFfmpegProcess(string videoId, Process proc, PlaybackStore store)
        {

            await proc.WaitForExitAsync();

            var filePath = Path.Combine(GlobalStore.DbDirAbs, $"{videoId}.mp3");
            GlobalStore.Db.TryGetValue(videoId, out var job);
            bool success =
                proc.ExitCode == 0 &&
                File.Exists(filePath) &&
                job != null &&
                job.Status != "incomplete";

            if (success)
            {
                var size = new FileInfo(filePath).Length;
                job!.Status = "ok";
                job.FileSize = size;
                store.UpdateFileMeta(videoId, (int)size, PlaybackInfo.Status.Complete);
                Console.WriteLine($"[FFMPEG] ✅ Completed {videoId}");
            }
            else
            {
                GlobalStore.Db.TryRemove(videoId, out _);
                Console.WriteLine($"[FFMPEG] ❌ Not Complete {videoId} (exit {proc.ExitCode})");
                store.UpdateFileMeta(videoId, 0, PlaybackInfo.Status.Incomplete);

            }
            // clean up tracking
            GlobalStore.FfmpegProcesses.TryRemove(videoId, out _);
            
        }
    }


}


