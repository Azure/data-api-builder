using System.Diagnostics.CodeAnalysis;
using HotChocolate.Language;
using HotChocolate.Utilities;

namespace Azure.DataGateway.Service.Services
{
    // This class shouldn't be inlined like this
    // need to review this service to better leverage
    // HotChocolate and how it handles things such as
    // caching, but one change at a time.
    public sealed class DocumentCache : IDocumentCache
    {
        private readonly Cache<DocumentNode> _cache;

        public DocumentCache(int capacity = 100)
        {
            _cache = new Cache<DocumentNode>(capacity);
        }

        public int Capacity => _cache.Size;

        public int Count => _cache.Usage;

        public void TryAddDocument(
            string documentId,
            DocumentNode document) =>
            _cache.GetOrCreate(documentId, () => document);

        public bool TryGetDocument(
            string documentId,
            [NotNullWhen(true)] out DocumentNode document) =>
            _cache.TryGet(documentId, out document!);

        public void Clear() => _cache.Clear();
    }
}

