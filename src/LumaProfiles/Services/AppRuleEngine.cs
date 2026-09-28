using LumaProfiles.Models;

namespace LumaProfiles.Services;

public enum RuleAction { None, Apply, Restore }

public sealed record RuleDecision(RuleAction Action, AppProfileRule? Rule = null);

/// <summary>Decides when a foreground-application rule should apply its profile or hand control back.</summary>
public sealed class AppRuleEngine
{
    private readonly string[] _ignoredProcesses;

    public AppRuleEngine(params string[] ignoredProcesses) =>
        _ignoredProcesses = ignoredProcesses.Select(Normalize).ToArray();

    public AppProfileRule? ActiveRule { get; private set; }

    public static string Normalize(string processName)
    {
        var name = processName.Trim();
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    public RuleDecision Evaluate(string? foregroundProcess, IEnumerable<AppProfileRule> rules)
    {
        // Unknown or own windows (including desktop transitions) never change the active state.
        if (string.IsNullOrWhiteSpace(foregroundProcess)) return new RuleDecision(RuleAction.None);
        var process = Normalize(foregroundProcess);
        if (_ignoredProcesses.Contains(process, StringComparer.OrdinalIgnoreCase)) return new RuleDecision(RuleAction.None);

        var match = rules.FirstOrDefault(rule =>
            !string.IsNullOrWhiteSpace(rule.ProcessName) &&
            Normalize(rule.ProcessName).Equals(process, StringComparison.OrdinalIgnoreCase));

        if (match is not null)
        {
            if (ReferenceEquals(match, ActiveRule)) return new RuleDecision(RuleAction.None);
            ActiveRule = match;
            return new RuleDecision(RuleAction.Apply, match);
        }

        if (ActiveRule is null) return new RuleDecision(RuleAction.None);
        ActiveRule = null;
        return new RuleDecision(RuleAction.Restore);
    }

    /// <summary>Forgets the active rule, e.g. when the rule list changed.</summary>
    public void Reset() => ActiveRule = null;
}
