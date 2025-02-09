using ApiReviewDotNet.Data;

using GitHubJwt;

using Octokit;

namespace ApiReviewDotNet.Services.GitHub;

public sealed class GitHubClientFactory
{
    private readonly IConfiguration _configuration;

    public GitHubClientFactory(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<GitHubClient> CreateForAppAsync()
    {
        // See: https://octokitnet.readthedocs.io/en/latest/github-apps/ for details.

        int appId = Convert.ToInt32(_configuration["GitHubAppId"]);
        string privateKey = _configuration["GitHubAppPrivateKey"]!;

        PlainStringPrivateKeySource privateKeySource = new PlainStringPrivateKeySource(privateKey);
        GitHubJwtFactory generator = new GitHubJwtFactory(
            privateKeySource,
            new GitHubJwtFactoryOptions
            {
                AppIntegrationId = appId,
                ExpirationSeconds = 8 * 60 // 600 is apparently too high
            });
        string? token = generator.CreateEncodedJwtToken();

        GitHubClient client = CreateForToken(token, AuthenticationType.Bearer);

        IReadOnlyList<Installation>? installations = await client.GitHubApps.GetAllInstallationsForCurrent();
        Installation installation = installations.Single();
        AccessToken? installationTokenResult = await client.GitHubApps.CreateInstallationToken(installation.Id);

        return CreateForToken(installationTokenResult.Token, AuthenticationType.Oauth);
    }

    private static GitHubClient CreateForToken(string token, AuthenticationType authenticationType)
    {
        ProductHeaderValue productInformation = new ProductHeaderValue(ApiReviewConstants.ProductName);
        GitHubClient client = new GitHubClient(productInformation)
        {
            Credentials = new Credentials(token, authenticationType)
        };
        return client;
    }

    public sealed class PlainStringPrivateKeySource : IPrivateKeySource
    {
        private readonly string _key;

        public PlainStringPrivateKeySource(string key)
        {
            _key = key;
        }

        public TextReader GetPrivateKeyReader()
        {
            return new StringReader(_key);
        }
    }
}
