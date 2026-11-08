namespace OpenSynapse.Core.Profiles;

/// <summary>
/// Runs one profile-backed device change: write the hardware first, change the
/// in-memory profile only after that succeeded, then save. A failed save restores
/// both the profile value and the hardware value.
/// </summary>
public static class ProfileSettingEdit
{
    public static async Task ApplyAsync<T>(
        Func<T> read,
        Action<T> write,
        T value,
        Func<CancellationToken, Task> applyAsync,
        Func<CancellationToken, Task> saveAsync,
        Func<T, CancellationToken, Task>? rollbackAsync = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(write);
        ArgumentNullException.ThrowIfNull(applyAsync);
        ArgumentNullException.ThrowIfNull(saveAsync);
        cancellationToken.ThrowIfCancellationRequested();

        var previous = read();
        await applyAsync(cancellationToken);
        try
        {
            write(value);
            await saveAsync(cancellationToken);
        }
        catch
        {
            write(previous);
            if (rollbackAsync is not null)
            {
                try
                {
                    await rollbackAsync(previous, CancellationToken.None);
                }
                catch
                {
                    // The save failure is the error the caller must see; the hardware
                    // rollback is best effort and must not mask it.
                }
            }
            throw;
        }
    }
}
