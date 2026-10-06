using System.Text.Json;
using Inventory.Application.Abstractions;
using Inventory.Application.Common;
using Inventory.Domain.Auditing;
using Inventory.Domain.Common;
using Inventory.Domain.School;

namespace Inventory.Application.School;

public sealed class SchoolSettingsService
{
    private readonly ISchoolSettingsRepository _repository;
    private readonly ISchoolSettingsProvider _provider;
    private readonly IClock _clock;
    private readonly ICurrentUserContext _currentUser;

    public SchoolSettingsService(
        ISchoolSettingsRepository repository,
        ISchoolSettingsProvider provider,
        IClock clock,
        ICurrentUserContext currentUser)
    {
        _repository = repository;
        _provider = provider;
        _clock = clock;
        _currentUser = currentUser;
    }

    public async Task<SchoolSettingsView> GetAsync(
        CancellationToken cancellationToken)
    {
        var profile = await _repository.GetAsync(
            forUpdate: false,
            cancellationToken);
        if (profile is not null)
        {
            return ToView(profile);
        }

        var defaults = await _provider.GetAsync(cancellationToken);
        return new SchoolSettingsView(
            defaults.Name,
            defaults.Code,
            defaults.TimeZone,
            defaults.QrBaseUrl,
            defaults.WarrantyWarningDays,
            IsPersisted: false,
            Version: null);
    }

    public async Task<SchoolSettingsView> UpdateAsync(
        UpdateSchoolSettingsCommand command,
        CancellationToken cancellationToken)
    {
        ValidateTimeZone(command.TimeZone);
        if (!Uri.TryCreate(command.QrBaseUrl, UriKind.Absolute, out var qrBaseUrl))
        {
            throw new RequestValidationException(
                "invalid_qr_base_url",
                "The QR base URL must be an absolute HTTPS URL.");
        }

        var actor = _currentUser.RequireActor();
        var profile = await _repository.GetAsync(
            forUpdate: true,
            cancellationToken);
        object? before = null;
        string action;
        if (profile is null)
        {
            if (command.Version.HasValue && command.Version.Value != Guid.Empty)
            {
                throw new ResourceConflictException(
                    "school_settings_not_created",
                    "The school profile has not been created yet; retry without a version.");
            }

            profile = SchoolProfile.Create(
                command.Name,
                command.Code,
                command.TimeZone,
                qrBaseUrl,
                command.WarrantyWarningDays);
            _repository.Add(profile);
            action = "school.settings.created";
        }
        else
        {
            if (!command.Version.HasValue)
            {
                throw new RequestValidationException(
                    "school_settings_version_required",
                    "The current school settings version is required.");
            }

            before = Snapshot(profile);
            try
            {
                profile.Update(
                    command.Name,
                    command.Code,
                    command.TimeZone,
                    qrBaseUrl,
                    command.WarrantyWarningDays,
                    command.Version);
            }
            catch (DomainRuleException exception)
                when (exception.Code == "concurrency_conflict")
            {
                throw new ResourceConflictException(
                    exception.Code,
                    exception.Message,
                    exception);
            }
            action = "school.settings.updated";
        }

        _repository.AddAuditLog(
            AuditLog.Create(
                actor.UserId,
                actor.DisplayName,
                action,
                "school_settings",
                profile.Id.ToString(),
                _clock.UtcNow,
                "succeeded",
                JsonSerializer.Serialize(
                    new
                    {
                        Before = before,
                        After = Snapshot(profile),
                    }),
                actor.IpAddress,
                actor.Client,
                actor.CorrelationId));
        await _repository.SaveChangesAsync(cancellationToken);
        return ToView(profile);
    }

    private static object Snapshot(SchoolProfile profile) =>
        new
        {
            profile.Name,
            profile.Code,
            profile.TimeZone,
            profile.QrBaseUrl,
            profile.WarrantyWarningDays,
        };

    private static SchoolSettingsView ToView(SchoolProfile profile) =>
        new(
            profile.Name,
            profile.Code,
            profile.TimeZone,
            profile.QrBaseUrl,
            profile.WarrantyWarningDays,
            IsPersisted: true,
            profile.Version);

    private static void ValidateTimeZone(string timeZone)
    {
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(timeZone.Trim());
        }
        catch (TimeZoneNotFoundException)
        {
            throw InvalidTimeZone();
        }
        catch (InvalidTimeZoneException)
        {
            throw InvalidTimeZone();
        }
    }

    private static RequestValidationException InvalidTimeZone() =>
        new(
            "invalid_time_zone",
            "The time zone is not recognised on this host.",
            new Dictionary<string, string[]>
            {
                ["timeZone"] = ["Supply a valid IANA or Windows time-zone identifier."],
            });
}
