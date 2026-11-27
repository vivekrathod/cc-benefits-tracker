using System.Text;

namespace Chitragupta.Infrastructure.Feeds.SimpleFin;

/// <summary>
/// Exchanges a single-use setup token for the access URL and writes it to a file only
/// its owner can read. Neither value is returned, printed or logged.
/// </summary>
public static class SimpleFinClaim
{
    public static async Task ClaimAsync(HttpClient http, string setupToken, string accessUrlFile, CancellationToken cancellationToken)
    {
        if (File.Exists(accessUrlFile))
        {
            throw new InvalidOperationException("An access URL file already exists. Remove it first if you mean to replace it.");
        }

        Uri claimUrl;
        try
        {
            claimUrl = new Uri(Encoding.UTF8.GetString(Convert.FromBase64String(setupToken.Trim())));
        }
        catch (Exception malformed) when (malformed is FormatException or UriFormatException)
        {
            throw new InvalidOperationException("That is not a SimpleFIN setup token.");
        }

        if (claimUrl.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("The setup token does not point to an https address.");
        }

        using var response = await http.PostAsync(claimUrl, new ByteArrayContent([]), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"The Bridge answered {(int)response.StatusCode}. A setup token works once; if it was used before, create a new one.");
        }

        var accessUrl = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
        SimpleFinAccess.Split(accessUrl);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(accessUrlFile))!);
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        await using var file = new FileStream(accessUrlFile, options);
        await file.WriteAsync(Encoding.UTF8.GetBytes(accessUrl + "\n"), cancellationToken);
    }
}
