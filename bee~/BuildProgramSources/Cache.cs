using System;
using System.Collections.Generic;
using System.ServiceModel.Configuration;

class Cache<TOutput, TKey>
{
    Dictionary<TKey, TOutput> _cache { get; } = new Dictionary<TKey, TOutput>();

    public void Clear() => _cache.Clear();

    public TOutput GetOrMake(TKey input, Func<TOutput> factory)
    {
        if (_cache.TryGetValue(input, out var result)) {
            return result;
        }
           
        result = factory();
        _cache.Add(input, result);
        return result;
    }
}
