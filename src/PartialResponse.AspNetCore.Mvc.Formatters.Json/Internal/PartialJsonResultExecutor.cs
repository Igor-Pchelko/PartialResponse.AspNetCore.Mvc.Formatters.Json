// Copyright (c) Arjen Post. See LICENSE and NOTICE in the project root for license information.

using System;
using System.Buffers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Newtonsoft.Json;
using PartialResponse.Core;

namespace PartialResponse.AspNetCore.Mvc.Formatters.Json.Internal
{
    /// <summary>
    /// Executes a <see cref="PartialJsonResult"/> to write to the response.
    /// </summary>
    public class PartialJsonResultExecutor
    {
        private static readonly string DefaultContentType = new MediaTypeHeaderValue("application/json") { Encoding = Encoding.UTF8 }.ToString();

        private readonly IArrayPool<char> charPool;

        /// <summary>
        /// Initializes a new instance of the <see cref="PartialJsonResultExecutor"/> class.
        /// </summary>
        /// <param name="writerFactory">The <see cref="IHttpResponseStreamWriterFactory"/>.</param>
        /// <param name="logger">The <see cref="ILogger{PartialJsonResultExecutor}"/>.</param>
        /// <param name="options">The <see cref="IOptions{MvcPartialJsonOptions}"/>.</param>
        /// <param name="charPool">The <see cref="ArrayPool{Char}"/> for creating <see cref="T:char[]"/> buffers.</param>
        public PartialJsonResultExecutor(IHttpResponseStreamWriterFactory writerFactory, ILogger<PartialJsonResultExecutor> logger, IOptions<MvcPartialJsonOptions> options, ArrayPool<char> charPool)
        {
            if (writerFactory == null)
            {
                throw new ArgumentNullException(nameof(writerFactory));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (charPool == null)
            {
                throw new ArgumentNullException(nameof(charPool));
            }

            this.WriterFactory = writerFactory;
            this.Logger = logger;
            this.Options = options.Value;
            this.charPool = new JsonArrayPool<char>(charPool);
        }

        /// <summary>
        /// Gets the <see cref="ILogger"/>.
        /// </summary>
        protected ILogger Logger { get; }

        /// <summary>
        /// Gets the <see cref="MvcPartialJsonOptions"/>.
        /// </summary>
        protected MvcPartialJsonOptions Options { get; }

        /// <summary>
        /// Gets the <see cref="IHttpResponseStreamWriterFactory"/>.
        /// </summary>
        protected IHttpResponseStreamWriterFactory WriterFactory { get; }

        /// <summary>
        /// Executes the <see cref="PartialJsonResult"/> and writes the response.
        /// </summary>
        /// <param name="context">The <see cref="ActionContext"/>.</param>
        /// <param name="result">The <see cref="PartialJsonResult"/>.</param>
        /// <returns>A <see cref="Task"/> which will complete when writing has completed.</returns>
        public Task ExecuteAsync(ActionContext context, PartialJsonResult result)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            var request = context.HttpContext.Request;
            var response = context.HttpContext.Response;

            Fields? fields;

            if (!request.TryGetFields(out fields))
            {
                response.StatusCode = 400;

                return Task.CompletedTask;
            }

            string resolvedContentType = null;
            Encoding resolvedContentTypeEncoding = null;

            ResponseContentTypeHelper.ResolveContentTypeAndEncoding(result.ContentType, response.ContentType, DefaultContentType, out resolvedContentType, out resolvedContentTypeEncoding);

            response.ContentType = resolvedContentType;

            if (result.StatusCode != null)
            {
                response.StatusCode = result.StatusCode.Value;
            }

            var serializerSettings = result.SerializerSettings ?? this.Options.SerializerSettings;

            this.Logger.PartialJsonResultExecuting(result.Value);

            using (var writer = this.WriterFactory.CreateWriter(response.Body, resolvedContentTypeEncoding))
            {
                using (var jsonWriter = new JsonTextWriter(writer))
                {
                    jsonWriter.ArrayPool = this.charPool;
                    jsonWriter.CloseOutput = false;

                    var jsonSerializer = JsonSerializer.Create(serializerSettings);

                    if (fields.HasValue)
                    {
                        jsonSerializer.Serialize(jsonWriter, result.Value, path => fields.Value.Matches(path, this.Options.IgnoreCase));
                    }
                    else
                    {
                        jsonSerializer.Serialize(jsonWriter, result.Value);
                    }
                }
            }

            return Task.CompletedTask;
        }
    }
}
