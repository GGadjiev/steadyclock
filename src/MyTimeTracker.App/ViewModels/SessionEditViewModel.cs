using System.Globalization;
using MyTimeTracker.App.Infrastructure;
using MyTimeTracker.App.Resources;
using MyTimeTracker.App.Services;
using MyTimeTracker.Core.Common;
using MyTimeTracker.Core.Models;

namespace MyTimeTracker.App.ViewModels;

public sealed class SessionEditViewModel : ObservableObject
{
    private readonly TimeTrackingController _controller;
    private readonly TimeZoneInfo _timeZone;
    private readonly TimeSpan? _existingDuration;
    private DateTime? _initialSelectedDate;
    private string _initialStartTimeText = string.Empty;
    private string _initialEndTimeText = string.Empty;

    private DateTime? _selectedDate;
    private string _startTimeText = string.Empty;
    private string _endTimeText = string.Empty;
    private string _comment = string.Empty;
    private string _errorMessage = string.Empty;
    private string _durationPreview = string.Empty;
    private bool _isBusy;

    public SessionEditViewModel(
        TimeTrackingController controller,
        TimeSelection selection,
        TimeZoneInfo timeZone)
    {
        _controller = controller;
        _timeZone = timeZone;
        ExistingSessionId = selection.ExistingSession?.Id;
        var existing = selection.ExistingSession;
        IsAutomaticExisting = existing?.Source == SessionSource.Automatic;

        if (existing is not null)
        {
            Title = Strings.Get("EditEntryTitle");
            _existingDuration = existing.Duration;

            // Show the actual tracked range rather than the overall record range,
            // which can include idle gaps after the last active segment.
            var trackedStart = existing.StartUtc;
            var trackedEnd = existing.EndUtc ?? selection.NowUtc;
            if (existing.Segments.Count > 0)
            {
                trackedStart = existing.Segments.Min(segment => segment.StartUtc);
                trackedEnd = existing.Segments
                    .Select(segment => segment.EndUtc ??
                                       (segment.StartUtc + segment.Duration))
                    .Max();
            }

            var localStart = TimeZoneInfo.ConvertTime(trackedStart, timeZone);
            var localEnd = TimeZoneInfo.ConvertTime(trackedEnd, timeZone);
            _selectedDate = localStart.Date;
            _startTimeText = localStart.ToString("HH:mm", CultureInfo.InvariantCulture);
            _endTimeText = localEnd.ToString("HH:mm", CultureInfo.InvariantCulture);
            _comment = existing.Comment;
        }
        else
        {
            Title = Strings.Get("NewEntryTitle");
            var localNow = TimeZoneInfo.ConvertTime(selection.NowUtc, timeZone);
            var endLocal = new DateTime(
                localNow.Year, localNow.Month, localNow.Day,
                localNow.Hour, localNow.Minute, 0, DateTimeKind.Unspecified);
            if (endLocal > localNow.DateTime)
            {
                endLocal = endLocal.AddMinutes(-1);
            }

            var startLocal = endLocal.AddMinutes(-30);
            if (startLocal.Date != endLocal.Date)
            {
                startLocal = endLocal.Date;
            }

            _selectedDate = endLocal.Date;
            _startTimeText = startLocal.ToString("HH:mm", CultureInfo.InvariantCulture);
            _endTimeText = endLocal.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        _initialSelectedDate = _selectedDate;
        _initialStartTimeText = _startTimeText;
        _initialEndTimeText = _endTimeText;

        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy, ShowTechnicalError);
        CancelCommand = new RelayCommand(() => CloseRequested?.Invoke(false));
        UpdateDurationPreview();
    }

    public long? ExistingSessionId { get; }
    public string Title { get; }
    public bool IsAutomaticExisting { get; }
    public string AutoConversionNote => Strings.Get("AutoEditNote");
    public string SaveButtonText => Strings.Get("SaveEntry");
    public string CancelButtonText => Strings.Get("CancelButton");

    public DateTime? SelectedDate
    {
        get => _selectedDate;
        set
        {
            if (SetProperty(ref _selectedDate, value))
            {
                UpdateDurationPreview();
            }
        }
    }

    public string StartTimeText
    {
        get => _startTimeText;
        set
        {
            if (SetProperty(ref _startTimeText, value))
            {
                UpdateDurationPreview();
            }
        }
    }

    public string EndTimeText
    {
        get => _endTimeText;
        set
        {
            if (SetProperty(ref _endTimeText, value))
            {
                UpdateDurationPreview();
            }
        }
    }

