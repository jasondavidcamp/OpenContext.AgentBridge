namespace OpenContext.AgentBridge.Core.Skills;

public sealed class SkillLoader
{
    public async Task<IReadOnlyList<Skill>> LoadAsync(
        IEnumerable<string> directories,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(directories);

        var skills = new List<Skill>();

        foreach (var directory in directories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*.md", SearchOption.TopDirectoryOnly))
            {
                if (string.Equals(Path.GetFileName(file), "README.md", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var instructions = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
                skills.Add(new Skill(GetSkillName(file, instructions), file, instructions));
            }
        }

        return skills
            .OrderBy(skill => skill.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string GetSkillName(string file, string instructions)
    {
        using var reader = new StringReader(instructions);

        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                return line[2..].Trim();
            }
        }

        return Path.GetFileNameWithoutExtension(file);
    }
}
