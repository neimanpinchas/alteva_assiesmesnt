namespace AltevaCrawler.Shared.Storage;

using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading.Tasks;

public class MockS3StorageService : IStorageService
{
    private readonly ConcurrentDictionary<string, byte[]> _s3Bucket = new();
    private readonly string _bucketName;

    public MockS3StorageService(string bucketName = "alteva-crawler-bucket")
    {
        _bucketName = bucketName;
    }

    public Task<string> SavePageAsync(string domain, string rawUrl, byte[] content)
    {
        string s3ObjectKey = $"s3://{_bucketName}/{domain}/{Guid.NewGuid():N}.html";
        _s3Bucket[s3ObjectKey] = content;
        return Task.FromResult(s3ObjectKey);
    }

    public Task<string?> ReadPageAsync(string storagePath)
    {
        if (_s3Bucket.TryGetValue(storagePath, out var bytes))
        {
            return Task.FromResult<string?>(Encoding.UTF8.GetString(bytes));
        }
        return Task.FromResult<string?>(null);
    }

    public Task DeletePageAsync(string storagePath)
    {
        _s3Bucket.TryRemove(storagePath, out _);
        return Task.CompletedTask;
    }
}
