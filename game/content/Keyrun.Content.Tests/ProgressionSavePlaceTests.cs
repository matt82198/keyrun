using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// Lane PROGRESSION-SAVE (BIBLE s7 "the chase is gear you keep between runs"): where the between-runs screen keeps the
/// banked progression. The interactive game saves in the Godot user data dir; a headless run (every scene test, CI,
/// the exported-exe e2e) never touches that save unless it names its own directory with <c>--progression-dir</c>.
/// </summary>
public sealed class ProgressionSavePlaceTests
{
    private const string UserDir = "C:/Users/someone/AppData/Roaming/Godot/app_userdata/Keyrun";

    [Fact]
    public void The_interactive_game_saves_in_the_user_data_dir()
    {
        Assert.Equal(UserDir, ProgressionSavePlace.Directory(new string[0], headless: false, UserDir));
        Assert.Equal(UserDir, ProgressionSavePlace.Directory(new[] { "--key", "3" }, headless: false, UserDir));
    }

    [Fact]
    public void A_headless_run_without_a_directory_never_touches_the_user_save()
    {
        Assert.Null(ProgressionSavePlace.Directory(new string[0], headless: true, UserDir));
        Assert.Null(ProgressionSavePlace.Directory(new[] { "--autorun", "--ticks", "600", "--solo" }, headless: true, UserDir));
    }

    [Fact]
    public void An_explicit_directory_wins_headless_or_not()
    {
        var args = new[] { "--autorun", "--progression-dir", "C:/Temp/keyrun-save", "--solo" };
        Assert.Equal("C:/Temp/keyrun-save", ProgressionSavePlace.Directory(args, headless: true, UserDir));
        Assert.Equal("C:/Temp/keyrun-save", ProgressionSavePlace.Directory(args, headless: false, UserDir));
    }

    [Fact]
    public void A_directory_flag_with_no_value_saves_nowhere()
    {
        Assert.Null(ProgressionSavePlace.Directory(new[] { "--progression-dir" }, headless: false, UserDir));
        Assert.Null(ProgressionSavePlace.Directory(new[] { "--progression-dir", "" }, headless: false, UserDir));
        Assert.Null(ProgressionSavePlace.Directory(new[] { "--progression-dir", "--solo" }, headless: true, UserDir));
    }
}
