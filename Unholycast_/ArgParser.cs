public class AppConfig
{
    public string Leader { get; set; }
    public List<string> Followers { get; set; } = new();
    public int Port { get; set; } = 6969;
    public string NewName { get; set; }
}
public static class ArgParser
{
    public static AppConfig Parse(string[] args)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var listArgs = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];

            if (!a.StartsWith("--"))
                continue;

            string key;
            string value = null;

            // --key=value
            if (a.Contains('='))
            {
                var parts = a.Substring(2).Split('=', 2);
                key = parts[0];
                value = parts[1];
            }
            else
            {
                key = a.Substring(2);

                // If next arg exists and is not another flag → treat as value
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                {
                    value = args[i + 1];
                    i++;
                }
                else
                {
                    value = "true";
                }
            }

            // For followers which can have multiple values
            if (key == "followers")
            {
                if (!listArgs.ContainsKey(key))
                    listArgs[key] = new List<string>();

                // Support comma-separated (CLI style)
                listArgs[key].AddRange(
                    value.Split(',', StringSplitOptions.RemoveEmptyEntries)
                );
            }
            else
            {
                dict[key] = value;
            }
        }

        // Build AppConfig
        var config = new AppConfig();

        if (!dict.TryGetValue("leader", out var leader))
            throw new ArgumentException("Missing required argument: --leader");

        config.Leader = leader;

        if (listArgs.TryGetValue("followers", out var follList))
            config.Followers = follList;

        if (dict.TryGetValue("port", out var portString) &&
            int.TryParse(portString, out var port))
            config.Port = port;

        if (dict.TryGetValue("new-name", out var newName))
            config.NewName = newName;

        return config;
    }
}
