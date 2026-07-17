using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyRock.RemoteControl;

/// <summary>
/// The live half of cold-launch validation (issue #668): probes a reported Roku address over ECP to
/// confirm the server can actually reach it for a <c>/launch</c> wake. This IS the device-validation
/// gate — a Roku the server can't reach (remote/cloud server, ECP off, powered dark) never answers, so
/// it never validates and is never advertised. Uses the ungated ECP <c>GET /query/device-info</c>
/// (needs neither developer mode nor "Control by mobile apps"), so it works against any Roku on the
/// server's LAN.
/// </summary>
public sealed class PairingValidationService
{
    // ECP listens on 8060; device-info is the cheapest ungated liveness/reachability signal. Kept as a
    // path suffix (not an int to interpolate) so the URL is built by concatenation — no culture-sensitive
    // number formatting, and nothing for CA1305 to flag.
    private const string EcpDeviceInfoSuffix = ":8060/query/device-info";

    // Short per-address budget: a LAN-local Roku answers in well under a second, and a report may list
    // several addresses to try in sequence, so keep each attempt snappy.
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PairingValidationService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PairingValidationService"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Factory for the ECP probe client.</param>
    /// <param name="logger">Logger.</param>
    public PairingValidationService(IHttpClientFactory httpClientFactory, ILogger<PairingValidationService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Return the first reported address whose ECP answers (the address to <c>/launch</c> for the wake),
    /// or <c>null</c> if none are reachable. Trying the reported addresses in order lets a multi-homed
    /// Roku (wired + wifi) validate on whichever interface the server can actually reach.
    /// </summary>
    /// <param name="reportedIps">The client's reported LAN addresses, in preference order.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The reachable wake address, or <c>null</c>.</returns>
    public async Task<string?> FindReachableWakeIpAsync(IReadOnlyList<string> reportedIps, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reportedIps);

        foreach (var ip in reportedIps)
        {
            if (await IsEcpReachableAsync(ip, cancellationToken).ConfigureAwait(false))
            {
                return ip;
            }
        }

        return null;
    }

    private async Task<bool> IsEcpReachableAsync(string ip, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ip)
            || !Uri.TryCreate("http://" + ip + EcpDeviceInfoSuffix, UriKind.Absolute, out var uri))
        {
            return false;
        }

        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = ProbeTimeout;
            using var response = await client.GetAsync(uri, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            _logger.LogDebug(ex, "[JellyRock] ECP device-info probe to {Ip} failed", ip);
            return false;
        }
    }
}
