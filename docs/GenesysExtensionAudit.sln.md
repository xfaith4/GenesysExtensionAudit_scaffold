# Outline

```text
GenesysExtensionAudit/
├─ GenesysExtensionAudit.sln
├─ .gitignore
├─ README.md
├─ Directory.Build.props
├─ src/
│  ├─ GenesysExtensionAudit.App/
│  │  ├─ GenesysExtensionAudit.App.csproj
│  │  ├─ appsettings.json
│  │  ├─ App.xaml
│  │  ├─ App.xaml.cs
│  │  ├─ MainWindow.xaml
│  │  ├─ MainWindow.xaml.cs
│  │  ├─ AssemblyInfo.cs
│  │  ├─ Views/
│  │  │  └─ RunAuditView.xaml
│  │  └─ ViewModels/
│  │     ├─ MainViewModel.cs
│  │     └─ RunAuditViewModel.cs
│  ├─ GenesysExtensionAudit.Core/
│  │  ├─ GenesysExtensionAudit.Core.csproj
│  │  ├─ Application/
│  │  │  ├─ AuditOptions.cs
│  │  │  ├─ AuditProgress.cs
│  │  │  ├─ AuditResult.cs
│  │  │  ├─ IAuditRunner.cs
│  │  │  └─ AuditRunner.cs
│  │  ├─ Domain/
│  │  │  ├─ Models/
│  │  │  │  ├─ UserProfileExtensionRecord.cs
│  │  │  │  ├─ AssignedExtensionRecord.cs
│  │  │  │  └─ AuditFindings.cs
│  │  │  ├─ Services/
│  │  │  │  ├─ IExtensionNormalizer.cs
│  │  │  │  ├─ ExtensionNormalizer.cs
│  │  │  │  ├─ IAuditAnalyzer.cs
│  │  │  │  └─ AuditAnalyzer.cs
│  │  │  └─ Paging/
│  │  │     ├─ PagedResult.cs
│  │  │     └─ IPaginator.cs
│  ├─ GenesysExtensionAudit.Infrastructure/
│  │  ├─ GenesysExtensionAudit.Infrastructure.csproj
│  │  ├─ Http/
│  │  │  ├─ GenesysRegionOptions.cs
│  │  │  ├─ ITokenProvider.cs
│  │  │  ├─ TokenProvider.cs
│  │  │  ├─ OAuthBearerHandler.cs
│  │  │  ├─ HttpLoggingHandler.cs
│  │  │  └─ RateLimitHandler.cs
│  │  ├─ Genesys/
│  │  │  ├─ Dtos/
│  │  │  │  ├─ UserDto.cs
│  │  │  │  ├─ ExtensionDto.cs
│  │  │  │  └─ PagedResponseDtos.cs
│  │  │  ├─ Clients/
│  │  │  │  ├─ IGenesysUsersClient.cs
│  │  │  │  ├─ GenesysUsersClient.cs
│  │  │  │  ├─ IGenesysExtensionsClient.cs
│  │  │  │  └─ GenesysExtensionsClient.cs
│  │  │  └─ Pagination/
│  │  │     └─ Paginator.cs
│  │  └─ Reporting/
│  │     ├─ IReportWriter.cs
│  │     └─ CsvReportWriter.cs
│  └─ GenesysExtensionAudit.Tests/
│     ├─ GenesysExtensionAudit.Tests.csproj
│     └─ Domain/
│        └─ ExtensionNormalizerTests.cs
└─ (optional) global.json
```

---

## GenesysExtensionAudit.sln

