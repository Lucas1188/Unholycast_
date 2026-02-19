using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using static Unholycast_.PlaybackInfo;

namespace Unholycast_
{
    public record class PlaybackInfo
    {
        public enum Status
        {
            Incomplete,
            Filing,
            Complete
        }
        public required string VideoId { get; set; }
        public string? Title { get; set; }
        public string? Artist { get; set; }
        public string? Source { get; set; }
        public string? Channel { get; set; }
        public string? Album { get; set; }
        public int duration { get; set; }
        public int bytes { get; set; }
        public Status fileStatus { get; set; }
    }
    public static class DIDLInfo
    {
        public static string GetInfo(PlaybackInfo info, string uri)
        {
            return
$"""
<DIDL-Lite xmlns:dc="http://purl.org/dc/elements/1.1/"
    xmlns:upnp="urn:schemas-upnp-org:metadata-1-0/upnp/"
    xmlns:r="urn:schemas-rinconnetworks-com:metadata-1-0/"
    xmlns="urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/">
    <item id="-1" parentID="-1" restricted="true">
        <dc:title>{info.Title}</dc:title>
        <dc:creator>{info.Artist}</dc:creator>
        <upnp:album>{info.Album}</upnp:album>
        <res protocolInfo="http-get:*:audio/mpeg:*"{GetDurationFromSeconds(info.duration)}>{uri}</res>
        <upnp:class>object.item.audioItem.musicTrack</upnp:class>
    </item>
</DIDL-Lite>
""";
        }
        public static string GetDurationFromSeconds(int seconds) {
            var h = seconds / 3600;
            var m = (seconds % 3600) / 60;
            var s = seconds % 60;
            return $"{h}:{m:00}:{s:00}";
        }
    }

    public class PlaybackStore
    {
        private string _filepath;
        public PlaybackStore(string filepath) 
        {
            if (File.Exists(filepath))
            {
                _filepath = filepath;
                using (var f = new StreamReader(File.OpenRead(filepath)))
                {
                    var store = JsonSerializer.Deserialize<Dictionary<string, PlaybackInfo>>(f.ReadToEnd());
                    if (store != null)
                    {
                        Store = new(store);
                    }
                    else
                    {
                        throw new InvalidOperationException("[PlaybackStore] Unable to deserialize store");
                    }
                }
            }
            else
            {
                Console.WriteLine("[PlaybackStore] Cannot open file path provided creating");
                _filepath = filepath;
                using (var f = File.Create(_filepath))
                {
                    f.Write(Encoding.UTF8.GetBytes("{}"));
                } 
            }
        }
        public ConcurrentDictionary<string, PlaybackInfo> Store { get; private set; } = new ();
        public PlaybackInfo.Status AddRecord(PlaybackInfo record) {
            if (record == null) throw new ArgumentNullException("[PlaybackStore] Invalid Record");
            if (!Store.ContainsKey(record.VideoId)) {
                record.bytes = 0;
                record.fileStatus = PlaybackInfo.Status.Incomplete;
                Store.TryAdd(record.VideoId, record); 
            }
            _ = Task.Run(SaveDB);
            return Store[record.VideoId].fileStatus;
        }
        public void SetStatus(string videoId, PlaybackInfo.Status status)
        {
            if (videoId == null) throw new ArgumentNullException("[PlaybackStore] Invalid Record");
            if (!Store.ContainsKey(videoId)) throw new InvalidOperationException("[PlaybackStore] Record not found");
            Store[videoId].fileStatus = status;
            _ = Task.Run(SaveDB);
        }
        public void UpdateFileMeta(string videoId, int realLength, PlaybackInfo.Status endStatus)
        {
            if (videoId == null) throw new ArgumentNullException("[PlaybackStore] Invalid Record");
            if (!Store.ContainsKey(videoId)) throw new InvalidOperationException("[PlaybackStore] Record not found");
            Store[videoId].fileStatus = endStatus;
            Store[videoId].bytes = realLength;
            _ = Task.Run(SaveDB);
        }
        public static string BuildInternalUri(string cIp, int port, string videoId)
        {
            return $"http://{cIp}:{port}/{videoId}.mp3";
        }

        public async Task SaveDB()
        {
            using (var f = File.Open(_filepath, FileMode.OpenOrCreate)) {
                await f.WriteAsync(JsonSerializer.SerializeToUtf8Bytes(this.Store));
            }
        }
    }
}
