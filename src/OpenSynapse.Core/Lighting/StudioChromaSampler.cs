namespace OpenSynapse.Core.Lighting;

/// <summary>Plays an imported .chroma keyboard animation on any studio matrix.</summary>
public interface IStudioChromaSampler
{
    /// <summary>Loop length in seconds.</summary>
    double DurationSeconds { get; }

    /// <summary>Returns the frame at <paramref name="seconds"/> resampled to rows x columns.</summary>
    StudioRgb[] Sample(int rows, int columns, double seconds);
}