```sln
Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio Version 17
VisualStudioVersion = 17.0.31903.59
MinimumVisualStudioVersion = 10.0.40219.1

Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "GenesysExtensionAudit.App", "src\GenesysExtensionAudit.App\GenesysExtensionAudit.App.csproj", "{5C9F9F8B-6AA2-4F17-94A4-4C0C7E0E3C8A}"
EndProject
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "GenesysExtensionAudit.Core", "src\GenesysExtensionAudit.Core\GenesysExtensionAudit.Core.csproj", "{D8E6D0AF-39D0-4D1D-BA06-7C2DA7B27A63}"
EndProject
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "GenesysExtensionAudit.Infrastructure", "src\GenesysExtensionAudit.Infrastructure\GenesysExtensionAudit.Infrastructure.csproj", "{4B3C6B5A-2CF8-4D0C-8A1D-8F5D65A6B7F7}"
EndProject
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "GenesysExtensionAudit.Tests", "src\GenesysExtensionAudit.Tests\GenesysExtensionAudit.Tests.csproj", "{A1C80E8C-7E1C-4C27-9B3B-5A2EBA2A6D5A}"
EndProject

Global
 GlobalSection(SolutionConfigurationPlatforms) = preSolution
  Debug|Any CPU = Debug|Any CPU
  Release|Any CPU = Release|Any CPU
 EndGlobalSection
 GlobalSection(ProjectConfigurationPlatforms) = postSolution
  {5C9F9F8B-6AA2-4F17-94A4-4C0C7E0E3C8A}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
  {5C9F9F8B-6AA2-4F17-94A4-4C0C7E0E3C8A}.Debug|Any CPU.Build.0 = Debug|Any CPU
  {5C9F9F8B-6AA2-4F17-94A4-4C0C7E0E3C8A}.Release|Any CPU.ActiveCfg = Release|Any CPU
  {5C9F9F8B-6AA2-4F17-94A4-4C0C7E0E3C8A}.Release|Any CPU.Build.0 = Release|Any CPU

  {D8E6D0AF-39D0-4D1D-BA06-7C2DA7B27A63}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
  {D8E6D0AF-39D0-4D1D-BA06-7C2DA7B27A63}.Debug|Any CPU.Build.0 = Debug|Any CPU
  {D8E6D0AF-39D0-4D1D-BA06-7C2DA7B27A63}.Release|Any CPU.ActiveCfg = Release|Any CPU
  {D8E6D0AF-39D0-4D1D-BA06-7C2DA7B27A63}.Release|Any CPU.Build.0 = Release|Any CPU

  {4B3C6B5A-2CF8-4D0C-8A1D-8F5D65A6B7F7}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
  {4B3C6B5A-2CF8-4D0C-8A1D-8F5D65A6B7F7}.Debug|Any CPU.Build.0 = Debug|Any CPU
  {4B3C6B5A-2CF8-4D0C-8A1D-8F5D65A6B7F7}.Release|Any CPU.ActiveCfg = Release|Any CPU
  {4B3C6B5A-2CF8-4D0C-8A1D-8F5D65A6B7F7}.Release|Any CPU.Build.0 = Release|Any CPU

  {A1C80E8C-7E1C-4C27-9B3B-5A2EBA2A6D5A}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
  {A1C80E8C-7E1C-4C27-9B3B-5A2EBA2A6D5A}.Debug|Any CPU.Build.0 = Debug|Any CPU
  {A1C80E8C-7E1C-4C27-9B3B-5A2EBA2A6D5A}.Release|Any CPU.ActiveCfg = Release|Any CPU
  {A1C80E8C-7E1C-4C27-9B3B-5A2EBA2A6D5A}.Release|Any CPU.Build.0 = Release|Any CPU
 EndGlobalSection
EndGlobal
```

---

### Directory.Build.props

```xml
<Project>
  <PropertyGroup>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```

---

### src/GenesysExtensionAudit.App/GenesysExtensionAudit.App.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <AssemblyName>GenesysExtensionAudit</AssemblyName>
    <RootNamespace>GenesysExtensionAudit</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\GenesysExtensionAudit.Core\GenesysExtensionAudit.Core.csproj" />
    <ProjectReference Include="..\GenesysExtensionAudit.Infrastructure\GenesysExtensionAudit.Infrastructure.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.3.2" />
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Http" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Logging.Console" Version="8.0.0" />
  </ItemGroup>

</Project>
```

### src/GenesysExtensionAudit.App/appsettings.json

```json
{
  "Genesys": {
    "Region": "mypurecloud.com",
    "PageSize": 100,
    "IncludeInactive": false,
    "MaxRequestsPerSecond": 3
  }
}
```

### src/GenesysExtensionAudit.App/App.xaml

```xml
<Application x:Class="GenesysExtensionAudit.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <Application.Resources>
  </Application.Resources>
