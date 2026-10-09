using System.Text.Json;
using System.Text.Json.Serialization;

namespace AuraIce.Core.Configuration;

public sealed record AuraIceSettings
{
    public int VendorId { get; init; } = 0xAA88;
    public int ProductId { get; init; } = 0x8666;
    public int UpdateIntervalMilliseconds { get; init; } = 1000;
    public string? PreferredSensorIdentifier { get; init; }
    public bool StartWithWindows { get; init; } = true;
}

public sealed class AuraIceSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string DirectoryPath { get; }
    public string FilePath { get; }

    public AuraIceSettingsStore(string? directoryPath = null)
    {
        DirectoryPath = directoryPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "AuraIce");
        FilePath = Path.Combine(DirectoryPath, "config.json");
    }

    public async Task<AuraIceSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(FilePath))
        {
            return new AuraIceSettings();
        }

        await using FileStream stream = File.OpenRead(FilePath);
        return await JsonSerializer.DeserializeAsync<AuraIceSettings>(stream, JsonOptions, cancellationToken)
            ?? new AuraIceSettings();
    }

    public async Task SaveAsync(AuraIceSettings settings, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(DirectoryPath);
        string temporaryPath = FilePath + ".tmp";

        await using (FileStream stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
        }

        File.Move(temporaryPath, FilePath, overwrite: true);
    }
}
