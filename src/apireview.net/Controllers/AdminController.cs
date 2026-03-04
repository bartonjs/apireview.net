using ApiReviewDotNet.Data;
using ApiReviewDotNet.Services;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApiReviewDotNet.Controllers;

public class AdminController : Controller
{
    [HttpPost("/admin/force-refresh")]
    [Authorize(Roles = ApiReviewConstants.ApiApproverRole)]
    public async Task<IActionResult> ForceRefresh([FromServices] IssueService issueService)
    {
        await issueService.ReloadAsync();
        return Redirect("/");
    }
}
