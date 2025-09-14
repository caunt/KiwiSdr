using System.Net.WebSockets;
using System.Text;

// =============== Top-level configuration ===============
double centerFrequencyHertz = 7050000.0;
int ioSampleRateHertz = 12000;
string kiwiHost = "okno.ddns.net";
string? kiwiPassword = Environment.GetEnvironmentVariable("KIWI_PWD");
int printEveryPackets = 10;
// =======================================================

// =============== Program entry ===============
var kiwiSettings = new KiwiSettings(kiwiHost, 8073, ioSampleRateHertz, centerFrequencyHertz, kiwiPassword);
var consoleConsumer = new ConsoleIqConsumer(printEveryPackets);
await using var kiwiClient = new KiwiClient(kiwiSettings, consoleConsumer);

Console.WriteLine($"Connecting to {kiwiClient.Settings.WebSocketUri} ...");
await kiwiClient.ConnectAndStreamAsync();
Console.WriteLine("Done. Press ENTER to exit.");
Console.ReadLine();
// ============================================


// ===================== OOP Layer =====================

sealed class KiwiClient : IAsyncDisposable
{
    public KiwiSettings Settings { get; }
    public IAudioConsumer AudioConsumer { get; }
    readonly ClientWebSocket webSocket = new();

    DateTime lastKeepAliveUtc = DateTime.MinValue;
    readonly byte[] receiveBuffer = new byte[1 << 16];
    readonly MemoryStream messageAssembler = new();

    public KiwiClient(KiwiSettings settings, IAudioConsumer audioConsumer)
    {
        Settings = settings;
        AudioConsumer = audioConsumer;
        webSocket.Options.SetRequestHeader("Origin", $"http://{settings.HostName}");
        webSocket.Options.SetRequestHeader("User-Agent", "KiwiClient/OOP");
    }

    public async Task ConnectAndStreamAsync()
    {
        await webSocket.ConnectAsync(Settings.WebSocketUri, CancellationToken.None);
        Console.WriteLine("Connected.");

        foreach (var command in KiwiProtocol.BuildHandshake(Settings))
            await SendTextAsync(command);

        while (webSocket.State == WebSocketState.Open)
        {
            await MaybeSendKeepAliveAsync();

            WebSocketReceiveResult receiveResult;
            try
            {
                receiveResult = await webSocket.ReceiveAsync(new ArraySegment<byte>(receiveBuffer), CancellationToken.None);
            }
            catch (Exception exception)
            {
                Console.WriteLine($"Receive exception: {exception.GetType().Name}: {exception.Message}");
                break;
            }

            if (receiveResult.MessageType == WebSocketMessageType.Close)
            {
                Console.WriteLine($"Server initiated close: {webSocket.CloseStatus} {webSocket.CloseStatusDescription}");
                break;
            }

            messageAssembler.Write(receiveBuffer, 0, receiveResult.Count);
            if (!receiveResult.EndOfMessage) continue;

            var message = messageAssembler.ToArray();
            messageAssembler.SetLength(0);

            if (KiwiProtocol.IsControlMessage(receiveResult.MessageType, message))
            {
                var line = Encoding.UTF8.GetString(message).Trim();
                if (KiwiProtocol.IsAuthFailure(line))
                {
                    Console.WriteLine("Password rejected. Set KIWI_PWD or hardcode the correct password.");
                    break;
                }
                Console.WriteLine(line);
                continue;
            }

            if (message.Length < 4) continue;

            var packet = KiwiProtocol.ParseAudioPacket(message);
            AudioConsumer.OnIqBlock(packet);
        }

        await CloseAsync("bye");
    }

    async Task MaybeSendKeepAliveAsync()
    {
        if ((DateTime.UtcNow - lastKeepAliveUtc).TotalSeconds < 15) return;
        await SendTextAsync("SET keepalive\n");
        lastKeepAliveUtc = DateTime.UtcNow;
    }

    async Task SendTextAsync(string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        await webSocket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
    }

    async Task CloseAsync(string reason)
    {
        if (webSocket.State == WebSocketState.Open)
            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, reason, CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync("dispose");
        webSocket.Dispose();
        messageAssembler.Dispose();
    }
}

static class KiwiProtocol
{
    public static Uri BuildWebSocketUri(string hostName, int port) =>
        new($"ws://{hostName}:{port}/ws/kiwi/{GenerateNewSessionIdentifier()}/SND");

