using System.Windows;
using System.Reflection;

namespace GenesysExtensionAudit;

public partial class MainWindow : Window
{
    public string AppVersionText { get; }

    public MainWindow()
    {
        var version = GetDisplayVersion();
        AppVersionText = $"v{version}";
        Title = $"Genesys Cloud Auditor {AppVersionText}";

        InitializeComponent();
    }

    private static string GetDisplayVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+');
            return plus > 0 ? informational[..plus] : informational;
        }

        var v = assembly.GetName().Version;
        if (v is null) return "1.0.0";

        var patch = v.Build >= 0 ? v.Build : 0;
        return $"{v.Major}.{v.Minor}.{patch}";
    }
}
