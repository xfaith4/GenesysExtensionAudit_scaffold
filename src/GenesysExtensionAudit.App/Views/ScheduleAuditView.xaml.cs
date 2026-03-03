using System.Windows.Controls;
using GenesysExtensionAudit.ViewModels;

namespace GenesysExtensionAudit.Views;

public partial class ScheduleAuditView : UserControl
{
    public ScheduleAuditView()
    {
        InitializeComponent();
    }

    private void PasswordBox_OnPasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is ScheduleAuditViewModel vm && sender is PasswordBox box)
            vm.RunAsPassword = box.Password;
    }
}
