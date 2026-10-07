using System;
using System.Linq;
using System.Text.RegularExpressions;
using LinkFlow.Models;

namespace LinkFlow.Services;

public record RuleMatchResult(BrowserItem Browser, string? ProfileId, RoutingRule MatchedRule);

public static class RuleMatcher
{
    public static RuleMatchResult? Match(string url, AppConfig config, bool ignoreAlwaysPrompt = false)
    {
        if (config.AlwaysPrompt && !ignoreAlwaysPrompt)
            return null;

        var parsed = UrlResolver.Parse(url);

        foreach (var rule in config.Rules.Where(r => r.IsEnabled))
        {
            if (string.IsNullOrWhiteSpace(rule.Pattern))
                continue;

            bool isMatch = false;
            var pattern = rule.Pattern.Trim();

            switch (rule.MatchType)
            {
                case RuleMatchType.Domain:
                    // Normalize pattern if user accidentally entered full URL or leading wildcards
                    var domainPattern = pattern;
                    if (domainPattern.Contains("://"))
                    {
                        var pUri = UrlResolver.Parse(domainPattern);
                        domainPattern = !string.IsNullOrEmpty(pUri.Domain) && pUri.Domain != "blank" ? pUri.Domain : pUri.HostWithPort;
                    }
                    else if (domainPattern.Contains('/'))
                    {
                        domainPattern = domainPattern.Split('/')[0];
                    }

                    if (domainPattern.StartsWith("*."))
                    {
                        domainPattern = domainPattern[2..];
                    }

                    if (!string.IsNullOrWhiteSpace(domainPattern))
                    {
                        if (domainPattern.Contains(':'))
                        {
                            if (parsed.HostWithPort.Equals(domainPattern, StringComparison.OrdinalIgnoreCase) ||
                                parsed.HostWithPort.EndsWith("." + domainPattern, StringComparison.OrdinalIgnoreCase))
                            {
                                isMatch = true;
                            }
                        }
                        else
                        {
                            if (parsed.Host.Equals(domainPattern, StringComparison.OrdinalIgnoreCase) ||
                                parsed.Host.EndsWith("." + domainPattern, StringComparison.OrdinalIgnoreCase) ||
                                parsed.Domain.Equals(domainPattern, StringComparison.OrdinalIgnoreCase))
                            {
                                isMatch = true;
                            }
                        }
                    }
                    break;

                case RuleMatchType.Hostname:
                    var hostPattern = pattern;
                    if (hostPattern.Contains("://"))
                    {
                        var pUri = UrlResolver.Parse(hostPattern);
                        hostPattern = pUri.HostWithPort;
                    }
                    else if (hostPattern.Contains('/'))
                    {
                        hostPattern = hostPattern.Split('/')[0];
                    }

                    if (hostPattern.Contains(':'))
                    {
                        if (parsed.HostWithPort.Equals(hostPattern, StringComparison.OrdinalIgnoreCase))
                        {
                            isMatch = true;
                        }
                    }
                    else
                    {
                        if (parsed.Host.Equals(hostPattern, StringComparison.OrdinalIgnoreCase))
                        {
                            isMatch = true;
                        }
                    }
                    break;

                case RuleMatchType.Prefix:
                    var prefixPattern = pattern;
                    // If user didn't specify protocol in pattern, allow matching URL without protocol
                    if (!prefixPattern.Contains("://"))
                    {
                        if (parsed.NormalizedWithoutScheme.StartsWith(prefixPattern, StringComparison.OrdinalIgnoreCase) ||
                            parsed.NormalizedUrl.StartsWith(prefixPattern, StringComparison.OrdinalIgnoreCase) ||
                            parsed.OriginalUrl.StartsWith(prefixPattern, StringComparison.OrdinalIgnoreCase))
                        {
                            isMatch = true;
                        }
                    }
                    else
                    {
                        if (parsed.NormalizedUrl.StartsWith(prefixPattern, StringComparison.OrdinalIgnoreCase) ||
                            parsed.OriginalUrl.StartsWith(prefixPattern, StringComparison.OrdinalIgnoreCase))
                        {
                            isMatch = true;
                        }
                    }
                    break;

                case RuleMatchType.Contains:
                    if (parsed.NormalizedUrl.Contains(pattern, StringComparison.OrdinalIgnoreCase) ||
                        parsed.OriginalUrl.Contains(pattern, StringComparison.OrdinalIgnoreCase) ||
                        parsed.NormalizedWithoutScheme.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    {
                        isMatch = true;
                    }
                    break;

                case RuleMatchType.Regex:
                    try
                    {
                        if (Regex.IsMatch(parsed.NormalizedUrl, pattern, RegexOptions.IgnoreCase) ||
                            Regex.IsMatch(parsed.OriginalUrl, pattern, RegexOptions.IgnoreCase) ||
                            Regex.IsMatch(parsed.NormalizedWithoutScheme, pattern, RegexOptions.IgnoreCase))
                        {
                            isMatch = true;
                        }
                    }
                    catch
                    {
                        // Invalid regex, skip
                    }
                    break;
            }

            if (isMatch)
            {
                var targetBrowser = config.Browsers.FindBrowser(rule.TargetBrowserId);
                if (targetBrowser != null && !targetBrowser.IsDisabled)
                {
                    return new RuleMatchResult(targetBrowser, rule.TargetProfileId, rule);
                }
            }
        }

        return null;
    }
}
