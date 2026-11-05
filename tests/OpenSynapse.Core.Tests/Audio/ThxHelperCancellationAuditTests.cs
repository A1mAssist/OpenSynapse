using System.Diagnostics;
using System.Text;
using OpenSynapse.Windows.Audio;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class ThxHelperCancellationAuditTests
{
    [Fact]
    public async Task CancelledSentRequestCannotPoisonTheNextEndpointResponse()
    {
        using var fixture = new FakeHelper();
        await using var helper = new ThxHelperProcess(fixture.StartInfo);
        using var cancellation = new CancellationTokenSource();
        var first = helper.SendAsync(new(1, "hold", "Speakers"), cancellation.Token);
        await fixture.WaitUntilRequestArrivesAsync();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        var second = await helper.SendAsync(new(1, "read", "Headphones"), default);
        Assert.Equal("Headphones", second.Error);
        Assert.Equal(2, fixture.Starts);
    }

    [Fact]
    public async Task CancellationWhileWaitingForGateDoesNotStopAnotherRequest()
    {
        using var fixture = new FakeHelper();
        await using var helper = new ThxHelperProcess(fixture.StartInfo);
        var first = helper.SendAsync(new(1, "hold", "Speakers"), default);
        await fixture.WaitUntilRequestArrivesAsync();
        using var cancellation = new CancellationTokenSource();
        var queued = helper.SendAsync(new(1, "read", "Headphones"), cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        fixture.Release();
        Assert.Equal("Speakers", (await first).Error);
        Assert.Equal(1, fixture.Starts);
    }

    private sealed class FakeHelper : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "OpenSynapse-ThxAudit-" + Guid.NewGuid().ToString("N"));
        internal int Starts;
        internal FakeHelper() => Directory.CreateDirectory(_directory);
        internal ProcessStartInfo StartInfo()
        {
            Starts++;
            var marker = Path.Combine(_directory, "arrived").Replace("'", "''");
            var release = Path.Combine(_directory, "release").Replace("'", "''");
            var script = $$"""
                [Console]::InputEncoding = [Text.UTF8Encoding]::new($false)
                [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
                while ($null -ne ($line = [Console]::ReadLine())) {
                    $request = ConvertFrom-Json -InputObject $line
                    [IO.File]::WriteAllText('{{marker}}', 'ready')
                    if ($request.Operation -eq 'hold') {
                        while (-not [IO.File]::Exists('{{release}}')) { Start-Sleep -Milliseconds 10 }
                    }
                    $reply = @{ Version = 1; Ok = $true; Status = @{ Availability = 2; Error = $request.Endpoint; State = $null }; Error = $null }
                    [Console]::WriteLine(($reply | ConvertTo-Json -Depth 5 -Compress))
                }
                """;
            var info = new ProcessStartInfo
            {
                FileName = "pwsh.exe", UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = new UTF8Encoding(false),
            };
            info.ArgumentList.Add("-NoProfile"); info.ArgumentList.Add("-NonInteractive");
            info.ArgumentList.Add("-EncodedCommand");
            info.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
            return info;
        }
        internal async Task WaitUntilRequestArrivesAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!File.Exists(Path.Combine(_directory, "arrived"))) await Task.Delay(20, timeout.Token);
        }
        internal void Release() => File.WriteAllText(Path.Combine(_directory, "release"), "release");
        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
