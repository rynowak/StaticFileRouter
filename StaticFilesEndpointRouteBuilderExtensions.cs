using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Endpoints;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.AspNetCore.StaticFiles.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

namespace Microsoft.AspNetCore.Builder
{
    public class StaticFileEndpointOptions
    {
        /// <summary>
        /// Used to map files to content-types.
        /// </summary>
        public IContentTypeProvider ContentTypeProvider { get; set; }

        /// <summary>
        /// The default content type for a request if the ContentTypeProvider cannot determine one.
        /// None is provided by default, so the client must determine the format themselves.
        /// http://www.w3.org/Protocols/rfc2616/rfc2616-sec7.html#sec7
        /// </summary>
        public string DefaultContentType { get; set; }

        /// <summary>
        /// If the file is not a recognized content-type should it be served?
        /// Default: false.
        /// </summary>
        public bool ServeUnknownFileTypes { get; set; }

        /// <summary>
        /// Indicates if files should be compressed for HTTPS requests when the Response Compression middleware is available.
        /// The default value is <see cref="HttpsCompressionMode.Compress"/>.
        /// </summary>
        /// <remarks>
        /// Enabling compression on HTTPS requests for remotely manipulable content may expose security problems.
        /// </remarks>
        public HttpsCompressionMode HttpsCompression { get; set; } = HttpsCompressionMode.Compress;

        /// <summary>
        /// Called after the status code and headers have been set, but before the body has been written.
        /// This can be used to add or change the response headers.
        /// </summary>
        public Action<StaticFileResponseContext> OnPrepareResponse { get; set; }

        /// <summary>
        /// The file system used to locate resources
        /// </summary>
        public IFileProvider FileProvider { get; set; }
    }

    public static class StaticFilesEndpointRouteBuilderExtensions
    {
        public static StaticFilesEndpointConventionBuilder MapStaticFiles(this IEndpointRouteBuilder endpoints)
        {
            if (endpoints == null)
            {
                throw new ArgumentNullException(nameof(endpoints));
            }

            return MapStaticFilesCore(endpoints, requestPath: PathString.Empty, options: null);
        }

        public static StaticFilesEndpointConventionBuilder MapStaticFiles(this IEndpointRouteBuilder endpoints, PathString requestPath)
        {
            return MapStaticFilesCore(endpoints, requestPath, options: null);
        }

        public static StaticFilesEndpointConventionBuilder MapStaticFiles(this IEndpointRouteBuilder endpoints, PathString requestPath, StaticFileEndpointOptions options)
        {
            if (endpoints == null)
            {
                throw new ArgumentNullException(nameof(endpoints));
            }

            return MapStaticFilesCore(endpoints, requestPath, options);
        }

        private static StaticFilesEndpointConventionBuilder MapStaticFilesCore(IEndpointRouteBuilder endpoints, PathString requestPath, StaticFileEndpointOptions options)
        {
            StaticFileOptions staticFileOptions;
            if (options == null)
            {
                var original = endpoints.ServiceProvider.GetRequiredService<IOptions<StaticFileOptions>>();
                staticFileOptions = new StaticFileOptions()
                {
                    ContentTypeProvider = original.Value.ContentTypeProvider,
                    DefaultContentType = original.Value.DefaultContentType,
                    FileProvider = original.Value.FileProvider,
                    HttpsCompression = original.Value.HttpsCompression,
                    OnPrepareResponse = original.Value.OnPrepareResponse,
                    ServeUnknownFileTypes = original.Value.ServeUnknownFileTypes,
                };
            }
            else
            {
                staticFileOptions = new StaticFileOptions()
                {
                    ContentTypeProvider = options.ContentTypeProvider,
                    DefaultContentType = options.DefaultContentType,
                    FileProvider = options.FileProvider,
                    HttpsCompression = options.HttpsCompression,
                    OnPrepareResponse = options.OnPrepareResponse,
                    ServeUnknownFileTypes = options.ServeUnknownFileTypes,
                };
            }

            staticFileOptions.ContentTypeProvider ??= new FileExtensionContentTypeProvider();
            staticFileOptions.FileProvider ??= endpoints.ServiceProvider.GetRequiredService<IWebHostEnvironment>().WebRootFileProvider;

            staticFileOptions.RequestPath = requestPath;

            var pattern = RoutePatternFactory.Parse(requestPath + "/{**path}", defaults: null, parameterPolicies: new { path = new FileExistsConstraint(options.FileProvider), });

            var app = endpoints.CreateApplicationBuilder();

            // Temporary hack
            app.Use(next => httpContext =>
            {
                httpContext.SetEndpoint(null);
                return next(httpContext);
            });

            app.UseStaticFiles(staticFileOptions);

            return new StaticFilesEndpointConventionBuilder(endpoints.Map(pattern, app.Build()));
        }
    }

    public sealed class StaticFilesEndpointConventionBuilder : IEndpointConventionBuilder
    {
        private readonly IEndpointConventionBuilder _inner;

        internal StaticFilesEndpointConventionBuilder(IEndpointConventionBuilder inner)
        {
            _inner = inner;
        }

        public void Add(Action<EndpointBuilder> convention)
        {
            if (convention == null)
            {
                throw new ArgumentNullException(nameof(convention));
            }

            _inner.Add(convention);
        }
    }

    internal sealed class FileExistsConstraint : IRouteConstraint
    {
        private readonly IFileProvider _files;

        public FileExistsConstraint(IFileProvider files)
        {
            _files = files;
        }

        public bool Match(HttpContext httpContext, IRouter route, string routeKey, RouteValueDictionary values, RouteDirection routeDirection)
        {
            var path = (values[routeKey]).ToString();
            return _files.GetFileInfo(path)?.Exists == true;
        }
    }
}
