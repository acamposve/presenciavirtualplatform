using PresenciaVirtual.Modules.Core.Security;

namespace PresenciaVirtual.Modules.Restaurant.Settings.UpdateRestaurantSettings;

public sealed class UpdateRestaurantSettingsHandler(IRestaurantSettingsRepository settingsRepository, ICurrentUserContext currentUser)
{
    public async Task<UpdateRestaurantSettingsResult> HandleAsync(UpdateRestaurantSettingsCommand command, CancellationToken cancellationToken = default)
    {
        var settings = RestaurantSettings.Configure(currentUser.TenantId, command.MaxAlcoholicItemQuantityPerLine);

        await settingsRepository.UpsertAsync(settings, cancellationToken);

        return new UpdateRestaurantSettingsResult(settings.MaxAlcoholicItemQuantityPerLine);
    }
}
