using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Froststrap.Models.APIs.RoValra;

namespace Froststrap.Utility.RoValra
{
    public sealed class RegionPingInfo
    {
        public string Key { get; init; } = "";
        public string City { get; init; } = "";
        public string? State { get; init; }
        public string Country { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public double Latitude { get; init; }
        public double Longitude { get; init; }
        public double DistanceKm { get; init; }
        public int PingMs { get; set; }
        public bool IsMeasured { get; set; }
        public List<int> DataCenterIds { get; init; } = [];
        public string? SampleIp { get; set; }

        public string Label =>
            PingMs > 0
                ? $"{DisplayName}  ·  {(IsMeasured ? "" : "~")}{PingMs} ms"
                : DisplayName;

        public string PingText =>
            PingMs <= 0 ? "—" : (IsMeasured ? $"{PingMs} ms" : $"~{PingMs} ms");
    }

    /// <summary>
    /// Builds a ping-ranked region list from RoValra datacenter coordinates.
    /// Prefers live ICMP/TCP RTT to known Roblox DC IPs when the network allows it;
    /// otherwise estimates RTT from great-circle distance (same approach RoValra uses for ranking).
    /// </summary>
    public static class RegionPingService
    {
        private const string LOG_IDENT = "RegionPingService";
        private static readonly ConcurrentDictionary<string, int> IpPingCache = new(StringComparer.OrdinalIgnoreCase);
        private static IReadOnlyList<RegionPingInfo>? _cached;
        private static DateTime _cacheUtc = DateTime.MinValue;
        private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(15);

        public static int EstimatePingFromDistanceKm(double distanceKm) =>
            Math.Max(5, (int)Math.Round(distanceKm * 0.022 + 8));

        public static async Task<IReadOnlyList<RegionPingInfo>> GetRegionsAsync(
            bool forceRefresh = false,
            bool measureLive = true,
            CancellationToken ct = default)
        {
            if (!forceRefresh && _cached is not null && DateTime.UtcNow - _cacheUtc < CacheTtl)
                return _cached;

            var dcs = await RoValraApi.GetDatacentersAsync(ct);
            if (dcs is null || dcs.Count == 0)
            {
                App.Logger.WriteLine(LOG_IDENT,
                    dcs is null
                        ? "RoValra datacenters returned null (possible schema mismatch)."
                        : "RoValra datacenters returned empty list.");
                return _cached ?? Array.Empty<RegionPingInfo>();
            }

            var (userLat, userLon) = await ResolveUserLocationAsync(ct);

            var regions = new Dictionary<string, RegionPingInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in dcs)
            {
                if (entry.Inactive || entry.Location is null)
                    continue;
                if (entry.Location.LatLong.Length < 2)
                    continue;
                if (!TryGetJsonElementDouble(entry.Location.LatLong[0], out double lat) ||
                    !TryGetJsonElementDouble(entry.Location.LatLong[1], out double lon))
                    continue;

                string city = entry.Location.City?.Trim() ?? "";
                string country = entry.Location.Country?.Trim() ?? "";
                if (string.IsNullOrEmpty(city) && string.IsNullOrEmpty(country))
                    continue;

                string key = FormatRegionKey(city, country);
                double distance = GetDistanceKm(userLat, userLon, lat, lon);
                int est = EstimatePingFromDistanceKm(distance);

                if (!regions.TryGetValue(key, out var existing) || distance < existing.DistanceKm)
                {
                    regions[key] = new RegionPingInfo
                    {
                        Key = key,
                        City = city,
                        State = entry.Location.Region,
                        Country = country,
                        DisplayName = FormatDisplayName(city, entry.Location.Region, country, entry.Location.CountryName),
                        Latitude = lat,
                        Longitude = lon,
                        DistanceKm = distance,
                        PingMs = est,
                        IsMeasured = false,
                        DataCenterIds = [.. entry.DataCenterIds]
                    };
                }
                else
                {
                    foreach (var id in entry.DataCenterIds)
                    {
                        if (!existing.DataCenterIds.Contains(id))
                            existing.DataCenterIds.Add(id);
                    }
                }
            }

            var list = regions.Values.OrderBy(r => r.PingMs).ThenBy(r => r.DisplayName).ToList();

            if (measureLive && list.Count > 0)
            {
                // Only probe the closest handful — full-world live probes are slow and often blocked.
                var probeTargets = list.Take(8).ToList();
                await TryMeasureLivePingsAsync(probeTargets, ct);
                list = list.OrderBy(r => r.PingMs).ThenBy(r => r.DisplayName).ToList();
            }

            _cached = list;
            _cacheUtc = DateTime.UtcNow;
            App.Logger.WriteLine(LOG_IDENT,
                list.Count > 0
                    ? $"Ranked {list.Count} regions (best={list[0].Label})."
                    : "No regions returned.");
            return list;
        }

        public static int? LookupPingMs(string? regionKeyOrCity)
        {
            if (string.IsNullOrWhiteSpace(regionKeyOrCity) || _cached is null)
                return null;

            foreach (var r in _cached)
            {
                if (r.Key.Equals(regionKeyOrCity, StringComparison.OrdinalIgnoreCase) ||
                    r.DisplayName.Equals(regionKeyOrCity, StringComparison.OrdinalIgnoreCase) ||
                    r.City.Equals(regionKeyOrCity, StringComparison.OrdinalIgnoreCase) ||
                    regionKeyOrCity.Contains(r.City, StringComparison.OrdinalIgnoreCase))
                    return r.PingMs;
            }
            return null;
        }

