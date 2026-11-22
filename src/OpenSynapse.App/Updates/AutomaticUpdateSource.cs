using Velopack;
using Velopack.Logging;
using Velopack.Sources;

namespace OpenSynapse.App.Updates;

/// <summary>
/// Automatic mode queries both mirrors, uses the higher version, and prefers
/// Gitee when the versions match. A stale Gitee mirror therefore cannot hide a
/// newer GitHub release, while a blocked GitHub endpoint cannot prevent an
/// update that Gitee already has.
/// </summary>
internal sealed class AutomaticUpdateSource : IUpdateSource
{
    private static readonly TimeSpan FeedTimeout = TimeSpan.FromSeconds(12);
    private readonly IUpdateSource _gitee;
    private readonly IUpdateSource _github;
    private IUpdateSource? _preferred;

    public AutomaticUpdateSource(IUpdateSource gitee, IUpdateSource github)
    {
        _gitee = gitee ?? throw new ArgumentNullException(nameof(gitee));
        _github = github ?? throw new ArgumentNullException(nameof(github));
    }

    public async Task<VelopackAssetFeed> GetReleaseFeed(
        IVelopackLogger logger,
        string? appId,
        string channel,
        Guid? stagingId,
        VelopackAsset? latestLocalRelease)
    {
        var giteeTask = TryGetFeedAsync(
            _gitee, logger, appId, channel, stagingId, latestLocalRelease);
        var githubTask = TryGetFeedAsync(
            _github, logger, appId, channel, stagingId, latestLocalRelease);
        var results = await Task.WhenAll(giteeTask, githubTask);
        var candidates = results
            .Where(result => result.Feed is { Assets.Length: > 0 })
            .OrderByDescending(result => result.Feed!.Assets.Max(asset => asset.Version))
            .ThenBy(result => ReferenceEquals(result.Source, _gitee) ? 0 : 1)
            .ToArray();
        if (candidates.Length == 0)
        {
            throw new AggregateException(
                "Neither Gitee nor GitHub returned a usable update feed.",
                results.Select(result => result.Error).OfType<Exception>());
        }

        _preferred = candidates[0].Source;
        logger?.Log(
            VelopackLogLevel.Information,
            $"Automatic update source selected {SourceName(_preferred)}.",
            exception: null);
        return candidates[0].Feed!;
    }

    public async Task DownloadReleaseEntry(
        IVelopackLogger logger,
        VelopackAsset releaseEntry,
        string localFile,
        Action<int> progress,
        CancellationToken cancelToken)
    {
        var sources = ReferenceEquals(_preferred, _github)
            ? new[] { _github, _gitee }
            : new[] { _gitee, _github };
        var errors = new List<Exception>();
        foreach (var source in sources)
        {
            try
            {
                await source.DownloadReleaseEntry(
                    logger,
                    releaseEntry,
                    localFile,
                    progress,
                    cancelToken);
                return;
            }
            catch (OperationCanceledException) when (cancelToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                errors.Add(exception);
                TryDelete(localFile);
            }
        }

        throw new AggregateException(
            $"Could not download {releaseEntry.FileName} from either update source.",
            errors);
    }

    private static async Task<FeedResult> TryGetFeedAsync(
        IUpdateSource source,
        IVelopackLogger logger,
        string? appId,
        string channel,
        Guid? stagingId,
        VelopackAsset? latestLocalRelease)
    {
        try
        {
            var task = source.GetReleaseFeed(
                logger, appId, channel, stagingId, latestLocalRelease);
            var completed = await Task.WhenAny(task, Task.Delay(FeedTimeout));
            if (!ReferenceEquals(completed, task))
            {
                ObserveFault(task);
                return new FeedResult(
                    source,
                    null,
                    new TimeoutException(
                        $"{SourceName(source)} did not return an update feed within " +
                        $"{FeedTimeout.TotalSeconds:0} seconds."));
            }

            return new FeedResult(source, await task, null);
        }
        catch (Exception exception)
        {
            return new FeedResult(source, null, exception);
        }
    }

    private static void ObserveFault(Task task) =>
        _ = task.ContinueWith(
            completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);

    private static string SourceName(IUpdateSource source) =>
        source is GiteeSource ? "Gitee" : "GitHub";

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private readonly record struct FeedResult(
        IUpdateSource Source,
        VelopackAssetFeed? Feed,
        Exception? Error);
}