    public string Comment
    {
        get => _comment;
        set => SetProperty(ref _comment, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public string DurationPreview
    {
        get => _durationPreview;
        private set => SetProperty(ref _durationPreview, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                SaveCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public AsyncRelayCommand SaveCommand { get; }
    public RelayCommand CancelCommand { get; }

    public event Action<bool>? CloseRequested;

    private async Task SaveAsync()
    {
        ErrorMessage = string.Empty;
        if (SelectedDate is null)
        {
            ErrorMessage = Strings.Get("InvalidDate");
            return;
        }

        if (!SettingsValidator.TryParseTime(StartTimeText, out var startTime) ||
            !SettingsValidator.TryParseTime(EndTimeText, out var endTime))
        {
            ErrorMessage = Strings.Get("InvalidTimeFormat");
            return;
        }

        if (startTime == endTime && (ExistingSessionId is null || !IsInitialRangeUnchanged()))
        {
            ErrorMessage = Strings.Get("TimeEqualError");
            return;
        }

        var date = DateOnly.FromDateTime(SelectedDate.Value);
        DateTimeOffset startUtc;
        DateTimeOffset endUtc;
        try
        {
            startUtc = TimeText.LocalToUtc(date, startTime, _timeZone);
            var endDate = endTime < startTime ? date.AddDays(1) : date;
            endUtc = TimeText.LocalToUtc(endDate, endTime, _timeZone);
        }
        catch (ArgumentException)
        {
            ErrorMessage = Strings.Get("DstInvalidTime");
            return;
        }

        var timesEdited = ExistingSessionId is null || !IsInitialRangeUnchanged();
        IsBusy = true;
        try
        {
            var result = await _controller.SaveManualSessionAsync(
                ExistingSessionId,
                new ManualSessionInput(startUtc, endUtc, Comment),
                timesEdited);
            if (result.Success)
            {
                CloseRequested?.Invoke(true);
                return;
            }

            if (result.TechnicalFailure)
            {
                ErrorMessage = Strings.Get("UnknownError");
                return;
            }

            ErrorMessage = result.ValidationError switch
            {
                ManualValidationError.StartInFuture or ManualValidationError.EndInFuture =>
                    Strings.Get("EntryFuture"),
                ManualValidationError.EndNotAfterStart => Strings.Get("InvalidTimeFormat"),
                ManualValidationError.OverlapsExisting => Strings.Get("EntryOverlap"),
                ManualValidationError.ActiveSessionCannotBeEdited => Strings.Get("ActiveEntryCannotEdit"),
                _ => Strings.Get("UnknownError")
            };
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool IsInitialRangeUnchanged()
    {
        return ExistingSessionId.HasValue &&
               SelectedDate.HasValue &&
               _initialSelectedDate.HasValue &&
               SelectedDate.Value.Date == _initialSelectedDate.Value.Date &&
               string.Equals(
                   StartTimeText.Trim(),
                   _initialStartTimeText,
                   StringComparison.Ordinal) &&
               string.Equals(
                   EndTimeText.Trim(),
                   _initialEndTimeText,
                   StringComparison.Ordinal);
    }

    private void UpdateDurationPreview()
    {
        if (ExistingSessionId.HasValue && _existingDuration.HasValue && IsInitialRangeUnchanged())
        {
            DurationPreview = Strings.Format(
                "DurationFormat",
                TimeText.FormatDuration(_existingDuration.Value));
            return;
        }

        if (SelectedDate is null ||
            !SettingsValidator.TryParseTime(StartTimeText, out var startTime) ||
            !SettingsValidator.TryParseTime(EndTimeText, out var endTime) ||
            startTime == endTime)
        {
            DurationPreview = string.Empty;
            return;
        }

        var date = DateOnly.FromDateTime(SelectedDate.Value);
        DateTimeOffset start;
        DateTimeOffset end;
        try
        {
            start = TimeText.LocalToUtc(date, startTime, _timeZone);
            var endDate = endTime < startTime ? date.AddDays(1) : date;
            end = TimeText.LocalToUtc(endDate, endTime, _timeZone);
        }
        catch (ArgumentException)
        {
            DurationPreview = Strings.Get("DstInvalidTime");
            return;
        }

        var duration = end - start;
        DurationPreview = Strings.Format("DurationFormat", TimeText.FormatDuration(duration));
    }

    private void ShowTechnicalError(Exception exception)
    {
        ErrorMessage = Strings.Get("UnknownError");
    }
}

public sealed record TimeSelection(TimeSession? ExistingSession, DateTimeOffset NowUtc);
