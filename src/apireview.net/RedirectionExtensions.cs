using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Net.Http.Headers;

namespace ApiReviewDotNet;

public static class RedirectionExtensions
{
    public static IApplicationBuilder UseHostRedirection(this IApplicationBuilder app, string oldHost, string newHost)
    {
        return app.Use(async (context, next) =>
        {
            Uri url = new Uri(context.Request.GetEncodedUrl());
            if (url.Host.Equals(oldHost, StringComparison.OrdinalIgnoreCase))
            {
                HttpResponse response = context.Response;
                response.StatusCode = StatusCodes.Status301MovedPermanently;

                string newUrl = new UriBuilder(url)
                {
                    Host = newHost
                }.ToString();

                response.Headers[HeaderNames.Location] = newUrl;
                return;
            }

            await next();
        });
    }
}
