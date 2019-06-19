﻿using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Statiq.Common.Documents;
using Statiq.Common.Modules;
using Statiq.Common.Execution;
using Statiq.Common;

namespace Statiq.Core.Modules.Control
{
    /// <summary>
    /// Replaces the content and merges metadata of each input document with the results of specified modules.
    /// </summary>
    /// <remarks>
    /// Replaces the content and merges the metadata of each input document with the results of the specified modules
    /// executed against an empty initial document. If more than one output document is generated by the specified modules,
    /// each input document will be merged with each result document.
    /// </remarks>
    /// <category>Control</category>
    public class Merge : ContainerModule
    {
        private bool _forEachDocument;

        /// <summary>
        /// The specified modules are executed against an empty initial document and the results
        /// are applied to every input document (possibly creating more than one output
        /// document for each input document).
        /// </summary>
        /// <param name="modules">The modules to execute.</param>
        public Merge(params IModule[] modules)
            : this((IEnumerable<IModule>)modules)
        {
        }

        /// <summary>
        /// The specified modules are executed against an empty initial document and the results
        /// are applied to every input document (possibly creating more than one output
        /// document for each input document).
        /// </summary>
        /// <param name="modules">The modules to execute.</param>
        public Merge(IEnumerable<IModule> modules)
            : base(modules)
        {
        }

        /// <summary>
        /// Specifies that the whole sequence of child modules should be executed for every input document
        /// (as opposed to the default behavior of the sequence of modules only being executed once
        /// with all input documents). This method has no effect if no modules are specified.
        /// </summary>
        /// <returns>The current module instance.</returns>
        public Merge ForEachDocument()
        {
            _forEachDocument = true;
            return this;
        }

        /// <inheritdoc />
        public override async Task<IEnumerable<IDocument>> ExecuteAsync(IReadOnlyList<IDocument> inputs, IExecutionContext context)
        {
            if (Children.Count > 0)
            {
                // Execute the modules for each input document
                if (_forEachDocument)
                {
                    return await inputs.SelectManyAsync(context, async input =>
                        (await context.ExecuteAsync(Children, new[] { input }))
                            .Select(result => context.GetDocument(input, result, result.ContentProvider)));
                }

                // Execute the modules once and apply to each input document
                List<IDocument> results = (await context.ExecuteAsync(Children)).ToList();
                return inputs.SelectMany(context, input => results.Select(result => context.GetDocument(input, result, result.ContentProvider)));
            }

            return inputs;
        }
    }
}
