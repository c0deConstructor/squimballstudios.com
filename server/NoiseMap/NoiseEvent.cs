namespace NoiseMap;

/// <summary>
/// A single honeypot connection event, serialised as JSON and pushed to all WebSocket clients.
/// </summary>
public sealed record NoiseEvent(
    long   Ts,           // Unix ms
    string SrcIp,        // e.g. "1.2.3.4"
    int    DstPort,      // e.g. 22
    string Proto,        // "TCP" | "UDP"
    double Lat,          // GeoIP latitude  (0 if unknown)
    double Lon,          // GeoIP longitude (0 if unknown)
    string Country,      // ISO-3166 two-letter code, e.g. "CN"  ("??" if unknown)
    string CountryName,  // e.g. "China"
    string? City,        // may be null
    string PortName,     // friendly name, e.g. "SSH"
    string? Asn          // e.g. "AS4134"  (null — reserved for future ASN DB)
);
