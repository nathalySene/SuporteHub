using SuporteHub.API.Models;

namespace SuporteHub.API.Storage;

public interface IAppStore
{
    string ProviderName { get; }
    bool IsLocal { get; }
    Task InitializeAsync();
    Task<bool> NeedsInitialAgent();
    Task Register(Conta account);
    Task<(T Value, int Version)?> Get<T>(string pk, string sk);
    Task<List<T>> Query<T>(string pk);
    Task Put<T>(string pk, string sk, T value, int? previousVersion = null);
    Task<int> NextId();
}

public sealed class StorageConflictException(string message, Exception? inner = null) : Exception(message, inner);
