using GenesysExtensionAudit.Application;
using GenesysExtensionAudit.Domain.Paging;
using GenesysExtensionAudit.Domain.Services;
using GenesysExtensionAudit.Infrastructure.Application;
using GenesysExtensionAudit.Infrastructure.Configuration;
using GenesysExtensionAudit.Infrastructure.Domain.Services;
using GenesysExtensionAudit.Infrastructure.Genesys.Clients;
using GenesysExtensionAudit.Infrastructure.Genesys.Pagination;
using GenesysExtensionAudit.Infrastructure.Http;
using GenesysExtensionAudit.Infrastructure.Reporting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;

return await RunAsync(args);

// ─── Top-level runner ────────────────────────────────────────────────────────

static async Task<int> RunAsync(string[] args)
{
    bool dryRun = Array.Exists(args, a => a.Equals("--dry-run", StringComparison.OrdinalIgnoreCase));

    // Bootstrap logger active before host is built (captures startup errors).
    Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
        .WriteTo.File(
            path: "logs/runner-.log",
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 30,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
        .CreateLogger();

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        Log.Warning("Cancellation requested (Ctrl+C).");
        cts.Cancel();
    };

    IHost? host = null;
    try
    {
        host = Host.CreateDefaultBuilder(args)
            .UseSerilog()
            .ConfigureAppConfiguration((_, cfg) =>
            {
                cfg.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);
            })
            .ConfigureServices((ctx, services) =>
            {
                // ── Options ──────────────────────────────────────────────────
                services.Configure<GenesysRegionOptions>(ctx.Configuration.GetSection("Genesys"));
                services.Configure<GenesysOAuthOptions>(ctx.Configuration.GetSection("GenesysOAuth"));
                services.Configure<AuditOptions>(ctx.Configuration.GetSection("Audit"));
                services.Configure<ExportOptions>(ctx.Configuration.GetSection("Export"));
                services.Configure<SharePointOptions>(ctx.Configuration.GetSection("SharePoint"));

                // ── Core domain services ──────────────────────────────────────
                services.AddSingleton<IExtensionNormalizer, ExtensionNormalizer>();
                services.AddSingleton<IAuditAnalyzer, AuditAnalyzer>();

                // ── HTTP handlers (must be transient for AddHttpMessageHandler) ─
                services.AddTransient<OAuthBearerHandler>();
                services.AddTransient<HttpLoggingHandler>();
                services.AddTransient<RateLimitHandler>();

                // ── Auth token provider (singleton — caches the token) ─────────
                services.AddSingleton<ITokenProvider, TokenProvider>();
                services.AddHttpClient("GenesysAuth");

                // ── Typed HTTP clients for the Genesys API ────────────────────
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

                services.AddSingleton<IPaginator, Paginator>();

                // ── Audit + reporting ─────────────────────────────────────────
                services.AddSingleton<IAuditOrchestrator, AuditOrchestrator>();
                services.AddSingleton<IExcelReportService, ExcelReportService>();
                services.AddSingleton<IFileUploadService, SharePointUploadService>();
            })
            .Build();

        var logger = host.Services.GetRequiredService<ILogger<Program>>();
        var orchestrator = host.Services.GetRequiredService<IAuditOrchestrator>();
        var excelService = host.Services.GetRequiredService<IExcelReportService>();
        var genesysOpts = host.Services.GetRequiredService<IOptions<GenesysRegionOptions>>().Value;
        var auditOpts = host.Services.GetRequiredService<IOptions<AuditOptions>>().Value;
        var exportOpts = host.Services.GetRequiredService<IOptions<ExportOptions>>().Value;
        var spOpts = host.Services.GetRequiredService<IOptions<SharePointOptions>>().Value;

        logger.LogInformation(
            "GenesysExtensionAudit Runner starting. Region={Region} DryRun={DryRun}",
            genesysOpts.Region, dryRun);

        // ── Run audit ─────────────────────────────────────────────────────────
        var progress = new Progress<AuditProgress>(p =>
        {
            if (!string.IsNullOrWhiteSpace(p.Message))
                logger.LogInformation("[{Percent,3}%] {Message}", p.Percent, p.Message);
        });

        var runOptions = new AuditRunOptions
        {
            PageSize = genesysOpts.PageSize,
            IncludeInactiveUsers = auditOpts.IncludeInactiveUsers,
            StaleFlowThresholdDays = auditOpts.StaleFlowThresholdDays,
            InactiveUserThresholdDays = auditOpts.InactiveUserThresholdDays
        };

        var report = await orchestrator.RunAsync(runOptions, progress, cts.Token);

        // ── Generate Excel ────────────────────────────────────────────────────
        logger.LogInformation("Generating Excel workbook...");
        var xlsx = await excelService.GenerateAsync(report, cts.Token);

        // ── Save locally ──────────────────────────────────────────────────────
        var outputDir = Path.GetFullPath(exportOpts.OutputDirectory);
        Directory.CreateDirectory(outputDir);

        var fileName = $"{exportOpts.FilePrefix}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
        var filePath = Path.Combine(outputDir, fileName);

        await File.WriteAllBytesAsync(filePath, xlsx, cts.Token);
        logger.LogInformation("Report saved: {FilePath} ({Bytes:N0} bytes)", filePath, xlsx.Length);

        // ── Upload to SharePoint ──────────────────────────────────────────────
        if (dryRun)
        {
            logger.LogInformation("--dry-run: SharePoint upload skipped.");
        }
        else if (spOpts.IsConfigured)
        {
            var uploadService = host.Services.GetRequiredService<IFileUploadService>();
            logger.LogInformation(
                "Uploading to SharePoint: {SiteUrl} / {FolderPath}",
                spOpts.SiteUrl, spOpts.FolderPath);
            var url = await uploadService.UploadAsync(fileName, xlsx, cts.Token);
            logger.LogInformation("Upload complete: {Url}", url);
        }
        else
        {
            logger.LogInformation("SharePoint not configured — local file only.");
        }

        logger.LogInformation("Runner finished successfully. Exit code: 0");
        return 0;
    }
    catch (OperationCanceledException)
    {
        Log.Warning("Audit cancelled.");
        return 2;
    }
    catch (Exception ex)
    {
        Log.Fatal(ex, "Unhandled exception in runner.");
        return 1;
    }
    finally
    {
        host?.Dispose();
        await Log.CloseAndFlushAsync();
    }
}
