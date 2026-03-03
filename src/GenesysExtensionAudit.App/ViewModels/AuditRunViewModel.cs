using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using GenesysExtensionAudit.Application;
using GenesysExtensionAudit.Infrastructure.Application;
using GenesysExtensionAudit.Infrastructure.Genesys.Clients;
using GenesysExtensionAudit.Infrastructure.Reporting;
using GenesysExtensionAudit.Services;
using Microsoft.Win32;

namespace GenesysExtensionAudit.ViewModels;

/// <summary>
/// ViewModel for running an audit.
/// Inputs:  PageSize, IncludeInactive, StaleFlowDays, InactiveUserDays
/// Controls: Start / Cancel
/// Feedback: Progress (percent + message), Error surface, last exported file path
/// </summary>
public sealed class RunAuditViewModel : INotifyPropertyChanged
{
    private const string AllCatalogEntitiesOption = "(All Catalog Entities)";

    private readonly IAuditOrchestrator _orchestrator;
    private readonly IExcelReportService _excelService;
    private readonly IAuditLogCatalogCache _auditLogCatalogCache;
    private readonly ObservableCollection<string> _auditLogEntities = [];
    private readonly ObservableCollection<RunSummaryRow> _lastRunSummary = [];

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
    private bool _runOperationalEventLogs;
    private int _operationalEventLookbackDays = 7;
    private bool _runOutboundEvents;
    private bool _isLoadingAuditLogEntities;
    private bool _auditLogEntitiesLoaded;
    private string _selectedAuditLogEntity = AllCatalogEntitiesOption;
    private bool _isRunning;
    private int _progressPercent;
    private string _progressMessage = string.Empty;
    private string _statusMessage = "Ready.";
    private string? _errorMessage;
    private string? _lastExportPath;
    private AuditReportData? _lastReport;
    private CancellationTokenSource? _cts;

    public RunAuditViewModel(
        IAuditOrchestrator orchestrator,
        IExcelReportService excelService,
        IAuditLogCatalogCache auditLogCatalogCache)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _excelService = excelService ?? throw new ArgumentNullException(nameof(excelService));
        _auditLogCatalogCache = auditLogCatalogCache ?? throw new ArgumentNullException(nameof(auditLogCatalogCache));

        StartCommand = new RelayCommand(StartAsync, () => !IsRunning);
        CancelCommand = new RelayCommand(Cancel, () => IsRunning);
        RefreshAuditCatalogCommand = new RelayCommand(RefreshAuditCatalog, () => !IsRunning && !IsLoadingAuditLogEntities);
        ExportCommand = new RelayCommand(ExportLastReport, () => !IsRunning && _lastReport is not null);

        _auditLogEntities.Add(AllCatalogEntitiesOption);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Page size used when calling the Genesys Cloud paginated endpoints.
    /// Valid range: 1–500.
    /// </summary>
    public int PageSize
    {
        get => _pageSize;
        set
        {
            var v = Math.Clamp(value, 1, 500);
            SetField(ref _pageSize, v);
        }
    }

    public bool IncludeInactive
    {
        get => _includeInactive;
        set => SetField(ref _includeInactive, value);
    }

    public int StaleFlowDays
    {
        get => _staleFlowDays;
        set => SetField(ref _staleFlowDays, Math.Max(1, value));
    }

    public int InactiveUserDays
    {
        get => _inactiveUserDays;
        set => SetField(ref _inactiveUserDays, Math.Max(1, value));
    }

    public bool RunExtensionAudit
    {
        get => _runExtensionAudit;
        set
        {
            if (SetField(ref _runExtensionAudit, value))
                OnAuditSelectionChanged();
        }
    }

    public bool RunGroupAudit
    {
        get => _runGroupAudit;
        set
        {
            if (SetField(ref _runGroupAudit, value))
                OnAuditSelectionChanged();
        }
    }

    public bool RunQueueAudit
    {
        get => _runQueueAudit;
        set
        {
            if (SetField(ref _runQueueAudit, value))
                OnAuditSelectionChanged();
        }
    }

    public bool RunFlowAudit
    {
        get => _runFlowAudit;
        set
        {
            if (SetField(ref _runFlowAudit, value))
                OnAuditSelectionChanged();
        }
    }

    public bool RunInactiveUserAudit
    {
        get => _runInactiveUserAudit;
        set
        {
            if (SetField(ref _runInactiveUserAudit, value))
                OnAuditSelectionChanged();
        }
    }

    public bool RunDidAudit
    {
        get => _runDidAudit;
        set
        {
            if (SetField(ref _runDidAudit, value))
                OnAuditSelectionChanged();
        }
    }

