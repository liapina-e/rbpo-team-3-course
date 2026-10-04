namespace DefaultNamespace;

public class StoredFile
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public User Owner { get; set; } = null!;

    public string FileName { get; set; } = null!;

    public long SizeBytes { get; set; }

    public string StoragePath { get; set; } = null!;

    public DateTime UploadedAt { get; set; }

    public List<Permission> Permissions { get; set; } = new();
}