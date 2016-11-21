using System;

namespace RedisObjectCache
{
    internal class BufferCacheEntry
    {
        public DateTime Created { get; set; }

        public object Value { get; set; }
    }
}