</Application>
```

### src/GenesysExtensionAudit.App/App.xaml.cs

```csharp
using System.Windows;
using GenesysExtensionAudit.Application;
using GenesysExtensionAudit.Domain.Services;
using GenesysExtensionAudit.Infrastructure.Genesys.Clients;
using GenesysExtensionAudit.Infrastructure.Genesys.Pagination;
using GenesysExtensionAudit.Infrastructure.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GenesysExtensionAudit;

public partial class App : System.Windows.Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(cfg =>
            {
                cfg.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
            })
            .ConfigureServices((ctx, services) =>
            {
                services.Configure<GenesysRegionOptions>(ctx.Configuration.GetSection("Genesys"));

                services.AddSingleton<IExtensionNormalizer, ExtensionNormalizer>();
                services.AddSingleton<IAuditAnalyzer, AuditAnalyzer>();
                services.AddSingleton<IAuditRunner, AuditRunner>();

                services.AddSingleton<ITokenProvider, TokenProvider>();
                services.AddSingleton<OAuthBearerHandler>();
                services.AddSingleton<RateLimitHandler>();
                services.AddSingleton<HttpLoggingHandler>();

                services.AddHttpClient("GenesysApi")
                    .AddHttpMessageHandler<RateLimitHandler>()
                    .AddHttpMessageHandler<HttpLoggingHandler>()
                    .AddHttpMessageHandler<OAuthBearerHandler>();

                services.AddHttpClient("GenesysAuth")
                    .AddHttpMessageHandler<HttpLoggingHandler>();

                services.AddSingleton<IPaginator, Paginator>();

                services.AddTransient<IGenesysUsersClient, GenesysUsersClient>();
                services.AddTransient<IGenesysExtensionsClient, GenesysExtensionsClient>();

                services.AddSingleton<ViewModels.MainViewModel>();
                services.AddSingleton<MainWindow>(sp =>
                {
                    var wnd = new MainWindow { DataContext = sp.GetRequiredService<ViewModels.MainViewModel>() };
                    return wnd;
                });
            })
            .ConfigureLogging(lb =>
            {
                lb.ClearProviders();
                lb.AddConsole();
            })
            .Build();

        await _host.StartAsync();

        var main = _host.Services.GetRequiredService<MainWindow>();
        main.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        base.OnExit(e);
    }
}
```

### src/GenesysExtensionAudit.App/MainWindow.xaml

```xml
<Window x:Class="GenesysExtensionAudit.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Genesys Extension Audit" Height="600" Width="980">
    <Grid Margin="12">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <TextBlock FontSize="18" FontWeight="SemiBold" Text="Genesys Extension Audit"/>

        <StackPanel Grid.Row="1" Margin="0,12,0,12">
            <TextBlock Text="{Binding StatusText}" Margin="0,0,0,6"/>
            <ProgressBar Height="18" Minimum="0" Maximum="100" Value="{Binding Percent}"/>
        </StackPanel>

        <StackPanel Grid.Row="2" Orientation="Horizontal" HorizontalAlignment="Right">
            <Button Content="Run" Width="120" Margin="0,0,8,0" Command="{Binding RunCommand}"/>
            <Button Content="Cancel" Width="120" Command="{Binding CancelCommand}"/>
        </StackPanel>
    </Grid>
</Window>
```

### src/GenesysExtensionAudit.App/MainWindow.xaml.cs

```csharp
using System.Windows;

namespace GenesysExtensionAudit;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
}
```

### src/GenesysExtensionAudit.App/ViewModels/MainViewModel.cs

```csharp
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenesysExtensionAudit.Application;

namespace GenesysExtensionAudit.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IAuditRunner _auditRunner;

    private CancellationTokenSource? _cts;

    [ObservableProperty] private string statusText = "Ready.";
    [ObservableProperty] private int percent;

    public MainViewModel(IAuditRunner auditRunner)
    {
        _auditRunner = auditRunner;
    }

    [RelayCommand]
    private async Task RunAsync()
    {
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        var progress = new Progress<AuditProgress>(p =>
        {
            StatusText = p.Message;
            Percent = p.Percent ?? 0;
        });

        var options = new AuditOptions();
        await _auditRunner.RunAsync(options, progress, _cts.Token);
        StatusText = "Completed.";
        Percent = 100;
    }

    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();
        StatusText = "Cancelling...";
    }
}
```

---

### src/GenesysExtensionAudit.Core/GenesysExtensionAudit.Core.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>

</Project>
```

