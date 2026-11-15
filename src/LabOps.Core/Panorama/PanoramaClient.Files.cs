using System.Net;
using System.Net.Http.Headers;

namespace LabOps.Core.Panorama;

/// <summary>Part of a file in a Panorama file area, and the whole file's length.</summary>
public sealed record PanoramaRange(byte[] Bytes, long FileLength);

public sealed partial class PanoramaClient
{
    /// <summary>
    /// Bytes [<paramref name="start"/>, start + <paramref name="length"/>) of a file in a file area,
    /// by an HTTP range request, which LabKey's WebDAV answers with just those bytes (206). Fewer
    /// come back when the file ends sooner.
    /// </summary>
    /// <param name="path">The file's WebDAV path, as a listing gives it.</param>
    public async Task<PanoramaRange> ReadRangeAsync(string path, long start, int length, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        var display = PanoramaPaths.ToFolder(path);
        using var request = new HttpRequestMessage(HttpMethod.Get, PanoramaPaths.Encode(path));
        request.Headers.Authorization = _credential.ToAuthenticationHeader();
        request.Headers.Range = new RangeHeaderValue(start, start + length - 1);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new PanoramaException($"Could not reach Panorama ({ex.Message}). Check the network connection.", ex);
        }

        using (response)
        {
            await SucceededAsync(response, display, allowMissing: false, cancellationToken).ConfigureAwait(false);

            // A server that ignores Range sends the whole file with 200; reading on would fetch
            // gigabytes, so that is refused rather than read.
            if (response.StatusCode != HttpStatusCode.PartialContent || response.Content.Headers.ContentRange is not { Length: { } total })
            {
                throw new PanoramaException($"Panorama sent the whole of {display} instead of part of it, so LabOps did not read it.");
            }

            try
            {
                var bytes = await response.Content.ReadAsByteArrayAsync(timeout.Token).ConfigureAwait(false);
                return new PanoramaRange(bytes, total);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                throw new PanoramaException($"The connection to Panorama broke while reading {display} ({ex.Message}).", ex);
            }
        }
    }
}
