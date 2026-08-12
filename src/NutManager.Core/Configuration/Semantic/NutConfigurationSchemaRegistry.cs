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
        new(NutConfigurationFileKind.NutConf,
        [
            Field(NutConfigurationFileKind.NutConf, "Nut.Mode", NutConfigurationEntryKind.Assignment, "MODE", NutConfigurationFieldScope.Global)
        ]),
        NutUpsConfigurationCatalog.CreateFileSchema(),
        new(NutConfigurationFileKind.UpsdConf,
        [
            Field(NutConfigurationFileKind.UpsdConf, "Upsd.Listen", NutConfigurationEntryKind.Directive, "LISTEN", NutConfigurationFieldScope.Repeated)
        ]),
        new(NutConfigurationFileKind.UpsdUsers,
        [
            Field(NutConfigurationFileKind.UpsdUsers, "UpsdUsers.Password", NutConfigurationEntryKind.Assignment, "password", NutConfigurationFieldScope.Section,
                sensitive: true, fieldKind: NutConfigurationFieldKind.SecretChange)
        ], new("UpsdUsers.Section", "Semantic.Section.User")),
        new(NutConfigurationFileKind.UpsmonConf,
        [
            Field(NutConfigurationFileKind.UpsmonConf, "Upsmon.Monitor", NutConfigurationEntryKind.Directive, "MONITOR", NutConfigurationFieldScope.Repeated,
                sensitive: true, fieldKind: NutConfigurationFieldKind.SecretChange)
        ])
    ], NutUpsConfigurationCatalog.CreateDriverSchemas());

    private static NutConfigurationFieldDescriptor Field(
        NutConfigurationFileKind fileKind,
        string id,
        NutConfigurationEntryKind entry,
        string name,
        NutConfigurationFieldScope scope,
        bool required = false,
        bool sensitive = false,
        NutConfigurationFieldKind fieldKind = NutConfigurationFieldKind.Text) =>
        new(fileKind, id, entry, name, scope, $"Semantic.Field.{id}.Label", $"Semantic.Field.{id}.Help",
            fieldKind, required, sensitive);
}
