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
[Route("api/files/{fileId:guid}/permissions")]
public class PermissionsController : ControllerBase
{
    private readonly AppDbContext _db;

    public PermissionsController(AppDbContext db) => _db = db;

    [HttpPost]
    public async Task<IActionResult> Grant(Guid fileId, GrantPermissionRequest req)
    {
        var userId = User.GetUserId();

        var file = await _db.Files.SingleOrDefaultAsync(f => f.Id == fileId);
        if (file is null || file.OwnerId != userId)
            return NotFound();

        if (req.ExpiresAt <= DateTime.UtcNow)
            return BadRequest("ExpiresAt must be in the future.");

        var recipient = await _db.Users
            .SingleOrDefaultAsync(u => u.Username == req.RecipientUsername);
        if (recipient is null)
            return BadRequest("Recipient not found.");

        if (recipient.Id == userId)
            return BadRequest("Cannot grant a permission to the file owner.");

        var permission = await _db.Permissions
            .SingleOrDefaultAsync(p => p.FileId == fileId && p.RecipientId == recipient.Id);

        if (permission is null)
        {
            permission = new Permission
            {
                Id = Guid.NewGuid(),
                FileId = fileId,
                RecipientId = recipient.Id,
                ExpiresAt = req.ExpiresAt,
                RevokedAt = null,
                CreatedAt = DateTime.UtcNow
            };
            _db.Permissions.Add(permission);
        }
        else
        {
            permission.ExpiresAt = req.ExpiresAt;
            permission.RevokedAt = null;
        }

        await _db.SaveChangesAsync();

        return Ok(new PermissionResponse(
            permission.Id,
            fileId,
            recipient.Username,
            permission.ExpiresAt,
            permission.RevokedAt,
            permission.IsActiveAt(DateTime.UtcNow)));
    }

    [HttpDelete("{recipientId:guid}")]
    public async Task<IActionResult> Revoke(Guid fileId, Guid recipientId)
    {
        var userId = User.GetUserId();

        var file = await _db.Files.SingleOrDefaultAsync(f => f.Id == fileId);
        if (file is null || file.OwnerId != userId)
            return NotFound();

        var permission = await _db.Permissions
            .SingleOrDefaultAsync(p => p.FileId == fileId && p.RecipientId == recipientId);
        if (permission is null)
            return NotFound();

        if (permission.RevokedAt is null)
        {
            permission.RevokedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        return NoContent();
    }
}