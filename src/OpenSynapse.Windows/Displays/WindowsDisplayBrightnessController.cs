using Windows.Graphics.Display;

namespace OpenSynapse.Windows.Displays;

public sealed class WindowsDisplayBrightnessController
{
    private const double Step = 0.1;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<double> StepAsync(
        bool increase,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var brightness = BrightnessOverride.GetDefaultForSystem();
            if (!brightness.IsSupported)
            {
                throw new InvalidOperationException("Windows 未提供可控的内置屏亮度。");
            }

            var target = CalculateStep(brightness.BrightnessLevel, increase);
            brightness.SetBrightnessLevel(target, DisplayBrightnessOverrideOptions.None);
            brightness.StartOverride();
            brightness.StopOverride();
            if (!await BrightnessOverride.SaveForSystemAsync(brightness))
            {
                throw new InvalidOperationException("Windows 拒绝保存内置屏亮度。");
            }

            cancellationToken.ThrowIfCancellationRequested();
            return target;
        }
        finally
        {
            _gate.Release();
        }
    }

    internal static double CalculateStep(double current, bool increase)
    {
        if (!double.IsFinite(current))
        {
            throw new ArgumentOutOfRangeException(nameof(current));
        }

        return Math.Clamp(current + (increase ? Step : -Step), 0, 1);
    }
}