### src/GenesysExtensionAudit.Core/Application/AuditOptions.cs

```csharp
namespace GenesysExtensionAudit.Application;

public sealed record AuditOptions
{
    public int PageSize { get; init; } = 100;
    public bool IncludeInactive { get; init; } = false;

    /// <summary>Region host, e.g. mypurecloud.com</summary>
    public string Region { get; init; } = "mypurecloud.com";
}
```

### src/GenesysExtensionAudit.Core/Application/AuditProgress.cs

```csharp
namespace GenesysExtensionAudit.Application;

public enum AuditPhase
{
    FetchUsers,
    FetchExtensions,
    Analyze,
    Done
}

public sealed record AuditProgress(
    AuditPhase Phase,
    string Message,
    int? Percent = null,
    int? PageNumber = null,
    int? PageCount = null,
    int? ItemsFetched = null);
```

### src/GenesysExtensionAudit.Core/Application/AuditResult.cs

```csharp
using GenesysExtensionAudit.Domain.Models;

namespace GenesysExtensionAudit.Application;

public sealed record AuditResult(
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    AuditFindings Findings);
```

### src/GenesysExtensionAudit.Core/Application/IAuditRunner.cs

```csharp
namespace GenesysExtensionAudit.Application;

public interface IAuditRunner
{
    Task<AuditResult> RunAsync(AuditOptions options, IProgress<AuditProgress> progress, CancellationToken ct);
}
```

### src/GenesysExtensionAudit.Core/Application/AuditRunner.cs

```csharp
using GenesysExtensionAudit.Domain.Models;
using GenesysExtensionAudit.Domain.Services;
using GenesysExtensionAudit.Infrastructure.Genesys.Clients;
using GenesysExtensionAudit.Infrastructure.Genesys.Pagination;

namespace GenesysExtensionAudit.Application;

public sealed class AuditRunner : IAuditRunner
{
    private readonly IGenesysUsersClient _users;
    private readonly IGenesysExtensionsClient _extensions;
    private readonly IPaginator _paginator;
    private readonly IAuditAnalyzer _analyzer;

    public AuditRunner(
        IGenesysUsersClient users,
        IGenesysExtensionsClient extensions,
        IPaginator paginator,
        IAuditAnalyzer analyzer)
    {
        _users = users;
        _extensions = extensions;
        _paginator = paginator;
        _analyzer = analyzer;
    }

    public async Task<AuditResult> RunAsync(AuditOptions options, IProgress<AuditProgress> progress, CancellationToken ct)
    {
        var started = DateTimeOffset.UtcNow;

        progress.Report(new AuditProgress(AuditPhase.FetchUsers, "Fetching users..."));

        var userDtos = await _paginator.FetchAllAsync(
            pageNumber => _users.GetUsersPageAsync(pageNumber, options.PageSize, options.IncludeInactive, ct),
            ct);

        progress.Report(new AuditProgress(AuditPhase.FetchExtensions, "Fetching extensions..."));

        var extDtos = await _paginator.FetchAllAsync(
            pageNumber => _extensions.GetExtensionsPageAsync(pageNumber, options.PageSize, ct),
            ct);

        progress.Report(new AuditProgress(AuditPhase.Analyze, "Analyzing..."));

        // Mapping DTOs -> domain records will be implemented later
        var findings = _analyzer.Analyze(
            userProfileExtensions: Array.Empty<UserProfileExtensionRecord>(),
            assignedExtensions: Array.Empty<AssignedExtensionRecord>());

        var finished = DateTimeOffset.UtcNow;
        progress.Report(new AuditProgress(AuditPhase.Done, "Done.", 100));

        return new AuditResult(started, finished, findings);
    }
}
```

---

