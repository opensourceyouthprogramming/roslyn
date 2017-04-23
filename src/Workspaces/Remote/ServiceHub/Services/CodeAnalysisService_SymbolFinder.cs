﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Editor.FindUsages;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.FindUsages;
using Microsoft.CodeAnalysis.Text;

namespace Microsoft.CodeAnalysis.Remote
{
    // root level service for all Roslyn services
    internal partial class CodeAnalysisService : IRemoteSymbolFinder
    {
        public async Task FindDocReferencesAsync(DocumentId documentId, int position)
        {
            var solution = await GetSolutionAsync().ConfigureAwait(false);
            var document = solution.GetDocument(documentId);

            var context = new FindUsagesContext(this);
            await AbstractFindUsagesService.TryFindReferencesInCurrentProcessAsync(
                document, position, context).ConfigureAwait(false);
        }

        public async Task FindSymbolReferencesAsync(SerializableSymbolAndProjectId symbolAndProjectIdArg, DocumentId[] documentArgs)
        {
            var solution = await GetSolutionAsync().ConfigureAwait(false);

            var symbolAndProjectId = await symbolAndProjectIdArg.TryRehydrateAsync(
                solution, CancellationToken).ConfigureAwait(false);

            var progressCallback = new FindSymbolReferencesProgressCallback(this);

            if (!symbolAndProjectId.HasValue)
            {
                await progressCallback.OnStartedAsync().ConfigureAwait(false);
                await progressCallback.OnCompletedAsync().ConfigureAwait(false);
                return;
            }

            var documents = documentArgs?.Select(solution.GetDocument)
                                         .ToImmutableHashSet();

            await SymbolFinder.FindReferencesInCurrentProcessAsync(
                symbolAndProjectId.Value, solution, 
                progressCallback, documents, CancellationToken).ConfigureAwait(false);
        }

        public async Task FindLiteralReferencesAsync(object value)
        {
            var solution = await GetSolutionAsync().ConfigureAwait(false);

            var progressCallback = new FindLiteralReferencesProgressCallback(this);
            await SymbolFinder.FindLiteralReferencesInCurrentProcessAsync(
                value, solution, progressCallback, CancellationToken).ConfigureAwait(false);
        }

        public async Task<SerializableSymbolAndProjectId[]> FindAllDeclarationsWithNormalQueryAsync(
            ProjectId projectId, string name, SearchKind searchKind, SymbolFilter criteria)
        {
            var solution = await GetSolutionAsync().ConfigureAwait(false);
            var project = solution.GetProject(projectId);

            using (var query = SearchQuery.Create(name, searchKind))
            {
                var result = await DeclarationFinder.FindAllDeclarationsWithNormalQueryInCurrentProcessAsync(
                    project, query, criteria, this.CancellationToken).ConfigureAwait(false);

                return result.Select(SerializableSymbolAndProjectId.Dehydrate).ToArray();
            }
        }

        public async Task<SerializableSymbolAndProjectId[]> FindSolutionSourceDeclarationsWithNormalQueryAsync(
            string name, bool ignoreCase, SymbolFilter criteria)
        {
            var solution = await GetSolutionAsync().ConfigureAwait(false);
            var result = await DeclarationFinder.FindSourceDeclarationsWithNormalQueryInCurrentProcessAsync(
                solution, name, ignoreCase, criteria, CancellationToken).ConfigureAwait(false);

            return result.Select(SerializableSymbolAndProjectId.Dehydrate).ToArray();
        }

        public async Task<SerializableSymbolAndProjectId[]> FindProjectSourceDeclarationsWithNormalQueryAsync(
            ProjectId projectId, string name, bool ignoreCase, SymbolFilter criteria)
        {
            var solution = await GetSolutionAsync().ConfigureAwait(false);
            var project = solution.GetProject(projectId);

            var result = await DeclarationFinder.FindSourceDeclarationsWithNormalQueryInCurrentProcessAsync(
                project, name, ignoreCase, criteria, CancellationToken).ConfigureAwait(false);

            return result.Select(SerializableSymbolAndProjectId.Dehydrate).ToArray();
        }

