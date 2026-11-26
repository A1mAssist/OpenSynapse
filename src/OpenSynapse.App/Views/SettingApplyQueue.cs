namespace OpenSynapse.App.Views;

// Used on the UI thread: pending edits to one setting replace each other.
internal sealed class SettingApplyQueue
{
    private readonly LinkedList<(object Key, Func<Task> Apply)> _pending = new();
    private bool _running;
    internal Task WhenIdle { get; private set; } = Task.CompletedTask;

    internal void Enqueue(object key, Func<Task> apply)
    {
        for (var node = _pending.First; node is not null; node = node.Next)
        {
            if (!Equals(node.Value.Key, key)) continue;
            node.Value = (key, apply);
            return;
        }
        _pending.AddLast((key, apply));
        if (_running) return;
        _running = true;
        WhenIdle = DrainAsync();
    }

    private async Task DrainAsync()
    {
        try
        {
            while (_pending.First is { } next)
            {
                _pending.RemoveFirst();
                await next.Value.Apply();
            }
        }
        finally { _running = false; }
    }
}
