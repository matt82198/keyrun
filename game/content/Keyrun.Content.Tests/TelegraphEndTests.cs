using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// TELEGRAPH-CANCEL, the view half (<see cref="TelegraphEnd"/>). src/View/TelegraphView.cs ends a floor shape on the
/// sim event that ends its telegraph. A <c>telegraph_hit</c> means the cast landed: the shape flashes, then fades. A
/// <c>telegraph_cancel</c> means the cast was kicked, stopped or overwritten and nothing lands: the shape is removed on
/// that tick with no flash, because a flash reads as a hit (BIBLE s3; wrong data shown is P0). Any other event leaves
/// the shape alone.
/// </summary>
public sealed class TelegraphEndTests
{
    [Fact(DisplayName = "A telegraph_hit flashes the shape")]
    public void Hit_flashes() => Assert.Equal(TelegraphEndKind.Flash, TelegraphEnd.Of("telegraph_hit"));

    [Fact(DisplayName = "A telegraph_cancel removes the shape at once, with no flash")]
    public void Cancel_vanishes() => Assert.Equal(TelegraphEndKind.Vanish, TelegraphEnd.Of("telegraph_cancel"));

    [Theory(DisplayName = "Other events leave the shape on the floor")]
    [InlineData("telegraph")]
    [InlineData("interrupt")]
    [InlineData("cast_stopped")]
    [InlineData("cast_complete")]
    public void Other_events_keep_the_shape(string type) => Assert.Equal(TelegraphEndKind.None, TelegraphEnd.Of(type));
}
