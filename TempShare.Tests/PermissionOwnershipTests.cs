using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using DefaultNamespace;
using Microsoft.Extensions.DependencyInjection;
using TempShare.Api.Data;
using TempShare.Api.Dtos;
using Xunit;

namespace TempShare.Tests;

// SR-02 / T-03 / D-02: выдавать, продлевать и отзывать разрешения может только владелец файла

public class PermissionOwnershipTests
{
    [Fact]
    public async Task Owner_CanGrantAndRevoke_OtherRecipientKeepsAccess()
    {
        using TempShareFactory factory = new TempShareFactory();
        HttpClient client = factory.CreateClient();

        string aliceToken = await RegisterAsync(client, "alice");
        string bobToken = await RegisterAsync(client, "bob");
        string carolToken = await RegisterAsync(client, "carol");
        Guid fileId = await UploadAsync(client, aliceToken);

        HttpResponseMessage grantBob = await GrantAsync(client, aliceToken, fileId, "bob", DateTimeOffset.UtcNow.AddHours(1));
        HttpResponseMessage grantCarol = await GrantAsync(client, aliceToken, fileId, "carol", DateTimeOffset.UtcNow.AddHours(1));
        Assert.Equal(HttpStatusCode.OK, grantBob.StatusCode);
        Assert.Equal(HttpStatusCode.OK, grantCarol.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await DownloadAsync(client, fileId, carolToken)).StatusCode);

        HttpResponseMessage revoke = await RevokeAsync(client, aliceToken, fileId, UserId(factory, "carol"));

        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await DownloadAsync(client, fileId, carolToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await DownloadAsync(client, fileId, bobToken)).StatusCode);
    }

    [Fact]
    public async Task Recipient_CannotExtendOwnPermission_ExpiryUnchanged()
    {
        using TempShareFactory factory = new TempShareFactory();
        HttpClient client = factory.CreateClient();

        string aliceToken = await RegisterAsync(client, "alice");
        string bobToken = await RegisterAsync(client, "bob");
        Guid fileId = await UploadAsync(client, aliceToken);
        (await GrantAsync(client, aliceToken, fileId, "bob", DateTimeOffset.UtcNow.AddHours(1))).EnsureSuccessStatusCode();
        DateTime expiresBefore = PermissionOf(factory, fileId, "bob").ExpiresAt;

        HttpResponseMessage extend = await GrantAsync(client, bobToken, fileId, "bob", DateTimeOffset.UtcNow.AddDays(30));

        Assert.Equal(HttpStatusCode.NotFound, extend.StatusCode);
        Assert.Equal(expiresBefore, PermissionOf(factory, fileId, "bob").ExpiresAt);
    }

    [Fact]
    public async Task Recipient_CannotGrantToThirdUser_PermissionSetUnchanged()
    {
        using TempShareFactory factory = new TempShareFactory();
        HttpClient client = factory.CreateClient();

        string aliceToken = await RegisterAsync(client, "alice");
        string bobToken = await RegisterAsync(client, "bob");
        string carolToken = await RegisterAsync(client, "carol");
        Guid fileId = await UploadAsync(client, aliceToken);
        (await GrantAsync(client, aliceToken, fileId, "bob", DateTimeOffset.UtcNow.AddHours(1))).EnsureSuccessStatusCode();

        HttpResponseMessage grant = await GrantAsync(client, bobToken, fileId, "carol", DateTimeOffset.UtcNow.AddHours(1));

        Assert.Equal(HttpStatusCode.NotFound, grant.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await DownloadAsync(client, fileId, carolToken)).StatusCode);
        Assert.Equal(1, PermissionCount(factory, fileId));
    }

    [Fact]
    public async Task NonOwner_CannotRevoke_PermissionStaysActive()
    {
        using TempShareFactory factory = new TempShareFactory();
        HttpClient client = factory.CreateClient();

        string aliceToken = await RegisterAsync(client, "alice");
        string bobToken = await RegisterAsync(client, "bob");
        string carolToken = await RegisterAsync(client, "carol");
        Guid fileId = await UploadAsync(client, aliceToken);
        (await GrantAsync(client, aliceToken, fileId, "bob", DateTimeOffset.UtcNow.AddHours(1))).EnsureSuccessStatusCode();
        Guid bobId = UserId(factory, "bob");

        HttpResponseMessage revokeByStranger = await RevokeAsync(client, carolToken, fileId, bobId);
        HttpResponseMessage revokeByRecipient = await RevokeAsync(client, bobToken, fileId, bobId);

        Assert.Equal(HttpStatusCode.NotFound, revokeByStranger.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, revokeByRecipient.StatusCode);
        Assert.Null(PermissionOf(factory, fileId, "bob").RevokedAt);
        Assert.Equal(HttpStatusCode.OK, (await DownloadAsync(client, fileId, bobToken)).StatusCode);
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
        HttpClient client, Guid fileId, string token)
    {
        HttpRequestMessage request = new HttpRequestMessage(
            HttpMethod.Get, $"/api/files/{fileId}/content");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private static Guid UserId(TempShareFactory factory, string username)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return db.Users.Single(u => u.Username == username).Id;
    }

    private static Permission PermissionOf(TempShareFactory factory, Guid fileId, string recipient)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Guid recipientId = db.Users.Single(u => u.Username == recipient).Id;
        return db.Permissions.Single(p => p.FileId == fileId && p.RecipientId == recipientId);
    }

    private static int PermissionCount(TempShareFactory factory, Guid fileId)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return db.Permissions.Count(p => p.FileId == fileId);
    }
}
