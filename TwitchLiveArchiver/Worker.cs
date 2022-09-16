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
        YoutubeDLP downloader = new YoutubeDLP(_config["YouTubeDL:ExecutablePath"]);
        downloader.StandardOutputEvent += (_, output) => _logger.LogInformation("Youtube-DLP: {Output}", output);
        downloader.StandardErrorEvent  += (_, error) => _logger.LogTrace("Youtube-DLP: {Error}", error);

        string outputDir = _config["YouTubeDL:OutputDirectory"];
        if (Directory.Exists(outputDir) == false)
            Directory.CreateDirectory(outputDir);

        string outputFile = Path.Join(outputDir, $"{channel} - {DateTime.Now:yyyy-MM-dd hhmmss}.mp4");
        downloader.Options.FilesystemOptions.Output = outputFile;
        
        #pragma warning disable CS4014
        downloader.DownloadAsync($"https://twitch.tv/{channel}");
        #pragma warning restore CS4014
        
        // downloader.DownloadAsync($"https://www.youtube.com/watch?v=t3oLeSPINOk");

        // await Task.Delay(TimeSpan.FromSeconds(10));
        // downloader.CancelDownload();

        _downloadClients.Add(downloader);

        _logger.LogDebug("Download started for {Channel}. Saving to: {Output}", channel, outputFile);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        LiveStreamMonitorService monitor = new LiveStreamMonitorService(_twitch);
        monitor.SetChannelsByName(_config.GetSection("YouTubeDL:ArchiveChannels").Get<List<string>>());

        monitor.OnStreamOnline += (_, args) => {
            _logger.LogInformation("{Channel} is now online", args.Channel);

            StartDownload(args.Channel);
        };

        monitor.OnStreamUpdate += (_, args) => {
            // _logger.LogDebug("{Channel} Uptime: {Uptime}, {Viewers} viewers", args.Channel,
            //                  (DateTime.Now.ToUniversalTime() - args.Stream.StartedAt.ToUniversalTime()).ToString("g"),
            //                  args.Stream.ViewerCount
            //                 );
            //
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
        await monitor.UpdateLiveStreamersAsync();

        while (!stoppingToken.IsCancellationRequested) {
            _logger.LogDebug("Running for: {Uptime}", (DateTime.Now - _startTime).ToString("g"));
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            List<string> toRemove = (from client in _downloadClients
                                     where client.IsDownloading == false
                                     select client.VideoUrl).ToList();
            if (toRemove.Any() == false)
                continue;

            _logger.LogInformation("Removing: {Downloads} from the list", string.Join(',', toRemove));
            _downloadClients.RemoveAll(c => toRemove.Contains(c.VideoUrl));
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken) {
        _logger.LogInformation("-------------------- Stopping Service --------------------");
    
        foreach (YoutubeDLP client in _downloadClients) {
            client.CancelDownload();
            while (client.IsDownloading)
                Thread.Sleep(50);
        }
    
        _logger.LogInformation("-------------------- Stopped Service --------------------");
        return Task.CompletedTask;
    }
}
