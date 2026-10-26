using System.Collections.Generic;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// R-PACKCHOICE-1 (k)(iv) and the S1 spec for per-dungeon forces: the Sunken Bellfoundry must read its own forces bar,
/// not the Hollow Vault's. Today <see cref="ContentLoader"/> reads one forces file, forces/hollow-vault.json
/// (<see cref="ContentLoader.ForcesFile"/>), for every dungeon, so the Bellfoundry's Required is whatever that file says.
/// These tests go through the real loader and never touch shipped data: <see cref="MutableSource"/> is an in-memory copy.
/// </summary>
public sealed class ForcesPerDungeonTests
{
    private const string Dungeon = "sunken-bellfoundry";
    private const string BellfoundryForcesFile = "forces/sunken-bellfoundry.json";
    private const int BellfoundryRequired = 1000;

    /// <summary>Records every path the loader reads, passing the reads through to the embedded content.</summary>
    private sealed class RecordingSource : IContentSource
    {
        private readonly IContentSource _inner;

        public RecordingSource(IContentSource inner) => _inner = inner;

        public List<string> Reads { get; } = new();

        public IReadOnlyCollection<string> Paths => _inner.Paths;

        public byte[] Read(string path)
        {
            Reads.Add(path);
            return _inner.Read(path);
        }
    }

    [Fact(DisplayName = "S1 forces-per-dungeon: loading the Bellfoundry reads its Required from a sunken-bellfoundry forces source, equal to 1000")]
    public void Bellfoundry_required_comes_from_its_own_forces_source()
    {
        var source = new RecordingSource(EmbeddedContentSource.Instance);

        ContentRegistry reg = ContentLoader.Load(source);

        Assert.Contains(BellfoundryForcesFile, source.Reads);
        Assert.Equal(BellfoundryRequired, reg.ForcesFor(Dungeon).Required);
    }

    [Fact(DisplayName = "S1 forces-per-dungeon: changing only forces/hollow-vault.json Required leaves the Bellfoundry Required at 1000")]
    public void Editing_hollow_vault_forces_does_not_change_bellfoundry_required()
    {
        // 900 keeps the hollow-vault layout valid (1200 and 1500 trip its SkipBar and PATH-PACKS rules), so the edit loads and the assertion is what fails.
        var source = new MutableSource().Edit(MutableSource.ForcesHead, n => n["required"] = 900).Build();

        ContentRegistry reg = ContentLoader.Load(source);

        Assert.Equal(BellfoundryRequired, reg.ForcesFor(Dungeon).Required);
    }
}
