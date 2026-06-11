using System.Text.RegularExpressions;
using WindowsMonitor.Core.Models;

namespace WindowsMonitor.Core.Services;

public sealed class RuleMatcher
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    public IEnumerable<RuleMatch> Match(IEnumerable<MonitorRule> rules, MonitorInput input)
    {
        foreach (var rule in rules)
        {
            if (!CanEvaluate(rule, input))
            {
                continue;
            }

            foreach (var keyword in rule.Keywords.Where(static item => !string.IsNullOrWhiteSpace(item)))
            {
                if (!IsKeywordMatch(input.Text, keyword, rule, input.Type))
                {
                    continue;
                }

                yield return new RuleMatch(rule, input, keyword, BuildSnippet(input.Text, keyword));
            }
        }
    }

    private static bool CanEvaluate(MonitorRule rule, MonitorInput input)
    {
        if (!rule.Enabled || !rule.ContentTypes.Contains(input.Type))
        {
            return false;
        }

        if ((rule.RuleType == MonitorRuleType.WindowTitle && input.Type != MonitorContentType.WindowTitle) ||
            (rule.RuleType == MonitorRuleType.Ocr && input.Type != MonitorContentType.OcrText) ||
            (rule.RuleType == MonitorRuleType.TaskbarFlash && input.Type != MonitorContentType.TaskbarFlash))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(rule.ProcessName) &&
            !string.Equals(rule.ProcessName, input.ProcessName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var title = input.Window?.Title;
        if (string.IsNullOrWhiteSpace(title) && input.Type == MonitorContentType.WindowTitle)
        {
            title = input.Text;
        }

        if (!string.IsNullOrWhiteSpace(rule.WindowTitlePattern) &&
            !string.IsNullOrWhiteSpace(title) &&
            !title.Contains(rule.WindowTitlePattern, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static bool IsKeywordMatch(string text, string keyword, MonitorRule rule, MonitorContentType inputType)
    {
        var comparison = rule.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        return rule.MatchMode switch
        {
            MatchMode.Contains => text.Contains(keyword, comparison) ||
                                  (inputType == MonitorContentType.OcrText && IsOcrTolerantMatch(text, keyword, rule.CaseSensitive)),
            MatchMode.WholeWord => Regex.IsMatch(
                text,
                $@"(?<!\w){Regex.Escape(keyword)}(?!\w)",
                rule.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase,
                RegexTimeout),
            MatchMode.Regex => Regex.IsMatch(
                text,
                keyword,
                rule.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase,
                RegexTimeout),
            _ => false
        };
    }

    private static bool IsOcrTolerantMatch(string text, string keyword, bool caseSensitive)
    {
        var normalizedText = NormalizeOcrText(text, caseSensitive);
        var normalizedKeyword = NormalizeOcrText(keyword, caseSensitive);
        if (normalizedText.Length == 0 || normalizedKeyword.Length == 0)
        {
            return false;
        }

        if (normalizedText.Contains(normalizedKeyword, StringComparison.Ordinal))
        {
            return true;
        }

        if (normalizedKeyword.Length < 3)
        {
            return false;
        }

        var maxDistance = normalizedKeyword.Length >= 6 ? 2 : 1;
        var minLength = Math.Max(1, normalizedKeyword.Length - maxDistance);
        var maxLength = normalizedKeyword.Length + maxDistance;
        for (var start = 0; start < normalizedText.Length; start++)
        {
            for (var length = minLength; length <= maxLength && start + length <= normalizedText.Length; length++)
            {
                if (LevenshteinDistanceWithin(normalizedKeyword, normalizedText.Substring(start, length), maxDistance))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string NormalizeOcrText(string text, bool caseSensitive)
    {
        var chars = text
            .Where(static item => !char.IsWhiteSpace(item) && !char.IsPunctuation(item) && !char.IsSeparator(item))
            .ToArray();
        var normalized = new string(chars);
        return caseSensitive ? normalized : normalized.ToUpperInvariant();
    }

    private static bool LevenshteinDistanceWithin(string left, string right, int maxDistance)
    {
        if (Math.Abs(left.Length - right.Length) > maxDistance)
        {
            return false;
        }

        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];
        for (var column = 0; column <= right.Length; column++)
        {
            previous[column] = column;
        }

        for (var row = 1; row <= left.Length; row++)
        {
            current[0] = row;
            var rowMin = current[0];
            for (var column = 1; column <= right.Length; column++)
            {
                var cost = left[row - 1] == right[column - 1] ? 0 : 1;
                current[column] = Math.Min(
                    Math.Min(current[column - 1] + 1, previous[column] + 1),
                    previous[column - 1] + cost);
                rowMin = Math.Min(rowMin, current[column]);
            }

            if (rowMin > maxDistance)
            {
                return false;
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length] <= maxDistance;
    }

    private static string BuildSnippet(string text, string keyword)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var index = text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return text.Length <= 120 ? text : text[..120];
        }

        var start = Math.Max(0, index - 40);
        var length = Math.Min(text.Length - start, keyword.Length + 80);
        return text.Substring(start, length);
    }
}
