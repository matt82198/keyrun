using System;
using System.IO;
using System.Linq;
using Keyrun.Sim.Tests.Encounter;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// Content JSON lives in two roots: the frozen repo-root <c>content/data/</c> and the C#-only <c>game/content/data/</c>
/// (same logical namespace, Keyrun.Content.csproj). A probe that walks up from bin and takes the FIRST
/// <c>content/data/&lt;kind&gt;</c> it meets lands on <c>game/content/data/</c> as soon as that root grows an
/// <c>enemies/</c>, <c>abilities/</c> or <c>effects/</c> folder (FORCES-ROUTE-PLAN PACK-CONTENT), and every test
/// that reads the repo root breaks. These build a throwaway two-root tree and drive the real probe.
/// </summary>
public sealed class ContentRootsTests : IDisposable
{
    private readonly string _repo = Path.Combine(Path.GetTempPath(), "keyrun-roots-" + Guid.NewGuid().ToString("N"));

    public ContentRootsTests()
    {
        Write("content/data/enemies/choir.json", "[]");
        Write("content/data/abilities/warden.json", "[]");
        Write("game/content/data/layout.json", "{}");
        Write("game/content/data/enemies/vault.json", "[]");
        Write("game/content/data/abilities/vault-casters.json", "[]");
        Directory.CreateDirectory(Bin);
    }

    private string Bin => Path.Combine(_repo, "game", "sim", "Keyrun.Sim.Tests", "bin", "Debug", "net8.0");

    private string At(string relative) => Path.Combine(new[] { _repo }.Concat(relative.Split('/')).ToArray());

    private void Write(string relative, string text)
    {
        string path = At(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    public void Dispose()
    {
        if (Directory.Exists(_repo)) Directory.Delete(_repo, recursive: true);
    }

    [Fact]
    public void Repo_root_is_found_past_a_game_content_data_enemies_folder()
    {
        Assert.Equal(_repo, ContentRoots.RepoRootAbove(Bin));
    }

    [Fact]
    public void Kind_files_come_from_both_roots_repo_root_first()
    {
        Assert.Equal(
            new[] { At("content/data/abilities/warden.json"), At("game/content/data/abilities/vault-casters.json") },
            ContentRoots.KindFiles(Bin, "abilities"));
        Assert.Equal(
            new[] { At("content/data/enemies/choir.json"), At("game/content/data/enemies/vault.json") },
            ContentRoots.KindFiles(Bin, "enemies"));
    }

    [Fact]
    public void A_kind_only_the_repo_root_has_still_resolves()
    {
        Write("content/data/facets/warden.json", "[]");

        Assert.Equal(new[] { At("content/data/facets/warden.json") }, ContentRoots.KindFiles(Bin, "facets"));
    }

    [Fact]
    public void A_tree_with_no_repo_root_fails_closed()
    {
        // Only the C#-only root, with no repo-root content/data beside it: the probe must not settle for it.
        string lone = _repo + "-lone";
        string start = Path.Combine(lone, "game", "sim", "bin");
        Directory.CreateDirectory(Path.Combine(lone, "game", "content", "data", "enemies"));
        Directory.CreateDirectory(start);
        try
        {
            Assert.Throws<DirectoryNotFoundException>(() => ContentRoots.RepoRootAbove(start));
        }
        finally
        {
            Directory.Delete(lone, recursive: true);
        }
    }

    [Fact]
    public void The_real_repo_root_holds_both_content_roots_and_the_kit_agrees()
    {
        string root = ContentRoots.RepoRootAbove(AppContext.BaseDirectory);

        Assert.True(File.Exists(Path.Combine(root, "content", "data", "enemies", "choir.json")), root);
        Assert.True(Directory.Exists(Path.Combine(root, "game", "content", "data")), root);
        Assert.Equal(root, EncounterTestKit.RepoRoot());
    }
}
