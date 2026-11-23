using MDel.Application.Abstractions;
using MDel.Domain;

namespace MDel.Infrastructure.Integrations;

/// <summary>Verifică dacă aplicația poate scrie, citi și șterge în calea de stocare a fișierelor.</summary>
public sealed class AttachmentStorageProbe(ISettingsService settings)
{
    public async Task<ConnectionTestResult> TestAsync(CancellationToken ct)
    {
        var path = await settings.GetAsync(SettingKeys.AttachmentsStoragePath, ct);
        if (string.IsNullOrWhiteSpace(path))
            return new ConnectionTestResult(false, "The attachment storage path is not configured.");

        var probe = Path.Combine(path, $".mdel-probe-{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(path);
            var content = Guid.NewGuid().ToString();
            await File.WriteAllTextAsync(probe, content, ct);
            var read = await File.ReadAllTextAsync(probe, ct);
            if (read != content) return new ConnectionTestResult(false, "The probe file content did not match.");

            var free = TryGetFreeSpace(path);
            return new ConnectionTestResult(true,
                $"Write/read/delete OK in '{path}'" + (free is { } gb ? $" – {gb:N1} GB free." : "."));
        }
        catch (Exception ex)
        {
            return new ConnectionTestResult(false, $"Cannot use '{path}': {ex.Message}");
        }
        finally
        {
            try { File.Delete(probe); } catch { /* best effort */ }
        }
    }

    private static double? TryGetFreeSpace(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            return root is null ? null : new DriveInfo(root).AvailableFreeSpace / 1024d / 1024 / 1024;
        }
        catch
        {
            return null; // ex. share UNC
        }
    }
}
