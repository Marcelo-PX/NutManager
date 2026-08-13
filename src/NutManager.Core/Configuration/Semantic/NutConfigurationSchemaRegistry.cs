namespace NutManager.Core.Configuration.Semantic;

public sealed class NutConfigurationSchemaRegistry
{
    private readonly IReadOnlyDictionary<NutConfigurationFileKind, NutConfigurationFileSchema> _files;
    private readonly IReadOnlyDictionary<string, NutConfigurationFieldDescriptor> _fields;
    private readonly IReadOnlyDictionary<string, NutDriverConfigurationSchema> _drivers;

    public NutConfigurationSchemaRegistry(
        IEnumerable<NutConfigurationFileSchema> files,
        IEnumerable<NutDriverConfigurationSchema>? drivers = null)
    {
        var fileArray = files?.ToArray() ?? throw new ArgumentNullException(nameof(files));
        if (fileArray.GroupBy(schema => schema.FileKind).Any(group => group.Count() > 1))
            throw new ArgumentException("Only one schema may be registered for a file kind.", nameof(files));
        var allFields = fileArray.SelectMany(schema => schema.Fields).ToArray();
        if (allFields.GroupBy(field => field.SemanticId, StringComparer.Ordinal).Any(group => group.Count() > 1))
            throw new ArgumentException("Semantic IDs must be globally unique.", nameof(files));
        var driverArray = drivers?.ToArray() ?? [];
        if (driverArray.GroupBy(driver => driver.DriverId, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            throw new ArgumentException("Driver IDs must be unique.", nameof(drivers));

        _files = fileArray.ToDictionary(schema => schema.FileKind);
        _fields = allFields.ToDictionary(field => field.SemanticId, StringComparer.Ordinal);
        _drivers = driverArray.ToDictionary(driver => driver.DriverId, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<NutConfigurationFileSchema> FileSchemas => _files.Values.OrderBy(schema => schema.FileKind).ToArray();
    public IReadOnlyList<NutDriverConfigurationSchema> DriverSchemas => _drivers.Values.OrderBy(schema => schema.DriverId, StringComparer.OrdinalIgnoreCase).ToArray();
    public NutConfigurationFileSchema? GetSchema(NutConfigurationFileKind fileKind) => _files.GetValueOrDefault(fileKind);
    public NutConfigurationFieldDescriptor? GetField(string semanticId) => _fields.GetValueOrDefault(semanticId);
    public IReadOnlyList<NutConfigurationFieldDescriptor> GetFields(NutConfigurationFileKind fileKind, NutConfigurationFieldScope scope) =>
        GetSchema(fileKind)?.Fields.Where(field => field.Scope == scope).ToArray() ?? [];
    public NutDriverConfigurationSchema? GetDriverSchema(string driverId) => _drivers.GetValueOrDefault(driverId);

    public static NutConfigurationSchemaRegistry CreateBuiltIn() => new(
    [
        NutServerConfigurationCatalog.CreateNutConfSchema(),
        NutUpsConfigurationCatalog.CreateFileSchema(),
        NutServerConfigurationCatalog.CreateUpsdConfSchema(),
        NutUpsdUsersConfigurationCatalog.CreateSchema(),
        NutUpsmonConfigurationCatalog.CreateSchema()
    ], NutUpsConfigurationCatalog.CreateDriverSchemas());
}
