namespace Unholycast_;

public record class StreamMetadataRequest
{
    public string VideoId { get; set; } = "";
    public string Title { get; set; } = "";
    public int Duration { get; set; }
    public string StreamUrl { get; set; } = "";
    public string Src { get; set; } = "yt";
    public string Channel { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public string PlaylistId { get; set; } = "";
    public string Params { get; set; } = "";
    public int? Index { get; set; }
    public string Ctt { get; set; } = "";
}

public record class ResolveRequest
{
    public string VideoId { get; set; } = "";
    public string PlaylistId { get; set; } = "";
    public string Params { get; set; } = "";
    public int? Index { get; set; }
    public string Ctt { get; set; } = "";
    public string Client { get; set; } = "yt";
}