        private static bool TryGetJsonElementDouble(JsonElement element, out double value)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Number:
                    return element.TryGetDouble(out value);
                case JsonValueKind.String:
                    return double.TryParse(
                        element.GetString(),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out value);
                default:
                    value = 0;
                    return false;
            }
        }

        public static RegionPingInfo? FindByDatacenterId(int datacenterId) =>
            _cached?.FirstOrDefault(r => r.DataCenterIds.Contains(datacenterId));

        public static RegionPingInfo? FindByCityCountry(string? city, string? country)
        {
            if (_cached is null || string.IsNullOrWhiteSpace(city))
                return null;

            return _cached.FirstOrDefault(r =>
                r.City.Equals(city, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(country) ||
                 r.Country.Equals(country, StringComparison.OrdinalIgnoreCase) ||
                 (country!.Length > 2 && (r.DisplayName.Contains(country, StringComparison.OrdinalIgnoreCase)))));
        }

        public static async Task<int?> MeasureHostPingAsync(string host, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(host))
                return null;

            if (IpPingCache.TryGetValue(host, out int cached))
                return cached;

            int? icmp = await MeasureIcmpAsync(host, ct);
            if (icmp is > 0)
            {
                IpPingCache[host] = icmp.Value;
                return icmp;
            }

            foreach (int port in new[] { 443, 80 })
            {
                ct.ThrowIfCancellationRequested();
                int? tcp = await MeasureTcpRttAsync(host, port, 1200, ct);
                if (tcp is > 0 and < 2500)
                {
                    IpPingCache[host] = tcp.Value;
                    return tcp;
                }
            }

            return null;
        }

        private static async Task TryMeasureLivePingsAsync(List<RegionPingInfo> regions, CancellationToken ct)
        {
            // Probe one IP per region via RoValra region API against a popular place (Blox Fruits).
            const long probePlaceId = 2753915549;
            var measureTasks = regions.Select(async region =>
            {
                try
                {
                    ct.ThrowIfCancellationRequested();
                    var resp = await RoValraApi.GetServersByRegionAsync(
                        probePlaceId, region.Country, region.City, "0", ct);
                    string? ip = resp?.Servers?
                        .Select(s => s.IpAddress)
                        .FirstOrDefault(a => !string.IsNullOrWhiteSpace(a));

                    if (string.IsNullOrWhiteSpace(ip))
                        return;

                    region.SampleIp = ip;
                    int? measured = await MeasureHostPingAsync(ip, ct);
                    if (measured is > 0)
                    {
                        region.PingMs = measured.Value;
                        region.IsMeasured = true;
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Live ping skip {region.Key}: {ex.Message}");
                }
            });

            // Cap concurrency so we don't hammer RoValra / the network.
            foreach (var batch in measureTasks.Chunk(4))
                await Task.WhenAll(batch);
        }

        private static async Task<(double lat, double lon)> ResolveUserLocationAsync(CancellationToken ct)
        {
            try
            {
                var ipinfo = await Http.GetJson<IPInfoResponse>("https://ipinfo.io/json");
                if (!string.IsNullOrEmpty(ipinfo?.Loc))
                {
                    string[] parts = ipinfo.Loc.Split(',');
                    if (parts.Length >= 2 &&
                        double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double lat) &&
                        double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double lon))
                        return (lat, lon);
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteException($"{LOG_IDENT}::UserLocation", ex);
            }

            // Rough fallback (continental US centroid) so ranking still works offline-ish.
            return (39.8283, -98.5795);
        }

        private static async Task<int?> MeasureIcmpAsync(string host, CancellationToken ct)
        {
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(host, 1500);
                ct.ThrowIfCancellationRequested();
                return reply.Status == IPStatus.Success ? (int)reply.RoundtripTime : null;
            }
            catch
            {
                return null;
            }
        }

        private static async Task<int?> MeasureTcpRttAsync(string host, int port, int timeoutMs, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                using var client = new TcpClient();
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
                linked.CancelAfter(timeoutMs);
                try
                {
                    await client.ConnectAsync(host, port, linked.Token);
                    sw.Stop();
                    return (int)sw.ElapsedMilliseconds;
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    return null;
                }
                catch (SocketException)
                {
                    // Connection refused / RST still reflects path RTT.
                    sw.Stop();
                    return sw.ElapsedMilliseconds > 0 ? (int)sw.ElapsedMilliseconds : null;
                }
            }
            catch
            {
                sw.Stop();
                return null;
            }
        }

        public static string FormatRegionKey(string city, string country) =>
            string.IsNullOrWhiteSpace(city) ? country.Trim() : $"{city.Trim()}, {country.Trim()}";

        private static string FormatDisplayName(string city, string? state, string country, string? countryName)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(city))
                parts.Add(city.Trim());
            if (!string.IsNullOrWhiteSpace(state) &&
                !state.Equals(city, StringComparison.OrdinalIgnoreCase))
                parts.Add(state.Trim());

            string countryLabel = !string.IsNullOrWhiteSpace(countryName) ? countryName!
                : country.Equals("US", StringComparison.OrdinalIgnoreCase) ? "USA"
                : country;
            if (!string.IsNullOrWhiteSpace(countryLabel) &&
                !parts.Any(p => p.Equals(countryLabel, StringComparison.OrdinalIgnoreCase)))
                parts.Add(countryLabel);

            return parts.Count > 0 ? string.Join(", ", parts) : FormatRegionKey(city, country);
        }

        public static double GetDistanceKm(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371;
            double dLat = Deg2Rad(lat2 - lat1);
            double dLon = Deg2Rad(lon2 - lon1);
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(Deg2Rad(lat1)) * Math.Cos(Deg2Rad(lat2)) *
                       Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        }

        private static double Deg2Rad(double deg) => deg * (Math.PI / 180.0);
    }
}