### src/GenesysExtensionAudit.Core/Domain/Models/UserProfileExtensionRecord.cs

```csharp
namespace GenesysExtensionAudit.Domain.Models;

public sealed record UserProfileExtensionRecord(
    string UserId,
    string? DisplayName,
    string? State,
    string? RawExtension,
    string? NormalizedExtension);
```

### src/GenesysExtensionAudit.Core/Domain/Models/AssignedExtensionRecord.cs

```csharp
namespace GenesysExtensionAudit.Domain.Models;

public sealed record AssignedExtensionRecord(
    string ExtensionId,
    string? RawExtension,
    string? NormalizedExtension,
    string? AssignedToType,
    string? AssignedToId);
```

### src/GenesysExtensionAudit.Core/Domain/Models/AuditFindings.cs

```csharp
namespace GenesysExtensionAudit.Domain.Models;

public sealed record DuplicateProfileExtension(string NormalizedExtension, IReadOnlyList<UserProfileExtensionRecord> Users);
public sealed record DuplicateAssignedExtension(string NormalizedExtension, IReadOnlyList<AssignedExtensionRecord> Assignments);
public sealed record UnassignedProfileExtension(string NormalizedExtension, UserProfileExtensionRecord User);

public sealed record AuditFindings(
    IReadOnlyList<DuplicateProfileExtension> DuplicateProfileExtensions,
    IReadOnlyList<DuplicateAssignedExtension> DuplicateAssignedExtensions,
    IReadOnlyList<UnassignedProfileExtension> ProfileOnlyExtensions);
```

### src/GenesysExtensionAudit.Core/Domain/Services/IExtensionNormalizer.cs

```csharp
namespace GenesysExtensionAudit.Domain.Services;

public interface IExtensionNormalizer
{
    string? Normalize(string? raw);
}
```

### src/GenesysExtensionAudit.Core/Domain/Services/ExtensionNormalizer.cs

```csharp
using System.Text;

namespace GenesysExtensionAudit.Domain.Services;

public sealed class ExtensionNormalizer : IExtensionNormalizer
{
    public string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        // Minimal normalization: digits only
        var sb = new StringBuilder(raw.Length);
        foreach (var ch in raw)
            if (char.IsDigit(ch)) sb.Append(ch);

        var digits = sb.ToString();
        return digits.Length == 0 ? null : digits;
    }
}
```

### src/GenesysExtensionAudit.Core/Domain/Services/IAuditAnalyzer.cs

```csharp
using GenesysExtensionAudit.Domain.Models;

namespace GenesysExtensionAudit.Domain.Services;

public interface IAuditAnalyzer
{
    AuditFindings Analyze(
        IReadOnlyList<UserProfileExtensionRecord> userProfileExtensions,
        IReadOnlyList<AssignedExtensionRecord> assignedExtensions);
}
```

### src/GenesysExtensionAudit.Core/Domain/Services/AuditAnalyzer.cs

```csharp
using GenesysExtensionAudit.Domain.Models;

namespace GenesysExtensionAudit.Domain.Services;

public sealed class AuditAnalyzer : IAuditAnalyzer
{
    public AuditFindings Analyze(
        IReadOnlyList<UserProfileExtensionRecord> userProfileExtensions,
        IReadOnlyList<AssignedExtensionRecord> assignedExtensions)
    {
        // Placeholder; implementation later.
        return new AuditFindings(
            DuplicateProfileExtensions: Array.Empty<DuplicateProfileExtension>(),
            DuplicateAssignedExtensions: Array.Empty<DuplicateAssignedExtension>(),
            ProfileOnlyExtensions: Array.Empty<UnassignedProfileExtension>());
    }
}
```

---

### src/GenesysExtensionAudit.Core/Domain/Paging/PagedResult.cs

```csharp
namespace GenesysExtensionAudit.Domain.Paging;

public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int PageNumber,
    int PageSize,
    int? PageCount);
```

### src/GenesysExtensionAudit.Core/Domain/Paging/IPaginator.cs

