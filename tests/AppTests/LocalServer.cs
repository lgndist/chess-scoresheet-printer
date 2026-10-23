using System.Net;
using System.Net.Sockets;

namespace NotasyonOtomasyonu.AppTests;

/// <summary>Testler için yerel HTTP sunucusu (localhost; yönetici izni gerekmez). İstek yolu → yanıt.</summary>
internal sealed class LocalServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly Func<HttpListenerRequest, (int Status, byte[] Body)> _handler;
    public string BaseUrl { get; }
    public int Requests;

    public LocalServer(Func<HttpListenerRequest, (int Status, byte[] Body)> handler)
    {
        _handler = handler;
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        BaseUrl = $"http://localhost:{port}";
        _listener.Prefixes.Add(BaseUrl + "/");
        _listener.Start();
        _ = Task.Run(LoopAsync);
    }

    private async Task LoopAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch { return; }
            Interlocked.Increment(ref Requests);
            try
            {
                var (status, body) = _handler(ctx.Request);
                ctx.Response.StatusCode = status;
                ctx.Response.ContentLength64 = body.Length;
                await ctx.Response.OutputStream.WriteAsync(body);
            }
            catch { ctx.Response.StatusCode = 500; }
            finally { ctx.Response.Close(); }
        }
    }

    public static (int, byte[]) Text(string s, int status = 200) => (status, System.Text.Encoding.UTF8.GetBytes(s));

    public void Dispose()
    {
        try { _listener.Stop(); _listener.Close(); } catch { }
    }
}
