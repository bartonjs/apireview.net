using Microsoft.JSInterop;

namespace ApiReviewDotNet.Data;

public sealed class TimeZoneService
{
    private readonly IJSRuntime _jsRuntime;

    private TimeSpan? _userOffset;

    public TimeZoneService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public async ValueTask<DateTimeOffset> ToLocalAsync(DateTimeOffset dateTime)
    {
        if (_userOffset is null)
        {
            int offsetInMinutes = await _jsRuntime.InvokeAsync<int>("getTimezoneOffset");
            _userOffset = TimeSpan.FromMinutes(-offsetInMinutes);
        }

        return dateTime.ToOffset(_userOffset.Value);
    }
}
