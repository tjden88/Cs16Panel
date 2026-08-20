using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace Cs16Panel.Services;

public sealed class RconClient
{
    private static readonly Regex ChallengeRegex = new(@"challenge rcon (?<value>-?\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<string> ExecuteAsync(string host, int port, string password, string command, CancellationToken ct = default)
    {
        using var udp = new UdpClient();
        udp.Connect(host, port);

        var challengeRequest = Bytes("\xFF\xFF\xFF\xFFchallenge rcon\n");
        await udp.SendAsync(challengeRequest, ct);

        var challengePacket = await ReceiveAsync(udp, ct, TimeSpan.FromSeconds(2));
        var challengeText = Decode(challengePacket);
        var match = ChallengeRegex.Match(challengeText);
        if (!match.Success)
            throw new InvalidOperationException($"Не удалось получить RCON challenge. Ответ сервера: {challengeText.Trim()}");

        var challenge = match.Groups["value"].Value;
        var payload = $"rcon {challenge} \"{password.Replace("\\", "\\\\").Replace("\"", "\\\"")}\" {command}\n";
        var packet = new byte[4 + Encoding.ASCII.GetByteCount(payload)];
        packet[0] = 0xFF;
        packet[1] = 0xFF;
        packet[2] = 0xFF;
        packet[3] = 0xFF;
        Encoding.ASCII.GetBytes(payload, 0, payload.Length, packet, 4);

        await udp.SendAsync(packet, ct);

        var sb = new StringBuilder();
        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(1200);
        do
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) break;

            try
            {
                var response = await ReceiveAsync(udp, ct, remaining);
                sb.Append(Decode(response));
            }
            catch (TimeoutException)
            {
                break;
            }
        } while (DateTime.UtcNow < deadline);

        return CleanResponse(sb.ToString());
    }

    private static byte[] Bytes(string value) => Encoding.ASCII.GetBytes(value);

    private static async Task<byte[]> ReceiveAsync(UdpClient udp, CancellationToken ct, TimeSpan timeout)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        try
        {
            var result = await udp.ReceiveAsync(timeoutCts.Token);
            return result.Buffer;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException();
        }
    }

    private static string Decode(byte[] packet)
    {
        if (packet.Length <= 4) return string.Empty;
        return Encoding.UTF8.GetString(packet, 4, packet.Length - 4);
    }

    private static string CleanResponse(string response)
    {
        return response.Replace("\0", string.Empty).Trim();
    }
}
