using DefaultNamespace;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TempShare.Api.Data;
using TempShare.Api.Dtos;
using TempShare.Api.Extensions;
using TempShare.Api.Storage;

namespace TempShare.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/files")]
public class FilesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public FilesController(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    [HttpPost]
    [RequestSizeLimit(52_428_800)]
    public async Task<IActionResult> Upload([FromForm] IFormFile file)
    {
        if (file is null || file.Length == 0)
            return BadRequest("File is required.");

        Guid userId = User.GetUserId();

        await using var stream = file.OpenReadStream();
        string storedName = await FileStorage.SaveAsync(_config, stream);

        StoredFile stored = new StoredFile
        {
            Id = Guid.NewGuid(),
            OwnerId = userId,
            FileName = Path.GetFileName(file.FileName),
            SizeBytes = file.Length,
            StoragePath = storedName,
            UploadedAt = DateTime.UtcNow
        };

        _db.Files.Add(stored);
        await _db.SaveChangesAsync();

        return Ok(new UploadResponse(stored.Id, stored.FileName, stored.SizeBytes, stored.UploadedAt));
    }

    [HttpGet("{fileId:guid}/content")]
    public async Task<IActionResult> Download(Guid fileId)
    {
        Guid userId = User.GetUserId();
        DateTime now = DateTime.UtcNow;

        StoredFile? file = await _db.Files
            .Include(f => f.Permissions)
            .SingleOrDefaultAsync(f => f.Id == fileId);

        if (file is null)
            return NotFound();

        bool isOwner = file.OwnerId == userId;
        bool hasActivePermission = file.Permissions
            .Any(p => p.RecipientId == userId && p.IsActiveAt(now));

        if (!isOwner && !hasActivePermission)
            return NotFound();

        Stream content = FileStorage.Open(_config, file.StoragePath);
        return File(content, "application/octet-stream", file.FileName);
    }
}