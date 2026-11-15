using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Lighting;

public sealed partial class BladeLightingController
{
    private sealed class EffectFrameSource : ISoftwareLightingFrameSource
    {
        private readonly BladeLightingEffect _effect;
        private readonly StarlightLightingRenderer? _starlight;

        public EffectFrameSource(BladeLightingEffect effect)
        {
            _effect = effect;
            if (effect.Mode == BladeLightingMode.Starlight)
            {
                _starlight = new StarlightLightingRenderer(effect.Color, seed: 710);
            }
        }

        public ValueTask<IReadOnlyList<RazerRgb>> RenderAsync(
            TimeSpan elapsed,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<RazerRgb> frame = _effect.Mode switch
            {
                BladeLightingMode.Off => QuickLightingEngine.RenderSolid(default),
                BladeLightingMode.Static => QuickLightingEngine.RenderSolid(_effect.Color),
                BladeLightingMode.Breathing => QuickLightingEngine.RenderBreathing(elapsed, _effect.Color),
                BladeLightingMode.Spectrum => QuickLightingEngine.RenderSpectrum(
                    elapsed, QuickLightingEngine.Rows, QuickLightingEngine.Columns, _effect.Palette),
                BladeLightingMode.Wave => QuickLightingEngine.RenderWave(
                    elapsed, _effect.Direction, QuickLightingEngine.Rows, QuickLightingEngine.Columns, _effect.Palette),
                BladeLightingMode.Fire => QuickLightingEngine.RenderFire(
                    elapsed, 100, QuickLightingEngine.Rows, QuickLightingEngine.Columns, _effect.Palette),
                BladeLightingMode.Wheel => QuickLightingEngine.RenderWheel(
                    elapsed,
                    _effect.Direction == BladeWaveDirection.Left
                        ? QuickLightingDirection.CounterClockwise
                        : QuickLightingDirection.Clockwise,
                    QuickLightingEngine.Rows, QuickLightingEngine.Columns, _effect.Palette,
                    _effect.AnimationSpeed),
                BladeLightingMode.Starlight => _starlight!.Render(elapsed),
                BladeLightingMode.Tidal => QuickLightingEngine.RenderTidal(
                    elapsed, _effect.Color, _effect.SecondColor, _effect.AnimationSpeed),
                _ => throw new InvalidOperationException("Unsupported Blade lighting mode."),
            };
            return ValueTask.FromResult(frame);
        }
    }

    private sealed class KeyboardEffectFrameSource(
        BladeLightingEffect effect,
        WindowsKeyboardLightingAdapter adapter) : ISoftwareLightingFrameSource
    {
        private static readonly TimeSpan Duration = TimeSpan.FromSeconds(1);
        private readonly List<QuickLightingKeyEvent> _events = [];

        public ValueTask<IReadOnlyList<RazerRgb>> RenderAsync(
            TimeSpan elapsed,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            adapter.DrainTo(_events);
            _events.RemoveAll(item => item.At <= elapsed - Duration);
            IReadOnlyList<RazerRgb> frame = effect.Mode switch
            {
                BladeLightingMode.Reactive =>
                    QuickLightingEngine.RenderReactive(elapsed, _events, effect.Color, Duration),
                BladeLightingMode.Ripple =>
                    QuickLightingEngine.RenderRipple(elapsed, _events, effect.Color, Duration),
                _ => throw new InvalidOperationException("The keyboard input source supports only Reactive or Ripple."),
            };
            return ValueTask.FromResult(frame);
        }
    }

    private sealed class AudioMeterFrameSource(
        WasapiAudioMeterAdapter adapter,
        LightingPalette? palette) : ISoftwareLightingFrameSource
    {
        private double _smoothedLevel;
        private TimeSpan _lastElapsed;

        public ValueTask<IReadOnlyList<RazerRgb>> RenderAsync(
            TimeSpan elapsed,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var level = adapter.ReadLevel();
            var delta = elapsed - _lastElapsed;
            _smoothedLevel = _lastElapsed == TimeSpan.Zero
                ? level
                : QuickLightingEngine.SmoothAudioLevel(_smoothedLevel, level, delta);
            _lastElapsed = elapsed;
            return ValueTask.FromResult<IReadOnlyList<RazerRgb>>(
                QuickLightingEngine.RenderAudioMeter(
                    _smoothedLevel,
                    colorBoost: 0,
                    QuickLightingEngine.Rows,
                    QuickLightingEngine.Columns,
                    palette));
        }
    }

    private sealed class AmbientFrameSource(WindowsDisplayCaptureAdapter adapter) : ISoftwareLightingFrameSource
    {
        public async ValueTask<IReadOnlyList<RazerRgb>> RenderAsync(
            TimeSpan elapsed,
            CancellationToken cancellationToken)
        {
            var frame = await adapter.ReadFrameAsync(cancellationToken).ConfigureAwait(false);
            return QuickLightingEngine.RenderAmbientAwareness(
                frame.Pixels,
                frame.Width,
                frame.Height);
        }
    }
}
