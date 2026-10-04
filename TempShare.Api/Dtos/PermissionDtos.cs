namespace TempShare.Api.Dtos;

public record GrantPermissionRequest(string RecipientUsername, DateTime ExpiresAt);

public record PermissionResponse(
    Guid Id,
    Guid FileId,
    string RecipientUsername,
    DateTime ExpiresAt,
    DateTime? RevokedAt,
    bool IsActive);