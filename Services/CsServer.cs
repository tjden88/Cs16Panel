using System.Text.RegularExpressions;

namespace Cs16Panel.Services;

public sealed class CsServer
{
    private readonly RconClient rcon = new();
    private readonly string host = Environment.GetEnvironmentVariable("CS_SERVER_HOST") ?? "127.0.0.1";
    private readonly int port = int.TryParse(Environment.GetEnvironmentVariable("CS_SERVER_PORT"), out var p) ? p : 27015;
    private readonly string password = Environment.GetEnvironmentVariable("CS_RCON_PASSWORD") ?? "";
    private readonly string mapsPath = Environment.GetEnvironmentVariable("MAPS_PATH") ?? "/maps";
    private readonly string publicHost = Environment.GetEnvironmentVariable("PUBLIC_HOST") ?? "cs16.local";
    private readonly int publicPort = int.TryParse(Environment.GetEnvironmentVariable("PUBLIC_PORT"), out var pp) ? pp : 27015;

    public bool IsOnline { get; private set; }
    public bool MatchActive { get; private set; }
    public string Map { get; private set; } = "unknown";
    public int Players { get; private set; }
    public int MaxPlayers { get; private set; } = 16;
    public string LastError { get; private set; } = "";
    public IReadOnlyList<string> Maps => maps;
    public IReadOnlyList<string> PlayerNames => playerNames;
    public string PublicHost => publicHost;
    public int PublicPort => publicPort;

    private readonly List<string> maps = [];
    private readonly List<string> playerNames = [];

    public CsServer()
    {
        ReloadMaps();
    }

    public async Task RefreshAsync()
    {
        try
        {
            var text = await rcon.ExecuteAsync(host, port, password, "status");
            ParseStatus(text);
            IsOnline = true;
            LastError = "";
        }
        catch (Exception ex)
        {
            IsOnline = false;
            LastError = ex.Message;
        }
    }

    public async Task StartGameAsync(string map, int bots, int difficulty)
    {
        if (!maps.Contains(map)) throw new InvalidOperationException("Неизвестная карта.");
        if (bots is < 0 or > 10) throw new InvalidOperationException("Некорректное количество ботов.");
        if (difficulty is < 0 or > 3) throw new InvalidOperationException("Некорректная сложность.");

        LastError = "";

        await rcon.ExecuteAsync(host, port, password, "yb_quota 0");
        await rcon.ExecuteAsync(host, port, password, $"changelevel {map}");

        // HLDS can be temporarily unavailable while loading the new map.
        // Wait until it answers RCON again before sending YaPB commands.
        await WaitForServerAsync(TimeSpan.FromSeconds(10));

        await rcon.ExecuteAsync(host, port, password, "yb_quota_mode normal");
        await rcon.ExecuteAsync(host, port, password, $"yb_difficulty {difficulty}");
        await rcon.ExecuteAsync(host, port, password, $"yb_quota {bots}");

        MatchActive = true;
    }

    public async Task ResetAsync()
    {
        await rcon.ExecuteAsync(host, port, password, "yb_quota 0");
        await rcon.ExecuteAsync(host, port, password, "changelevel cs_assault");
        await WaitForServerAsync(TimeSpan.FromSeconds(10));
        MatchActive = false;
    }

    private async Task WaitForServerAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        Exception? lastError = null;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var text = await rcon.ExecuteAsync(host, port, password, "status");
                ParseStatus(text);
                IsOnline = true;
                LastError = "";
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
                await Task.Delay(500);
            }
        }

        throw new TimeoutException(
            $"Сервер не ответил после смены карты за {timeout.TotalSeconds:0} сек.",
            lastError);
    }

    private void ParseStatus(string text)
    {
        var mapMatch = Regex.Match(text, @"map\s*:\s*(?<map>[^\s]+)", RegexOptions.IgnoreCase);
        if (mapMatch.Success) Map = mapMatch.Groups["map"].Value;

        var playersMatch = Regex.Match(text, @"players\s*:\s*(?<count>\d+)\s+active\s*\((?<max>\d+)\s+max\)", RegexOptions.IgnoreCase);
        if (playersMatch.Success)
        {
            Players = int.Parse(playersMatch.Groups["count"].Value);
            MaxPlayers = int.Parse(playersMatch.Groups["max"].Value);
        }

        playerNames.Clear();
        foreach (var line in text.Split('\n'))
        {
            if (!Regex.IsMatch(line, @"^\s*#\s*\d+\s+", RegexOptions.IgnoreCase)) continue;
            var match = Regex.Match(line, @"^\s*#\s*\d+\s+""(?<name>.*?)""");
            if (match.Success) playerNames.Add(match.Groups["name"].Value);
        }
    }

    private void ReloadMaps()
    {
        maps.Clear();
        if (Directory.Exists(mapsPath))
        {
            maps.AddRange(Directory.EnumerateFiles(mapsPath, "*.bsp", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileNameWithoutExtension)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Order(StringComparer.OrdinalIgnoreCase));
        }

        if (maps.Count == 0)
        {
            maps.AddRange(new[] { "de_dust2", "de_inferno", "de_nuke", "de_train", "de_cbble", "cs_assault", "cs_italy", "fy_pool_day" });
        }
    }
}
