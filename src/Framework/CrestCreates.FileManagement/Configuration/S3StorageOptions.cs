using System;

namespace CrestCreates.FileManagement.Configuration;

public class S3StorageOptions
{
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string Region { get; set; } = "us-east-1";
    public string BucketName { get; set; } = string.Empty;
    public string BucketPrefix { get; set; } = "files";
    public string ServiceUrl { get; set; } = string.Empty;
    public TimeSpan DefaultPresignedExpiry { get; set; } = TimeSpan.FromHours(1);
    public bool ForcePathStyle { get; set; } = false;
}