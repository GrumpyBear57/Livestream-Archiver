using NYoutubeDL;

using TwitchLib.Api;
using TwitchLib.Api.Services;

namespace TwitchLiveArchiver;

public class Worker : BackgroundService {
    private readonly IConfiguration _config;
    private readonly ILogger<Worker> _logger;
    private readonly TwitchAPI _twitch;

    private readonly List<YoutubeDLP> _downloadClients = new List<YoutubeDLP>();
    private readonly DateTime _startTime;

    public Worker(ILogger<Worker> logger, TwitchAPI twitch, IConfiguration config) {
        _logger = logger;
        _twitch = twitch;
        _config = config;

        _twitch.Settings.ClientId = config["Twitch:ClientID"];
        _twitch.Settings.Secret   = config["Twitch:ClientSecret"];

        _startTime = DateTime.Now;
    }

    private void StartDownload(string channel) {
        YoutubeDLP downloader = new YoutubeDLP("C:\\Users\\Akuma\\Downloads\\yt-dlp.exe");
        downloader.StandardOutputEvent += (_, output) => _logger.LogInformation("Youtube-DLP: {Output}", output);
        downloader.StandardErrorEvent  += (_, error) => _logger.LogDebug("Youtube-DLP: {Error}", error);

        string outputFile = $"\\\\AYUMI\\Yuuki\\Unsorted\\Twitch\\{channel} - {DateTime.Now:u}.mp4";
        downloader.Options.FilesystemOptions.Output = outputFile;
        downloader.DownloadAsync($"https://twitch.tv/{channel}");
        _downloadClients.Add(downloader);

        _logger.LogDebug("Download started for {Channel}. Saving to: {Output}", channel, outputFile);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        LiveStreamMonitorService monitor = new LiveStreamMonitorService(_twitch);
        monitor.SetChannelsByName(new List<string> { "museun", "rseding91" });

        monitor.OnStreamOnline += (_, args) => {
            _logger.LogInformation("{Channel} is now online", args.Channel);

            StartDownload(args.Channel);
        };

        monitor.OnStreamUpdate += (_, args) => {
            _logger.LogDebug("{Channel} Uptime: {Uptime}, {Viewers} viewers", args.Channel,
                             (DateTime.Now.ToUniversalTime() - args.Stream.StartedAt.ToUniversalTime()).ToString("g"),
                             args.Stream.ViewerCount
                            );

            if (_downloadClients.Exists(c => c.VideoUrl.Contains(args.Channel)) == false) {
                _logger.LogWarning("No active downloads for {Channel}! Starting one now...", args.Channel);
                StartDownload(args.Channel);
            }
        };

        monitor.OnStreamOffline += (_, args) => {
            _logger.LogInformation("{Channel} is now offline", args.Channel);
        };

        _logger.LogInformation("-------------------- Starting Service --------------------");

        monitor.Start();

        while (!stoppingToken.IsCancellationRequested) {
            _logger.LogDebug("Running for: {Uptime}", (DateTime.Now - _startTime).ToString("g"));
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            List<string> toRemove =
                (from client in _downloadClients where client.IsDownloading == false select client.VideoUrl).ToList();
            _logger.LogInformation("Removing: {Downloads} from the list", string.Join(',', toRemove));
            _downloadClients.RemoveAll(c => toRemove.Contains(c.VideoUrl));
        }

        _logger.LogInformation("-------------------- Stopping Service --------------------");

        foreach (YoutubeDLP client in _downloadClients) {
            client.CancelDownload();
        }
    }
}
