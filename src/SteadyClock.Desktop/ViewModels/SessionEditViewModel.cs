using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyTimeTracker.Core.Common;
using MyTimeTracker.Core.Models;
using SteadyClock.Desktop.Resources;
using SteadyClock.Desktop.Services;

namespace SteadyClock.Desktop.ViewModels;

public partial class SessionEditViewModel : ObservableObject
{
    private readonly TimeTrackingController _controller;
    private readonly TimeZoneInfo _timeZone;
    private readonly TimeSpan? _existingDuration;
    private readonly string _initialDateText = string.Empty;
    private readonly string _initialStartText = string.Empty;
    private readonly string _initialEndText = string.Empty;

    [ObservableProperty]
    private string _dateText = string.Empty;

    [ObservableProperty]
    private string _startTimeText = string.Empty;

    [ObservableProperty]
    private string _endTimeText = string.Empty;

    [ObservableProperty]
    private string _comment = string.Empty;

    [ObservableProperty]
    private string _durationPreview = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    public SessionEditViewModel(
        TimeTrackingController controller,
        TimeZoneInfo timeZone,
        TimeSession? existing,
        DateTimeOffset nowUtc)
    {
        _controller = controller;
        _timeZone = timeZone;
        ExistingSessionId = existing?.Id;
        IsAutomaticExisting = existing?.Source == SessionSource.Automatic;
        Title = existing is null ? Strings.Get("NewEntryTitle") : Strings.Get("EditEntryTitle");

        if (existing is not null)
        {
            _existingDuration = existing.Duration;
            var start = existing.StartUtc;
            var end = existing.EndUtc ?? nowUtc;
            if (existing.Segments.Count > 0)
            {
                start = existing.Segments.Min(segment => segment.StartUtc);
                end = existing.Segments
                    .Select(segment => segment.EndUtc ??
                                       (segment.StartUtc + segment.Duration))
                    .Max();
            }

            var localStart = TimeZoneInfo.ConvertTime(start, timeZone);
            var localEnd = TimeZoneInfo.ConvertTime(end, timeZone);
            DateText = localStart.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
            StartTimeText = localStart.ToString("HH:mm", CultureInfo.InvariantCulture);
            EndTimeText = localEnd.ToString("HH:mm", CultureInfo.InvariantCulture);
            Comment = existing.Comment;
        }
        else
        {
            var localNow = TimeZoneInfo.ConvertTime(nowUtc, timeZone);
            var endLocal = new DateTime(
                localNow.Year,
                localNow.Month,
                localNow.Day,
                localNow.Hour,
                localNow.Minute,
                0,
                DateTimeKind.Unspecified);
            if (endLocal > localNow.DateTime)
            {
                endLocal = endLocal.AddMinutes(-1);
            }

            var startLocal = endLocal.AddMinutes(-30);
            if (startLocal.Date != endLocal.Date)
            {
                startLocal = endLocal.Date;
            }

            DateText = endLocal.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
            StartTimeText = startLocal.ToString("HH:mm", CultureInfo.InvariantCulture);
            EndTimeText = endLocal.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        _initialDateText = DateText;
        _initialStartText = StartTimeText;
        _initialEndText = EndTimeText;
        UpdatePreview();
    }

    public long? ExistingSessionId { get; }
    public string Title { get; }
    public bool IsAutomaticExisting { get; }
    public string AutoConversionNote => Strings.Get("AutoEditNote");
    public string SaveButtonText => Strings.Get("SaveEntry");
    public string CancelButtonText => Strings.Get("CancelButton");
    public string ExistingStatusLabel => IsAutomaticExisting
        ? Strings.Get("ManualBadge")
        : string.Empty;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    partial void OnDateTextChanged(string value) => UpdatePreview();
    partial void OnStartTimeTextChanged(string value) => UpdatePreview();
    partial void OnEndTimeTextChanged(string value) => UpdatePreview();
    partial void OnErrorMessageChanged(string value) => OnPropertyChanged(nameof(HasError));

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy)
        {
            return;
        }

        ErrorMessage = string.Empty;
        if (!TryParseDate(DateText, out var date))
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

        var unchanged = IsInitialRangeUnchanged();
        if (startTime == endTime && !unchanged)
        {
            ErrorMessage = Strings.Get("TimeEqualError");
            return;
        }

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

        IsBusy = true;
        try
        {
            var timesEdited = ExistingSessionId is null || !unchanged;
            var result = await _controller.SaveManualSessionAsync(
                ExistingSessionId,
                new ManualSessionInput(startUtc, endUtc, Comment),
                timesEdited);
            if (result.Success)
            {
                CloseRequested?.Invoke(true);
                return;
            }

            ErrorMessage = result.TechnicalFailure
                ? Strings.Get("UnknownError")
                : result.ValidationError switch
                {
                    ManualValidationError.StartInFuture or ManualValidationError.EndInFuture =>
                        Strings.Get("EntryFuture"),
                    ManualValidationError.EndNotAfterStart => Strings.Get("TimeEqualError"),
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

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);

    public event Action<bool>? CloseRequested;

    private bool IsInitialRangeUnchanged()
    {
        return ExistingSessionId.HasValue &&
               string.Equals(DateText.Trim(), _initialDateText, StringComparison.Ordinal) &&
               string.Equals(StartTimeText.Trim(), _initialStartText, StringComparison.Ordinal) &&
               string.Equals(EndTimeText.Trim(), _initialEndText, StringComparison.Ordinal);
    }

    private void UpdatePreview()
    {
        if (ExistingSessionId.HasValue && _existingDuration.HasValue && IsInitialRangeUnchanged())
        {
            DurationPreview = Strings.Format(
                "DurationFormat",
                TimeText.FormatDuration(_existingDuration.Value));
            return;
        }

        if (!TryParseDate(DateText, out var date) ||
            !SettingsValidator.TryParseTime(StartTimeText, out var startTime) ||
            !SettingsValidator.TryParseTime(EndTimeText, out var endTime) ||
            startTime == endTime)
        {
            DurationPreview = string.Empty;
            return;
        }

        try
        {
            var start = TimeText.LocalToUtc(date, startTime, _timeZone);
            var endDate = endTime < startTime ? date.AddDays(1) : date;
            var end = TimeText.LocalToUtc(endDate, endTime, _timeZone);
            DurationPreview = Strings.Format(
                "DurationFormat",
                TimeText.FormatDuration(end - start));
        }
        catch (ArgumentException)
        {
            DurationPreview = Strings.Get("DstInvalidTime");
        }
    }

    private static bool TryParseDate(string text, out DateOnly date) =>
        DateOnly.TryParseExact(
            text.Trim(),
            "dd.MM.yyyy",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);
}
