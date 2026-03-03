using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace GenesysExtensionAudit.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly INavigationService _navigation;

    public MainViewModel(INavigationService navigation)
    {
        _navigation = navigation;
        HelpCommand = new RelayCommand(ShowHelp);
    }

    public ReadOnlyObservableCollection<NavigationItem> NavigationItems => _navigation.Items;

    public string StatusText => "Idle";
    public string FooterText => "Ready";
    public ICommand HelpCommand { get; }

    public NavigationItem? CurrentItem
    {
        get => _navigation.Current;
        set
        {
            // TabControl sets SelectedItem directly; keep navigation service in sync.
            if (value is null) return;
            _navigation.Navigate(value.Key);
            OnPropertyChanged();
        }
    }

    private static void ShowHelp()
    {
        const string helpText =
            "Audit Paths\n\n" +
            "1. Extensions\n" +
            "- Duplicate profile extensions\n" +
            "- Duplicate assigned extensions\n" +
            "- Profile extensions not assigned\n" +
            "- Assigned extensions missing from profiles\n" +
            "- Invalid extension values\n\n" +
            "2. Groups\n" +
            "- Empty groups\n" +
            "- Single-member groups\n\n" +
            "3. Queues\n" +
            "- Empty queues\n" +
            "- Duplicate queue names\n\n" +
            "4. Flows\n" +
            "- Never-published flows\n" +
            "- Stale published flows\n\n" +
            "5. Inactive Users\n" +
            "- No login/token activity over threshold\n\n" +
            "6. DIDs\n" +
            "- Unassigned DIDs\n" +
            "- DIDs assigned to missing/inactive users\n" +
            "- DID numbers not found on any user profile\n\n" +
            "7. Audit Logs\n" +
            "- Service mapping -> submit query -> poll transaction -> fetch paged results\n" +
            "- Exposes service/action/user/entity activity rows in the exported workbook";

        MessageBox.Show(
            helpText,
            "Audit Help",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}