```csharp
using GenesysExtensionAudit.Domain.Paging;

namespace GenesysExtensionAudit.Domain.Paging;

public interface IPaginator
{
    Task<IReadOnlyList<T>> FetchAllAsync<T>(Func<int, Task<PagedResult<T>>> getPage, CancellationToken ct);
}
```

---

### src/GenesysExtensionAudit.Infrastructure/GenesysExtensionAudit.Infrastructure.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\GenesysExtensionAudit.Core\GenesysExtensionAudit.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" Version="8.0.0" />
  </ItemGroup>

</Project>
```

### src/GenesysExtensionAudit.Infrastructure/Http/GenesysRegionOptions.cs

```csharp
namespace GenesysExtensionAudit.Infrastructure.Http;

public sealed class GenesysRegionOptions
{
    public string Region { get; set; } = "mypurecloud.com";
    public int PageSize { get; set; } = 100;
    public bool IncludeInactive { get; set; } = false;
    public int MaxRequestsPerSecond { get; set; } = 3;

    public string ApiBaseUrl => $"https://api.{Region}";
    public string AuthBaseUrl => $"https://login.{Region}";
}
```

### src/GenesysExtensionAudit.Infrastructure/Http/ITokenProvider.cs

```csharp
namespace GenesysExtensionAudit.Infrastructure.Http;

public interface ITokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken ct);
}
```

### src/GenesysExtensionAudit.Infrastructure/Http/TokenProvider.cs

```csharp
namespace GenesysExtensionAudit.Infrastructure.Http;

public sealed class TokenProvider : ITokenProvider
{
    public Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        // Placeholder: implement client-credentials OAuth later.
        return Task.FromResult("TOKEN_NOT_CONFIGURED");
    }
}
```

### src/GenesysExtensionAudit.Infrastructure/Http/OAuthBearerHandler.cs

```csharp
using System.Net.Http.Headers;

namespace GenesysExtensionAudit.Infrastructure.Http;

public sealed class OAuthBearerHandler : DelegatingHandler
{
    private readonly ITokenProvider _tokens;

    public OAuthBearerHandler(ITokenProvider tokens) => _tokens = tokens;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await _tokens.GetAccessTokenAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await base.SendAsync(request, cancellationToken);
    }
}
```

### src/GenesysExtensionAudit.Infrastructure/Http/HttpLoggingHandler.cs

```csharp
using Microsoft.Extensions.Logging;

namespace GenesysExtensionAudit.Infrastructure.Http;

public sealed class HttpLoggingHandler : DelegatingHandler
{
    private readonly ILogger<HttpLoggingHandler> _logger;

    public HttpLoggingHandler(ILogger<HttpLoggingHandler> logger) => _logger = logger;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var start = DateTimeOffset.UtcNow;
        var resp = await base.SendAsync(request, cancellationToken);
        var elapsed = DateTimeOffset.UtcNow - start;

        _logger.LogInformation("HTTP {Method} {Uri} -> {StatusCode} in {ElapsedMs}ms",
            request.Method.Method,
            request.RequestUri?.GetLeftPart(UriPartial.Path),
            (int)resp.StatusCode,
            (int)elapsed.TotalMilliseconds);

        return resp;
    }
}
```

### src/GenesysExtensionAudit.Infrastructure/Http/RateLimitHandler.cs

```csharp
namespace GenesysExtensionAudit.Infrastructure.Http;

public sealed class RateLimitHandler : DelegatingHandler
{
    private static readonly SemaphoreSlim _gate = new(1, 1);
    private static DateTimeOffset _last = DateTimeOffset.MinValue;

