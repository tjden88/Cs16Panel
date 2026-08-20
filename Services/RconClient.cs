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

        // 1. Получаем challenge
        var challengeRequest =
            Encoding.ASCII.GetBytes("\xFF\xFF\xFF\xFFchallenge rcon\n");

        await udp.SendAsync(challengeRequest, ct);

        var challengePacket = await ReceiveAsync(
            udp,
            ct,
            TimeSpan.FromSeconds(2));

        var challengeText = Decode(challengePacket);

        var challengeMatch = ChallengeRegex.Match(challengeText);

        if (!challengeMatch.Success)
        {
            throw new InvalidOperationException(
                $"Не удалось получить RCON challenge. Ответ сервера: {challengeText.Trim()}");
        }

        var challenge = challengeMatch.Groups["value"].Value;

        // 2. Отправляем команду
        var payload =
            $"rcon {challenge} \"{Escape(password)}\" {command}\n";

        var payloadBytes = Encoding.ASCII.GetBytes(payload);

        var packet = new byte[4 + payloadBytes.Length];

        packet[0] = 0xFF;
        packet[1] = 0xFF;
        packet[2] = 0xFF;
        packet[3] = 0xFF;

        Buffer.BlockCopy(
            payloadBytes,
            0,
            packet,
            4,
            payloadBytes.Length);

        await udp.SendAsync(packet, ct);

        // 3. Читаем ответ.
        // Для наших коротких команд одного пакета достаточно.
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