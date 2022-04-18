// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BenchmarkDotNet.Running;

namespace Microsoft.CopyOnWrite.Benchmarking;

public sealed class BenchmarkProgram
{
    static void Main(string[] args)
    {
        BenchmarkRunner.Run<CoWComparisons>(args: args);
    }
}
