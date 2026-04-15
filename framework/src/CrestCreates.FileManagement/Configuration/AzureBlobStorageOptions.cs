namespace CrestCreates.FileManagement.Configuration;

public class AzureBlobStorageOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string ContainerPrefix { get; set; } = "files";
    public string? StaticWebHost { get; set; }
    public TimeSpan DefaultPresignedExpiry { get; set; } = TimeSpan.FromHours(1);
}
