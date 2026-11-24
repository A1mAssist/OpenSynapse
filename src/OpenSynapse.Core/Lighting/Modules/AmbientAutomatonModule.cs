namespace OpenSynapse.Core.Lighting;

/// <summary>Conway's life on the matrix, advanced by elapsed wall time rather than by frames.</summary>
internal sealed class AmbientAutomatonModule : IRenderModuleFactory
{
    public RenderModuleInfo Info { get; } = new("ambient.automaton", "元胞自动机");

    public IRenderModule Create(RenderModuleParams p)
    {
        var config = new Config(
            Period: p.Number("period", 0.35, 0.08, 5),
            Density: p.Number("density", 0.32, 0.05, 0.9),
            Decay: p.Number("decay", 0.35, 0, 0.95),
            Gain: p.Number("gain", 1, 0, 2));
        p.EnsureKnown();
        return new Layer(config);
    }

    private readonly record struct Config(double Period, double Density, double Decay, double Gain);

    private sealed class Layer(Config c) : IRenderModule
    {
        public void Render(in RenderModuleContext context)
        {
            AmbientPaint.Clear(context.Output);
            var t = double.IsFinite(context.Seconds) ? Math.Max(0, context.Seconds) : 0;
            var target = (int)Math.Floor(t / c.Period);
            var state = context.State.Tag as LifeState;
            if (state is null || state.Rows != context.Rows || state.Columns != context.Columns ||
                target < state.Step)
            {
                state = new LifeState(context.Rows, context.Columns, target, c.Density);
                context.State.Tag = state;
            }
            // shortcut: a large time jump resyncs instead of replaying every generation, refresh when exact history matters.
            if (target - state.Step > 96)
            {
                state = new LifeState(context.Rows, context.Columns, target, c.Density);
                context.State.Tag = state;
            }

            while (state.Step < target)
            {
                Advance(state);
                state.Step++;
            }

            var columns = context.Columns;
            for (var row = 0; row < context.Rows; row++)
            for (var column = 0; column < columns; column++)
            {
                var index = row * columns + column;
                var glow = state.Glow[index];
                if (glow <= 0.003) continue;
                var color = context.Palette.Sample(AmbientMath.Hash(column, row, 71));
                AmbientPaint.Put(context.Output, context.Rows, columns, row, column, color,
                    Math.Pow(glow, 1.6) * c.Gain);
            }
        }

        private void Advance(LifeState state)
        {
            var rows = state.Rows;
            var columns = state.Columns;
            for (var row = 0; row < rows; row++)
            for (var column = 0; column < columns; column++)
            {
                var neighbors = 0;
                for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var y = (row + dy + rows) % rows;
                    var x = (column + dx + columns) % columns;
                    if (state.Cells[y * columns + x]) neighbors++;
                }
                var index = row * columns + column;
                state.Next[index] = state.Cells[index] ? neighbors is 2 or 3 : neighbors == 3;
            }

            for (var index = 0; index < state.Cells.Length; index++)
            {
                state.Glow[index] *= (float)(1 - c.Decay);
                if (state.Next[index]) state.Glow[index] = 1;
            }
            (state.Cells, state.Next) = (state.Next, state.Cells);
        }
    }

    private sealed class LifeState
    {
        public LifeState(int rows, int columns, int step, double density)
        {
            Rows = rows;
            Columns = columns;
            Step = step;
            Cells = new bool[rows * columns];
            for (var index = 0; index < Cells.Length; index++)
                Cells[index] = AmbientMath.Hash(index, step, 89) < density;
            Next = new bool[Cells.Length];
            Glow = new float[Cells.Length];
            for (var index = 0; index < Cells.Length; index++)
                if (Cells[index]) Glow[index] = 1;
        }

        public int Rows { get; }
        public int Columns { get; }
        public int Step { get; set; }
        public bool[] Cells { get; set; }
        public bool[] Next { get; set; }
        public float[] Glow { get; }
    }
}
