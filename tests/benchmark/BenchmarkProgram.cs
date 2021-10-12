// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BenchmarkDotNet.Running;
using Microsoft.CopyOnWrite.TestUtilities;

namespace Microsoft.CopyOnWrite.Benchmarking
{
    public class BenchmarkProgram
    {
        static void Main(string[] args)
        {
            BenchmarkRunner.Run<CoWComparisons>(args: args);
        }
    }
}