    public static IEnumerable<string> BuildHandshake(KiwiSettings settings)
    {
        yield return !string.IsNullOrEmpty(settings.UserPassword)
            ? $"SET auth t=kiwi p={settings.UserPassword}\n"
            : "SET auth t=kiwi\n";

        yield return $"SET AR OK in={settings.InputOutputSampleRateHertz} out={settings.InputOutputSampleRateHertz}\n";
        yield return "SET compression=0\n";
        yield return "SET ident_user=KiwiClient\n";
        yield return "SET squelch=0 max=0\n";
        yield return "SET agc=1 hang=0 thresh=-110 slope=6 decay=1000 manGain=50\n";
        yield return $"SET mod=iq low_cut=-6000 high_cut=6000 freq={settings.CenterFrequencyHertz / 1e3:F3}\n";
        yield return "SET keepalive\n";
    }

    public static bool IsControlMessage(WebSocketMessageType type, byte[] message)
    {
        if (type == WebSocketMessageType.Text) return true;
        return message.Length >= 4 && message[0] == (byte)'M' && message[1] == (byte)'S' && message[2] == (byte)'G' && message[3] == (byte)' ';
    }

    public static bool IsAuthFailure(string line) =>
        line.Contains("auth") && line.Contains("failed");

    public static KiwiAudioPacket ParseAudioPacket(byte[] message)
    {
        ushort sequence = (ushort)(message[0] | (message[1] << 8));
        byte flags = message[2];
        byte sMeter = message[3];

        int payloadByteCount = message.Length - 4;
        int complexSamplePairs = payloadByteCount / 4;

        double sumOfSquares = 0;
        for (int i = 0; i < complexSamplePairs; i++)
        {
            int p = 4 + 4 * i;
            short i16 = (short)(message[p] | (message[p + 1] << 8));
            short q16 = (short)(message[p + 2] | (message[p + 3] << 8));
            double iFloat = i16 / 32768.0, qFloat = q16 / 32768.0;
            sumOfSquares += iFloat * iFloat + qFloat * qFloat;
        }

        double rootMeanSquare = Math.Sqrt(sumOfSquares / (2.0 * complexSamplePairs));
        return new KiwiAudioPacket(sequence, flags, sMeter, complexSamplePairs, rootMeanSquare, message, 4, payloadByteCount);
    }

    static string GenerateNewSessionIdentifier() =>
        ((ulong)Random.Shared.NextInt64(long.MaxValue)).ToString();
}

sealed record KiwiSettings(
    string HostName,
    int Port,
    int InputOutputSampleRateHertz,
    double CenterFrequencyHertz,
    string? UserPassword)
{
    public Uri WebSocketUri { get; } = KiwiProtocol.BuildWebSocketUri(HostName, Port);
}

sealed record KiwiAudioPacket(
    ushort Sequence,
    byte Flags,
    byte SMeter,
    int ComplexSamplePairs,
    double RootMeanSquare,
    byte[] BackingBuffer,
    int PayloadOffset,
    int PayloadByteCount);

interface IAudioConsumer
{
    void OnIqBlock(KiwiAudioPacket packet);
}

sealed class ConsoleIqConsumer : IAudioConsumer
{
    readonly int printEveryPackets;
    ushort? previousSequence;
    long packetCount, sampleCount;

    public ConsoleIqConsumer(int printEveryPackets = 10) => this.printEveryPackets = Math.Max(1, printEveryPackets);

    public void OnIqBlock(KiwiAudioPacket packet)
    {
        if (previousSequence is ushort expectedPrev && ((ushort)(expectedPrev + 1) != packet.Sequence))
            Console.WriteLine($"packet gap: expected {(ushort)(expectedPrev + 1)}, got {packet.Sequence}");

        previousSequence = packet.Sequence;

        packetCount++; sampleCount += packet.ComplexSamplePairs;
        if ((packetCount % printEveryPackets) != 0) return;

        Console.WriteLine($"SEQ={packet.Sequence} flags=0x{packet.Flags:X2} S={packet.SMeter}  IQ: {packet.ComplexSamplePairs} @ {ProgramIoRate()} Hz, RMS={packet.RootMeanSquare:F3}");
    }

    static int ProgramIoRate()
    {
        // tiny helper to avoid passing around the rate; change if you want multi-rate consumers
        return 12000;
    }
}
// =================== /OOP Layer ===================
