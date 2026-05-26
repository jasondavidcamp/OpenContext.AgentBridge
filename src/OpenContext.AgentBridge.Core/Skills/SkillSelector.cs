namespace OpenContext.AgentBridge.Core.Skills;

public static class SkillSelector
{
    public static IReadOnlyList<Skill> Select(
        IReadOnlyList<Skill> skills,
        IEnumerable<string> requestedSkills)
    {
        ArgumentNullException.ThrowIfNull(skills);
        ArgumentNullException.ThrowIfNull(requestedSkills);

        var requested = requestedSkills
            .SelectMany(SplitSkillNames)
            .Select(name => name.Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (requested.Length == 0)
        {
            return skills;
        }

        var selected = new List<Skill>();
        var missing = new List<string>();

        foreach (var requestedSkill in requested)
        {
            var match = skills.FirstOrDefault(skill => IsMatch(skill, requestedSkill));
            if (match is null)
            {
                missing.Add(requestedSkill);
                continue;
            }

            if (!selected.Any(skill => string.Equals(skill.Path, match.Path, StringComparison.OrdinalIgnoreCase)))
            {
                selected.Add(match);
            }
        }

        if (missing.Count > 0)
        {
            var available = skills.Count == 0
                ? "none"
                : string.Join(", ", skills.Select(skill => skill.Name));
            throw new ArgumentException(
                $"Unknown skill(s): {string.Join(", ", missing)}. Available skills: {available}.");
        }

        return selected;
    }

    private static bool IsMatch(Skill skill, string requestedSkill)
    {
        var fileName = Path.GetFileNameWithoutExtension(skill.Path);

        return string.Equals(skill.Name, requestedSkill, StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, requestedSkill, StringComparison.OrdinalIgnoreCase)
            || string.Equals(Normalize(skill.Name), Normalize(requestedSkill), StringComparison.Ordinal)
            || string.Equals(Normalize(fileName), Normalize(requestedSkill), StringComparison.Ordinal);
    }

    private static IEnumerable<string> SplitSkillNames(string value)
    {
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string Normalize(string value)
    {
        return new string(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }
}
