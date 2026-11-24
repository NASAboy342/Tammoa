using Newtonsoft.Json.Linq;
using Tammoa.Enums;

namespace Tammoa.Models;

/// <summary>Small app-wide choices, kept in settings.json in the data folder.</summary>
public record AppSettings(bool CheckUpdatesAtStart);

/// <summary>Update: only the fields that are sent are changed.</summary>
public record SaveSettingsRequest(bool? CheckUpdatesAtStart);

/// <summary>Answer of the update check. <see cref="Error"/> is a readable reason when the check could not be done (offline, no release yet…); it never blocks the app.</summary>
public record UpdateInfo(string CurrentVersion, string? LatestVersion, bool UpdateAvailable, string? Url, string? Error);

// ---- backup of everything the user made (no keys, no run history, no agent token)
/// <summary>One flow of a backup. The id is kept, so restoring the same file twice does not make copies.</summary>
public record BackupFlow(string Id, string Name, string Description, JObject Definition, JObject? Data);

/// <summary>A connection WITHOUT its key: after a restore the user types the key again.</summary>
public record BackupConnection(string Id, string Name, ConnectionProvider Provider, string BaseUrl, bool HasKey = false);

public record BackupDocument(string? Format, int Version, DateTimeOffset CreatedAt, string? AppVersion,
    List<BackupFlow>? Flows, List<BackupConnection>? Connections, JObject? SharedData);

/// <summary>A backup file in the backups folder.</summary>
public record BackupFileInfo(string FileName, string Path, long Size, DateTimeOffset CreatedAt, int Flows, int Connections);

public record RestoreFileRequest(string? FileName);

/// <summary>What a restore did. Nothing is ever overwritten: what already exists is skipped.</summary>
public record RestoreResult(int FlowsAdded, int FlowsSkipped, int ConnectionsAdded, int ConnectionsSkipped, int SharedTextsAdded,
    IReadOnlyList<string> ConnectionsNeedingKey, IReadOnlyList<string> Problems);

// ---- flow templates
public record FlowTemplateInfo(string Id, string Name, string Description, string Icon, bool NeedsAi, IReadOnlyList<string> Needs, int CardCount);

/// <summary>For templates with an AI card: which connection (and model) to fill in. Both optional: the card then asks for them.</summary>
public record CreateFromTemplateRequest(string? ConnectionId, string? Model);
