using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Caching;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace RedisObjectCache
{
    internal sealed class RedisCacheStore
    {
        private readonly IDatabase _redisDatabase;
        private readonly JsonSerializerSettings _jsonSerializerSettings;
        private readonly MemoryCache _bufferCache;

        internal RedisCacheStore(IDatabase redisDatabase)
        {
            _redisDatabase = redisDatabase;

            var redisJsonContractResolver = new RedisJsonContractResolver();

            //http://stackoverflow.com/a/13278092/794
            redisJsonContractResolver.IgnoreSerializableAttribute = true;

            _jsonSerializerSettings = new JsonSerializerSettings()
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                ContractResolver = redisJsonContractResolver,
                TypeNameHandling = TypeNameHandling.Objects
            };

            _bufferCache = MemoryCache.Default;
        }

        internal object Set(RedisCacheEntry entry)
        {
            var ttl = GetTtl(entry.State);

            var valueJson = JsonConvert.SerializeObject(entry.Value, _jsonSerializerSettings);
            var stateJson = JsonConvert.SerializeObject(entry.State, _jsonSerializerSettings);

            _redisDatabase.StringSet(entry.Key, valueJson, ttl);
            _redisDatabase.StringSet(entry.StateKey, stateJson, ttl);

            _bufferCache.Add(entry.Key, entry.Value, BufferOffset(entry.State));

            return entry.Value;
        }

        internal object Get(string key)
        {
            var redisCacheKey = new RedisCacheKey(key);

            var value = _bufferCache.Get(key);

            var stateJson = _redisDatabase.StringGet(redisCacheKey.StateKey);
            
            if (string.IsNullOrEmpty(stateJson))
            {
                if (value != null)
                {
                    _bufferCache.Remove(key); //redis should be the truth
                }
                return null;
            }

            var state = JsonConvert.DeserializeObject<RedisCacheEntryState>(stateJson);

            if (value == null) //TODO: deal with sliding if value found in buffer
            {
                var valueJson = _redisDatabase.StringGet(redisCacheKey.Key);

                value = GetObjectFromString(valueJson, state.TypeName);

                _bufferCache.Set(key, value, BufferOffset(state));
            }

            if (state.IsSliding)
            {
                //ignore buffer cache here - let it time out naturally
                state.UpdateUsage();
                stateJson = JsonConvert.SerializeObject(state, _jsonSerializerSettings);

                var ttl = GetTtl(state);
                _redisDatabase.StringSet(redisCacheKey.StateKey, stateJson, ttl);
                _redisDatabase.KeyExpire(redisCacheKey.Key, ttl);
            }

            return value;
        }

        internal object Remove(string key)
        {
            var redisCacheKey = new RedisCacheKey(key);
            var valueJson = _redisDatabase.StringGet(redisCacheKey.Key);
            if (string.IsNullOrEmpty(valueJson))
                return null;

            var value = JsonConvert.DeserializeObject(valueJson, _jsonSerializerSettings);

            _redisDatabase.KeyDelete(redisCacheKey.Key);
            _redisDatabase.KeyDelete(redisCacheKey.StateKey);

            _bufferCache.Remove(key);

            return value;
        }

        private TimeSpan GetTtl(RedisCacheEntryState state)
        {
            return state.UtcAbsoluteExpiration.Subtract(DateTime.UtcNow);
        }

        private object GetObjectFromString(string json, string typeName)
        {
            MethodInfo method = typeof(JsonConvert).GetMethods().Where(m => m.Name == "DeserializeObject" && m.IsGenericMethod).ElementAt(2);
            var t = Type.GetType(typeName);
            MethodInfo genericMethod = method.MakeGenericMethod(t);
            return genericMethod.Invoke(null, new object[]{ json, _jsonSerializerSettings }); // No target, no arguments
        }

        private DateTimeOffset BufferOffset(RedisCacheEntryState state)
        {
            var stateExpiration = state.UtcAbsoluteExpiration;
            var defaultBufferExpiration = DateTimeOffset.UtcNow.AddHours(1);

            if (stateExpiration > defaultBufferExpiration)
            {
                return defaultBufferExpiration;
            }
            return stateExpiration;
        }
    }
}
