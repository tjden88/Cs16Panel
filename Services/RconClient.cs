using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace Cs16Panel.Services;

public sealed class RconClient
{
    private static readonly Regex ChallengeRegex =
        new(@"challenge rcon (?<value>-?\d+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<string> ExecuteAsync(
        string host,
        int port,
        string password,
        string command,
        CancellationToken ct = default)
    {
        using var udp = new UdpClient();

        udp.Connect(host, port);

        // GoldSrc connectionless packet header: FF FF FF FF
        var challengeRequest = Combine(
            [0xFF, 0xFF, 0xFF, 0xFF],
            Encoding.ASCII.GetBytes("challenge rcon\n"));

        await udp.SendAsync(challengeRequest, ct);

        var challengePacket = await ReceiveAsync(
            udp,
            ct,
            TimeSpan.FromSeconds(2));

        var challengeText = Decode(challengePacket);

        var match = ChallengeRegex.Match(challengeText);

        if (!match.Success)
        {
            throw new InvalidOperationException(
                $"Не удалось получить RCON challenge. Ответ сервера: {challengeText.Trim()}");
        }

        var challenge = match.Groups["value"].Value;

        var payload =
            $"rcon {challenge} \"{Escape(password)}\" {command}\n";

        var packet = Combine(
            [0xFF, 0xFF, 0xFF, 0xFF],
            Encoding.ASCII.GetBytes(payload));

        await udp.SendAsync(packet, ct);

        var response = await ReceiveAsync(
            udp,
            ct,
            TimeSpan.FromSeconds(2));

        return Decode(response).Trim('\0', '\r', '\n', ' ');
    }

    private static string Escape(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"");
    }

    private static byte[] Combine(byte[] prefix, byte[] data)
    {
        var result = new byte[prefix.Length + data.Length];

        Buffer.BlockCopy(prefix, 0, result, 0, prefix.Length);
        Buffer.BlockCopy(data, 0, result, prefix.Length, data.Length);

        return result;
    }

    private static async Task<byte[]> ReceiveAsync(
        UdpClient udp,
        CancellationToken ct,
        TimeSpan timeout)
    {
        using var timeoutCts =
            CancellationTokenSource.CreateLinkedTokenSource(ct);

        timeoutCts.CancelAfter(timeout);

        try
        {
            var result = await udp.ReceiveAsync(timeoutCts.Token);
            return result.Buffer;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException("RCON timeout.");
        }
    }

    private static string Decode(byte[] packet)
    {
        if (packet.Length <= 4)
            return string.Empty;

        return Encoding.UTF8.GetString(
            packet,
            4,
            packet.Length - 4);
    }
}