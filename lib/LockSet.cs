// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.CopyOnWrite;

/// <summary>
/// This type is effectively 'void', but usable as a type parameter when a value type is needed.
/// </summary>
/// <remarks>
/// This is useful for generic methods dealing in tasks, since one can avoid having an overload for both
/// <see cref="Task" /> and <see cref="Task{TResult}" />. One instead provides only a <see cref="Task{TResult}" />
/// overload, and callers with a void result return <see cref="Void" />.
/// </remarks>
internal readonly struct ValueUnit
{
    /// <summary>
    /// Void unit type
    /// </summary>
    public static readonly ValueUnit Void = new();
}

/// <summary>
/// This is a collection of per-key exclusive locks.
/// Borrowed from the Domino code-base
/// </summary>
internal sealed class LockSet<TKey> where TKey : IEquatable<TKey>
{
    private readonly ConcurrentDictionary<TKey, LockHandle> _exclusiveLocks;

    // ReSharper disable once StaticFieldInGenericType
    private static long _currentHandleId = 1;

    public LockSet()
    {
        _exclusiveLocks = new ConcurrentDictionary<TKey, LockHandle>();
    }

    public LockSet(IEqualityComparer<TKey> keyComparer)
    {
        _exclusiveLocks = new ConcurrentDictionary<TKey, LockHandle>(keyComparer);
    }

    /// <summary>
    /// Acquires an exclusive lock for the given key. Dispose the returned handle
    /// to release the lock.
    /// </summary>
    public async
#if NET6_0 || NETSTANDARD2_1
    ValueTask<LockHandle>
#elif NETSTANDARD2_0
    Task<LockHandle>
#else
#error Target Framework not supported
#endif
        AcquireAsync(TKey key)
    {
        var thisHandle = new LockHandle(this, key);

        while (true)
        {
            LockHandle currentHandle = _exclusiveLocks.GetOrAdd(key, thisHandle);
            if (currentHandle == thisHandle)
            {
                break;
            }

            await currentHandle.TaskCompletionSource.Task.ConfigureAwait(false);
        }

        return thisHandle;
    }

    /// <summary>
    /// Releases an exclusive lock for the given key. One must release a lock after first await-ing an
    /// <see cref="AcquireAsync(TKey)" /> (by disposing the returned lock handle).
    /// </summary>
    private void Release(LockHandle handle)
    {
        bool removeSucceeded = _exclusiveLocks.TryRemoveSpecific(handle.Key, handle);
#if DEBUG
        if (!removeSucceeded)
        {
            throw new InvalidOperationException(
                "TryRemoveSpecific should not fail, since Release should only be called after AcquireAsync.");
        }
#endif

        handle.TaskCompletionSource.TrySetResult(ValueUnit.Void);
    }

    /// <summary>
    /// Represents an acquired lock in the collection. Call <see cref="Dispose" />
    /// to release the acquired lock.
    /// </summary>
    /// <remarks>
    /// FxCop requires equality operations to be overloaded for value types.
    /// Because lock handles should never be compared, these will all throw.
    /// </remarks>
    public readonly struct LockHandle : IEquatable<LockHandle>, IDisposable
    {
        private readonly LockSet<TKey> _locks;
        private readonly long _handleId;

        /// <summary>
        /// The associated TaskCompletionSource.
        /// </summary>
        public readonly TaskCompletionSource<ValueUnit> TaskCompletionSource;
            
        /// <summary>
        /// The associated Key.
        /// </summary>
        public TKey Key { get; }

        /// <summary>
        /// Construct a new instance for the given collection/key.
        /// </summary>
        public LockHandle(LockSet<TKey> locks, TKey key)
        {
            TaskCompletionSource = new TaskCompletionSource<ValueUnit>(TaskCreationOptions.RunContinuationsAsynchronously);
            _locks = locks;
            Key = key;
            _handleId = Interlocked.Increment(ref _currentHandleId);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            _locks.Release(this);
        }

        /// <inheritdoc/>
        public bool Equals(LockHandle other)
        {
            return (this == other);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            if (obj is LockHandle handle)
            {
                return Equals(handle);
            }
            return false;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return unchecked((int)_handleId);
        }

        public static bool operator ==(LockHandle a, LockHandle b)
        {
            return a._handleId == b._handleId;
        }

        public static bool operator !=(LockHandle a, LockHandle b)
        {
            return !(a == b);
        }
    }
}

internal static class ConcurrentDictionaryExtensions
{
    /// <summary>
    /// Attempt to remove a specific item from the ConcurrentDictionary.
    /// </summary>
    /// <remarks>
    /// https://devblogs.microsoft.com/pfxteam/little-known-gems-atomic-conditional-removals-from-concurrentdictionary/
    /// </remarks>
    public static bool TryRemoveSpecific<TKey, TValue>(
        this ConcurrentDictionary<TKey, TValue> dictionary,
        TKey key,
        TValue value)
        where TKey: notnull
    {
        return ((ICollection<KeyValuePair<TKey, TValue>>)dictionary).Remove(
            new KeyValuePair<TKey, TValue>(key, value));
    }
}
