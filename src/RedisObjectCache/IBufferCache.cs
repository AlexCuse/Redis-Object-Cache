using System;
using System.Runtime.Caching;

namespace RedisObjectCache
{
    interface IBufferCache
    {
        BufferCacheEntry Get(string key);

        void Set(string key, RedisCacheEntryState state, object value);

        void Remove(string key);
    }

    class BufferCache : IBufferCache
    {
        private readonly IRedisCacheOptions _redisCacheOptions;
        private readonly MemoryCache _buffer;

        public BufferCache(IRedisCacheOptions redisCacheOptions)
        {
            _redisCacheOptions = redisCacheOptions;
            if (_redisCacheOptions.BufferEnabled)
            {
                _buffer = MemoryCache.Default;
            }
        }

        public BufferCacheEntry Get(string key)
        {
            if (!_redisCacheOptions.BufferEnabled)
            {
                return null;
            }

            return _buffer.Get(key) as BufferCacheEntry;
        }

        public void Set(string key, RedisCacheEntryState state, object value)
        {
            if (!_redisCacheOptions.BufferEnabled)
            {
                return;
            }

            if (ShouldBuffer(key))
            {
                _buffer.Set(key, new BufferCacheEntry { Created = state.UtcCreated, Value = value },
                    BufferOffset(state));
            }
            else
            {
                _buffer.Remove(key);//this is potentially redundant but want to make sure it is removed if buffer value is stale
            }
        }

        public void Remove(string key)
        {
            if (!_redisCacheOptions.BufferEnabled)
            {
                return;
            }

            _buffer.Remove(key);
        }

        private bool ShouldBuffer(string key)
        {
            return !key.EndsWith(RedisCache.SKIP_BUFFER_KEY_ENDING);
        }

        private DateTimeOffset BufferOffset(RedisCacheEntryState state)
        {
            var stateExpiration = state.UtcAbsoluteExpiration;
            var defaultBufferExpiration = DateTimeOffset.UtcNow.AddMinutes(_redisCacheOptions.BufferTimeoutInMinutes);

            if (stateExpiration > defaultBufferExpiration)
            {
                return defaultBufferExpiration;
            }
            return stateExpiration;
        }
    }
}
