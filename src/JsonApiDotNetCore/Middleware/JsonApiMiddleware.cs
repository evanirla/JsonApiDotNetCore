using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using JsonApiDotNetCore.Configuration;
using JsonApiDotNetCore.Resources.Annotations;
using JsonApiDotNetCore.Serialization;
using JsonApiDotNetCore.Serialization.Objects;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json;

namespace JsonApiDotNetCore.Middleware
{
    /// <summary>
    /// Intercepts HTTP requests to populate injected <see cref="IJsonApiRequest"/> instance for JSON:API requests.
    /// </summary>
    public sealed class JsonApiMiddleware
    {
        private static readonly MediaTypeHeaderValue _mediaType = MediaTypeHeaderValue.Parse(HeaderConstants.MediaType);
        private static readonly MediaTypeHeaderValue _atomicOperationsMediaType = MediaTypeHeaderValue.Parse(HeaderConstants.AtomicOperationsMediaType);

        private readonly RequestDelegate _next;

        public JsonApiMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext httpContext, 
            IControllerResourceMapping controllerResourceMapping, 
            IJsonApiOptions options, 
            IJsonApiRequest request, 
            IResourceContextProvider resourceContextProvider)
        {
            if (httpContext == null) throw new ArgumentNullException(nameof(httpContext));
            if (controllerResourceMapping == null) throw new ArgumentNullException(nameof(controllerResourceMapping));
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (resourceContextProvider == null) throw new ArgumentNullException(nameof(resourceContextProvider));

            var routeValues = httpContext.GetRouteData().Values;

            var primaryResourceContext = CreatePrimaryResourceContext(routeValues, controllerResourceMapping, resourceContextProvider);
            if (primaryResourceContext != null)
            {
                if (!await ValidateContentTypeHeaderAsync(HeaderConstants.MediaType, httpContext, options.SerializerSettings) || 
                    !await ValidateAcceptHeaderAsync(_mediaType, httpContext, options.SerializerSettings))
                {
                    return;
                }

                SetupResourceRequest((JsonApiRequest)request, primaryResourceContext, routeValues, options, resourceContextProvider, httpContext.Request);

                httpContext.RegisterJsonApiRequest();
            }
            else if (IsOperationsRequest(routeValues))
            {
                if (!await ValidateContentTypeHeaderAsync(HeaderConstants.AtomicOperationsMediaType, httpContext, options.SerializerSettings) || 
                    !await ValidateAcceptHeaderAsync(_atomicOperationsMediaType, httpContext, options.SerializerSettings))
                {
                    return;
                }

                SetupOperationsRequest((JsonApiRequest)request, options, httpContext.Request);

                httpContext.RegisterJsonApiRequest();
            }

            await _next(httpContext);
        }

        private static ResourceContext CreatePrimaryResourceContext(RouteValueDictionary routeValues,
            IControllerResourceMapping controllerResourceMapping, IResourceContextProvider resourceContextProvider)
        {
            var controllerName = (string) routeValues["controller"];
            if (controllerName != null)
            {
                var resourceType = controllerResourceMapping.GetResourceTypeForController(controllerName);
                if (resourceType != null)
                {
                    return resourceContextProvider.GetResourceContext(resourceType);
                }
            }

            return null;
        }

        private static async Task<bool> ValidateContentTypeHeaderAsync(string allowedContentType, HttpContext httpContext, JsonSerializerSettings serializerSettings)
        {
            var contentType = httpContext.Request.ContentType;
            if (contentType != null && contentType != allowedContentType)
            {
                await FlushResponseAsync(httpContext.Response, serializerSettings, new Error(HttpStatusCode.UnsupportedMediaType)
                {
                    Title = "The specified Content-Type header value is not supported.",
                    Detail = $"Please specify '{allowedContentType}' instead of '{contentType}' for the Content-Type header value."
                });
                return false;
            }

            return true;
        }

        private static async Task<bool> ValidateAcceptHeaderAsync(MediaTypeHeaderValue allowedMediaTypeValue, HttpContext httpContext, JsonSerializerSettings serializerSettings)
        {
            StringValues acceptHeaders = httpContext.Request.Headers["Accept"];
            if (!acceptHeaders.Any())
            {
                return true;
            }

            bool seenCompatibleMediaType = false;

            foreach (var acceptHeader in acceptHeaders)
            {
                if (MediaTypeWithQualityHeaderValue.TryParse(acceptHeader, out var headerValue))
                {
                    headerValue.Quality = null;

                    if (headerValue.MediaType == "*/*" || headerValue.MediaType == "application/*")
                    {
                        seenCompatibleMediaType = true;
                        break;
                    }

                    if (allowedMediaTypeValue.Equals(headerValue))
                    {
                        seenCompatibleMediaType = true;
                        break;
                    }
                }
            }

            if (!seenCompatibleMediaType)
            {
                await FlushResponseAsync(httpContext.Response, serializerSettings, new Error(HttpStatusCode.NotAcceptable)
                {
                    Title = "The specified Accept header value does not contain any supported media types.",
                    Detail = $"Please include '{allowedMediaTypeValue}' in the Accept header values."
                });
                return false;
            }

            return true;
        }

