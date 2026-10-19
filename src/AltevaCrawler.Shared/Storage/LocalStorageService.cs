namespace AltevaCrawler.Shared.Storage;

using System;
using System.IO;
using System.Threading.Tasks;
using AltevaCrawler.Shared.Utilities;

public class LocalStorageService : IStorageService
{
    private readonly string _storageRoot;

    public LocalStorageService(string? storageRoot = null)
    {
        _storageRoot = storageRoot ?? Path.Combine(Directory.GetCurrentDirectory(), "storage");
        Directory.CreateDirectory(_storageRoot);
    }

    public async Task<string> SavePageAsync(string domain, string rawUrl, byte[] content)
    {
        Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri);
        string relativePath = StoragePathBuilder.ToRelativePath(uri ?? new Uri($"https://{domain}"));
        string fullPath = Path.Combine(_storageRoot, relativePath);

        string? parentDir = Path.GetDirectoryName(fullPath);
        if (parentDir != null) Directory.CreateDirectory(parentDir);

        await File.WriteAllBytesAsync(fullPath, content);
        return relativePath;
    }

    public async Task<string?> ReadPageAsync(string storagePath)
    {
        if (string.IsNullOrEmpty(storagePath)) return null;

        string fullPath = Path.Combine(_storageRoot, storagePath);
        if (!StoragePathBuilder.IsUnderRoot(fullPath, _storageRoot) || !File.Exists(fullPath))
        {
            return null;
        }

        return await File.ReadAllTextAsync(fullPath);
    }

    public Task DeletePageAsync(string storagePath)
    {
        if (string.IsNullOrEmpty(storagePath)) return Task.CompletedTask;

        string fullPath = Path.Combine(_storageRoot, storagePath);
        if (StoragePathBuilder.IsUnderRoot(fullPath, _storageRoot) && File.Exists(fullPath))
        {
            try { File.Delete(fullPath); } catch { }
        }

        return Task.CompletedTask;
    }
}
