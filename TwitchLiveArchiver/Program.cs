using TwitchLib.Api;

using TwitchLiveArchiver;

IHost host = Host.CreateDefaultBuilder(args)
                 .ConfigureServices(services => {
                                        services.AddHostedService<Worker>();
                                        services.AddSingleton<TwitchAPI>();
                                    }
                                   )
                 .Build();

await host.RunAsync();
