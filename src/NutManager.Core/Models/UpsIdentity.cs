namespace NutManager.Core.Models;

public sealed record UpsIdentity
{
    public UpsIdentity(
        string name,
        string? description = null,
        string? manufacturer = null,
        string? model = null,
        string? serialNumber = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name;
        Description = description;
        Manufacturer = manufacturer;
        Model = model;
        SerialNumber = serialNumber;
    }

    public string Name { get; }

    public string? Description { get; }

    public string? Manufacturer { get; }

    public string? Model { get; }

    public string? SerialNumber { get; }
}