    public bool RunAuditLogs
    {
        get => _runAuditLogs;
        set
        {
            if (SetField(ref _runAuditLogs, value))
            {
                if (value && !_auditLogEntitiesLoaded)
                    LoadAuditCatalog(forceRefresh: false);
                OnAuditSelectionChanged();
            }
        }
    }

    public bool RunOperationalEventLogs
    {
        get => _runOperationalEventLogs;
        set
        {
            if (SetField(ref _runOperationalEventLogs, value))
                OnAuditSelectionChanged();
        }
    }

    public int OperationalEventLookbackDays
    {
        get => _operationalEventLookbackDays;
        set => SetField(ref _operationalEventLookbackDays, Math.Max(1, value));
    }

    public bool RunOutboundEvents
    {
        get => _runOutboundEvents;
        set
        {
            if (SetField(ref _runOutboundEvents, value))
                OnAuditSelectionChanged();
        }
    }

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

    public bool IsAuditLogSelectionEnabled => RunAuditLogs && !IsLoadingAuditLogEntities;

    public bool SelectAllAudits
    {
        get => RunExtensionAudit && RunGroupAudit && RunQueueAudit && RunFlowAudit && RunInactiveUserAudit && RunDidAudit && RunAuditLogs && RunOperationalEventLogs && RunOutboundEvents;
        set
        {
            RunExtensionAudit = value;
            RunGroupAudit = value;
            RunQueueAudit = value;
            RunFlowAudit = value;
            RunInactiveUserAudit = value;
            RunDidAudit = value;
            RunAuditLogs = value;
            RunOperationalEventLogs = value;
            RunOutboundEvents = value;
        }
    }

    public bool HasAnyAuditSelected =>
        RunExtensionAudit || RunGroupAudit || RunQueueAudit || RunFlowAudit || RunInactiveUserAudit || RunDidAudit || RunAuditLogs || RunOperationalEventLogs || RunOutboundEvents;

    public string? LastExportPath
    {
        get => _lastExportPath;
        private set => SetField(ref _lastExportPath, value);
    }

    public bool HasExport => !string.IsNullOrWhiteSpace(LastExportPath);

