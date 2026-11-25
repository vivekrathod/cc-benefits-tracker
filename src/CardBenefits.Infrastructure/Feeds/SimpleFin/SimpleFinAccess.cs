using System.Net.Http.Headers;
using System.Text;
using CardBenefits.Application.Ports;

namespace CardBenefits.Infrastructure.Feeds.SimpleFin;

/// <summary>
/// The SimpleFIN access URL, which carries the credentials to the household's bank
/// data. It lives in a file outside the repository, is read each time it is needed,
/// and is split straight away so the credentials never sit in a URL that could be
/// logged or end up in an error message.
/// </summary>
internal sealed class SimpleFinAccess(string? accessUrlFile)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(accessUrlFile) && File.Exists(accessUrlFile);

    public (Uri Endpoint, AuthenticationHeaderValue Credentials) Read()
    {
        if (!IsConfigured)
        {
            throw new FeedUnavailableException("The SimpleFIN access URL file is not configured or does not exist.");
        }

        return Split(File.ReadAllText(accessUrlFile!));
    }

    internal static (Uri Endpoint, AuthenticationHeaderValue Credentials) Split(string accessUrl)
    {
        if (!Uri.TryCreate(accessUrl.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrEmpty(uri.UserInfo))
        {
            // Say what is wrong without echoing any part of the value.
            throw new FeedUnavailableException("The SimpleFIN access URL file does not hold an https URL with credentials.");
        }

        var endpoint = new UriBuilder(uri) { UserName = "", Password = "" }.Uri;
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(Uri.UnescapeDataString(uri.UserInfo)));
        return (endpoint, new AuthenticationHeaderValue("Basic", credentials));
    }
}
