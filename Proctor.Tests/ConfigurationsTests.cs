using Proctor;

namespace Proctor.Tests;

/// <summary>The report of an exam with axes (plans/axes-build.md, slice 3): the stratified comparison, checked against numbers worked by hand.</summary>
public class ConfigurationsTests
{
    [Fact]
    public void OneStratum_IsMannWhitney()
    {
        var one = Configurations.Stratified([([4, 5, 6], [1, 2, 3])], higherIsBetter: true);
        Assert.Equal(Tries.MannWhitneyP([4, 5, 6], [1, 2, 3]), one.P, 9);
        Assert.Equal("better", one.Direction);
        Assert.False(one.Confirmed);   // 3 against 3 cannot be confirmed alone: p = 2/20
    }

    [Fact]
    public void TwoStrataTheSameWay_ConfirmWhatNeitherCouldAlone()
    {
        // Each stratum is at its extreme: 1 deal in 20. Both at once, either way round: 2 * (1/20)^2.
        var both = Configurations.Stratified([([4, 5, 6], [1, 2, 3]), ([14, 15, 16], [11, 12, 13])], higherIsBetter: true);
        Assert.Equal(0.005, both.P, 9);
        Assert.True(both.Confirmed);
        Assert.Equal((18, 0, 0), (both.Higher, both.Lower, both.Tied));
        Assert.Equal(0.005, Configurations.MinP([(3, 3), (3, 3)]), 9);
        // A try is ranked only inside its own stratum: the second stratum's values all being higher than the first's changes nothing.
        Assert.Equal(both.P, Configurations.Stratified([([4, 5, 6], [1, 2, 3]), ([4, 5, 6], [1, 2, 3])], higherIsBetter: true).P, 9);
    }

    [Fact]
    public void StrataThatDisagree_CancelOut_AndFewerIsBetterTurnsTheWordRound()
    {
        var opposed = Configurations.Stratified([([4, 5, 6], [1, 2, 3]), ([1, 2, 3], [4, 5, 6])], higherIsBetter: true);
        Assert.Equal("same", opposed.Direction);
        Assert.Equal(1, opposed.P, 9);

        var fewer = Configurations.Stratified([([1, 2, 3], [4, 5, 6]), ([1, 2, 3], [4, 5, 6])], higherIsBetter: false);
        Assert.Equal("better", fewer.Direction);
        Assert.True(fewer.Confirmed);
    }

    [Fact]
    public void OneTryASide_InThreeStrata_CannotConfirm_AndSaysSo()
    {
        // code-change's shape: one try of each assistant in each of three repositories.
        var thin = Configurations.Stratified([([1], [0]), ([1], [0]), ([1], [0])], higherIsBetter: true);
        Assert.Equal(0.25, thin.P, 9);
        Assert.Equal(0.25, Configurations.MinP([(1, 1), (1, 1), (1, 1)]), 9);
        Assert.False(thin.Confirmed);
    }

    [Fact]
    public void Ties_AreHalfAWinEach_AndAllTiedIsTheSame()
    {
        var tied = Configurations.Stratified([([2, 2], [2, 2]), ([3, 3], [3, 3])], higherIsBetter: true);
        Assert.Equal("same", tied.Direction);
        Assert.Equal(8, tied.Tied);
        Assert.Equal(1, tied.P, 9);
    }
}
