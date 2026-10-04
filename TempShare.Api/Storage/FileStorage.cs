namespace TempShare.Api.Storage;

public static class FileStorage
{
    public static string ResolveRoot(IConfiguration config)
    {
        string? root = config["Storage:Root"];
        if (string.IsNullOrWhiteSpace(root))
            root = Path.Combine(AppContext.BaseDirectory, "storage");

        Directory.CreateDirectory(root);
        return root;
    }

    public static async Task<string> SaveAsync(IConfiguration config, Stream content)
    {
        string root = ResolveRoot(config);
        string name = Guid.NewGuid().ToString("N");
        string fullPath = Path.Combine(root, name);

        await using var target = System.IO.File.Create(fullPath);
        await content.CopyToAsync(target);

        return name;
    }

    public static Stream Open(IConfiguration config, string storedName)
    {
        string root = ResolveRoot(config);
        string fullPath = Path.Combine(root, Path.GetFileName(storedName));
        return System.IO.File.OpenRead(fullPath);
    }
}