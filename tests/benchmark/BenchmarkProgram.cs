// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;

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