        private static async Task FlushResponseAsync(HttpResponse httpResponse, JsonSerializerSettings serializerSettings, Error error)
        {
            httpResponse.ContentType = HeaderConstants.MediaType;
            httpResponse.StatusCode = (int) error.StatusCode;

            JsonSerializer serializer = JsonSerializer.CreateDefault(serializerSettings);
            serializer.ApplyErrorSettings();

            // https://github.com/JamesNK/Newtonsoft.Json/issues/1193
            await using (var stream = new MemoryStream())
            {
                await using (var streamWriter = new StreamWriter(stream, leaveOpen: true))
                {
                    using var jsonWriter = new JsonTextWriter(streamWriter);
                    serializer.Serialize(jsonWriter, new ErrorDocument(error));
                }

                stream.Seek(0, SeekOrigin.Begin);
                await stream.CopyToAsync(httpResponse.Body);
            }

            await httpResponse.Body.FlushAsync();
        }

        private static void SetupResourceRequest(JsonApiRequest request, ResourceContext primaryResourceContext,
            RouteValueDictionary routeValues, IJsonApiOptions options, IResourceContextProvider resourceContextProvider,
            HttpRequest httpRequest)
        {
            request.IsReadOnly = httpRequest.Method == HttpMethod.Get.Method;
            request.Kind = EndpointKind.Primary;
            request.PrimaryResource = primaryResourceContext;
            request.PrimaryId = GetPrimaryRequestId(routeValues);
            request.BasePath = GetBasePath(primaryResourceContext.PublicName, options, httpRequest);

            var relationshipName = GetRelationshipNameForSecondaryRequest(routeValues);
            if (relationshipName != null)
            {
                request.Kind = IsRouteForRelationship(routeValues) ? EndpointKind.Relationship : EndpointKind.Secondary;

                var requestRelationship =
                    primaryResourceContext.Relationships.SingleOrDefault(relationship =>
                        relationship.PublicName == relationshipName);

                if (requestRelationship != null)
                {
                    request.Relationship = requestRelationship;
                    request.SecondaryResource = resourceContextProvider.GetResourceContext(requestRelationship.RightType);
                }
            }

            var isGetAll = request.PrimaryId == null && request.IsReadOnly;
            request.IsCollection = isGetAll || request.Relationship is HasManyAttribute;
        }

        private static string GetPrimaryRequestId(RouteValueDictionary routeValues)
        {
            return routeValues.TryGetValue("id", out var id) ? (string) id : null;
        }

        private static string GetBasePath(string resourceName, IJsonApiOptions options, HttpRequest httpRequest)
        {
            var builder = new StringBuilder();

            if (!options.UseRelativeLinks)
            {
                builder.Append(httpRequest.Scheme);
                builder.Append("://");
                builder.Append(httpRequest.Host);
            }

            string customRoute = GetCustomRoute(resourceName, options.Namespace, httpRequest.HttpContext);
            if (!string.IsNullOrEmpty(customRoute))
            {
                builder.Append('/');
                builder.Append(customRoute);
            }
            else if (!string.IsNullOrEmpty(options.Namespace))
            {
                builder.Append('/');
                builder.Append(options.Namespace);
            }

            return builder.ToString();
        }

        private static string GetCustomRoute(string resourceName, string apiNamespace, HttpContext httpContext)
        {
            if (resourceName != null)
            {
                var endpoint = httpContext.GetEndpoint();
                var routeAttribute = endpoint.Metadata.GetMetadata<RouteAttribute>();
                if (routeAttribute != null)
                {
                    var trimmedComponents = httpContext.Request.Path.Value.Trim('/').Split('/').ToList();
                    var resourceNameIndex = trimmedComponents.FindIndex(c => c == resourceName);
                    var newComponents = trimmedComponents.Take(resourceNameIndex).ToArray();
                    var customRoute = string.Join('/', newComponents);
                    return customRoute == apiNamespace ? null : customRoute;
                }
            }

            return null;
        }

        private static string GetRelationshipNameForSecondaryRequest(RouteValueDictionary routeValues)
        {
            return routeValues.TryGetValue("relationshipName", out object routeValue) ? (string) routeValue : null;
        }

        private static bool IsRouteForRelationship(RouteValueDictionary routeValues)
        {
            var actionName = (string)routeValues["action"];
            return actionName.EndsWith("Relationship", StringComparison.Ordinal);
        }

        private static bool IsOperationsRequest(RouteValueDictionary routeValues)
        {
            var actionName = (string)routeValues["action"];
            return actionName == "PostOperations";
        }

        private static void SetupOperationsRequest(JsonApiRequest request, IJsonApiOptions options, HttpRequest httpRequest)
        {
            request.IsReadOnly = false;
            request.Kind = EndpointKind.AtomicOperations;
            request.BasePath = GetBasePath(null, options, httpRequest);
        }
    }
}
