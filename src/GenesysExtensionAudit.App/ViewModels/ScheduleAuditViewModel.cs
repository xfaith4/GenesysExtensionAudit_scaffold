using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using GenesysExtensionAudit.Scheduling;
using GenesysExtensionAudit.Services;

namespace GenesysExtensionAudit.ViewModels;

public sealed class ScheduleAuditViewModel : INotifyPropertyChanged
{
    private const string AllCatalogEntitiesOption = "(All Catalog Entities)";

    private readonly IScheduledAuditService _scheduledAuditService;
    private readonly IAuditLogCatalogCache _auditLogCatalogCache;
    private readonly ObservableCollection<string> _auditLogEntities = [];
    private readonly ObservableCollection<ScheduledTaskInfo> _scheduledTasks = [];

    private string _scheduleName = "Scheduled Audit";
    private ScheduledRecurrenceType _selectedRecurrence = ScheduledRecurrenceType.Once;
    private DateTime _startDate = DateTime.Today.AddDays(1);
    private string _startTime = "09:00";
    private bool _weeklyMonday = true;
    private bool _weeklyTuesday;
    private bool _weeklyWednesday;
    private bool _weeklyThursday;
    private bool _weeklyFriday;
    private bool _weeklySaturday;
    private bool _weeklySunday;

    private string _runAsUserName = $"{Environment.UserDomainName}\\{Environment.UserName}";
    private string _runAsPassword = string.Empty;

    private int _pageSize = 100;
    private bool _includeInactive;
    private int _staleFlowDays = 90;
    private int _inactiveUserDays = 90;
    private bool _runExtensionAudit = true;
    private bool _runGroupAudit = true;
    private bool _runQueueAudit = true;
    private bool _runFlowAudit = true;
    private bool _runInactiveUserAudit = true;
    private bool _runDidAudit = true;
    private bool _runAuditLogs;
    private int _auditLogLookbackHours = 1;
    private bool _runOperationalEventLogs;
    private int _operationalEventLookbackDays = 7;
    private bool _runOutboundEvents;
    private string _selectedAuditLogEntity = AllCatalogEntitiesOption;
    private bool _pushToGitHub;
    private bool _isLoadingAuditLogEntities;
    private bool _isBusy;
    private string _statusMessage = "Ready.";
    private string? _errorMessage;
    private ScheduledTaskInfo? _selectedTask;