    public int MaxRequestsPerSecond { get; set; } = 3;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var minInterval = TimeSpan.FromSeconds(1.0 / Math.Max(1, MaxRequestsPerSecond));
            var now = DateTimeOffset.UtcNow;
            var wait = (_last + minInterval) - now;
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait, cancellationToken);

            _last = DateTimeOffset.UtcNow;
        }
        finally
        {
            _gate.Release();
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
```

---

### src/GenesysExtensionAudit.Infrastructure/Genesys/Clients/IGenesysUsersClient.cs

```csharp
using GenesysExtensionAudit.Domain.Paging;
using GenesysExtensionAudit.Infrastructure.Genesys.Dtos;

namespace GenesysExtensionAudit.Infrastructure.Genesys.Clients;

public interface IGenesysUsersClient
{
    // Endpoint focus:
    // /api/v2/users?pageSize={PageSize}&pageNumber={page}[&state=active]
    Task<PagedResult<UserDto>> GetUsersPageAsync(int pageNumber, int pageSize, bool includeInactive, CancellationToken ct);
}
```

### src/GenesysExtensionAudit.Infrastructure/Genesys/Clients/GenesysUsersClient.cs

```csharp
using System.Net.Http.Json;
using GenesysExtensionAudit.Domain.Paging;
using GenesysExtensionAudit.Infrastructure.Genesys.Dtos;
using GenesysExtensionAudit.Infrastructure.Http;
using Microsoft.Extensions.Options;

namespace GenesysExtensionAudit.Infrastructure.Genesys.Clients;

public sealed class GenesysUsersClient : IGenesysUsersClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<GenesysRegionOptions> _opt;

    public GenesysUsersClient(IHttpClientFactory httpClientFactory, IOptions<GenesysRegionOptions> opt)
    {
        _httpClientFactory = httpClientFactory;
        _opt = opt;
    }

    public async Task<PagedResult<UserDto>> GetUsersPageAsync(int pageNumber, int pageSize, bool includeInactive, CancellationToken ct)
    {
        var http = _httpClientFactory.CreateClient("GenesysApi");
        http.BaseAddress = new Uri(_opt.Value.ApiBaseUrl);

        var state = includeInactive ? "" : "&state=active";
        var path = $"/api/v2/users?pageSize={pageSize}&pageNumber={pageNumber}{state}";

        var dto = await http.GetFromJsonAsync<UsersPageDto>(path, cancellationToken: ct)
                  ?? new UsersPageDto();

        return new PagedResult<UserDto>(
            Items: dto.Entities ?? new List<UserDto>(),
            PageNumber: dto.PageNumber ?? pageNumber,
            PageSize: dto.PageSize ?? pageSize,
            PageCount: dto.PageCount);
    }
}
```

### src/GenesysExtensionAudit.Infrastructure/Genesys/Clients/IGenesysExtensionsClient.cs

```csharp
using GenesysExtensionAudit.Domain.Paging;
using GenesysExtensionAudit.Infrastructure.Genesys.Dtos;

namespace GenesysExtensionAudit.Infrastructure.Genesys.Clients;

public interface IGenesysExtensionsClient
{
    // Endpoint focus:
    // /api/v2/telephony/providers/edges/extensions?pageSize={PageSize}&pageNumber={PageNumber}
    Task<PagedResult<ExtensionDto>> GetExtensionsPageAsync(int pageNumber, int pageSize, CancellationToken ct);
}
```

### src/GenesysExtensionAudit.Infrastructure/Genesys/Clients/GenesysExtensionsClient.cs

```csharp
using System.Net.Http.Json;
using GenesysExtensionAudit.Domain.Paging;
using GenesysExtensionAudit.Infrastructure.Genesys.Dtos;
using GenesysExtensionAudit.Infrastructure.Http;
using Microsoft.Extensions.Options;

namespace GenesysExtensionAudit.Infrastructure.Genesys.Clients;

public sealed class GenesysExtensionsClient : IGenesysExtensionsClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<GenesysRegionOptions> _opt;

    public GenesysExtensionsClient(IHttpClientFactory httpClientFactory, IOptions<GenesysRegionOptions> opt)
    {
        _httpClientFactory = httpClientFactory;
        _opt = opt;
    }

    public async Task<PagedResult<ExtensionDto>> GetExtensionsPageAsync(int pageNumber, int pageSize, CancellationToken ct)
    {
        var http = _httpClientFactory.CreateClient("GenesysApi");
        http.BaseAddress = new Uri(_opt.Value.ApiBaseUrl);

        var path = $"/api/v2/telephony/providers/edges/extensions?pageSize={pageSize}&pageNumber={pageNumber}";
        var dto = await http.GetFromJsonAsync<ExtensionsPageDto>(path, cancellationToken: ct)
                  ?? new ExtensionsPageDto();

        return new PagedResult<ExtensionDto>(
            Items: dto.Entities ?? new List<ExtensionDto>(),
            PageNumber: dto.PageNumber ?? pageNumber,
            PageSize: dto.PageSize ?? pageSize,
            PageCount: dto.PageCount);
    }
}
```

### src/GenesysExtensionAudit.Infrastructure/Genesys/Dtos/PagedResponseDtos.cs

```csharp
namespace GenesysExtensionAudit.Infrastructure.Genesys.Dtos;

