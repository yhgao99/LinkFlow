using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace LinkFlow.Services;

public class ParsedUrlInfo
{
    public string OriginalUrl { get; set; } = string.Empty;
    public string NormalizedUrl { get; set; } = string.Empty;
    public string NormalizedWithoutScheme { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public string HostWithPort { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty; // e.g. github.com
    public string DisplayHost { get; set; } = string.Empty;
    public string DisplayPath { get; set; } = string.Empty;
    public bool IsValid { get; set; }
}

public static class UrlResolver
{
    private static readonly HttpClient HttpClient = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 5,
        ConnectTimeout = TimeSpan.FromSeconds(1.5)
    })
    {
        Timeout = TimeSpan.FromSeconds(2.5)
    };

    public static ParsedUrlInfo Parse(string? inputUrl)
    {
        var raw = (inputUrl ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(raw))
        {
            return new ParsedUrlInfo
            {
                OriginalUrl = string.Empty,
                NormalizedUrl = "about:blank",
                NormalizedWithoutScheme = "about:blank",
                Host = "about:blank",
                HostWithPort = "about:blank",
                Domain = "blank",
                DisplayHost = "about:blank",
                DisplayPath = string.Empty,
                IsValid = false
            };
        }

        var normalized = raw;
        if (!normalized.Contains("://") && !normalized.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(normalized, @"^[a-zA-Z][a-zA-Z0-9+\-.]*:[^/].*"))
            {
                // Has standard URI scheme like mailto:foo@bar.com
            }
            else
            {
                normalized = "https://" + normalized;
            }
        }

        try
        {
            var uri = new Uri(normalized, UriKind.Absolute);
            var host = uri.Host;
            var hostWithPort = uri.IsDefaultPort || uri.Port <= 0 ? host : $"{host}:{uri.Port}";
            var domain = ExtractDomain(host);

            var pathAndQuery = uri.PathAndQuery;
            var fragment = uri.Fragment;
            var fullPath = string.IsNullOrEmpty(fragment) ? pathAndQuery : (pathAndQuery + fragment);
            if (fullPath == "/") fullPath = string.Empty;

            var noScheme = uri.AbsoluteUri;
            var schemeIdx = noScheme.IndexOf("://", StringComparison.Ordinal);
            if (schemeIdx >= 0)
            {
                noScheme = noScheme.Substring(schemeIdx + 3);
            }

            return new ParsedUrlInfo
            {
                OriginalUrl = raw,
                NormalizedUrl = uri.AbsoluteUri,
                NormalizedWithoutScheme = noScheme,
                Host = host,
                HostWithPort = hostWithPort,
                Domain = domain,
                DisplayHost = hostWithPort,
                DisplayPath = fullPath,
                IsValid = true
            };
        }
        catch
        {
            return new ParsedUrlInfo
            {
                OriginalUrl = raw,
                NormalizedUrl = raw,
                NormalizedWithoutScheme = raw,
                Host = raw,
                HostWithPort = raw,
                Domain = raw,
                DisplayHost = raw,
                DisplayPath = string.Empty,
                IsValid = false
            };
        }
    }

    public static string ExtractDomain(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return string.Empty;

        // If host is an IP address (IPv4 or IPv6), preserve it as full host
        if (System.Net.IPAddress.TryParse(host, out _))
        {
            return host;
        }

        var parts = host.Split('.');
        if (parts.Length <= 2) return host;

        // Check common multi-part TLDs (e.g. .com.cn, .co.uk, .gov.cn)
        if (parts.Length >= 3)
        {
            var secondToLast = parts[^2].ToLowerInvariant();
            var last = parts[^1].ToLowerInvariant();
            if ((secondToLast is "com" or "net" or "org" or "gov" or "edu" or "co") && last.Length == 2)
            {
                return $"{parts[^3]}.{parts[^2]}.{parts[^1]}";
            }
        }

        return $"{parts[^2]}.{parts[^1]}";
    }

    public static bool IsShortUrl(string url, List<string>? shorteners)
    {
        if (string.IsNullOrWhiteSpace(url) || shorteners == null || shorteners.Count == 0)
            return false;

        var parsed = Parse(url);
        if (!parsed.IsValid) return false;

        foreach (var s in shorteners)
        {
            if (parsed.Host.Equals(s, StringComparison.OrdinalIgnoreCase) ||
                parsed.Host.EndsWith("." + s, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static async Task<string> ResolveRedirectAsync(string url, List<string> shorteners, CancellationToken ct = default)
    {
        try
        {
            if (!IsShortUrl(url, shorteners))
                return url;

            var parsed = Parse(url);
            using var req = new HttpRequestMessage(HttpMethod.Head, parsed.NormalizedUrl);
            req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) LinkFlow/1.0");

            var response = await HttpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.RequestMessage?.RequestUri != null)
            {
                var targetUri = response.RequestMessage.RequestUri.AbsoluteUri;
                if (!string.Equals(targetUri, parsed.NormalizedUrl, StringComparison.OrdinalIgnoreCase))
                {
                    return targetUri;
                }
            }

            // If HEAD was rejected (e.g. 405 Method Not Allowed), retry with GET
            if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.Found && response.StatusCode != System.Net.HttpStatusCode.MovedPermanently)
            {
                using var getReq = new HttpRequestMessage(HttpMethod.Get, parsed.NormalizedUrl);
                getReq.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) LinkFlow/1.0");
                var getRes = await HttpClient.SendAsync(getReq, HttpCompletionOption.ResponseHeadersRead, ct);
                if (getRes.RequestMessage?.RequestUri != null)
                {
                    return getRes.RequestMessage.RequestUri.AbsoluteUri;
                }
            }
        }
        catch
        {
            // Ignore timeout or resolution errors, fallback to original
        }

        return url;
    }
}
