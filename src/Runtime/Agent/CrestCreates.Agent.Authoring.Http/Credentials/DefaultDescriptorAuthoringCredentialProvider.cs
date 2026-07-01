using Microsoft.Extensions.Configuration;

namespace CrestCreates.Agent.Authoring.Http.Credentials;

public sealed class DefaultDescriptorAuthoringCredentialProvider : IDescriptorAuthoringCredentialProvider
{
    private readonly IConfiguration _configuration;

    public DefaultDescriptorAuthoringCredentialProvider(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public Task<string> GetApiKeyAsync(string credentialReference, CancellationToken cancellationToken = default)
    {
        var apiKey = _configuration[credentialReference];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException($"API key not found for credential reference: {credentialReference}");
        }
        return Task.FromResult(apiKey);
    }
}
