namespace MinecraftTopo.Core.Elevation;

/// <summary>Downloads files into a cache with temp-then-rename semantics and retries.</summary>
public sealed class Downloader
{
    private readonly HttpClient _http;

    public Downloader(HttpClient http)
    {
        _http = http;
    }

    /// <summary>
    /// Downloads <paramref name="url"/> to <paramref name="destPath"/> unless the file already
    /// exists. Reports bytes received so far through <paramref name="bytes"/>.
    /// </summary>
    public async Task<string> GetFileAsync(string url, string destPath, IProgress<(long Received, long? Total)>? bytes, CancellationToken ct)
    {
        if (File.Exists(destPath)) return destPath;
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
        string temp = destPath + ".part-" + Guid.NewGuid().ToString("N");

        Exception? last = null;
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();
                long? total = response.Content.Headers.ContentLength;
                await using (var src = await response.Content.ReadAsStreamAsync(ct))
                await using (var dst = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
                {
                    var buffer = new byte[1 << 16];
                    long received = 0;
                    int read;
                    while ((read = await src.ReadAsync(buffer, ct)) > 0)
                    {
                        await dst.WriteAsync(buffer.AsMemory(0, read), ct);
                        received += read;
                        bytes?.Report((received, total));
                    }
                }
                File.Move(temp, destPath, overwrite: true);
                return destPath;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException && attempt < 3)
            {
                last = ex;
                TryDelete(temp);
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2), ct);
            }
            catch
            {
                TryDelete(temp);
                throw;
            }
        }
        throw new IOException($"Download failed after 3 attempts: {url}", last);
    }

    public async Task<string> GetStringAsync(string url, CancellationToken ct)
    {
        Exception? last = null;
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var response = await _http.GetAsync(url, ct);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync(ct);
            }
            catch (HttpRequestException ex) when (attempt < 3)
            {
                last = ex;
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2), ct);
            }
        }
        throw new IOException($"Request failed after 3 attempts: {url}", last);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
    }
}