public sealed class UsersPageDto
{
    public List<UserDto>? Entities { get; set; }
    public int? PageSize { get; set; }
    public int? PageNumber { get; set; }
    public int? PageCount { get; set; }
}

public sealed class ExtensionsPageDto
{
    public List<ExtensionDto>? Entities { get; set; }
    public int? PageSize { get; set; }
    public int? PageNumber { get; set; }
    public int? PageCount { get; set; }
}
```

### src/GenesysExtensionAudit.Infrastructure/Genesys/Dtos/UserDto.cs

```csharp
namespace GenesysExtensionAudit.Infrastructure.Genesys.Dtos;

public sealed class UserDto
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? State { get; set; }
    public UserProfileDto? Profile { get; set; }
}

public sealed class UserProfileDto
{
    // Work phone extension field (exact name may vary in API; map later)
    public string? PrimaryContactInfo { get; set; }
    public string? WorkPhoneExtension { get; set; }
}
```

### src/GenesysExtensionAudit.Infrastructure/Genesys/Dtos/ExtensionDto.cs

```csharp
namespace GenesysExtensionAudit.Infrastructure.Genesys.Dtos;

public sealed class ExtensionDto
{
    public string? Id { get; set; }
    public string? Extension { get; set; }
    public AssignedToDto? AssignedTo { get; set; }
}

public sealed class AssignedToDto
{
    public string? Id { get; set; }
    public string? Type { get; set; }
}
```

---

### src/GenesysExtensionAudit.Infrastructure/Genesys/Pagination/Paginator.cs

```csharp
using GenesysExtensionAudit.Domain.Paging;

namespace GenesysExtensionAudit.Infrastructure.Genesys.Pagination;

public sealed class Paginator : IPaginator
{
    public async Task<IReadOnlyList<T>> FetchAllAsync<T>(Func<int, Task<PagedResult<T>>> getPage, CancellationToken ct)
    {
        var all = new List<T>();
        var pageNumber = 1;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var page = await getPage(pageNumber);
            if (page.Items.Count == 0) break;

            all.AddRange(page.Items);

            if (page.PageCount.HasValue && pageNumber >= page.PageCount.Value)
                break;

            pageNumber++;
        }

        return all;
    }
}
```

---

### src/GenesysExtensionAudit.Tests/GenesysExtensionAudit.Tests.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\GenesysExtensionAudit.Core\GenesysExtensionAudit.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.10.0" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>

</Project>
```

### src/GenesysExtensionAudit.Tests/Domain/ExtensionNormalizerTests.cs

```csharp
using GenesysExtensionAudit.Domain.Services;
using Xunit;

namespace GenesysExtensionAudit.Tests.Domain;

public sealed class ExtensionNormalizerTests
{
    [Fact]
    public void Normalize_RemovesNonDigits()
    {
        IExtensionNormalizer n = new ExtensionNormalizer();
        Assert.Equal("1234", n.Normalize("12-34"));
    }
}
```

---

### .gitignore

```gitignore
bin/
obj/
.vs/
*.user
*.suo
*.cache
*.log
TestResults/
```

---

### README.md

```md
# Genesys Extension Audit (Skeleton)

WPF/.NET 8 desktop app skeleton for auditing Genesys Cloud extension assignments vs user profile work phone extension.

Endpoints (focus):
- Users: `/api/v2/users?pageSize={PageSize}&pageNumber={page}[&state=active]`
- Extensions: `/api/v2/telephony/providers/edges/extensions?pageSize={PageSize}&pageNumber={page}`

This is a project structure + solution skeleton. OAuth, DTO mapping, and full analysis/reporting will be implemented next.
```
```
