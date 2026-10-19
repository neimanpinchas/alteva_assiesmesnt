namespace AltevaCrawler.Shared.Storage;

using System.Threading.Tasks;

public interface IStorageService
{
    Task<string> SavePageAsync(string domain, string rawUrl, byte[] content);
    Task<string?> ReadPageAsync(string storagePath);
    Task DeletePageAsync(string storagePath);
}
