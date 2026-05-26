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

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"agentbridge-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
