namespace OpenSynapse.Core.Lighting;

/// <summary>Live screen mappings and local system indicators. Missing sources remain dark.</summary>
internal static class SystemRenderModules
{
    public static IReadOnlyList<IRenderModuleFactory> All { get; } = Array.AsReadOnly<IRenderModuleFactory>(
    [
        new ScreenModule("screen.follow", "屏幕跟随", StudioScreenMode.Follow),
        new ScreenModule("screen.band", "屏幕色带", StudioScreenMode.Band),
        new ScreenModule("screen.dominant", "区域主色", StudioScreenMode.Dominant),
        new ScreenModule("screen.monet", "莫奈配色", StudioScreenMode.Monet),
        new SystemModule("system.time-progress", "时间进度"),
        new SystemModule("system.load", "电脑负载"),
    ]);

    private sealed class ScreenModule(string id, string name, StudioScreenMode mode) : IRenderModuleFactory
    {
        public RenderModuleInfo Info { get; } = new(id, name);

        public IRenderModule Create(RenderModuleParams parameters)
        {
            var x = parameters.Number("x", 0, 0, 1);
            var y = parameters.Number("y", 0, 0, 1);
            var width = parameters.Number("width", 1 - x, StudioScreenRect.MinimumSize, 1);
            var height = parameters.Number("height", 1 - y, StudioScreenRect.MinimumSize, 1);
            if (width < StudioScreenRect.MinimumSize || height < StudioScreenRect.MinimumSize ||
                x + width > 1 || y + height > 1)
                throw new InvalidDataException($"Module '{id}' region must fit inside the captured screen.");
            var effect = new StudioEffectParams
            {
                Effect = StudioEffectKind.Ambient,
                ScreenMode = mode,
                Screen = "custom",
                ScreenRect = new StudioScreenRect(x, y, width, height),
                Blur = parameters.Integer("blur", 0, 0, 20),
            };
            parameters.EnsureKnown();
            return new ScreenLayer(effect);
        }
    }

    private sealed class ScreenLayer(StudioEffectParams effect) : IRenderModule
    {
        public void Render(in RenderModuleContext context)
        {
            Array.Clear(context.Output);
            var uv = context.ScreenSamplerUv;
            if (uv is null && context.ScreenSampler is { } grid)
            {
                var rows = context.Rows;
                var columns = context.Columns;
                uv = (u, v) => grid(Math.Clamp((int)(u * columns), 0, columns - 1),
                    Math.Clamp((int)(v * rows), 0, rows - 1));
            }
            if (uv is null) return;
            // Module regions are relative to the sampler's already captured crop.
            var frame = StudioEffectRenderer.Render(effect, context.Rows, context.Columns, context.Seconds,
                new StudioEffectInput(ScreenSamplerUv: uv), context.State);
            frame.CopyTo(context.Output, 0);
        }
    }

    private sealed class SystemModule(string id, string name) : IRenderModuleFactory
    {
        public RenderModuleInfo Info { get; } = new(id, name);

        public IRenderModule Create(RenderModuleParams parameters)
        {
            parameters.EnsureKnown();
            return new SystemLayer(id);
        }
    }

    private sealed class SystemLayer(string id) : IRenderModule
    {
        public void Render(in RenderModuleContext context)
        {
            Array.Clear(context.Output);
            if (context.System is not { } system) return;
            switch (id)
            {
                case "system.time-progress":
                    var hours = system.Now.TimeOfDay.TotalHours;
                    Bar(context, 0, hours % 1);
                    if (context.Rows > 1) Bar(context, 1, Math.Clamp((hours - 9) / 9, 0, 1));
                    break;
                case "system.load":
                    Bar(context, 0, system.CpuLoad);
                    if (context.Rows > 1) Bar(context, 1, system.MemoryLoad);
                    break;
            }
        }
    }

    private static void Bar(in RenderModuleContext context, int row, double? value)
    {
        if (value is not { } amount || !double.IsFinite(amount)) return;
        amount = Math.Clamp(amount, 0, 1);
        for (var column = 0; column < context.Columns; column++)
        {
            var coverage = Math.Clamp(amount * context.Columns - column, 0, 1);
            context.Output[row * context.Columns + column] = StudioRgb.Lerp(StudioRgb.Black,
                context.Palette.Sample(column / (double)Math.Max(1, context.Columns - 1)), coverage);
        }
    }
}
