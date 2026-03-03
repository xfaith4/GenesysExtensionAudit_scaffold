using GenesysExtensionAudit.Application;
using GenesysExtensionAudit.Domain.Paging;
using GenesysExtensionAudit.Domain.Services;
using GenesysExtensionAudit.Infrastructure.Application;
using GenesysExtensionAudit.Infrastructure.Domain.Services;
using GenesysExtensionAudit.Infrastructure.Genesys.Clients;
using GenesysExtensionAudit.Infrastructure.Genesys.Pagination;
using GenesysExtensionAudit.Infrastructure.Http;
using GenesysExtensionAudit.Infrastructure.Logging;
using GenesysExtensionAudit.Infrastructure.Reporting;
using GenesysExtensionAudit.Scheduling;
using GenesysExtensionAudit.ViewModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GenesysExtensionAudit;

/// <summary>
/// Central DI/hosting bootstrap for the WPF app.
/// Keeps App.xaml.cs minimal and makes services testable.
/// </summary>
public static class Bootstrapper
{
    private static IHost? _host;

    public static IServiceProvider Services
        => _host?.Services ?? throw new InvalidOperationException("Host not initialized. Call Bootstrapper.Initialize().");

    public static void Initialize()
    {
        if (_host is not null) return;

        var hostBuilder = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(cfg =>
            {
                cfg.SetBasePath(AppContext.BaseDirectory);
                cfg.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
            })
            .ConfigureServices((ctx, services) =>
            {
                // Options
                services.Configure<GenesysRegionOptions>(ctx.Configuration.GetSection("Genesys"));
                services.Configure<GenesysOAuthOptions>(ctx.Configuration.GetSection("GenesysOAuth"));
                services.Configure<ScheduledAuditOptions>(ctx.Configuration.GetSection("Scheduling"));

                // Core domain services
                services.AddSingleton<IExtensionNormalizer, ExtensionNormalizer>();
                services.AddSingleton<IAuditAnalyzer, AuditAnalyzer>();

                // HTTP handlers (must be transient for AddHttpMessageHandler)
                services.AddTransient<OAuthBearerHandler>();
                services.AddTransient<HttpLoggingHandler>();
                services.AddTransient<RateLimitHandler>();

                // Auth token provider (singleton — caches the token)
                services.AddSingleton<ITokenProvider, TokenProvider>();

                // Named HttpClient for the Genesys auth endpoint (used by TokenProvider)
                services.AddHttpClient("GenesysAuth");

                // Typed HTTP clients for the Genesys API (transient lifetime managed by IHttpClientFactory)
                services.AddHttpClient<IGenesysUsersClient, GenesysUsersClient>()
                    .AddHttpMessageHandler<OAuthBearerHandler>()
                    .AddHttpMessageHandler<HttpLoggingHandler>()
                    .AddHttpMessageHandler<RateLimitHandler>();

                services.AddHttpClient<IGenesysExtensionsClient, GenesysExtensionsClient>()
                    .AddHttpMessageHandler<OAuthBearerHandler>()
                    .AddHttpMessageHandler<HttpLoggingHandler>()
                    .AddHttpMessageHandler<RateLimitHandler>();

                services.AddHttpClient<IGenesysGroupsClient, GenesysGroupsClient>()
                    .AddHttpMessageHandler<OAuthBearerHandler>()
                    .AddHttpMessageHandler<HttpLoggingHandler>()
                    .AddHttpMessageHandler<RateLimitHandler>();

                services.AddHttpClient<IGenesysQueuesClient, GenesysQueuesClient>()
                    .AddHttpMessageHandler<OAuthBearerHandler>()
                    .AddHttpMessageHandler<HttpLoggingHandler>()
                    .AddHttpMessageHandler<RateLimitHandler>();

                services.AddHttpClient<IGenesysFlowsClient, GenesysFlowsClient>()
                    .AddHttpMessageHandler<OAuthBearerHandler>()
                    .AddHttpMessageHandler<HttpLoggingHandler>()
                    .AddHttpMessageHandler<RateLimitHandler>();

                services.AddHttpClient<IGenesysDidsClient, GenesysDidsClient>()
                    .AddHttpMessageHandler<OAuthBearerHandler>()
                    .AddHttpMessageHandler<HttpLoggingHandler>()
                    .AddHttpMessageHandler<RateLimitHandler>();

                services.AddHttpClient<IGenesysAuditLogsClient, GenesysAuditLogsClient>()
                    .AddHttpMessageHandler<OAuthBearerHandler>()
                    .AddHttpMessageHandler<HttpLoggingHandler>()
                    .AddHttpMessageHandler<RateLimitHandler>();

                services.AddSingleton<IPaginator, Paginator>();

                // Orchestrator + reporting
                services.AddSingleton<IAuditOrchestrator, AuditOrchestrator>(); // IAuditOrchestrator is in Infrastructure.Application
                services.AddSingleton<IExcelReportService, ExcelReportService>();
                services.AddSingleton<IScheduledAuditService, ScheduledAuditService>();

                // Navigation / MVVM shell
                services.AddSingleton<INavigationService, NavigationService>();

                // ViewModels
                services.AddSingleton<MainViewModel>();
                services.AddTransient<RunAuditViewModel>();
                services.AddTransient<ScheduleAuditViewModel>();

                // Shell window
                services.AddSingleton<MainWindow>(sp =>
                {
                    var window = new MainWindow();
                    window.DataContext = sp.GetRequiredService<MainViewModel>();
                    return window;
                });

            });

        Logging.ConfigureSerilog(hostBuilder);
        _host = hostBuilder.Build();

        var navigation = _host.Services.GetRequiredService<INavigationService>();
        navigation.Register(
            key: "RunAudit",
            displayName: "Run Audit",
            factory: () => _host.Services.GetRequiredService<RunAuditViewModel>());
        navigation.Register(
            key: "ScheduleAudit",
            displayName: "Schedule Audits",
            factory: () => _host.Services.GetRequiredService<ScheduleAuditViewModel>());
        navigation.Navigate("RunAudit");
    }

    public static Task StartAsync()
        => _host?.StartAsync() ?? Task.CompletedTask;

    public static Task StopAsync()
        => _host?.StopAsync() ?? Task.CompletedTask;

    public static void Dispose()
    {
        _host?.Dispose();
        _host = null;
    }
}
