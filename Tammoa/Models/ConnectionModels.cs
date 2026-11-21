using Tammoa.Enums;

namespace Tammoa.Models;

/// <summary>A saved "address + key" for an AI service. Internal: carries the secret key. Never sent to the UI.</summary>
public record Connection(string Id, string Name, ConnectionProvider Provider, string BaseUrl, string? ApiKey, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

/// <summary>What the UI sees: everything except the key (only whether one is saved).</summary>
public record ConnectionInfo(string Id, string Name, ConnectionProvider Provider, string BaseUrl, bool HasKey, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

/// <summary>Create / update. On update, ApiKey null keeps the saved key, a non-empty value replaces it, ClearApiKey removes it.</summary>
public record SaveConnectionRequest(string? Name, ConnectionProvider? Provider, string? BaseUrl, string? ApiKey, bool ClearApiKey = false);

public record ConnectionTestResult(bool Ok, string? Version, IReadOnlyList<string> Models, string? Error);

public record ProviderInfo(ConnectionProvider Provider, string Name, string DefaultBaseUrl, bool KeyRequired, string KeyHelp);
