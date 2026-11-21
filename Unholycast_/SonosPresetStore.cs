using System.Text.Json;

namespace Unholycast_;

public record class SonosPresetDevice
{
    public required string Ip { get; init; }
    public required string CoordinatorIp { get; init; }
    public int Volume { get; init; }
    public bool Muted { get; init; }
}

public record class SonosPreset
{
    public required string Name { get; init; }
    public List<SonosPresetDevice> Devices { get; init; } = new();
    public DateTime CreatedUtc { get; init; }
    public DateTime UpdatedUtc { get; init; }
}

public class SonosPresetStore
{
    private readonly string _presetFile;
    private readonly object _sync = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public SonosPresetStore(string dbDir)
    {
        _presetFile = Path.Join(dbDir, "sonos-presets.json");
    }

    public IReadOnlyCollection<SonosPreset> GetAll()
    {
        lock (_sync)
        {
            return LoadAll().Values.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    public SonosPreset? Get(string name)
    {
        lock (_sync)
        {
            var all = LoadAll();
            return all.TryGetValue(name, out var preset) ? preset : null;
        }
    }

    public SonosPreset Save(string name, IEnumerable<SonosDeviceInfo> devices)
    {
        lock (_sync)
        {
            var all = LoadAll();
            var now = DateTime.UtcNow;

            var list = devices.Select(d => new SonosPresetDevice
            {
                Ip = d.Ip,
                CoordinatorIp = d.CoordinatorIp,
                Volume = d.Volume,
                Muted = d.Muted
            }).ToList();

            var created = all.TryGetValue(name, out var existing) ? existing.CreatedUtc : now;

            var preset = new SonosPreset
            {
                Name = name,
                Devices = list,
                CreatedUtc = created,
                UpdatedUtc = now
            };

            all[name] = preset;
            SaveAll(all);
            return preset;
        }
    }

    public bool Delete(string name)
    {
        lock (_sync)
        {
            var all = LoadAll();
            var removed = all.Remove(name);
            if (removed)
            {
                SaveAll(all);
            }
            return removed;
        }
    }

    private Dictionary<string, SonosPreset> LoadAll()
    {
        if (!File.Exists(_presetFile))
        {
            return new Dictionary<string, SonosPreset>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var json = File.ReadAllText(_presetFile);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new Dictionary<string, SonosPreset>(StringComparer.OrdinalIgnoreCase);
            }

            var loaded = JsonSerializer.Deserialize<List<SonosPreset>>(json, _jsonOptions) ?? new();
            return loaded.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PRESET] Failed loading presets: {ex.Message}");
            return new Dictionary<string, SonosPreset>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void SaveAll(Dictionary<string, SonosPreset> presets)
    {
        try
        {
            var list = presets.Values.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
            var json = JsonSerializer.Serialize(list, _jsonOptions);
            File.WriteAllText(_presetFile, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PRESET] Failed saving presets: {ex.Message}");
            throw;
        }
    }
}
