using System;
using System.Collections.Concurrent;

namespace YSMInstaller {
    /// <summary>
    /// Process-lifetime cache with per-entry TTL. Bounded keyset only — expired entries
    /// stay in <see cref="Store"/> until overwritten (no background sweep), so do not use
    /// for unbounded keys. Callers should Set only on successful responses so failures
    /// stay uncached and the next attempt re-fetches.
    /// </summary>
    public static class MemoryCache {
        private readonly struct Entry {
            public Entry(object value, DateTime expiresAt) {
                Value = value;
                ExpiresAt = expiresAt;
            }
            public object Value { get; }
            public DateTime ExpiresAt { get; }
        }

        private static readonly ConcurrentDictionary<string, Entry> Store =
            new ConcurrentDictionary<string, Entry>(StringComparer.Ordinal);

        public static bool TryGet<T>(string key, out T value) {
            if (Store.TryGetValue(key, out Entry entry) && entry.ExpiresAt > DateTime.UtcNow) {
                if (entry.Value is T typed) {
                    value = typed;
                    return true;
                }
            }
            value = default!;
            return false;
        }

        public static void Set<T>(string key, T value, TimeSpan ttl) where T : notnull {
            Store[key] = new Entry(value, DateTime.UtcNow.Add(ttl));
        }
    }
}
