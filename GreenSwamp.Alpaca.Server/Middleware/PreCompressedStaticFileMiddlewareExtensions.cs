using Microsoft.AspNetCore.StaticFiles;

namespace GreenSwamp.Alpaca.Server.Middleware;

/// <summary>
/// Extension methods for registering pre-compressed static file serving middleware.
/// </summary>
internal static class PreCompressedStaticFileMiddlewareExtensions
{
    /// <summary>
    /// Serves pre-compressed (.br, .gz) versions of static files when the client supports them.
    /// Register after <c>UseResponseCompression</c> and before <c>UseStaticFiles</c>.
    /// </summary>
    internal static IApplicationBuilder UsePreCompressedStaticFiles(this IApplicationBuilder app)
    {
        var env = app.ApplicationServices.GetRequiredService<IWebHostEnvironment>();
        var webRootPath = env.WebRootPath;
        var contentTypeProvider = new FileExtensionContentTypeProvider();

        return app.Use((context, next) =>
            ServePreCompressedStaticFileAsync(context, next, webRootPath, contentTypeProvider));
    }

    private static async Task ServePreCompressedStaticFileAsync(
        HttpContext context,
        Func<Task> next,
        string webRootPath,
        FileExtensionContentTypeProvider contentTypeProvider)
    {
        if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
        {
            // Check if the request is for a static file that can be served pre-compressed
            var requestPath = context.Request.Path.Value;
            if (!string.IsNullOrEmpty(requestPath) &&
                !string.IsNullOrEmpty(webRootPath) &&
                !requestPath.EndsWith('/') &&
                !requestPath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) &&
                !requestPath.EndsWith(".br", StringComparison.OrdinalIgnoreCase))
            {
                var relativePath = requestPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                var physicalPath = Path.Combine(webRootPath, relativePath);

                // If the exact file doesn't exist, check for pre-compressed versions (.gz, .br) and serve if available
                if (!File.Exists(physicalPath))
                {
                    var acceptEncoding = context.Request.Headers.AcceptEncoding.ToString();
                    var compressedPath = string.Empty;
                    var encoding = string.Empty;

                    if (acceptEncoding.Contains("br", StringComparison.OrdinalIgnoreCase))
                    {
                        var brPath = physicalPath + ".br";
                        if (File.Exists(brPath))
                        {
                            compressedPath = brPath;
                            encoding = "br";
                        }
                    }

                    if (string.IsNullOrEmpty(compressedPath) && acceptEncoding.Contains("gzip", StringComparison.OrdinalIgnoreCase))
                    {
                        var gzPath = physicalPath + ".gz";
                        if (File.Exists(gzPath))
                        {
                            compressedPath = gzPath;
                            encoding = "gzip";
                        }
                    }

                    // If a pre-compressed file is found, serve it with the appropriate Content-Encoding and Content-Type headers
                    if (!string.IsNullOrEmpty(compressedPath))
                    {
                        if (!contentTypeProvider.TryGetContentType(physicalPath, out var contentType))
                        {
                            contentType = "application/octet-stream";
                        }

                        context.Response.Headers.ContentEncoding = encoding;
                        context.Response.Headers.Vary = "Accept-Encoding";
                        context.Response.ContentType = contentType;

                        await context.Response.SendFileAsync(compressedPath, context.RequestAborted);
                        return;
                    }
                }
            }
        }

        await next();
    }
}
