using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Tammoa.Helper;
using Tammoa.Models;
using Tammoa.Services;

namespace Tammoa.Api;

/// <summary>Settings, update check, backup and flow templates: what the Settings page and the first-run welcome use.</summary>
public static class AppSupportEndpoints
{
    public static void MapAppSupportEndpoints(this WebApplication app, ISettingsService settings, IUpdateService updates, IBackupService backup, IFlowTemplateService templates)
    {
        app.MapGet("/api/settings", () => EndpointHelpers.TrySync(() => JsonHelper.Ok(settings.Get())));
        app.MapPut("/api/settings", (HttpRequest req) => EndpointHelpers.Try(async () =>
        {
            var body = await JsonHelper.ReadBodyAsync<SaveSettingsRequest>(req) ?? throw new ArgumentException("A request body is required.");
            return JsonHelper.Ok(settings.Update(body));
        }));

        app.MapGet("/api/app/update", (CancellationToken ct) => EndpointHelpers.Try(async () => JsonHelper.Ok(await updates.CheckAsync(ct))));

        app.MapGet("/api/backup", () => EndpointHelpers.TrySync(() => JsonHelper.Ok(new { folder = backup.Folder, files = backup.List() })));
        app.MapPost("/api/backup", () => EndpointHelpers.TrySync(() => JsonHelper.Ok(backup.Create(), StatusCodes.Status201Created)));
        app.MapPost("/api/backup/restore", (HttpRequest req) => EndpointHelpers.Try(async () =>
        {
            BackupDocument? body;
            try { body = await JsonHelper.ReadBodyAsync<BackupDocument>(req); }
            catch (Newtonsoft.Json.JsonException) { throw new ArgumentException("That file is not a Tammoa backup (it could not be read)."); }
            return JsonHelper.Ok(backup.Restore(body ?? throw new ArgumentException("A backup file is required.")));
        }));
        app.MapPost("/api/backup/restore-file", (HttpRequest req) => EndpointHelpers.Try(async () =>
        {
            var body = await JsonHelper.ReadBodyAsync<RestoreFileRequest>(req) ?? throw new ArgumentException("A request body is required.");
            return JsonHelper.Ok(backup.RestoreFile(body.FileName));
        }));
        app.MapPost("/api/backup/open-folder", () => EndpointHelpers.TrySync(() =>
        {
            backup.OpenFolder();
            return Results.NoContent();
        }));

        app.MapGet("/api/templates", () => EndpointHelpers.TrySync(() => JsonHelper.Ok(templates.List())));
        app.MapPost("/api/templates/{id}/create", (string id, HttpRequest req) => EndpointHelpers.Try(async () =>
        {
            var body = await JsonHelper.ReadBodyAsync<CreateFromTemplateRequest>(req) ?? new CreateFromTemplateRequest(null, null);
            return JsonHelper.Ok(templates.Create(id, body), StatusCodes.Status201Created);
        }));
    }
}
