namespace CrestCreates.Agent.Authoring.Http.Credentials;

public interface IDescriptorAuthoringCredentialProvider
{
    Task<string> GetApiKeyAsync(string credentialReference, CancellationToken cancellationToken = default);
}
