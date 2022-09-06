// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.CopyOnWrite.Tests;

public sealed class LockSetTests
{
    private readonly LockSet<string> _lockSet = new();
            
    [TestMethod]
    public async Task Acquire()
    {
        using (await _lockSet.AcquireAsync("key1"))
        {
        }
    }

    [TestMethod]
    public async Task AcquireBlocksAnotherAcquireOfSameKey()
    {
        Task secondAcquire;

        using (await _lockSet.AcquireAsync("key1"))
        {
            secondAcquire = Task.Run(async () =>
            {
                using (await _lockSet.AcquireAsync("key1"))
                {
                }
            });
            Assert.IsFalse(secondAcquire.Wait(50));
        }

        secondAcquire.Wait();
    }

    [TestMethod]
    public async Task AcquireDoesNotBlocksAnotherAcquireOfDifferKey()
    {
        using (await _lockSet.AcquireAsync("key1"))
        {
            Task secondAcquire = Task.Run(async () =>
            {
                using (await _lockSet.AcquireAsync("key2"))
                {
                }
            });

            Assert.IsTrue(secondAcquire.Wait(5000));
        }
    }

    [TestMethod]
    public void DifferentKeysHaveDifferentHandles()
    {
        using var handle1 = _lockSet.AcquireAsync("key1").Result;
        using var handle2 = _lockSet.AcquireAsync("key2").Result;
        Assert.AreNotEqual(handle2, handle1);
        Assert.AreNotEqual(handle2.GetHashCode(), handle1.GetHashCode());
    }

    [TestMethod]
    public void EqualsGivesFalseForOtherType()
    {
        using var handle = _lockSet.AcquireAsync("key1").Result;
        Assert.IsFalse(handle.Equals(new object()));
    }
}
