namespace OpenSynapse.Core.Sensors;

public interface IPowerSourceProvider
{
    bool? IsPluggedIn { get; }
}
