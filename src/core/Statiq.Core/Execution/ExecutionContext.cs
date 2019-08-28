﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Statiq.Common;

namespace Statiq.Core
{
    internal class ExecutionContext : IExecutionContext
    {
        // Cache the HttpMessageHandler (the HttpClient is really just a thin wrapper around this)
        private static readonly HttpMessageHandler _httpMessageHandler = new HttpClientHandler();

        private readonly ExecutionContextData _contextData;

        /// <inheritdoc/>
        public Guid ExecutionId => _contextData.ExecutionId;

        /// <inheritdoc/>
        public IReadOnlyCollection<byte[]> DynamicAssemblies => _contextData.Engine.DynamicAssemblies;

        /// <inheritdoc/>
        public IReadOnlyCollection<string> Namespaces => _contextData.Engine.Namespaces;

        /// <inheritdoc/>
        public string PipelineName => _contextData.PipelinePhase.PipelineName;

        /// <inheritdoc/>
        public Phase Phase => _contextData.PipelinePhase.Phase;

        /// <inheritdoc/>
        public IPipelineOutputs Outputs => _contextData.Outputs;

        /// <inheritdoc/>
        public IReadOnlyFileSystem FileSystem => _contextData.Engine.FileSystem;

        /// <inheritdoc/>
        public IReadOnlySettings Settings => _contextData.Engine.Settings;

        /// <inheritdoc/>
        public IReadOnlyShortcodeCollection Shortcodes => _contextData.Engine.Shortcodes;

        /// <inheritdoc/>
        public IServiceProvider Services => _contextData.Services;  // This has to come from the context data and not the engine because it's a child provider scope

        /// <inheritdoc/>
        public string ApplicationInput => _contextData.Engine.ApplicationInput;

        /// <inheritdoc/>
        public IMemoryStreamFactory MemoryStreamFactory => _contextData.Engine.MemoryStreamFactory;

        /// <inheritdoc/>
        public CancellationToken CancellationToken => _contextData.CancellationToken;

        /// <inheritdoc/>
        public IExecutionContext Parent { get; }

        /// <inheritdoc/>
        public IModule Module { get; }

        /// <inheritdoc/>
        public ImmutableArray<IDocument> Inputs { get; }

        /// <inheritdoc/>
        public ILogger Logger { get; }

        internal ExecutionContext(ExecutionContextData contextData, IExecutionContext parent, IModule module, ImmutableArray<IDocument> inputs)
        {
            _contextData = contextData ?? throw new ArgumentNullException(nameof(contextData));
            Parent = parent;
            Module = module ?? throw new ArgumentNullException(nameof(module));
            Inputs = inputs;
            Logger = GetLogger(parent, module, contextData.PipelinePhase, contextData.Services.GetRequiredService<ILoggerFactory>());
        }

        private static ILogger GetLogger(IExecutionContext parent, IModule module, PipelinePhase pipelinePhase, ILoggerFactory loggerFactory)
        {
            Stack<string> modules = new Stack<string>();
            modules.Push(module.GetType().Name);
            while (parent != null)
            {
                modules.Push(parent.Module.GetType().Name);
                parent = parent.Parent;
            }
            return loggerFactory?.CreateLogger($"[{pipelinePhase.PipelineName}][{pipelinePhase.Phase}][{string.Join('.', modules)}]") ?? NullLogger.Instance;
        }

        /// <inheritdoc/>
        public HttpClient CreateHttpClient() => CreateHttpClient(_httpMessageHandler);

        /// <inheritdoc/>
        public HttpClient CreateHttpClient(HttpMessageHandler handler)
        {
            HttpClient client = new HttpClient(handler, false)
            {
                Timeout = TimeSpan.FromSeconds(60)
            };
            client.DefaultRequestHeaders.Add("User-Agent", "Statiq");
            return client;
        }

        /// <inheritdoc/>
        public async Task<Stream> GetContentStreamAsync(string content = null)
        {
            if (this.Bool(Common.Keys.UseStringContentFiles))
            {
                // Use a temp file for strings
                IFile tempFile = FileSystem.GetTempFile();
                if (!string.IsNullOrEmpty(content))
                {
                    await tempFile.WriteAllTextAsync(content);
                }
                return new ContentStream(new FileContent(tempFile), tempFile.Open(), true);
            }

            // Otherwise get a memory stream from the pool and use that
            Stream memoryStream = MemoryStreamFactory.GetStream(content);
            return new ContentStream(new Common.StreamContent(MemoryStreamFactory, memoryStream), memoryStream, false);
        }

        /// <inheritdoc/>
        public async Task<ImmutableArray<IDocument>> ExecuteModulesAsync(IEnumerable<IModule> modules, IEnumerable<IDocument> inputs) =>
            await Engine.ExecuteModulesAsync(_contextData, this, modules, inputs?.ToImmutableArray() ?? ImmutableArray<IDocument>.Empty, Logger);

        /// <inheritdoc/>
        public IJavaScriptEnginePool GetJavaScriptEnginePool(
            Action<IJavaScriptEngine> initializer = null,
            int startEngines = 10,
            int maxEngines = 25,
            int maxUsagesPerEngine = 100,
            TimeSpan? engineTimeout = null) =>
            new JavaScriptEnginePool(
                initializer,
                startEngines,
                maxEngines,
                maxUsagesPerEngine,
                engineTimeout ?? TimeSpan.FromSeconds(5));

        // IDocumentFactory

        /// <inheritdoc />
        public IDocument CreateDocument(
            FilePath source,
            FilePath destination,
            IEnumerable<KeyValuePair<string, object>> items,
            IContentProvider contentProvider = null) =>
            _contextData.Engine.DocumentFactory.CreateDocument(source, destination, items, contentProvider);

        /// <inheritdoc />
        public TDocument CreateDocument<TDocument>(
            FilePath source,
            FilePath destination,
            IEnumerable<KeyValuePair<string, object>> items,
            IContentProvider contentProvider = null)
            where TDocument : FactoryDocument, IDocument, new() =>
            _contextData.Engine.DocumentFactory.CreateDocument<TDocument>(source, destination, items, contentProvider);

        // IMetadata

        /// <inheritdoc/>
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <inheritdoc/>
        public IEnumerator<KeyValuePair<string, object>> GetEnumerator() => Settings.GetEnumerator();

        /// <inheritdoc/>
        public int Count => Settings.Count;

        /// <inheritdoc/>
        public bool ContainsKey(string key) => Settings.ContainsKey(key);

        /// <inheritdoc/>
        public object this[string key] => Settings[key];

        /// <inheritdoc/>
        public IEnumerable<string> Keys => Settings.Keys;

        /// <inheritdoc/>
        public IEnumerable<object> Values => Settings.Values;

        /// <inheritdoc/>
        public bool TryGetRaw(string key, out object value) => Settings.TryGetRaw(key, out value);

        /// <inheritdoc/>
        public bool TryGetValue(string key, out object value) => TryGetValue<object>(key, out value);

        /// <inheritdoc/>
        public bool TryGetValue<TValue>(string key, out TValue value) => Settings.TryGetValue(key, out value);

        /// <inheritdoc/>
        public IMetadata GetMetadata(params string[] keys) => Settings.GetMetadata(keys);
    }
}
