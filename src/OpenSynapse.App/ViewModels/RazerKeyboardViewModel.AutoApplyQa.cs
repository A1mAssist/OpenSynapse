#if DEBUG
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerKeyboardViewModel
{
    internal static RazerKeyboardViewModel CreateAutoApplyPreview()
    {
        var profile = ProfileDocument.CreateDefault();
        var vm = new RazerKeyboardViewModel(new("qa-keyboard", 0x0249, "Razer Pro Type · QA", 91, false),
            new AutoApplyTransport(), () => profile, _ => Task.FromResult(true));
        vm.ApplyState(new(null, null, null, null, false, 1000, []));
        vm.ApplyLighting(new(60, RazerChromaEffect.Spectrum, [], null, null, null, null, []));
        return vm;
    }

    internal static async Task<string> VerifyAutoApplyAsync()
    {
        var profile = ProfileDocument.CreateDefault();
        var transport = new AutoApplyTransport();
        var failSave = false;
        var vm = new RazerKeyboardViewModel(new("qa-keyboard", 0x0249, "QA", 91, false), transport,
            () => profile, _ => { var saved = !failSave; failSave = false; return Task.FromResult(saved); });
        var token = CancellationToken.None;
        void Check(bool condition, string detail)
        {
            if (!condition) throw new InvalidOperationException("Keyboard auto apply: " + detail);
        }
        vm.ApplyState(new(null, null, null, null, false, 1000, []));
        vm.ApplyLighting(new(60, RazerChromaEffect.Spectrum, [], null, null, null, null, []));
        Check(vm.PrepareGameModeAutoApply(token) is null && vm.PreparePollingAutoApply(token) is null &&
            vm.PrepareBrightnessAutoApply(token) is null && transport.Writes == 0, "readback wrote");

        vm.GameModeEnabled = true;
        var game = vm.PrepareGameModeAutoApply(token)!;
        vm.GameModeEnabled = false;
        Check(await game() is null && transport.GameMode &&
            profile.Devices[vm.ProfileDeviceKey].Keyboard.GamingModeEnabled == true, "game mode snapshot");

        vm.SelectedPollingIndex = 4;
        var polling = vm.PreparePollingAutoApply(token)!;
        vm.SelectedPollingIndex = 6;
        Check(await polling() is null && transport.Polling == 0x10 &&
            profile.Devices[vm.ProfileDeviceKey].Keyboard.PollingRateHertz == 500, "polling snapshot");

        vm.Brightness = 80;
        var brightness = vm.PrepareBrightnessAutoApply(token)!;
        failSave = true;
        Check(!string.IsNullOrEmpty(await brightness()) && vm.Brightness == 60 &&
            transport.Brightness == RazerChromaStandardProtocol.ToBrightnessLevel(60), "save rollback");
        Check(await brightness() is null && profile.Devices[vm.ProfileDeviceKey].Chroma?.BrightnessPercent == 80,
            "retry did not save captured brightness");
        Check(vm.PrepareBrightnessAutoApply(token) is null, "saved brightness remained dirty");

        vm.GameModeEnabled = false;
        var failed = vm.PrepareGameModeAutoApply(token)!;
        transport.FailNextWrite = true;
        Check(!string.IsNullOrEmpty(await failed()) &&
            profile.Devices[vm.ProfileDeviceKey].Keyboard.GamingModeEnabled == true, "rejected write persisted");
        Check(await failed() is null, "rejected write could not retry");

        vm.SelectedPollingIndex = 3;
        var stale = vm.PreparePollingAutoApply(token)!;
        var before = transport.Writes;
        profile.ActiveProfileName = "Other";
        Check(!string.IsNullOrEmpty(await stale()) && transport.Writes == before, "active profile crossed");
        stale = vm.PreparePollingAutoApply(token)!;
        profile = ProfileDocument.CreateDefault();
        Check(!string.IsNullOrEmpty(await stale()) && transport.Writes == before, "document crossed");
        using var cancelled = new CancellationTokenSource();
        var cancel = vm.PreparePollingAutoApply(cancelled.Token)!;
        cancelled.Cancel();
        Check(!string.IsNullOrEmpty(await cancel()) && transport.Writes == before, "cancelled write ran");
        vm._restoringProfile = true;
        Check(vm.PreparePollingAutoApply(token) is null, "profile restore prepared another write");
        return "PASS keyboard: readback, captured values, rollback/retry, rejected write, profile/cancellation guards";
    }

    private sealed class AutoApplyTransport : IRazerFeatureTransport
    {
        internal bool GameMode;
        internal byte Polling = 0x08;
        internal byte Brightness = 153;
        internal int Writes;
        internal bool FailNextWrite;

        public Task<byte[]> QueryAsync(string devicePath, byte transactionId, byte dataSize, byte commandClass,
            byte commandId, ReadOnlyMemory<byte> arguments, TimeSpan deviceWait,
            CancellationToken cancellationToken, bool allowRemainingPacketsMismatch = false) =>
            throw new NotSupportedException();

        public Task<byte[]> QueryPreparedAsync(string devicePath, ReadOnlyMemory<byte> request,
            TimeSpan deviceWait, CancellationToken cancellationToken, bool allowRemainingPacketsMismatch = false)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (devicePath != "qa-keyboard") throw new InvalidOperationException("Unexpected QA target");
            var bytes = request.ToArray();
            var command = (bytes[7], bytes[8]);
            if (command is (0x00, 0x08) or (0x00, 0x40) or (0x10, 0x05))
            {
                Writes++;
                if (FailNextWrite) { FailNextWrite = false; throw new IOException("QA rejected write"); }
                if (command == (0x00, 0x08)) GameMode = bytes[9] != 0;
                else if (command == (0x00, 0x40)) Polling = bytes[10];
                else Brightness = bytes[11];
            }
            byte[] data = command switch
            {
                (0x00, 0x88) or (0x00, 0x08) => [(byte)(GameMode ? 1 : 0), 0, 0, 0],
                (0x00, 0xC0) or (0x00, 0x40) => [0, Polling],
                (0x10, 0x85) or (0x10, 0x05) => [Brightness],
                _ => throw new NotSupportedException(),
            };
            var response = new byte[RazerFeatureReport.Length];
            response[1] = 2; response[2] = bytes[2]; response[6] = (byte)data.Length;
            response[7] = bytes[7]; response[8] = bytes[8];
            data.CopyTo(response, RazerFeatureReport.ArgumentsOffset);
            response[89] = RazerFeatureReport.CalculateCrc(response);
            return Task.FromResult(response);
        }
    }
}
#endif
