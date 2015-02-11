﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace RedisObjectCache
{
    internal sealed class RedisCacheStore
    {
        private readonly IDatabase _redisDatabase;

        internal RedisCacheStore(IDatabase redisDatabase)
        {
            _redisDatabase = redisDatabase;
        }

        internal void Set(RedisCacheEntry entry)
        {
            var ttl = GetTtl(entry.State);

            var valueJson = JsonConvert.SerializeObject(entry.Value);
            var stateJson = JsonConvert.SerializeObject(entry.State);

            _redisDatabase.StringSet(entry.Key, valueJson, ttl);
            _redisDatabase.StringSet(entry.StateKey, stateJson, ttl);
        }

        internal object Get(string key)
        {
            var redisCacheKey = new RedisCacheKey(key);

            var stateJson = _redisDatabase.StringGet(redisCacheKey.StateKey);
            var valueJson = _redisDatabase.StringGet(redisCacheKey.Key);

            var state = JsonConvert.DeserializeObject<RedisCacheEntryState>(stateJson);
            var value = JsonConvert.DeserializeObject(valueJson);

            if (state.IsSliding)
            {
                state.UpdateUsage();
                stateJson = JsonConvert.SerializeObject(state);

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
            var value = JsonConvert.DeserializeObject(valueJson);

            _redisDatabase.KeyDelete(redisCacheKey.Key);
            _redisDatabase.KeyDelete(redisCacheKey.StateKey);

            return value;
        }

        private TimeSpan GetTtl(RedisCacheEntryState state)
        {
            return state.AbsoluteExpiration.Subtract(DateTime.UtcNow);
        }
    }
}