using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RedisObjectCache
{
    public interface IRedisCacheOptions
    {
        bool BufferEnabled { get; }

        int BufferTimeoutInMinutes { get; }
    }
}