        public async Task<SerializableSymbolAndProjectId[]> FindProjectSourceDeclarationsWithPatternAsync(
            ProjectId projectId, string pattern, SymbolFilter criteria)
        {
            var solution = await GetSolutionAsync().ConfigureAwait(false);
            var project = solution.GetProject(projectId);

            var result = await DeclarationFinder.FindSourceDeclarationsWithPatternInCurrentProcessAsync(
                project, pattern, criteria, CancellationToken).ConfigureAwait(false);

            return result.Select(SerializableSymbolAndProjectId.Dehydrate).ToArray();
        }

        private class FindUsagesContext : IFindUsagesContext
        {
            private readonly CodeAnalysisService _service;

            public FindUsagesContext(CodeAnalysisService service)
            {
                _service = service;
            }

            public CancellationToken CancellationToken
                => _service.CancellationToken;

            public Task ReportMessageAsync(string message)
                => _service.Rpc.InvokeAsync(nameof(ReportMessageAsync), message);

            public Task SetSearchTitleAsync(string title)
                => _service.Rpc.InvokeAsync(nameof(SetSearchTitleAsync), title);

            public Task ReportProgressAsync(int current, int maximum)
                => _service.Rpc.InvokeAsync(nameof(ReportProgressAsync), current, maximum);

            public Task OnDefinitionFoundAsync(DefinitionItem definition)
                => _service.Rpc.InvokeAsync(nameof(OnDefinitionFoundAsync),
                    SerializableDefinitionItem.Dehydrate(definition));

            public Task OnReferenceFoundAsync(SourceReferenceItem reference)
                => _service.Rpc.InvokeAsync(nameof(OnReferenceFoundAsync),
                    SerializableSourceReferenceItem.Dehydrate(reference));
        }

        private class FindLiteralReferencesProgressCallback : IStreamingFindLiteralReferencesProgress
        {
            private readonly CodeAnalysisService _service;

            public FindLiteralReferencesProgressCallback(CodeAnalysisService service)
            {
                _service = service;
            }

            public Task ReportProgressAsync(int current, int maximum)
                => _service.Rpc.InvokeAsync(nameof(ReportProgressAsync), current, maximum);

            public Task OnReferenceFoundAsync(Document document, TextSpan span)
                => _service.Rpc.InvokeAsync(nameof(OnReferenceFoundAsync), document.Id, span);
        }

        private class FindSymbolReferencesProgressCallback : IStreamingFindReferencesProgress
        {
            private readonly CodeAnalysisService _service;

            public FindSymbolReferencesProgressCallback(CodeAnalysisService service)
            {
                _service = service;
            }

            public Task OnStartedAsync()
                => _service.Rpc.InvokeAsync(nameof(OnStartedAsync));

            public Task OnCompletedAsync()
                => _service.Rpc.InvokeAsync(nameof(OnCompletedAsync));

            public Task ReportProgressAsync(int current, int maximum)
                => _service.Rpc.InvokeAsync(nameof(ReportProgressAsync), current, maximum);

            public Task OnFindInDocumentStartedAsync(Document document)
                => _service.Rpc.InvokeAsync(nameof(OnFindInDocumentStartedAsync), document.Id);

            public Task OnFindInDocumentCompletedAsync(Document document)
                => _service.Rpc.InvokeAsync(nameof(OnFindInDocumentCompletedAsync), document.Id);

            public Task OnDefinitionFoundAsync(SymbolAndProjectId definition)
                => _service.Rpc.InvokeAsync(nameof(OnDefinitionFoundAsync),
                    SerializableSymbolAndProjectId.Dehydrate(definition));

            public Task OnReferenceFoundAsync(
                SymbolAndProjectId definition, ReferenceLocation reference)
            {
                return _service.Rpc.InvokeAsync(nameof(OnReferenceFoundAsync),
                    SerializableSymbolAndProjectId.Dehydrate(definition),
                    SerializableReferenceLocation.Dehydrate(reference));
            }
        }
    }
}