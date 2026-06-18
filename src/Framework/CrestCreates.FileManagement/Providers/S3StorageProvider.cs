using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.FileManagement.Configuration;
using CrestCreates.FileManagement.Models;
using Microsoft.Extensions.Logging;

namespace CrestCreates.FileManagement.Providers;

public class S3StorageProvider : ICloudStorageProvider
{
    private readonly S3StorageOptions _options;
    private readonly ILogger<S3StorageProvider> _logger;
    private readonly HttpClient _httpClient;

    public string ProviderName => "AmazonS3";

    public S3StorageProvider(S3StorageOptions options, ILogger<S3StorageProvider> logger)
    {
        _options = options;
        _logger = logger;
        _httpClient = new HttpClient();
    }

    public async Task<string> UploadAsync(Stream stream, FileEntity entity, CancellationToken ct = default)
    {
        var storageKey = entity.Key.ToStorageKey();
        var objectKey = GetObjectKey(storageKey);
        var url = GetRequestUrl(objectKey);

        using var content = new StreamContent(stream);
        content.Headers.Add("Content-Type", entity.ContentType);

        var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = content };
        await SignRequestAsync(request, "PUT", objectKey, entity.ContentType);
        await SendRequestAsync(request, ct);

        return storageKey;
    }

    public async Task<Stream> DownloadAsync(string storageKey, CancellationToken ct = default)
    {
        var objectKey = GetObjectKey(storageKey);
        var url = GetRequestUrl(objectKey);

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        await SignRequestAsync(request, "GET", objectKey);

        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStreamAsync(ct);
    }

    public async Task DeleteAsync(string storageKey, CancellationToken ct = default)
    {
        var objectKey = GetObjectKey(storageKey);
        var url = GetRequestUrl(objectKey);

        var request = new HttpRequestMessage(HttpMethod.Delete, url);
        await SignRequestAsync(request, "DELETE", objectKey);

        await SendRequestAsync(request, ct);
    }

    public async Task<bool> ExistsAsync(string storageKey, CancellationToken ct = default)
    {
        var objectKey = GetObjectKey(storageKey);
        var url = GetRequestUrl(objectKey);

        var request = new HttpRequestMessage(HttpMethod.Head, url);
        await SignRequestAsync(request, "HEAD", objectKey);

        try
        {
            var response = await _httpClient.SendAsync(request, ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<IStorageMetadata> GetMetadataAsync(string storageKey, CancellationToken ct = default)
    {
        var objectKey = GetObjectKey(storageKey);
        var url = GetRequestUrl(objectKey);

        var request = new HttpRequestMessage(HttpMethod.Head, url);
        await SignRequestAsync(request, "HEAD", objectKey);

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        return new LocalStorageMetadata
        {
            Size = response.Content.Headers.ContentLength ?? 0,
            LastModified = response.Content.Headers.LastModified ?? DateTimeOffset.UtcNow,
            ContentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream"
        };
    }

    public string GeneratePresignedUrl(string storageKey, TimeSpan expiry)
    {
        var objectKey = GetObjectKey(storageKey);
        var host = _options.ForcePathStyle
            ? $"{GetServiceHost()}/{_options.BucketName}"
            : $"{_options.BucketName}.{GetServiceHost()}";

        var expires = ((int)DateTimeOffset.UtcNow.Add(expiry).ToUnixTimeSeconds()).ToString();
        var stringToSign = $"GET\n\n\n{expires}\n/{_options.BucketName}/{objectKey}";

        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(_options.SecretKey));
        var signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign)));

        var protocol = string.IsNullOrEmpty(_options.ServiceUrl) || _options.ServiceUrl.StartsWith("https") ? "https" : "http";
        return $"{protocol}://{host}/{objectKey}?AWSAccessKeyId={Uri.EscapeDataString(_options.AccessKey)}&Expires={expires}&Signature={Uri.EscapeDataString(signature)}";
    }

    private string GetObjectKey(string storageKey)
    {
        return string.IsNullOrEmpty(_options.BucketPrefix) ? storageKey : $"{_options.BucketPrefix}/{storageKey}";
    }

    private string GetRequestUrl(string objectKey)
    {
        var host = _options.ForcePathStyle
            ? $"{GetServiceHost()}/{_options.BucketName}"
            : $"{_options.BucketName}.{GetServiceHost()}";

        var protocol = string.IsNullOrEmpty(_options.ServiceUrl) || _options.ServiceUrl.StartsWith("https") ? "https" : "http";
        return $"{protocol}://{host}/{objectKey}";
    }

    private string GetServiceHost()
    {
        if (!string.IsNullOrEmpty(_options.ServiceUrl))
        {
            var uri = new Uri(_options.ServiceUrl);
            return uri.Host;
        }

        return $"s3.{_options.Region}.amazonaws.com";
    }

    private Task SignRequestAsync(HttpRequestMessage request, string method, string objectKey, string? contentType = null)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ");
        var date = DateTime.UtcNow.ToString("yyyyMMdd");

        request.Headers.Add("Host", request.RequestUri!.Host);
        request.Headers.Add("x-amz-date", timestamp);
        request.Headers.Add("x-amz-content-sha256", "UNSIGNED-PAYLOAD");

        string canonicalHeaders;
        string signedHeaders;

        if (contentType != null)
        {
            canonicalHeaders = "content-type;host;x-amz-content-sha256;x-amz-date";
            signedHeaders = "content-type;host;x-amz-content-sha256;x-amz-date";
        }
        else
        {
            canonicalHeaders = "host;x-amz-content-sha256;x-amz-date";
            signedHeaders = "host;x-amz-content-sha256;x-amz-date";
        }

        var canonicalRequest = $"{method}\n/{_options.BucketName}/{objectKey}\n\n{canonicalHeaders}\n\n{signedHeaders}\nUNSIGNED-PAYLOAD";

        using var sha256 = SHA256.Create();
        var canonicalRequestHash = Convert.ToHexString(sha256.ComputeHash(Encoding.UTF8.GetBytes(canonicalRequest))).ToLowerInvariant();

        var stringToSign = $"AWS4-HMAC-SHA256\n{timestamp}\n{date}/{_options.Region}/s3/aws4_request\n{canonicalRequestHash}";
        var signingKey = GetSigningKey(date);

        using var hmac = new HMACSHA256(signingKey);
        var signature = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign))).ToLowerInvariant();

        var credential = $"{_options.AccessKey}/{date}/{_options.Region}/s3/aws4_request";
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("AWS4-HMAC-SHA256", $"Credential={credential}, SignedHeaders={signedHeaders}, Signature={signature}");

        return Task.CompletedTask;
    }

    private byte[] GetSigningKey(string date)
    {
        byte[] key = Encoding.UTF8.GetBytes($"AWS4{_options.SecretKey}");
        using var hmacDate = new HMACSHA256(key);
        var dateKey = hmacDate.ComputeHash(Encoding.UTF8.GetBytes(date));

        using var hmacRegion = new HMACSHA256(dateKey);
        var regionKey = hmacRegion.ComputeHash(Encoding.UTF8.GetBytes(_options.Region));

        using var hmacService = new HMACSHA256(regionKey);
        var serviceKey = hmacService.ComputeHash(Encoding.UTF8.GetBytes("s3"));

        using var hmacRequest = new HMACSHA256(serviceKey);
        return hmacRequest.ComputeHash(Encoding.UTF8.GetBytes("aws4_request"));
    }

    private async Task SendRequestAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }
}
