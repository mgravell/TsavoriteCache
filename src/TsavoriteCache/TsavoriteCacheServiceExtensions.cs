﻿using TsavoriteCache;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace Microsoft.Extensions.DependencyInjection;
#pragma warning restore IDE0130 // Namespace does not match folder structure

/// <summary>
/// Allows Tsavorite to be used with dependency injection
/// </summary>
[Experimental("FCACHE002")]
public static class TsavoriteCacheServiceExtensions
{
    public static TValue? Get<TState, TValue>(this IDistributedCache cache, string key, in TState state, Func<TState, ReadOnlySequence<byte>, TValue> deserializer)
    {
        if (cache is ITsavoriteDistributedCache TsavoriteCache)
        {
            return TsavoriteCache.Get(key, state, deserializer);
        }
#if NET9_0_OR_GREATER
        if (cache is IBufferDistributedCache bufferCache)
        {
            return bufferCache.Get(key, state, deserializer);
        }
#endif
        var arr = cache.Get(key);
        return arr is null ? default : deserializer(state, new(arr));
    }

#if NET9_0_OR_GREATER
    private static TValue? Get<TState, TValue>(this IBufferDistributedCache cache, string key, in TState state, Func<TState, ReadOnlySequence<byte>, TValue> deserializer)
    {
        var bw = new ArrayBufferWriter<byte>(); // TODO: recycling
        return cache.TryGet(key, bw) ? deserializer(state, new(bw.WrittenMemory)) : default;
    }
#endif
    public static ValueTask<TValue?> GetAsync<TState, TValue>(this IDistributedCache cache, string key, in TState state, Func<TState, ReadOnlySequence<byte>, TValue> deserializer, CancellationToken token = default)
    {
        if (cache is ITsavoriteDistributedCache TsavoriteCache)
        {
            return TsavoriteCache.GetAsync(key, state, deserializer, token);
        }

#if NET9_0_OR_GREATER
        if (cache is IBufferDistributedCache bufferCache)
        {
            return bufferCache.GetAsync(key, state, deserializer, token);
        }
#endif
        var pending = cache.GetAsync(key, token);
        if (!pending.IsCompletedSuccessfully) return Awaited(pending, state, deserializer);

        var arr = pending.GetAwaiter().GetResult();
        return arr is null ? default : new(deserializer(state, new(arr)));

        static async ValueTask<TValue?> Awaited(Task<byte[]?> pending, TState state, Func<TState, ReadOnlySequence<byte>, TValue> deserializer)
        {
            var arr = await pending;
            return arr is null ? default : deserializer(state, new(arr));
        }
    }

#if NET9_0_OR_GREATER
    private static ValueTask<TValue?> GetAsync<TState, TValue>(this IBufferDistributedCache cache, string key, in TState state, Func<TState, ReadOnlySequence<byte>, TValue> deserializer, CancellationToken token = default)
    {
        var bw = new ArrayBufferWriter<byte>(); // TODO: recycling
        var pending = cache.TryGetAsync(key, bw, token);
        if (!pending.IsCompletedSuccessfully) return Awaited(pending, bw, state, deserializer);

        return pending.GetAwaiter().GetResult() ? new(deserializer(state, new(bw.WrittenMemory))) : default;

        static async ValueTask<TValue?> Awaited(ValueTask<bool> pending, ArrayBufferWriter<byte> bw, TState state, Func<TState, ReadOnlySequence<byte>, TValue> deserializer)
        {
            return await pending ? deserializer(state, new(bw.WrittenMemory)) : default;
        }
    }
#endif

    public static void AddTsavoriteCache(this IServiceCollection services, Action<TsavoriteCacheOptions> setupAction)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(setupAction);
        services.AddLogging();
        if (setupAction is not null)
        {
            services.Configure(setupAction)
                .AddOptionsWithValidateOnStart<TsavoriteCacheOptions, TsavoriteCacheOptions.Validator>();
        }
        services.TryAddSingleton<CacheService>();
    }

    public static void AddTsavoriteDistributedCache(this IServiceCollection services, Action<TsavoriteCacheOptions>? setupAction = null)
    {
        AddTsavoriteCache(services, setupAction!); // the shared core handles null (for reuse scenarios)
        services.TryAddSingleton<IDistributedCache, DistributedCache>();
    }
}
