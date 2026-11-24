using System.Security.Cryptography;
using MDel.Application.Abstractions;
using MDel.Domain;
using MDel.Domain.Entities;
using MDel.Infrastructure.Localization;
using MDel.Infrastructure.Persistence;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;

namespace MDel.Infrastructure.Delegations;

public sealed record UploadFile(string FileName, long Length, Func<Stream> OpenReadStream);

public sealed record AttachmentDownload(string FileName, string ContentType, Stream Content);

/// <summary>
/// Atașamente pe disc, în calea din Administrare: {root}/{yyyy}/{MM}/{guid}.bin.
/// Numele trimis de client nu se folosește niciodată pe disc (previne path traversal / execuție).
/// </summary>
public sealed class AttachmentService(
    MDelDbContext db,
    ISettingsService settings,
    ICurrentUser currentUser,
    IAuditService audit,
    TimeProvider time)
{
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    public async Task<IReadOnlyList<int>> UploadAsync(int delegationId, IReadOnlyList<UploadFile> files, CancellationToken ct)
    {
        if (!await db.Delegations.AnyAsync(d => d.Id == delegationId, ct))
            throw new KeyNotFoundException($"Delegation {delegationId} not found.");

        var root = await GetRootAsync(ct);
        var maxMb = await settings.GetIntAsync(SettingKeys.AttachmentsMaxSizeMb, 100, ct);
        var allowed = ParseExtensions(await settings.GetAsync(SettingKeys.AttachmentsAllowedExtensions, ct));

        // Validăm toate fișierele înainte de a scrie ceva.
        var errors = new List<string>();
        foreach (var f in files)
        {
            var name = SafeFileName(f.FileName);
            var ext = Path.GetExtension(name).ToLowerInvariant();
            if (f.Length == 0) errors.Add(Messages.Get("FileEmpty", name));
            else if (f.Length > maxMb * 1024L * 1024L) errors.Add(Messages.Get("FileTooLarge", name, maxMb));
            if (allowed.Count > 0 && !allowed.Contains(ext)) errors.Add(Messages.Get("FileExtension", name, string.Join(", ", allowed)));
        }

        if (files.Count == 0) errors.Add(Messages.Get("FileEmpty", "-"));
        if (errors.Count > 0)
            throw new DelegationValidationException(new Dictionary<string, string[]> { ["files"] = errors.ToArray() });

        var now = time.GetUtcNow().UtcDateTime;
        var written = new List<string>();
        var entities = new List<DelegationAttachment>();
        try
        {
            foreach (var f in files)
            {
                var name = SafeFileName(f.FileName);
                var relative = $"{now:yyyy}/{now:MM}/{Guid.NewGuid():N}.bin";
                var full = ResolvePath(root, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);

                string hash;
                long size;
                await using (var input = f.OpenReadStream())
                await using (var output = new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                {
                    written.Add(full);
                    using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    var buffer = new byte[81920];
                    size = 0;
                    int read;
                    while ((read = await input.ReadAsync(buffer, ct)) > 0)
                    {
                        size += read;
                        if (size > maxMb * 1024L * 1024L) throw new DelegationValidationException(
                            new Dictionary<string, string[]> { ["files"] = [Messages.Get("FileTooLarge", name, maxMb)] });
                        sha.AppendData(buffer, 0, read);
                        await output.WriteAsync(buffer.AsMemory(0, read), ct);
                    }

                    hash = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
                }

                entities.Add(new DelegationAttachment
                {
                    DelegationId = delegationId,
                    OriginalFileName = name,
                    StoredFileName = relative,
                    ContentType = ContentTypes.TryGetContentType(name, out var type) ? type : "application/octet-stream",
                    SizeBytes = size,
                    Sha256 = hash,
                    UploadedById = currentUser.UserId ?? throw new InvalidOperationException("User not provisioned."),
                    UploadedUtc = now,
                });
            }

            db.DelegationAttachments.AddRange(entities);
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            // Fără fișiere orfane dacă ceva eșuează.
            foreach (var path in written) TryDelete(path);
            throw;
        }

        foreach (var a in entities)
        {
            await audit.WriteAsync(new AuditEntry("Attachment.Uploaded", nameof(Delegation), delegationId.ToString(),
                NewValues: new { a.Id, a.OriginalFileName, a.SizeBytes, a.Sha256 }), ct);
        }

        return entities.Select(e => e.Id).ToList();
    }

    public async Task<AttachmentDownload?> OpenAsync(int delegationId, int attachmentId, CancellationToken ct)
    {
        var a = await db.DelegationAttachments.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == attachmentId && x.DelegationId == delegationId, ct);
        if (a is null) return null;
        if (a.PurgedUtc is not null) throw new DelegationConflictException(Messages.Get("FilePurged"));

        var full = ResolvePath(await GetRootAsync(ct), a.StoredFileName);
        if (!File.Exists(full)) return null;

        await audit.WriteAsync(new AuditEntry("Attachment.Downloaded", nameof(Delegation), delegationId.ToString(),
            NewValues: new { a.Id, a.OriginalFileName }), ct);

        var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return new AttachmentDownload(a.OriginalFileName, a.ContentType, stream);
    }

    public async Task<bool> DeleteAsync(int delegationId, int attachmentId, CancellationToken ct)
    {
        var a = await db.DelegationAttachments
            .FirstOrDefaultAsync(x => x.Id == attachmentId && x.DelegationId == delegationId, ct);
        if (a is null) return false;

        var root = await settings.GetAsync(SettingKeys.AttachmentsStoragePath, ct);
        db.DelegationAttachments.Remove(a);
        await db.SaveChangesAsync(ct);
        if (!string.IsNullOrWhiteSpace(root) && a.PurgedUtc is null) TryDelete(ResolvePath(root, a.StoredFileName));

        await audit.WriteAsync(new AuditEntry("Attachment.Deleted", nameof(Delegation), delegationId.ToString(),
            OldValues: new { a.Id, a.OriginalFileName, a.SizeBytes, a.Sha256 }), ct);
        return true;
    }

    public static IReadOnlyList<string> ParseExtensions(string? value) =>
        (value ?? string.Empty)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.ToLowerInvariant())
            .Distinct()
            .ToList();

    private async Task<string> GetRootAsync(CancellationToken ct)
    {
        var root = await settings.GetAsync(SettingKeys.AttachmentsStoragePath, ct);
        if (string.IsNullOrWhiteSpace(root))
            throw new DelegationConflictException(Messages.Get("StorageNotConfigured"));
        return root;
    }

    /// <summary>Combină și verifică că rezultatul rămâne în interiorul rădăcinii.</summary>
    private static string ResolvePath(string root, string relative)
    {
        var rootFull = Path.GetFullPath(root);
        var full = Path.GetFullPath(Path.Combine(rootFull, relative.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = rootFull.EndsWith(Path.DirectorySeparatorChar) ? rootFull : rootFull + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Invalid attachment path.");
        return full;
    }

    /// <summary>Doar numele (fără cale), fără caractere invalide, max. 200 caractere.</summary>
    private static string SafeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName.Replace('\\', '/').Split('/').Last());
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        name = name.Trim();
        if (name.Length == 0) name = "file";
        if (name.Length > 200)
        {
            // Extensia se păstrează doar dacă e scurtă (altfel „a.<250 de caractere>” ar da index negativ).
            var ext = Path.GetExtension(name);
            name = ext.Length is > 0 and <= 20 ? name[..(200 - ext.Length)] + ext : name[..200];
        }

        return name;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* best effort */ }
    }
}
