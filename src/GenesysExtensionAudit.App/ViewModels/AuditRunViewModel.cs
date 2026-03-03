using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using GenesysExtensionAudit.Application;
using GenesysExtensionAudit.Infrastructure.Application;
using GenesysExtensionAudit.Infrastructure.Reporting;
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
    private readonly IAuditOrchestrator _orchestrator;
    private readonly IExcelReportService _excelService;

    private int _pageSize = 100;
    private bool _includeInactive;
    private int _staleFlowDays = 90;
    private int _inactiveUserDays = 90;
    private bool _isRunning;
    private int _progressPercent;
    private string _progressMessage = string.Empty;
    private string _statusMessage = "Ready.";
    private string? _errorMessage;
    private string? _lastExportPath;
    private CancellationTokenSource? _cts;

    public RunAuditViewModel(IAuditOrchestrator orchestrator, IExcelReportService excelService)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _excelService = excelService ?? throw new ArgumentNullException(nameof(excelService));

        StartCommand = new RelayCommand(StartAsync, () => !IsRunning);
        CancelCommand = new RelayCommand(Cancel, () => IsRunning);
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

    public string? LastExportPath
    {
        get => _lastExportPath;
        private set => SetField(ref _lastExportPath, value);
    }

    public bool HasExport => !string.IsNullOrWhiteSpace(LastExportPath);

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

    public bool CanStart => !IsRunning;
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

    private async void StartAsync()
    {
        if (IsRunning) return;

        ErrorMessage = null;
        ProgressPercent = 0;
        ProgressMessage = string.Empty;

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
                InactiveUserThresholdDays = InactiveUserDays
            }, progress, ct).ConfigureAwait(true);

            ProgressMessage = "Generating Excel report...";
            var xlsx = await _excelService.GenerateAsync(report, ct).ConfigureAwait(true);

            // Prompt user for save location
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
