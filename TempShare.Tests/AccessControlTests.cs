using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using DefaultNamespace;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using TempShare.Api.Data;
using TempShare.Api.Dtos;
using Xunit;

namespace TempShare.Tests;

public class AccessControlTests
{
    // SR-04: без подтверждённой аутентификации операции с файлами отклоняются

    [Fact]
    public async Task Download_WithoutToken_Returns401()
    {
        using TempShareFactory factory = new TempShareFactory();
        HttpClient client = factory.CreateClient();

        string ownerToken = await RegisterAsync(client, "alice");
        Guid fileId = await UploadAsync(client, ownerToken);

        HttpResponseMessage response = await DownloadAsync(client, fileId, token: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // SR-01: содержимое выдаётся только владельцу и получателю с действующим разрешением

    [Fact]
    public async Task Download_ByStrangerWithoutPermission_Returns404()
    {
        using TempShareFactory factory = new TempShareFactory();
        HttpClient client = factory.CreateClient();

        string ownerToken = await RegisterAsync(client, "alice");
        string strangerToken = await RegisterAsync(client, "carol");
        Guid fileId = await UploadAsync(client, ownerToken);

        HttpResponseMessage response = await DownloadAsync(client, fileId, strangerToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Download_ByRecipientWithActivePermission_Returns200()
    {
        using TempShareFactory factory = new TempShareFactory();
        HttpClient client = factory.CreateClient();

        string ownerToken = await RegisterAsync(client, "alice");
        string recipientToken = await RegisterAsync(client, "bob");
        Guid fileId = await UploadAsync(client, ownerToken);

        HttpResponseMessage grant = await GrantAsync(
            client, ownerToken, fileId, "bob", DateTimeOffset.UtcNow.AddHours(1));
        grant.EnsureSuccessStatusCode();

        HttpResponseMessage response = await DownloadAsync(client, fileId, recipientToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        byte[] content = await response.Content.ReadAsByteArrayAsync();
        Assert.NotEmpty(content);
    }

    [Fact]
    public async Task Download_ByOwner_Returns200()
    {
        using TempShareFactory factory = new TempShareFactory();
        HttpClient client = factory.CreateClient();

        string ownerToken = await RegisterAsync(client, "alice");
        Guid fileId = await UploadAsync(client, ownerToken);

        HttpResponseMessage response = await DownloadAsync(client, fileId, ownerToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Download_AfterRevoke_Returns404()
    {
        using TempShareFactory factory = new TempShareFactory();
        HttpClient client = factory.CreateClient();

        string ownerToken = await RegisterAsync(client, "alice");
        string recipientToken = await RegisterAsync(client, "bob");
        Guid fileId = await UploadAsync(client, ownerToken);

        HttpResponseMessage grant = await GrantAsync(
            client, ownerToken, fileId, "bob", DateTimeOffset.UtcNow.AddHours(1));
        grant.EnsureSuccessStatusCode();

        Guid bobId = UserId(factory, "bob");
        HttpResponseMessage revoke = await RevokeAsync(client, ownerToken, fileId, bobId);
        revoke.EnsureSuccessStatusCode();

        HttpResponseMessage response = await DownloadAsync(client, fileId, recipientToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Download_AfterExpiry_Returns404()
    {
        using TempShareFactory factory = new TempShareFactory();
        HttpClient client = factory.CreateClient();

        string ownerToken = await RegisterAsync(client, "alice");
        string recipientToken = await RegisterAsync(client, "bob");
        Guid fileId = await UploadAsync(client, ownerToken);

        HttpResponseMessage grant = await GrantAsync(
            client, ownerToken, fileId, "bob", DateTimeOffset.UtcNow.AddHours(1));
        grant.EnsureSuccessStatusCode();

        ExpirePermission(factory, fileId);

        HttpResponseMessage response = await DownloadAsync(client, fileId, recipientToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // SR-02: выдавать и отзывать разрешения может только владелец файла

    [Fact]
    public async Task Grant_ByNonOwner_Returns404_AndGrantsNoAccess()
    {
        using TempShareFactory factory = new TempShareFactory();
        HttpClient client = factory.CreateClient();

        string ownerToken = await RegisterAsync(client, "alice");
        string otherToken = await RegisterAsync(client, "bob");
        string targetToken = await RegisterAsync(client, "carol");
        Guid fileId = await UploadAsync(client, ownerToken);

        HttpResponseMessage grant = await GrantAsync(client, otherToken, fileId, "carol", DateTimeOffset.UtcNow.AddHours(1));

        Assert.Equal(HttpStatusCode.NotFound, grant.StatusCode);

        HttpResponseMessage download = await DownloadAsync(client, fileId, targetToken);
        Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);
    }

    // SR-03: перечень получателей виден только владельцу файла

    [Fact]
    public async Task Recipients_ByNonOwner_Returns404()
    {
        using TempShareFactory factory = new TempShareFactory();
        HttpClient client = factory.CreateClient();

        string ownerToken = await RegisterAsync(client, "alice");
        string otherToken = await RegisterAsync(client, "bob");
        Guid fileId = await UploadAsync(client, ownerToken);

        HttpRequestMessage request = new HttpRequestMessage(
            HttpMethod.Get, $"/api/files/{fileId}/recipients");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", otherToken);
        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // SR-04 / T-04 / D-03: токен, подписанный не секретом сервера, отклоняется

    [Fact]
    public async Task Download_WithTokenSignedByWrongKey_Returns401()
    {
        using TempShareFactory factory = new TempShareFactory();
        HttpClient client = factory.CreateClient();

        string ownerToken = await RegisterAsync(client, "alice");
        Guid fileId = await UploadAsync(client, ownerToken);
        Guid aliceId = UserId(factory, "alice");

        // Токен с идентификатором alice, но подписанный чужим ключом.
        string forged = ForgeToken(
            aliceId, "alice", "a-totally-different-wrong-signing-key-32bytes!!");

        HttpResponseMessage response = await DownloadAsync(client, fileId, forged);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Download_WithServerIssuedToken_Works()
    {
        using TempShareFactory factory = new TempShareFactory();
        HttpClient client = factory.CreateClient();

        string ownerToken = await RegisterAsync(client, "alice");
        Guid fileId = await UploadAsync(client, ownerToken);

        HttpResponseMessage response = await DownloadAsync(client, fileId, ownerToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<string> RegisterAsync(HttpClient client, string username)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/auth/register", new { username, password = "pass-" + username });
        response.EnsureSuccessStatusCode();
        AuthResponse? body = await response.Content.ReadFromJsonAsync<AuthResponse>();
        return body!.Token;
    }

    private static async Task<Guid> UploadAsync(HttpClient client, string token)
    {
        using MultipartFormDataContent form = new MultipartFormDataContent();
        ByteArrayContent file = new ByteArrayContent(Encoding.UTF8.GetBytes("file contents"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", "report.pdf");

        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "/api/files")
        {
            Content = form
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        UploadResponse? body = await response.Content.ReadFromJsonAsync<UploadResponse>();
        return body!.Id;
    }

    private static async Task<HttpResponseMessage> GrantAsync(
        HttpClient client, string token, Guid fileId, string recipientUsername, DateTimeOffset expiresAt)
    {
        HttpRequestMessage request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/files/{fileId}/permissions")
        {
            Content = JsonContent.Create(new
            {
                recipientUsername,
                expiresAt = expiresAt.ToString("o")
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> RevokeAsync(
        HttpClient client, string token, Guid fileId, Guid recipientId)
    {
        HttpRequestMessage request = new HttpRequestMessage(
            HttpMethod.Delete, $"/api/files/{fileId}/permissions/{recipientId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> DownloadAsync(
        HttpClient client, Guid fileId, string? token)
    {
        HttpRequestMessage request = new HttpRequestMessage(
            HttpMethod.Get, $"/api/files/{fileId}/content");
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private static Guid UserId(TempShareFactory factory, string username)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return db.Users.Single(u => u.Username == username).Id;
    }

    private static void ExpirePermission(TempShareFactory factory, Guid fileId)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Permission permission = db.Permissions.Single(p => p.FileId == fileId);
        permission.ExpiresAt = DateTime.UtcNow.AddMinutes(-5);
        db.SaveChanges();
    }

    private static string ForgeToken(Guid userId, string username, string key)
    {
        Claim[] claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Name, username)
        };
        SigningCredentials creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            SecurityAlgorithms.HmacSha256);
        JwtSecurityToken token = new JwtSecurityToken(
            issuer: TempShareFactory.Issuer,
            audience: TempShareFactory.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}