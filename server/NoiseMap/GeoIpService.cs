using System.Collections.Concurrent;
using System.Net;
using MaxMind.GeoIP2;
using MaxMind.GeoIP2.Exceptions;

namespace NoiseMap;

public sealed record GeoResult(
    double Lat, double Lon,
    string Country, string CountryName,
    string? City);

/// <summary>
/// Thin wrapper around a local MaxMind GeoLite2-City.mmdb database.
/// Caches results by IP address to avoid redundant disk reads.
/// Download the free database at: https://dev.maxmind.com/geoip/geolite2-free-geolocation-data
/// </summary>
public sealed class GeoIpService : IDisposable
{
    private readonly ILogger<GeoIpService> _log;
    private readonly string _dbPath;
    private DatabaseReader? _reader;

    // Simple in-memory cache: IP string → result (or null for private/unknown)
    private readonly ConcurrentDictionary<string, GeoResult?> _cache = new();

    private static readonly GeoResult Unknown = new(0, 0, "??", "Unknown", null);

    public GeoIpService(IConfiguration cfg, ILogger<GeoIpService> log)
    {
        _log    = log;
        _dbPath = cfg["NoiseMap:GeoLite2CityDbPath"] ?? "GeoLite2-City.mmdb";
    }

    /// <summary>Call once at startup to open the mmdb file.</summary>
    public void Initialize()
    {
        if (!File.Exists(_dbPath))
        {
            _log.LogWarning("GeoLite2-City.mmdb not found at {Path}. GeoIP disabled — " +
                            "download it from https://dev.maxmind.com/geoip/geolite2-free-geolocation-data",
                            _dbPath);
            return;
        }
        _reader = new DatabaseReader(_dbPath);
        _log.LogInformation("GeoIP database loaded from {Path}", _dbPath);
    }

    /// <summary>
    /// Look up a source IP address. Returns Unknown for private/loopback addresses
    /// or when the database is unavailable.
    /// </summary>
    public GeoResult Lookup(string ipStr)
    {
        if (_cache.TryGetValue(ipStr, out var cached))
            return cached ?? Unknown;

        var result = DoLookup(ipStr);
        _cache.TryAdd(ipStr, result);
        return result ?? Unknown;
    }

    private GeoResult? DoLookup(string ipStr)
    {
        if (_reader is null) return null;

        // Skip private / loopback ranges — they'll never GeoIP correctly.
        if (!IPAddress.TryParse(ipStr, out var addr)) return null;
        if (IsPrivate(addr)) return null;

        try
        {
            var r = _reader.City(addr);
            return new GeoResult(
                r.Location.Latitude  ?? 0,
                r.Location.Longitude ?? 0,
                r.Country.IsoCode    ?? "??",
                r.Country.Name       ?? "Unknown",
                r.City.Name);
        }
        catch (AddressNotFoundException) { return null; }
        catch (Exception ex)
        {
            _log.LogDebug("GeoIP lookup failed for {IP}: {Msg}", ipStr, ex.Message);
            return null;
        }
    }

    private static bool IsPrivate(IPAddress addr)
    {
        if (addr.IsLoopback()) return true;
        if (addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 &&
            addr.IsIPv6LinkLocal) return true;

        // IPv4 private ranges
        var b = addr.GetAddressBytes();
        if (b.Length != 4) return false;
        return b[0] == 10
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168);
    }

    public void Dispose() => _reader?.Dispose();
}
