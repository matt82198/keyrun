using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// TOOLKIT-CONTRACT lane L2 SIM-DISPLACE: <c>{"op":"displace"}</c> parses from ability data like the other C#-only ops
/// (<c>pull</c>, <c>damagePct</c>). The embedded tree carries no displace op yet (lane L4 owns Blink and Charge data), so the
/// test swaps Undertow's pull for a displace in an in-memory copy of the tree and loads that.
/// </summary>
public sealed class DisplaceOpParseTests
{
    private sealed class SwappedSource : IContentSource
    {
        private readonly IContentSource _inner = EmbeddedContentSource.Instance;
        private readonly string _path;
        private readonly byte[] _bytes;

        public SwappedSource(string path, byte[] bytes)
        {
            _path = path;
            _bytes = bytes;
        }

        public IReadOnlyCollection<string> Paths => _inner.Paths;

        public byte[] Read(string path) => path == _path ? _bytes : _inner.Read(path);
    }

    [Fact]
    public void A_displace_op_in_ability_data_loads_as_a_DisplaceOp_with_its_fields()
    {
        string path = EmbeddedContentSource.Instance.Paths.Single(p =>
            p.StartsWith("abilities/", StringComparison.Ordinal) && Encoding.UTF8.GetString(EmbeddedContentSource.Instance.Read(p)).Contains("drowned.undertow", StringComparison.Ordinal));
        JsonNode root = JsonNode.Parse(EmbeddedContentSource.Instance.Read(path))!;
        JsonNode undertow = (root is JsonArray arr ? arr.Select(n => n!) : new[] { root }).First(a => (string)a["id"]! == "drowned.undertow");
        JsonArray effects = undertow["effects"]!.AsArray();
        int at = effects.ToList().FindIndex(e => (string)e!["op"]! == "pull");
        effects[at] = JsonNode.Parse("{\"op\":\"displace\",\"distanceMm\":8000,\"freesBinds\":true,\"towardTarget\":true}");

        ContentRegistry reg = ContentLoader.Load(new SwappedSource(path, Encoding.UTF8.GetBytes(root.ToJsonString())));

        DisplaceOp op = Assert.Single(reg.Abilities["drowned.undertow"].Effects.OfType<DisplaceOp>());
        Assert.Equal(8000, op.DistanceMm);
        Assert.True(op.FreesBinds);
        Assert.True(op.TowardTarget);
    }
}