    public ScheduleAuditViewModel(
        IScheduledAuditService scheduledAuditService,
        IAuditLogCatalogCache auditLogCatalogCache)
    {
        _scheduledAuditService = scheduledAuditService ?? throw new ArgumentNullException(nameof(scheduledAuditService));
        _auditLogCatalogCache = auditLogCatalogCache ?? throw new ArgumentNullException(nameof(auditLogCatalogCache));

        _auditLogEntities.Add(AllCatalogEntitiesOption);

        CreateScheduledTaskCommand = new RelayCommand(CreateScheduledTask, () => !IsBusy);
        RefreshScheduledTasksCommand = new RelayCommand(RefreshScheduledTasks, () => !IsBusy);
        DeleteScheduledTaskCommand = new RelayCommand(DeleteScheduledTask, () => !IsBusy && SelectedTask is not null);
        RefreshAuditCatalogCommand = new RelayCommand(RefreshAuditCatalog, () => !IsBusy && !IsLoadingAuditLogEntities);

        RefreshScheduledTasks();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand CreateScheduledTaskCommand { get; }
    public ICommand RefreshScheduledTasksCommand { get; }
    public ICommand DeleteScheduledTaskCommand { get; }
    public ICommand RefreshAuditCatalogCommand { get; }

    public string ScheduleName
    {
        get => _scheduleName;
        set => SetField(ref _scheduleName, value);
    }

    public ScheduledRecurrenceType SelectedRecurrence
    {
        get => _selectedRecurrence;
        set
        {
            if (SetField(ref _selectedRecurrence, value))
            {
                OnPropertyChanged(nameof(IsOnceRecurrence));
                OnPropertyChanged(nameof(IsWeeklyRecurrence));
            }
        }
    }

    public bool IsOnceRecurrence => SelectedRecurrence == ScheduledRecurrenceType.Once;
    public bool IsWeeklyRecurrence => SelectedRecurrence == ScheduledRecurrenceType.Weekly;

    public DateTime StartDate
    {
        get => _startDate;
        set => SetField(ref _startDate, value);
    }

    public string StartTime
    {
        get => _startTime;
        set => SetField(ref _startTime, value);
    }

    public bool WeeklyMonday { get => _weeklyMonday; set => SetField(ref _weeklyMonday, value); }
    public bool WeeklyTuesday { get => _weeklyTuesday; set => SetField(ref _weeklyTuesday, value); }
    public bool WeeklyWednesday { get => _weeklyWednesday; set => SetField(ref _weeklyWednesday, value); }
    public bool WeeklyThursday { get => _weeklyThursday; set => SetField(ref _weeklyThursday, value); }
    public bool WeeklyFriday { get => _weeklyFriday; set => SetField(ref _weeklyFriday, value); }
    public bool WeeklySaturday { get => _weeklySaturday; set => SetField(ref _weeklySaturday, value); }
    public bool WeeklySunday { get => _weeklySunday; set => SetField(ref _weeklySunday, value); }

    public string RunAsUserName
    {
        get => _runAsUserName;
        set => SetField(ref _runAsUserName, value);
    }

    public string RunAsPassword
    {
        get => _runAsPassword;
        set => SetField(ref _runAsPassword, value);
    }

    public int PageSize
    {
        get => _pageSize;
        set => SetField(ref _pageSize, Math.Clamp(value, 1, 500));
    }

    public bool IncludeInactive { get => _includeInactive; set => SetField(ref _includeInactive, value); }
    public int StaleFlowDays { get => _staleFlowDays; set => SetField(ref _staleFlowDays, Math.Max(1, value)); }
    public int InactiveUserDays { get => _inactiveUserDays; set => SetField(ref _inactiveUserDays, Math.Max(1, value)); }

    public bool RunExtensionAudit { get => _runExtensionAudit; set => SetField(ref _runExtensionAudit, value); }
    public bool RunGroupAudit { get => _runGroupAudit; set => SetField(ref _runGroupAudit, value); }
    public bool RunQueueAudit { get => _runQueueAudit; set => SetField(ref _runQueueAudit, value); }
    public bool RunFlowAudit { get => _runFlowAudit; set => SetField(ref _runFlowAudit, value); }
    public bool RunInactiveUserAudit { get => _runInactiveUserAudit; set => SetField(ref _runInactiveUserAudit, value); }
    public bool RunDidAudit { get => _runDidAudit; set => SetField(ref _runDidAudit, value); }

    public bool RunAuditLogs
    {
        get => _runAuditLogs;
        set
        {
            if (SetField(ref _runAuditLogs, value))
            {
                if (value && AuditLogEntities.Count <= 1)
                    LoadAuditCatalog(forceRefresh: false);
            }
        }
    }

    public int AuditLogLookbackHours
    {
        get => _auditLogLookbackHours;
        set => SetField(ref _auditLogLookbackHours, Math.Max(1, value));
    }

    public bool RunOperationalEventLogs { get => _runOperationalEventLogs; set => SetField(ref _runOperationalEventLogs, value); }

    public int OperationalEventLookbackDays
    {
        get => _operationalEventLookbackDays;
        set => SetField(ref _operationalEventLookbackDays, Math.Max(1, value));
    }

    public bool RunOutboundEvents { get => _runOutboundEvents; set => SetField(ref _runOutboundEvents, value); }

    /// <summary>
    /// When true the scheduled runner will push the generated report to the
    /// GitHub repository configured in appsettings.json after saving locally.
    /// </summary>
    public bool PushToGitHub { get => _pushToGitHub; set => SetField(ref _pushToGitHub, value); }

    public ObservableCollection<string> AuditLogEntities => _auditLogEntities;

    public string SelectedAuditLogEntity
    {
        get => _selectedAuditLogEntity;
        set => SetField(ref _selectedAuditLogEntity, string.IsNullOrWhiteSpace(value) ? AllCatalogEntitiesOption : value);
    }

    public bool IsLoadingAuditLogEntities
    {
        get => _isLoadingAuditLogEntities;
        private set
        {
            if (SetField(ref _isLoadingAuditLogEntities, value))
                RaiseCommandCanExecuteChanged();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
                RaiseCommandCanExecuteChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetField(ref _errorMessage, value))
                OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public ObservableCollection<ScheduledTaskInfo> ScheduledTasks => _scheduledTasks;

    public ScheduledTaskInfo? SelectedTask
    {
        get => _selectedTask;
        set
        {
            if (SetField(ref _selectedTask, value))
                RaiseCommandCanExecuteChanged();
        }
    }

    private async void RefreshScheduledTasks()
    {
        if (IsBusy) return;
        ErrorMessage = null;
        IsBusy = true;

        try
        {
            var items = await _scheduledAuditService.ListAsync(CancellationToken.None).ConfigureAwait(true);
            _scheduledTasks.Clear();
            foreach (var item in items)
                _scheduledTasks.Add(item);

            StatusMessage = $"Loaded {_scheduledTasks.Count} scheduled task(s).";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            StatusMessage = "Failed to load scheduled tasks.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async void CreateScheduledTask()
    {
        if (IsBusy) return;
        ErrorMessage = null;
        IsBusy = true;

        try
        {
            if (!TryParseStartDateTime(out var startDateTime, out var parseError))
                throw new InvalidOperationException(parseError);

            var definition = new ScheduledAuditDefinition
            {
                Name = ScheduleName,
                RecurrenceType = SelectedRecurrence,
                StartLocalDateTime = startDateTime,
                WeeklyDays = GetSelectedWeeklyDays(),
                RunAsUserName = RunAsUserName,
                RunAsPassword = RunAsPassword,
                PageSize = PageSize,
                IncludeInactiveUsers = IncludeInactive,
                StaleFlowThresholdDays = StaleFlowDays,
                InactiveUserThresholdDays = InactiveUserDays,
                RunExtensionAudit = RunExtensionAudit,
                RunGroupAudit = RunGroupAudit,
                RunQueueAudit = RunQueueAudit,
                RunFlowAudit = RunFlowAudit,
                RunInactiveUserAudit = RunInactiveUserAudit,
                RunDidAudit = RunDidAudit,
                RunAuditLogs = RunAuditLogs,
                AuditLogLookbackHours = AuditLogLookbackHours,
                RunOperationalEventLogs = RunOperationalEventLogs,
                OperationalEventLookbackDays = OperationalEventLookbackDays,
                RunOutboundEvents = RunOutboundEvents,
                PushToGitHub = PushToGitHub,
                AuditLogServiceName = string.Equals(SelectedAuditLogEntity, AllCatalogEntitiesOption, StringComparison.Ordinal)
                    ? null
                    : SelectedAuditLogEntity
            };

            var created = await _scheduledAuditService.CreateAsync(definition, CancellationToken.None).ConfigureAwait(true);
            StatusMessage = $"Created task: {created.TaskName}";
            RefreshScheduledTasks();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            StatusMessage = "Create scheduled task failed.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async void DeleteScheduledTask()
    {
        if (IsBusy || SelectedTask is null) return;
        ErrorMessage = null;
        IsBusy = true;

        try
        {
            await _scheduledAuditService.DeleteAsync(SelectedTask, CancellationToken.None).ConfigureAwait(true);
            StatusMessage = $"Deleted task: {SelectedTask.TaskName}";
            RefreshScheduledTasks();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            StatusMessage = "Delete scheduled task failed.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RefreshAuditCatalog()
        => LoadAuditCatalog(forceRefresh: true);

    private async void LoadAuditCatalog(bool forceRefresh)
    {
        if (IsLoadingAuditLogEntities || IsBusy)
            return;

        IsLoadingAuditLogEntities = true;
        ErrorMessage = null;

        try
        {
            var ordered = await _auditLogCatalogCache
                .GetOrRefreshAsync(forceRefresh, CancellationToken.None)
                .ConfigureAwait(true);

            _auditLogEntities.Clear();
            _auditLogEntities.Add(AllCatalogEntitiesOption);
            foreach (var entity in ordered)
                _auditLogEntities.Add(entity);

            SelectedAuditLogEntity = AllCatalogEntitiesOption;
            StatusMessage = $"Loaded {_auditLogEntities.Count - 1} audit-log catalog entities.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load audit-log catalog entities: {ex.Message}";
            StatusMessage = "Failed to load audit-log catalog entities.";
        }
        finally
        {
            IsLoadingAuditLogEntities = false;
        }
    }

    private IReadOnlyList<DayOfWeek> GetSelectedWeeklyDays()
    {
        var days = new List<DayOfWeek>();
        if (WeeklySunday) days.Add(DayOfWeek.Sunday);
        if (WeeklyMonday) days.Add(DayOfWeek.Monday);
        if (WeeklyTuesday) days.Add(DayOfWeek.Tuesday);
        if (WeeklyWednesday) days.Add(DayOfWeek.Wednesday);
        if (WeeklyThursday) days.Add(DayOfWeek.Thursday);
        if (WeeklyFriday) days.Add(DayOfWeek.Friday);
        if (WeeklySaturday) days.Add(DayOfWeek.Saturday);
        return days;
    }

    private bool TryParseStartDateTime(out DateTime dateTime, out string error)
    {
        dateTime = default;
        error = string.Empty;

        if (!TimeSpan.TryParse(StartTime, out var time))
        {
            error = "Start time must be in HH:mm format (example: 09:00).";
            return false;
        }

        dateTime = StartDate.Date.Add(time);
        return true;
    }

    private void RaiseCommandCanExecuteChanged()
    {
        if (CreateScheduledTaskCommand is RelayCommand c1) c1.RaiseCanExecuteChanged();
        if (RefreshScheduledTasksCommand is RelayCommand c2) c2.RaiseCanExecuteChanged();
        if (DeleteScheduledTaskCommand is RelayCommand c3) c3.RaiseCanExecuteChanged();
        if (RefreshAuditCatalogCommand is RelayCommand c4) c4.RaiseCanExecuteChanged();
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
