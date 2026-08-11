using NutManager.App.Localization;

namespace NutManager.App.ViewModels;

public enum AdministrationSection
{
    NutConfiguration,
    WindowsService,
    DevicesAndDrivers,
    RemoteAccess
}

public sealed record AdministrationSectionItemViewModel(
    AdministrationSection Section,
    string Title,
    string Description,
    bool IsApplicable,
    string AvailabilityText);

public static class AdministrationPresentation
{
    public static IReadOnlyList<AdministrationSectionItemViewModel> CreateSections(
        NutManagerLocalizer strings,
        bool isRemote,
        bool canManage)
    {
        ArgumentNullException.ThrowIfNull(strings);
        var localAvailability = isRemote
            ? strings.Get("Administration.Availability.LocalOnly")
            : strings.Get("Administration.Availability.Available");
        var remoteAvailability = isRemote
            ? strings.Get("Administration.Availability.Available")
            : strings.Get("Administration.Availability.RemoteOnly");
        var manageAvailability = canManage
            ? strings.Get("Administration.Availability.Manage")
            : strings.Get("Administration.Availability.ReadOnly");

        return
        [
            new(AdministrationSection.NutConfiguration,
                strings.Get("Administration.Section.Configuration"),
                strings.Get("Administration.Section.Configuration.Description"),
                true,
                manageAvailability),
            new(AdministrationSection.WindowsService,
                strings.Get("Administration.Section.WindowsService"),
                strings.Get("Administration.Section.WindowsService.Description"),
                !isRemote,
                localAvailability),
            new(AdministrationSection.DevicesAndDrivers,
                strings.Get("Administration.Section.DevicesDrivers"),
                strings.Get("Administration.Section.DevicesDrivers.Description"),
                !isRemote,
                localAvailability),
            new(AdministrationSection.RemoteAccess,
                strings.Get("Administration.Section.RemoteAccess"),
                strings.Get("Administration.Section.RemoteAccess.Description"),
                isRemote,
                remoteAvailability)
        ];
    }
}
