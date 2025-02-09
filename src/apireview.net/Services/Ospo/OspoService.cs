namespace ApiReviewDotNet.Services.Ospo;

public sealed class OspoService
{
    private readonly ILogger<OspoService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;

    public OspoService(ILogger<OspoService> logger,
                       IConfiguration configuration,
                       IWebHostEnvironment environment)
    {
        _logger = logger;
        _configuration = configuration;
        _environment = environment;
    }

    public async Task ReloadAsync()
    {
        if (_environment.IsDevelopment())
            return;

        try
        {
            OspoClient client = new OspoClient(_configuration["OspoToken"]!);
            LinkSet = await client.GetAllAsync();
            _logger.LogInformation("Loaded {count} OSPO links", LinkSet.Links.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading OSPO links");
        }
    }

    public OspoLinkSet LinkSet { get; private set; } = OspoLinkSet.Empty;
}
