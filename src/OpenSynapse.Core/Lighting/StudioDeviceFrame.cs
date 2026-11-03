using OpenSynapse.Core.Lighting;

namespace OpenSynapse.Core.Lighting;

/// <summary>One device's rendered frame plus its matrix dimensions.</summary>
public sealed record StudioDeviceFrame(int Rows, int Columns, StudioRgb[] Frame);
