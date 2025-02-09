
using ApiReviewDotNet.Data;
using ApiReviewDotNet.Services.Calendar;

using Microsoft.AspNetCore.Components;

namespace ApiReviewDotNet.Pages;

public sealed partial class Schedule
{
    private DateTimeOffset? Today { get; set; }
    private DateTimeOffset? CurrentDate { get; set; }

    private CalendarCell[] Cells { get; set; } = Array.Empty<CalendarCell>();

    [Inject]
    public TimeZoneService TimeZoneService { get; set; } = null!;

    [Inject]
    public CalendarService CalendarService { get; set; } = null!;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
            await UpdateCellsAsync();
    }

    private async Task UpdateCellsAsync()
    {
        if (Today is null)
        {
            DateTimeOffset userDateTime = await TimeZoneService.ToLocalAsync(DateTime.UtcNow);
            Today = new DateTimeOffset(userDateTime.Year, userDateTime.Month, userDateTime.Day, 0, 0, 0, 0, userDateTime.Offset);
        }

        if (CurrentDate is null)
            CurrentDate = Today;

        Cells = (await CalendarService.GetCellsAsync(CurrentDate.Value)).ToArray();
        StateHasChanged();
    }

    private async Task TodayAsync()
    {
        CurrentDate = null;
        await UpdateCellsAsync();
    }

    private async Task PreviousMonthAsync()
    {
        if (CurrentDate is not null)
        {
            CurrentDate = CurrentDate.Value.AddMonths(-1);
            await UpdateCellsAsync();
        }
    }

    private async Task NextMonthAsync()
    {
        if (CurrentDate is not null)
        {
            CurrentDate = CurrentDate.Value.AddMonths(1);
            await UpdateCellsAsync();
        }
    }
}
