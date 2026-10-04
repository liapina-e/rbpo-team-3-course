namespace TempShare.Api.Dtos;

public record UploadResponse(Guid Id, string FileName, long SizeBytes, DateTime UploadedAt);