using System.Security.Claims;

using ApiReviewDotNet;
using ApiReviewDotNet.Data;
using ApiReviewDotNet.Services;
using ApiReviewDotNet.Services.Calendar;
using ApiReviewDotNet.Services.GitHub;
using ApiReviewDotNet.Services.Ospo;
using ApiReviewDotNet.Services.YouTube;

using AspNet.Security.OAuth.GitHub;
using Azure.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Octokit.Webhooks;
using Octokit.Webhooks.AspNetCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddSingleton<IssueService>();
builder.Services.AddSingleton<GitHubClientFactory>();
builder.Services.AddSingleton<YouTubeServiceFactory>();
builder.Services.AddSingleton<AreaOwnerService>();
builder.Services.AddSingleton<OspoService>();
builder.Services.AddSingleton<RepositoryGroupService>();
builder.Services.AddSingleton<WebhookEventProcessor, GitHubEventProcessor>();
builder.Services.AddSingleton<CalendarService>();
builder.Services.AddSingleton<YouTubeManager>();
builder.Services.AddSingleton<GitHubMembershipService>();
builder.Services.AddSingleton<GitHubManager>();
builder.Services.AddSingleton<GitHubTeamService>();
builder.Services.AddHostedService<RefreshService>();

//builder.Configuration.AddAzureKeyVault(
//    new Uri($"https://{builder.Configuration["KeyVaultName"]}.vault.azure.net/"),
//    new DefaultAzureCredential());

builder.Services.AddSingleton<SummaryManager>();
builder.Services.AddSingleton<SummaryPublishingService>();

builder.Services.AddScoped<NotesService>();
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(options =>
{
    options.LoginPath = "/signin";
    options.LogoutPath = "/signout";
})
.AddGitHub(options =>
{
    options.ClientId = builder.Configuration["GitHubClientId"]!;
    options.ClientSecret = builder.Configuration["GitHubClientSecret"]!;
    options.ClaimActions.MapJsonKey(ApiReviewConstants.GitHubAvatarUrl, ApiReviewConstants.GitHubAvatarUrl);
    options.Events.OnCreatingTicket = async context =>
    {
        RepositoryGroupService groupService = context.HttpContext.RequestServices.GetRequiredService<RepositoryGroupService>();
        GitHubMembershipService membershipService = context.HttpContext.RequestServices.GetRequiredService<GitHubMembershipService>();

        string? accessToken = context.AccessToken;
        string orgName = ApiReviewConstants.ApiApproverOrgName;
        IReadOnlyList<string> teamSlugs = groupService.ApproverTeamSlugs;
        if (accessToken is not null && context.Identity?.Name is not null)
        {
            string userName = context.Identity.Name;
            bool isMember = await membershipService.IsMemberOfAnyTeamAsync(accessToken, orgName, teamSlugs, userName);
            if (isMember)
                context.Identity.AddClaim(new Claim(context.Identity.RoleClaimType, ApiReviewConstants.ApiApproverRole));

            context.Identity.AddClaim(new Claim(ApiReviewConstants.TokenClaim, accessToken));
        }
    };
});
builder.Services.AddHttpClient();

WebApplication app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHostRedirection("apireview.azurewebsites.net", "apireview.net");

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapGet("/signin", (string? returnUrl) =>
    Results.Challenge(
        new AuthenticationProperties { RedirectUri = "/" + returnUrl },
        [GitHubAuthenticationDefaults.AuthenticationScheme]));

app.MapGet("/signout", () =>
    Results.SignOut(
        new AuthenticationProperties { RedirectUri = "/" },
        [CookieAuthenticationDefaults.AuthenticationScheme]));

app.MapPost("/signout", () =>
    Results.SignOut(
        new AuthenticationProperties { RedirectUri = "/" },
        [CookieAuthenticationDefaults.AuthenticationScheme]));

app.MapPost("/admin/force-refresh",
    [Authorize(Roles = ApiReviewConstants.ApiApproverRole)]
    [RequireAntiforgeryToken]
    async (IssueService issueService) =>
    {
        await issueService.ReloadAsync();
        return Results.Redirect("/");
    });

app.MapGitHubWebhooks();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