    public bool HasReport => _lastReport is not null;
    public ObservableCollection<RunSummaryRow> LastRunSummary => _lastRunSummary;

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetField(ref _isRunning, value))
            {
                RaiseCommandCanExecuteChanged();
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(CanCancel));
            }
        }
    }

    public bool CanStart => !IsRunning && HasAnyAuditSelected;
    public bool CanCancel => IsRunning;

    public int ProgressPercent
    {
        get => _progressPercent;
        private set => SetField(ref _progressPercent, value);
    }

    public string ProgressMessage
    {
        get => _progressMessage;
        private set => SetField(ref _progressMessage, value);
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

    public ICommand StartCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand RefreshAuditCatalogCommand { get; }
    public ICommand ExportCommand { get; }

    private async void StartAsync()
    {
        if (IsRunning) return;

        ErrorMessage = null;
        ProgressPercent = 0;
        ProgressMessage = string.Empty;

        if (!HasAnyAuditSelected)
        {
            ErrorMessage = "Select at least one audit path.";
            StatusMessage = "No audit paths selected.";
            return;
        }

        IsRunning = true;
        StatusMessage = "Starting audit...";

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        var progress = new Progress<AuditProgress>(p =>
        {
            try
            {
                if (p.Percent is >= 0 and <= 100)
                    ProgressPercent = p.Percent;

                if (!string.IsNullOrWhiteSpace(p.Message))
                    ProgressMessage = p.Message;

                if (!string.IsNullOrWhiteSpace(p.Status))
                    StatusMessage = p.Status;
            }
            catch
            {
                // ignore progress update failures
            }
        });

        try
        {
            StatusMessage = "Running audit...";
            var report = await _orchestrator.RunAsync(new AuditRunOptions
            {
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
                AuditLogLookbackHours = 1,
                AuditLogServiceNames = GetSelectedAuditLogServiceNames(),
                RunOperationalEventLogs = RunOperationalEventLogs,
                OperationalEventLookbackDays = OperationalEventLookbackDays,
                RunOutboundEvents = RunOutboundEvents
            }, progress, ct).ConfigureAwait(true);

            _lastReport = report;
            BuildLastRunSummary(report);
            OnPropertyChanged(nameof(HasReport));
            RaiseCommandCanExecuteChanged();

            ProgressMessage = "Generating Excel report...";
            await SaveReportToFileAsync(report, ct).ConfigureAwait(true);

            ProgressPercent = 100;
            ProgressMessage = "Completed.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Audit cancelled.";
            ProgressMessage = "Cancelled.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            StatusMessage = "Audit failed.";
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            IsRunning = false;
        }
    }

    private void Cancel()
    {
        try
        {
            _cts?.Cancel();
            StatusMessage = "Cancelling...";
        }
        catch
        {
            // ignore
        }
    }

    private void RaiseCommandCanExecuteChanged()
    {
        if (StartCommand is RelayCommand s) s.RaiseCanExecuteChanged();
        if (CancelCommand is RelayCommand c) c.RaiseCanExecuteChanged();
        if (ExportCommand is RelayCommand e) e.RaiseCanExecuteChanged();
    }

    private void OnAuditSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectAllAudits));
        OnPropertyChanged(nameof(HasAnyAuditSelected));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(IsAuditLogSelectionEnabled));
        RaiseCommandCanExecuteChanged();
    }

    private IReadOnlyList<string> GetSelectedAuditLogServiceNames()
    {
        if (!RunAuditLogs)
            return [];

        if (string.IsNullOrWhiteSpace(SelectedAuditLogEntity) ||
            string.Equals(SelectedAuditLogEntity, AllCatalogEntitiesOption, StringComparison.Ordinal))
        {
            return [];
        }

        return [SelectedAuditLogEntity];
    }

    private void RefreshAuditCatalog()
        => LoadAuditCatalog(forceRefresh: true);

    private async void LoadAuditCatalog(bool forceRefresh)
    {
        if (IsLoadingAuditLogEntities)
            return;

        IsLoadingAuditLogEntities = true;

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
            _auditLogEntitiesLoaded = true;
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

    private async void ExportLastReport()
    {
        if (_lastReport is null || IsRunning)
            return;

        try
        {
            await SaveReportToFileAsync(_lastReport, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Export failed: {ex.Message}";
            StatusMessage = "Export failed.";
        }
    }

    private async Task SaveReportToFileAsync(AuditReportData report, CancellationToken ct)
    {
        var xlsx = await _excelService.GenerateAsync(report, ct).ConfigureAwait(true);

        var dlg = new SaveFileDialog
        {
            Title = "Save Audit Report",
            Filter = "Excel Workbook (*.xlsx)|*.xlsx",
            FileName = $"GenesysAudit_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
            DefaultExt = ".xlsx"
        };

        if (dlg.ShowDialog() == true)
        {
            await File.WriteAllBytesAsync(dlg.FileName, xlsx, ct).ConfigureAwait(true);
            LastExportPath = dlg.FileName;
            OnPropertyChanged(nameof(HasExport));
            StatusMessage = $"Saved: {Path.GetFileName(dlg.FileName)}";
        }
        else
        {
            StatusMessage = "Audit complete — export skipped.";
        }
    }

    private void BuildLastRunSummary(AuditReportData report)
    {
        _lastRunSummary.Clear();

        var ext = report.ExtensionReport;
        var runFlags = new (string Name, bool Ran, int Count)[]
        {
            ("Extensions", report.Options.RunExtensionAudit,
                ext.DuplicateProfileExtensions.Count
                + ext.ProfileExtensionsNotAssigned.Count
                + ext.DuplicateAssignedExtensions.Count
                + ext.AssignedExtensionsMissingFromProfiles.Count
                + ext.InvalidProfileExtensions.Count
                + ext.InvalidAssignedExtensions.Count),
            ("Groups", report.Options.RunGroupAudit, report.GroupFindings.Count),
            ("Queues", report.Options.RunQueueAudit, report.QueueFindings.Count),
            ("Flows", report.Options.RunFlowAudit, report.FlowFindings.Count),
            ("Users with Stale Token", report.Options.RunInactiveUserAudit, report.InactiveUserFindings.Count),
            ("Users Missing Location", report.Options.RunInactiveUserAudit, report.NoLocationUserFindings.Count),
            ("DIDs", report.Options.RunDidAudit, report.DidFindings.Count),
            ("Audit Logs", report.Options.RunAuditLogs, report.AuditLogFindings.Count),
            ("Operational Event Logs", report.Options.RunOperationalEventLogs, report.OperationalEventFindings.Count),
            ("OutboundEvents", report.Options.RunOutboundEvents, report.OutboundEventFindings.Count)
        };

        foreach (var item in runFlags)
        {
            if (!item.Ran)
                continue;

            _lastRunSummary.Add(new RunSummaryRow(item.Name, item.Count));
        }
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

public sealed record RunSummaryRow(string AuditPath, int Items);

/// <summary>Simple synchronous command with CanExecute support.</summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => _execute();
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
