namespace CrestCreates.Sample.AssetManagement.Host.ProductionRelease;

internal static class AssetMesRegistration
{
    internal static void AddAssetMes(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<AssetMesClient>();
        services.AddHttpClient(AssetMesClient.ClientName, client =>
        {
            if (!Uri.TryCreate(configuration[AssetMesClient.AddressSetting], UriKind.Absolute, out var address) ||
                (address.Scheme != Uri.UriSchemeHttps && !(address.Scheme == Uri.UriSchemeHttp && address.IsLoopback)) ||
                address.AbsolutePath != "/" || !string.IsNullOrEmpty(address.UserInfo) ||
                !string.IsNullOrEmpty(address.Query) || !string.IsNullOrEmpty(address.Fragment))
            {
                throw new InvalidOperationException("AssetManagement:Mes:BaseAddress must be an HTTPS origin or a loopback HTTP origin.");
            }

            client.BaseAddress = address;
            client.Timeout = TimeSpan.FromSeconds(15);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
    }
}
