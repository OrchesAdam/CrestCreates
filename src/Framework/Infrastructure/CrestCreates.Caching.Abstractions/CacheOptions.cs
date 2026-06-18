using System;

namespace CrestCreates.Caching.Abstractions;

public class CacheOptions
{
    public TimeSpan DefaultExpiration { get; set; } = TimeSpan.FromMinutes(30);
    public int MaximumSize { get; set; } = 10000;
    public bool Enabled { get; set; } = true;
    public string? Prefix { get; set; } = "Crest:";
}