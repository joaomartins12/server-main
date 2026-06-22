using DigitalWorldOnline.Application;
using DigitalWorldOnline.Application.Admin.Repositories;
using DigitalWorldOnline.Application.Extensions;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Repositories.Admin;
using DigitalWorldOnline.Game.Managers;
using DigitalWorldOnline.GameHost;
using DigitalWorldOnline.GameHost.EventsServer;
using DigitalWorldOnline.Infrastructure;
using DigitalWorldOnline.Infrastructure.Mapping;
using DigitalWorldOnline.Infrastructure.Repositories.Account;
using DigitalWorldOnline.Infrastructure.Repositories.Admin;
using DigitalWorldOnline.Infrastructure.Repositories.Character;
using DigitalWorldOnline.Infrastructure.Repositories.Config;
using DigitalWorldOnline.Infrastructure.Repositories.Routine;
using DigitalWorldOnline.Infrastructure.Repositories.Server;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using System.Globalization;
using System.Reflection;
using DigitalWorldOnline.Commons.Utils;
using DigitalWorldOnline.Game.PacketProcessors;

namespace DigitalWorldOnline.Game
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Run();
        }

        private static void UnhandledExceptionHandler(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception exception)
            {
                Console.WriteLine("==== Unhandled Exception ====");
                Console.WriteLine($"Message: {exception.Message}");
                Console.WriteLine($"StackTrace: {exception.StackTrace}");
                Console.WriteLine($"Inner: {exception.InnerException}");
            }

            if (e.IsTerminating)
            {
                Console.WriteLine("Terminating due to unhandled exception...");
            }
            else
            {
                Console.WriteLine("Unhandled exception caught, continuing execution...");
            }
        }

        public static IHost CreateHostBuilder(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += UnhandledExceptionHandler;
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

            var host = Host.CreateDefaultBuilder(args)
                .UseSerilog()
                .UseEnvironment("Development")
                .ConfigureServices((context, services) =>
                {
                    services.AddDbContext<DatabaseContext>();

                    // ==== Repositories ====
                    services.AddScoped<IAdminQueriesRepository, AdminQueriesRepository>();
                    services.AddScoped<IAdminCommandsRepository, AdminCommandsRepository>();

                    services.AddScoped<IAccountQueriesRepository, AccountQueriesRepository>();
                    services.AddScoped<IAccountCommandsRepository, AccountCommandsRepository>();

                    services.AddScoped<IServerQueriesRepository, ServerQueriesRepository>();
                    services.AddScoped<IServerCommandsRepository, ServerCommandsRepository>();

                    services.AddScoped<ICharacterQueriesRepository, CharacterQueriesRepository>();
                    services.AddScoped<ICharacterCommandsRepository, CharacterCommandsRepository>();

                    services.AddScoped<IConfigQueriesRepository, ConfigQueriesRepository>();
                    services.AddScoped<IConfigCommandsRepository, ConfigCommandsRepository>();

                    services.AddScoped<IRoutineRepository, RoutineRepository>();

                    // ==== Managers ====
                    services.AddSingleton<AssetsLoader>();
                    services.AddSingleton<ConfigsLoader>();
                    services.AddSingleton<DropManager>();
                    services.AddSingleton<StatusManager>();
                    services.AddSingleton<ExpManager>();
                    services.AddSingleton<PartyManager>();
                    services.AddSingleton<EventManager>();
                    services.AddSingleton<AttackManager>();
                    services.AddSingleton<DigimonSkillManager>();
                    services.AddSingleton<EventQueueManager>();

                    // ==== Servers ====
                    services.AddSingleton<MapServer>();
                    services.AddSingleton<PvpServer>();
                    services.AddSingleton<EventServer>();
                    services.AddSingleton<DungeonsServer>();

                    // ==== Commands / Processors ====
                    services.AddSingleton<GameMasterCommandsProcessor>();
                    services.AddSingleton<PlayerCommands>();
                    services.AddSingleton<BanForCheating>();

                    // ==== Mediator / CQRS ====
                    services.AddSingleton<ISender, ScopedSender<Mediator>>();
                    services.AddMediatR(typeof(MediatorApplicationHandlerExtension).GetTypeInfo().Assembly);
                    services.AddTransient<Mediator>();

                    // ==== Packet Processor ====
                    services.AddSingleton<IProcessor, GamePacketProcessor>();

                    // ==== Logging ====
                    services.AddSingleton(ConfigureLogger(context.Configuration));

                    // ==== Hosted Service ====
                    services.AddHostedService<GameServer>();

                    // ==== AutoMapper ====
                    AddAutoMapper(services);

                    // ==== Packet Processors dinâmicos ====
                    AddProcessors(services);
                })
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    var env = hostingContext.HostingEnvironment;

                    config.SetBasePath(Directory.GetCurrentDirectory())
                          .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                          .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: true)
                          .AddEnvironmentVariables(Constants.Configuration.EnvironmentPrefix)
                          .AddUserSecrets<Program>();
                })
                .Build();

            // Resolver global
            SingletonResolver.Services = host.Services;

            return host;
        }

        private static void AddAutoMapper(IServiceCollection services)
        {
            services.AddAutoMapper(typeof(AccountProfile));
            services.AddAutoMapper(typeof(AssetsProfile));
            services.AddAutoMapper(typeof(CharacterProfile));
            services.AddAutoMapper(typeof(ConfigProfile));
            services.AddAutoMapper(typeof(DigimonProfile));
            services.AddAutoMapper(typeof(GameProfile));
            services.AddAutoMapper(typeof(SecurityProfile));
            services.AddAutoMapper(typeof(ArenaProfile));
        }

        private static void AddProcessors(IServiceCollection services)
        {
            var packetProcessors = Assembly.GetExecutingAssembly()
                .GetTypes()
                .Where(t => typeof(IGamePacketProcessor).IsAssignableFrom(t) && !t.IsInterface)
                .ToList();

            foreach (var processor in packetProcessors)
                services.AddSingleton(typeof(IGamePacketProcessor), processor);
        }

        private static ILogger ConfigureLogger(IConfiguration configuration)
        {
            return new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
                    restrictedToMinimumLevel: LogEventLevel.Information)
                .WriteTo.Logger(lc => lc
                    .Filter.ByIncludingOnly(e => e.Level == LogEventLevel.Verbose)
                    .WriteTo.RollingFile(configuration["Log:VerboseRepository"] ?? "logs\\Verbose\\GameServer",
                        retainedFileCountLimit: 10))
                .WriteTo.Logger(lc => lc
                    .Filter.ByIncludingOnly(e => e.Level == LogEventLevel.Debug)
                    .WriteTo.RollingFile(configuration["Log:DebugRepository"] ?? "logs\\Debug\\GameServer",
                        retainedFileCountLimit: 5))
                .WriteTo.Logger(lc => lc
                    .Filter.ByIncludingOnly(e => e.Level == LogEventLevel.Information)
                    .WriteTo.RollingFile(configuration["Log:InformationRepository"] ?? "logs\\Information\\GameServer",
                        retainedFileCountLimit: 5))
                .WriteTo.Logger(lc => lc
                    .Filter.ByIncludingOnly(e => e.Level == LogEventLevel.Warning)
                    .WriteTo.RollingFile(configuration["Log:WarningRepository"] ?? "logs\\Warning\\GameServer",
                        retainedFileCountLimit: 5))
                .WriteTo.Logger(lc => lc
                    .Filter.ByIncludingOnly(e => e.Level == LogEventLevel.Error)
                    .WriteTo.RollingFile(configuration["Log:ErrorRepository"] ?? "logs\\Error\\GameServer",
                        retainedFileCountLimit: 5))
                .CreateLogger();
        }
    }
}
