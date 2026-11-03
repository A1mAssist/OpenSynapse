using OpenSynapse.Core.Displays;
using OpenSynapse.Core.Sensors;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class EcoDisplayPolicyTests
{
    [Fact]
    public void RestoreIsRetriedUntilTheAppliedRateIsConfirmed()
    {
        var policy = new EcoDisplayPolicy();
        Assert.True(policy.SetActive(true));
        Assert.Equal(60, policy.Resolve([60, 240], 240, null));
        policy.ConfirmApplied(60, null);
        Assert.True(policy.SetActive(false));
        Assert.Equal(240, policy.Resolve([60, 240], 60, null));
        policy.ConfirmApplied(60, null);
        Assert.True(policy.SetActive(false));
        Assert.Equal(240, policy.Resolve([60, 240], 60, null));
        policy.ConfirmApplied(240, null);
        Assert.False(policy.SetActive(false));
        Assert.Null(policy.Resolve([60, 240], 240, null));
    }

    [Fact]
    public void ConfiguredRateReplacesTheLoanOnlyAfterSuccessfulApplication()
    {
        var policy = new EcoDisplayPolicy();
        policy.SetActive(true);
        policy.Resolve([60, 120, 240], 240, null);
        Assert.Equal(120, policy.Resolve([60, 120, 240], 60, 120));
        policy.SetActive(false);
        Assert.Equal(240, policy.Resolve([60, 120, 240], 60, null));
        policy.ConfirmApplied(120, 120);
        Assert.Null(policy.Resolve([60, 120, 240], 120, null));
    }

    [Fact]
    public void TemporarilyMissingRestoreRateDoesNotEraseTheLoan()
    {
        var policy = new EcoDisplayPolicy();
        policy.SetActive(true);
        policy.Resolve([60, 240], 240, null);
        policy.SetActive(false);
        Assert.Null(policy.Resolve([60], 60, null));
        Assert.Equal(240, policy.Resolve([60, 240], 60, null));
    }

    [Fact]
    public void SelectPrefersSixtyHertzThenTheLowestSupportedRate()
    {
        Assert.Equal(60, EcoDisplayPolicy.Select([60, 120, 240], null));
        Assert.Equal(48, EcoDisplayPolicy.Select([48, 120, 240], null));
        Assert.Null(EcoDisplayPolicy.Select([], null));
        Assert.Equal(120, EcoDisplayPolicy.Select([60, 120, 240], 120));
    }

    [Theory]
    [InlineData(true, PowerSourceKind.Battery, true)]
    [InlineData(true, PowerSourceKind.FullPowerAdapter, false)]
    [InlineData(false, PowerSourceKind.Battery, false)]
    public void PowerSavingRequiresIntegratedModeOnBattery(
        bool discreteGpuDisabled,
        PowerSourceKind powerSource,
        bool expected)
    {
        Assert.Equal(
            expected,
            EcoDisplayPolicy.IsPowerSaving(discreteGpuDisabled, powerSource));
    }
}
