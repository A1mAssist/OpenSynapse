namespace OpenSynapse.App;

public partial class App
{
    internal async Task SetChromaSourceAllowedAsync(string title, bool allowed)
    {
        title = title.Trim();
        var previouslyBlocked = _behaviorSettings.ChromaBlockedSources.Contains(title);
        if (allowed) _behaviorSettings.ChromaBlockedSources.Remove(title);
        else _behaviorSettings.ChromaBlockedSources.Add(title);
        try
        {
            _behaviorSettings.Save();
        }
        catch
        {
            if (previouslyBlocked) _behaviorSettings.ChromaBlockedSources.Add(title);
            else _behaviorSettings.ChromaBlockedSources.Remove(title);
            try { _behaviorSettings.Save(); }
            catch (Exception saveException) when (saveException is IOException or UnauthorizedAccessException or
                InvalidOperationException or System.Security.SecurityException)
            {
                _diagnosticLog.TryWrite("chroma-rest", $"source preference rollback failed: {saveException}");
            }
            throw;
        }
        // A restoration failure must not silently allow a source the user blocked.
        if (_chromaRestHost is { } host)
            await host.SetSourceAllowedAsync(title, allowed);
    }
}
