using System.Net;
using System.Net.Sockets;

namespace NoiseMap;

/// <summary>
/// Background service that opens passive listeners on a set of well-known honeypot ports.
/// For each inbound TCP connection or UDP datagram, it grabs the source IP, GeoIP-resolves it,
/// and publishes a NoiseEvent to the EventBus for all connected WebSocket clients.
///
/// Every TCP connection is immediately closed — we send no banner.
/// Every UDP datagram is read and discarded — we send no reply.
/// </summary>
public sealed class HoneypotService : BackgroundService
{
    private static readonly Dictionary<int, string> PortNames = new()
    {
        { 21,    "FTP"      },
        { 22,    "SSH"      },
        { 23,    "Telnet"   },
        { 80,    "HTTP"     },
        { 443,   "HTTPS"    },
        { 445,   "SMB"      },
        { 1433,  "MSSQL"    },
        { 3306,  "MySQL"    },
        { 3389,  "RDP"      },
        { 5900,  "VNC"      },
        { 6379,  "Redis"    },
        { 8080,  "HTTP-ALT" },
        { 27017, "MongoDB"  },
        { 53,    "DNS"      },
        { 123,   "NTP"      },
        { 161,   "SNMP"     },
    };

    private readonly ILogger<HoneypotService> _log;
    private readonly EventBus                 _bus;
    private readonly GeoIpService             _geo;
    private readonly int[]                    _tcpPorts;
    private readonly int[]                    _udpPorts;

    public HoneypotService(
        IConfiguration cfg,
        ILogger<HoneypotService> log,
        EventBus bus,
        GeoIpService geo)
    {
        _log      = log;
        _bus      = bus;
        _geo      = geo;
        _tcpPorts = cfg.GetSection("NoiseMap:HoneypotTcpPorts").Get<int[]>()
                    ?? [21, 22, 23, 80, 445, 1433, 3306, 3389, 5900, 6379, 8080, 27017];
        _udpPorts = cfg.GetSection("NoiseMap:HoneypotUdpPorts").Get<int[]>()
                    ?? [53, 123, 161];
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var tasks = new List<Task>();

        foreach (var port in _tcpPorts)
            tasks.Add(ListenTcp(port, ct));

        foreach (var port in _udpPorts)
            tasks.Add(ListenUdp(port, ct));

        _log.LogInformation(
            "Honeypot active — TCP: [{Tcp}]  UDP: [{Udp}]",
            string.Join(", ", _tcpPorts),
            string.Join(", ", _udpPorts));

        await Task.WhenAll(tasks);
    }

    private async Task ListenTcp(int port, CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Any, port);
        try
        {
            listener.Start();
            _log.LogInformation("TCP listener started on port {Port} ({Name})",
                port, PortName(port));
        }
        catch (SocketException ex)
        {
            _log.LogWarning("Cannot bind TCP port {Port} ({Name}): {Msg} — skipping",
                port, PortName(port), ex.Message);
            return;
        }

        try
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await listener.AcceptTcpClientAsync(ct); }
                catch (OperationCanceledException) { break; }
                catch (SocketException ex)
                {
                    _log.LogDebug("TCP accept error on {Port}: {Msg}", port, ex.Message);
                    continue;
                }

                // Fire-and-forget: immediately close connection, then publish event.
                _ = Task.Run(() =>
                {
                    try
                    {
                        var ep  = client.Client.RemoteEndPoint as IPEndPoint;
                        var ip  = ep?.Address.ToString() ?? "0.0.0.0";
                        client.Close();
                        Emit(ip, port, "TCP");
                    }
                    catch { /* ignore */ }
                }, ct);
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    private async Task ListenUdp(int port, CancellationToken ct)
    {
        UdpClient udp;
        try
        {
            udp = new UdpClient(port);
            _log.LogInformation("UDP listener started on port {Port} ({Name})",
                port, PortName(port));
        }
        catch (SocketException ex)
        {
            _log.LogWarning("Cannot bind UDP port {Port} ({Name}): {Msg} — skipping",
                port, PortName(port), ex.Message);
            return;
        }

        try
        {
            while (!ct.IsCancellationRequested)
            {
                UdpReceiveResult result;
                try { result = await udp.ReceiveAsync(ct); }
                catch (OperationCanceledException) { break; }
                catch (SocketException ex)
                {
                    _log.LogDebug("UDP receive error on {Port}: {Msg}", port, ex.Message);
                    continue;
                }

                var ip = result.RemoteEndPoint.Address.ToString();
                _ = Task.Run(() => Emit(ip, port, "UDP"), ct);
            }
        }
        finally
        {
            udp.Close();
        }
    }

    private void Emit(string ip, int port, string proto)
    {
        var geo = _geo.Lookup(ip);
        var evt = new NoiseEvent(
            Ts:          DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SrcIp:       ip,
            DstPort:     port,
            Proto:       proto,
            Lat:         geo.Lat,
            Lon:         geo.Lon,
            Country:     geo.Country,
            CountryName: geo.CountryName,
            City:        geo.City,
            PortName:    PortName(port),
            Asn:         null   // reserved — add GeoLite2-ASN.mmdb lookup here if wanted
        );
        _bus.Publish(evt);
    }

    private static string PortName(int port) =>
        PortNames.TryGetValue(port, out var n) ? n : $"PORT-{port}";
}
