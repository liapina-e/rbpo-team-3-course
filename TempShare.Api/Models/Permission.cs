namespace DefaultNamespace;

public class Permission
{
    public Guid Id { get; set; }

    public Guid FileId { get; set; }
    public StoredFile File { get; set; } = null!;

    public Guid RecipientId { get; set; }
    public User Recipient { get; set; } = null!;

    public DateTime ExpiresAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public bool IsActiveAt(DateTime now) => RevokedAt == null && now < ExpiresAt;
}