using System.Net.Sockets;
using System.Text;

namespace SteamServerTool.Core.Services;

/// <summary>
/// Implements the Source Engine RCON protocol over TCP.
/// Packet format: int32 length | int32 id | int32 type | string body (null-terminated) | null byte
/// </summary>
public class RconClient : IDisposable
{
    private const int SERVERDATA_AUTH = 3;
    private const int SERVERDATA_AUTH_RESPONSE = 2;
    private const int SERVERDATA_EXECCOMMAND = 2;
    private const int SERVERDATA_RESPONSE_VALUE = 0;

    private readonly string _host;
    private readonly int _port;
    private readonly string _password;

    private TcpClient? _client;
    private NetworkStream? _stream;
    private int _packetId = 1;

    public bool IsConnected => _client?.Connected == true && _stream != null;

    public RconClient(string host, int port, string password)
    {
        _host = host;
        _port = port;
        _password = password;
    }

    public async Task<bool> ConnectAsync()
    {
        try
        {
            _client = new TcpClient();
            await _client.ConnectAsync(_host, _port);
            _stream = _client.GetStream();

            var authId = _packetId++;
            await SendPacketAsync(authId, SERVERDATA_AUTH, _password);

            var response = await ReadPacketAsync();
            if (response == null) return false;

            // Auth response: id == -1 means failed
            return response.Value.Id != -1;
        }
        catch
        {
            Disconnect();
            return false;
        }
    }

    public async Task<string> SendCommandAsync(string command)
    {
        if (!IsConnected)
            throw new InvalidOperationException("Not connected to RCON server.");

        var cmdId = _packetId++;
        await SendPacketAsync(cmdId, SERVERDATA_EXECCOMMAND, command);

        var sb = new StringBuilder();

        // Send a follow-up empty command to detect end of multi-packet responses
        var endId = _packetId++;
        await SendPacketAsync(endId, SERVERDATA_EXECCOMMAND, "");

        while (true)
        {
            var packet = await ReadPacketAsync();
            if (packet == null) break;
            if (packet.Value.Id == endId) break;
            if (packet.Value.Id == cmdId)
                sb.Append(packet.Value.Body);
        }

        return sb.ToString();
    }

    public void Disconnect()
    {
        _stream?.Close();
        _client?.Close();
        _stream = null;
        _client = null;
    }

    private async Task SendPacketAsync(int id, int type, string body)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        // length = id(4) + type(4) + body + 2 null bytes
        var length = 4 + 4 + bodyBytes.Length + 2;

        var packet = new byte[4 + length];
        WriteInt32(packet, 0, length);
        WriteInt32(packet, 4, id);
        WriteInt32(packet, 8, type);
        Array.Copy(bodyBytes, 0, packet, 12, bodyBytes.Length);
        // last two bytes are already 0 (null terminators)

        await _stream!.WriteAsync(packet, 0, packet.Length);
        await _stream.FlushAsync();
    }

    private async Task<(int Id, int Type, string Body)?> ReadPacketAsync()
    {
        var lengthBuf = new byte[4];
        if (!await ReadExactAsync(lengthBuf, 4)) return null;
        var length = ReadInt32(lengthBuf, 0);

        if (length < 10 || length > 4096 + 10) return null;

        var payload = new byte[length];
        if (!await ReadExactAsync(payload, length)) return null;

        var id = ReadInt32(payload, 0);
        var type = ReadInt32(payload, 4);
        // body starts at offset 8, ends before the two null bytes
        var bodyLength = length - 8 - 2;
        var body = bodyLength > 0 ? Encoding.UTF8.GetString(payload, 8, bodyLength) : "";

        return (id, type, body);
    }

    private async Task<bool> ReadExactAsync(byte[] buffer, int count)
    {
        var read = 0;
        while (read < count)
        {
            var n = await _stream!.ReadAsync(buffer, read, count - read);
            if (n == 0) return false;
            read += n;
        }
        return true;
    }

    private static void WriteInt32(byte[] buf, int offset, int value)
    {
        buf[offset] = (byte)(value & 0xFF);
        buf[offset + 1] = (byte)((value >> 8) & 0xFF);
        buf[offset + 2] = (byte)((value >> 16) & 0xFF);
        buf[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    private static int ReadInt32(byte[] buf, int offset)
    {
        return buf[offset]
               | (buf[offset + 1] << 8)
               | (buf[offset + 2] << 16)
               | (buf[offset + 3] << 24);
    }

    public void Dispose() => Disconnect();
}
