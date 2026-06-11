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
                if (!IsKeywordMatch(input.Text, keyword, rule))
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

        if (!string.IsNullOrWhiteSpace(rule.WindowTitlePattern) &&
            input.Window is { Title.Length: > 0 } window &&
            !window.Title.Contains(rule.WindowTitlePattern, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static bool IsKeywordMatch(string text, string keyword, MonitorRule rule)
    {
        var comparison = rule.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        return rule.MatchMode switch
        {
            MatchMode.Contains => text.Contains(keyword, comparison),
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
