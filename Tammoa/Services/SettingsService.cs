using Tammoa.Models;
using Tammoa.Repositories;

namespace Tammoa.Services;

public class SettingsService : ISettingsService
{
    private readonly ISettingsRepository _repository;

    public SettingsService(ISettingsRepository repository) => _repository = repository;

    public AppSettings Get() => _repository.Load() ?? new AppSettings(false);

    public AppSettings Update(SaveSettingsRequest request)
    {
        var current = Get();
        var updated = current with { CheckUpdatesAtStart = request.CheckUpdatesAtStart ?? current.CheckUpdatesAtStart };
        _repository.Save(updated);
        return updated;
    }
}
