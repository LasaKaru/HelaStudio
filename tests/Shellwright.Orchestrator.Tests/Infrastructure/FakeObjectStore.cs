using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;

namespace Shellwright.Orchestrator.Tests.Infrastructure;

/// <summary>
/// A minimal S3-compatible endpoint, over real HTTP.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ A real socket rather than a mocked <c>IAmazonS3</c>, and the difference
/// is the point. Mocking the interface tests that our code calls the methods we
/// think it calls; it says nothing about whether the AWS SDK can actually reach
/// a path-style endpoint, whether the request it builds is well formed, or
/// whether bytes survive the round trip. Those are exactly the things that
/// break against R2.
/// </para>
/// <para>
/// ⚠️ It is not an S3 implementation and does not pretend to be. It speaks the
/// four verbs this store uses — PUT, GET, HEAD, DELETE — over an in-memory
/// dictionary, and answers 404 with the shape the SDK maps to a
/// <c>NotFound</c>. Anything else is out of scope and would be a second,
/// worse S3.
/// </para>
/// <para>
/// ⚠️ It does <i>not</i> verify signatures. It records that an Authorization
/// header arrived, which is enough to catch a client configured with no
/// credentials at all, and stops well short of reimplementing SigV4 — a
/// reimplementation that agreed with our own mistakes would be worse than no
/// check.
/// </para>
/// </remarks>
public sealed class FakeObjectStore : IDisposable
{
    private readonly HttpListener listener = new();
    private readonly ConcurrentDictionary<string, byte[]> objects = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource stopping = new();
    private readonly Task loop;

    /// <summary>Starts the endpoint on a free loopback port.</summary>
    public FakeObjectStore()
    {
        Port = FreePort();
        ServiceUrl = string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.1:{Port}");

        listener.Prefixes.Add(ServiceUrl + "/");
        listener.Start();

        loop = Task.Run(ServeAsync);
    }

    /// <summary>Where the endpoint is listening.</summary>
    public string ServiceUrl { get; }

    /// <summary>The port it bound to.</summary>
    public int Port { get; }

    /// <summary>How many requests arrived without an Authorization header.</summary>
    public int UnauthenticatedRequests { get; private set; }

    /// <summary>The object keys currently held.</summary>
    public IReadOnlyCollection<string> Keys => objects.Keys.ToList();

    /// <summary>Reads an object back, for assertions.</summary>
    /// <param name="key">The key, without the bucket prefix.</param>
    /// <returns>The bytes, or null.</returns>
    public byte[]? Read(string key) => objects.TryGetValue(key, out var value) ? value : null;

    /// <inheritdoc />
    public void Dispose()
    {
        stopping.Cancel();
        listener.Stop();
        listener.Close();

        try
        {
            loop.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // The listener being torn down mid-await is the expected way this
            // ends; a failure to stop cleanly must not fail a test.
        }

        stopping.Dispose();
    }

