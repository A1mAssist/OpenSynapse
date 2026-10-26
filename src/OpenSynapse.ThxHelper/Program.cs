using System.Text;
using System.Text.Json;
using OpenSynapse.Windows.Audio;

ThxAudioHelperSession? laptopSession = null;
ThxHeadsetHelperSession? headsetSession = null;
using var input = new StreamReader(
    Console.OpenStandardInput(),
    new UTF8Encoding(false, true),
    detectEncodingFromByteOrderMarks: false,
    bufferSize: 4096,
    leaveOpen: false);
using var output = new StreamWriter(
    Console.OpenStandardOutput(),
    new UTF8Encoding(false, true),
    bufferSize: 4096,
    leaveOpen: false)
{
    AutoFlush = true,
};

while (await input.ReadLineAsync() is { } line)
{
    ThxAudioHelperResponse response;
    try
    {
        var request = JsonSerializer.Deserialize<ThxAudioHelperRequest>(line)
            ?? throw new InvalidDataException("THX helper request is empty.");
        if (request.NativeVersion is null)
        {
            laptopSession ??= new ThxAudioHelperSession();
            response = laptopSession.Execute(request);
        }
        else
        {
            headsetSession ??= new ThxHeadsetHelperSession();
            response = headsetSession.Execute(request);
        }
    }
    catch (Exception)
    {
        response = new ThxAudioHelperResponse(
            ThxAudioHelperProtocol.Version,
            false,
            null,
            "ThxHelperFailed");
    }

    await output.WriteLineAsync(JsonSerializer.Serialize(response));
}
