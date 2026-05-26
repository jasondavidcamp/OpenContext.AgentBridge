using OpenContext.AgentBridge.Core.Skills;

namespace OpenContext.AgentBridge.Tests;

public sealed class SkillLoaderTests
{
    [Fact]
    public async Task LoadAsync_uses_markdown_heading_as_skill_name()
    {
        var root = CreateTempDirectory();

        try
        {
            var skillFile = Path.Combine(root, "splunk.md");
            await File.WriteAllTextAsync(skillFile, "# Splunk Search\nUse SPL carefully.");

            var skills = await new SkillLoader().LoadAsync(new[] { root });

            var skill = Assert.Single(skills);
            Assert.Equal("Splunk Search", skill.Name);
            Assert.Equal(skillFile, skill.Path);
            Assert.Contains("Use SPL carefully.", skill.Instructions);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_ignores_readme_files()
    {
        var root = CreateTempDirectory();

        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "README.md"), "# Skills");

            var skills = await new SkillLoader().LoadAsync(new[] { root });

            Assert.Empty(skills);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SkillSelector_returns_all_skills_when_no_names_are_requested()
    {
        var skills = new[]
        {
            new Skill("PowerShell", "powershell.md", "instructions"),
            new Skill(".NET", "dotnet.md", "instructions")
        };

        var selected = SkillSelector.Select(skills, Array.Empty<string>());

        Assert.Equal(skills, selected);
    }

    [Fact]
    public void SkillSelector_matches_by_heading_filename_or_comma_separated_names()
    {
        var skills = new[]
        {
            new Skill("PowerShell", "powershell.md", "instructions"),
            new Skill("Azure DevOps Server", "azure-devops-server.md", "instructions")
        };

        var selected = SkillSelector.Select(skills, new[] { "power-shell,azure-devops-server" });

        Assert.Equal(new[] { "PowerShell", "Azure DevOps Server" }, selected.Select(skill => skill.Name));
    }

    [Fact]
    public void SkillSelector_throws_for_unknown_skill()
    {
        var skills = new[]
        {
            new Skill("PowerShell", "powershell.md", "instructions")
        };

        var ex = Assert.Throws<ArgumentException>(() => SkillSelector.Select(skills, new[] { "splunk" }));

        Assert.Contains("Unknown skill(s): splunk", ex.Message);
        Assert.Contains("Available skills: PowerShell", ex.Message);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"agentbridge-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