    private static int FreePort()
    {
        using var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        probe.Start();
        var port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private async Task ServeAsync()
    {
        while (!stopping.IsCancellationRequested)
        {
            HttpListenerContext context;

            try
            {
                context = await listener.GetContextAsync();
            }
            catch (HttpListenerException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            try
            {
                await HandleAsync(context);
            }
            catch (HttpListenerException)
            {
                // The client went away mid-response. Not this class's problem.
            }
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        if (string.IsNullOrEmpty(request.Headers["Authorization"]))
        {
            UnauthenticatedRequests++;
        }

        // Path style: /{bucket}/{key...}
        var path = request.Url!.AbsolutePath.TrimStart('/');
        var separator = path.IndexOf('/', StringComparison.Ordinal);
        var key = separator < 0 ? string.Empty : path[(separator + 1)..];

        switch (request.HttpMethod)
        {
            case "PUT":
                using (var buffer = new MemoryStream())
                {
                    await request.InputStream.CopyToAsync(buffer);

                    // ⚠️ The SDK frames the body as `aws-chunked` whenever it
                    // signs the payload, which it does over plain HTTP. A real
                    // S3 endpoint decodes that framing; a fake that stored the
                    // raw bytes would silently record chunk headers as part of
                    // the artifact, and the round-trip test would fail by a few
                    // hundred bytes with no obvious cause.
                    //
                    // Keyed on Content-Encoding rather than on the
                    // x-amz-content-sha256 value, which the SDK sends as
                    // STREAMING-AWS4-HMAC-SHA256-PAYLOAD *or* the -TRAILER
                    // variant depending on version. Matching one exact string
                    // silently stopped decoding and produced the same
                    // few-hundred-byte discrepancy.
                    var isChunked = (request.Headers["Content-Encoding"] ?? string.Empty)
                        .Contains("aws-chunked", StringComparison.OrdinalIgnoreCase);

                    var decoded = isChunked ? DecodeChunked(buffer.ToArray()) : buffer.ToArray();

                    // The SDK states the true payload length up front. Checking
                    // it here means a decoding bug fails in this class, where
                    // the cause is visible, rather than as a mismatched
                    // assertion in whichever test happens to run.
                    if (long.TryParse(
                            request.Headers["x-amz-decoded-content-length"],
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out var expected)
                        && decoded.Length != expected)
                    {
                        throw new InvalidOperationException(
                            $"Chunk decoding produced {decoded.Length} bytes; the request declared {expected}.");
                    }

                    objects[key] = decoded;
                }

                response.StatusCode = 200;
                response.Headers["ETag"] = "\"fake\"";
                break;

            case "HEAD":
                if (objects.TryGetValue(key, out var head))
                {
                    response.StatusCode = 200;
                    response.ContentLength64 = head.Length;
                }
                else
                {
                    response.StatusCode = 404;
                }

                break;

            case "GET":
                if (objects.TryGetValue(key, out var body))
                {
                    response.StatusCode = 200;
                    response.ContentLength64 = body.Length;
                    await response.OutputStream.WriteAsync(body);
                }
                else
                {
                    await NotFoundAsync(response, key);
                }

                break;

            case "DELETE":
                objects.TryRemove(key, out _);
                response.StatusCode = 204;
                break;

            default:
                response.StatusCode = 405;
                break;
        }

        response.Close();
    }

    /// <summary>
    /// Unwraps AWS's chunked transfer framing.
    /// </summary>
    /// <remarks>
    /// Each chunk is <c>&lt;hex length&gt;;chunk-signature=&lt;hex&gt;\r\n</c>
    /// then that many bytes then <c>\r\n</c>, ending with a zero-length chunk.
    /// The signatures are not checked here, for the reason given on the class:
    /// a reimplementation of SigV4 would agree with our own mistakes.
    /// </remarks>
    /// <param name="body">The raw request body.</param>
    /// <returns>The payload without its framing.</returns>
    private static byte[] DecodeChunked(byte[] body)
    {
        using var payload = new MemoryStream();
        var position = 0;

        while (position < body.Length)
        {
            var lineEnd = IndexOfCrLf(body, position);

            if (lineEnd < 0)
            {
                break;
            }

            var header = Encoding.ASCII.GetString(body, position, lineEnd - position);
            var semicolon = header.IndexOf(';', StringComparison.Ordinal);
            var lengthText = semicolon < 0 ? header : header[..semicolon];

            if (!int.TryParse(
                    lengthText,
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out var length)
                || length == 0)
            {
                break;
            }

            var start = lineEnd + 2;
            payload.Write(body, start, length);

            // Past the chunk and its trailing CRLF.
            position = start + length + 2;
        }

        return payload.ToArray();
    }

    private static int IndexOfCrLf(byte[] body, int from)
    {
        for (var index = from; index + 1 < body.Length; index++)
        {
            if (body[index] == (byte)'\r' && body[index + 1] == (byte)'\n')
            {
                return index;
            }
        }

        return -1;
    }

    private static async Task NotFoundAsync(HttpListenerResponse response, string key)
    {
        // The shape the SDK maps to AmazonS3Exception with StatusCode NotFound.
        var payload = Encoding.UTF8.GetBytes(
            $"""
             <?xml version="1.0" encoding="UTF-8"?>
             <Error><Code>NoSuchKey</Code><Message>The specified key does not exist.</Message><Key>{key}</Key></Error>
             """);

        response.StatusCode = 404;
        response.ContentType = "application/xml";
        response.ContentLength64 = payload.Length;

        await response.OutputStream.WriteAsync(payload);
    }
}
