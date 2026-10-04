namespace TempShare.Api.Dtos;

public record OwnedFileResponse(
    Guid Id,
    string FileName,
    long SizeBytes,
    DateTime UploadedAt,
    int ActivePermissionCount);

public record SharedFileResponse(
    Guid Id,
    string FileName,
    long SizeBytes,
    string OwnerUsername,
    DateTime ExpiresAt);

public record RecipientResponse(
    Guid RecipientId,
    string RecipientUsername,
    DateTime ExpiresAt,
    DateTime? RevokedAt,
    bool IsActive);