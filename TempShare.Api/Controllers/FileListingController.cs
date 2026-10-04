using DefaultNamespace;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TempShare.Api.Data;
using TempShare.Api.Dtos;
using TempShare.Api.Extensions;

namespace TempShare.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/files")]
public class FileListingController : ControllerBase
{
    private readonly AppDbContext _db;

    public FileListingController(AppDbContext db) => _db = db;

    [HttpGet("mine")]
    public async Task<IActionResult> MyFiles()
    {
        Guid userId = User.GetUserId();
        DateTime now = DateTime.UtcNow;

        List<StoredFile> files = await _db.Files
            .Where(f => f.OwnerId == userId)
            .Include(f => f.Permissions)
            .OrderByDescending(f => f.UploadedAt)
            .ToListAsync();

        IEnumerable<OwnedFileResponse> result = files.Select(f => new OwnedFileResponse(
            f.Id,
            f.FileName,
            f.SizeBytes,
            f.UploadedAt,
            f.Permissions.Count(p => p.IsActiveAt(now))));

        return Ok(result);
    }

    [HttpGet("shared-with-me")]
    public async Task<IActionResult> SharedWithMe()
    {
        Guid userId = User.GetUserId();
        DateTime now = DateTime.UtcNow;

        List<Permission> permissions = await _db.Permissions
            .Where(p => p.RecipientId == userId && p.RevokedAt == null && p.ExpiresAt > now)
            .Include(p => p.File)
            .ThenInclude(f => f.Owner)
            .OrderBy(p => p.ExpiresAt)
            .ToListAsync();

        IEnumerable<SharedFileResponse> result = permissions.Select(p => new SharedFileResponse(
            p.File.Id,
            p.File.FileName,
            p.File.SizeBytes,
            p.File.Owner.Username,
            p.ExpiresAt));

        return Ok(result);
    }

    [HttpGet("{fileId:guid}/recipients")]
    public async Task<IActionResult> Recipients(Guid fileId)
    {
        Guid userId = User.GetUserId();
        DateTime now = DateTime.UtcNow;

        StoredFile? file = await _db.Files
            .Include(f => f.Permissions)
            .ThenInclude(p => p.Recipient)
            .SingleOrDefaultAsync(f => f.Id == fileId);

        if (file is null || file.OwnerId != userId)
            return NotFound();

        IEnumerable<RecipientResponse> result = file.Permissions
            .OrderBy(p => p.ExpiresAt)
            .Select(p => new RecipientResponse(
                p.RecipientId,
                p.Recipient.Username,
                p.ExpiresAt,
                p.RevokedAt,
                p.IsActiveAt(now)));

        return Ok(result);
    }
}