
using ApiReviewDotNet.Services.Calendar;

using Microsoft.AspNetCore.Components;

namespace ApiReviewDotNet.Pages;

public sealed partial class Schedule
{
    private static readonly TimeZoneInfo PacificTime = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");

    [SupplyParameterFromQuery(Name = "month")]
    public string? MonthParam { get; set; }

    private DateTimeOffset Today { get; set; }
    private DateTimeOffset CurrentDate { get; set; }
    private CalendarCell[] Cells { get; set; } = Array.Empty<CalendarCell>();

    [Inject]
    public CalendarService CalendarService { get; set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        DateTimeOffset nowPacific = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, PacificTime);
        Today = new DateTimeOffset(nowPacific.Year, nowPacific.Month, nowPacific.Day, 0, 0, 0, nowPacific.Offset);

        if (!string.IsNullOrEmpty(MonthParam) &&
            DateTime.TryParseExact(MonthParam, "yyyy-MM", null, System.Globalization.DateTimeStyles.None, out DateTime parsed))
        {
            TimeSpan offset = PacificTime.GetUtcOffset(new DateTime(parsed.Year, parsed.Month, 1));
            CurrentDate = new DateTimeOffset(parsed.Year, parsed.Month, 1, 0, 0, 0, offset);
        }
        else
        {
            CurrentDate = Today;
        }

        Cells = (await CalendarService.GetCellsAsync(CurrentDate)).ToArray();
    }
}
