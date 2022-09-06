// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.CopyOnWrite.Tests;

public sealed class FileHelperTests
{
    private static readonly string OsSpecificRoot = EnvironmentHelper.IsWindows ? @"c:\" : "/";

    [TestMethod]
    public void IsSubpathOfEmptyPathTest()
    {
        Assert.IsTrue(string.Empty.IsSubpathOf(string.Empty));
        Assert.IsFalse(string.Empty.IsSubpathOf(Path.Combine("a", "b", "c")));
    }

    [TestMethod]
    public void IsSubpathOfEmptyGivenPathTest()
    {
        Assert.IsTrue(string.Empty.IsSubpathOf(string.Empty));
    }

    [TestMethod]
    public void IsSubpathOfAbsoluteFolderPathPositiveTests()
    {
        TestExpectedSuccessSubpaths(Path.Combine(OsSpecificRoot, "a", "b"),
            Path.Combine(OsSpecificRoot, "a", "b", "c"),
            Path.Combine(OsSpecificRoot, "a", "b", "a.txt"));
    }

    [TestMethod]
    public void IsSubpathOfPathRelativeFolderPathPositiveTests()
    {
        TestExpectedSuccessSubpaths(@"a",
            Path.Combine("a", "a.txt"),
            Path.Combine("a", "b", "a.txt"));

        TestExpectedSuccessSubpaths(Path.Combine("a", "b"),
            Path.Combine("a", "b", "c"),
            Path.Combine("a", "b", "a.txt"),
            Path.Combine("a", "b", "c", "a.txt"));
    }

    [TestMethod]
    public void IsSubpathOfAbsoluteFilePathPositiveTest()
    {
        TestExpectedSuccessSubpaths(Path.Combine(OsSpecificRoot, "a.txt"));
        TestExpectedSuccessSubpaths(Path.Combine(OsSpecificRoot, "a", "b", "a.txt"));
    }

    [TestMethod]
    public void IsSubpathOfRelativeFilePathPositiveTest()
    {
        TestExpectedSuccessSubpaths("a.txt");
        TestExpectedSuccessSubpaths(Path.Combine("a", "b", "a.txt"));
    }

    [TestMethod]
    public void IsSubpathOfAbsoluteFolderPathNegativeTests()
    {
        if (EnvironmentHelper.IsWindows)
        {
            TestExpectedFailSubpaths("c:", "d:");
            TestExpectedFailSubpaths(@"c:\a\b", "c:");
        }

        TestExpectedFailSubpaths(Path.Combine(OsSpecificRoot, "a", "b"),
            Path.Combine(OsSpecificRoot, "a"),
            Path.Combine(OsSpecificRoot, "a", "b.txt"),
            Path.Combine(OsSpecificRoot, "ab", "b"),
            Path.Combine(OsSpecificRoot, "a", "c"),
            Path.Combine(OsSpecificRoot, "a", "ba"));

        TestExpectedFailSubpaths(Path.Combine(OsSpecificRoot, "foo", "barbarbar"), Path.Combine(OsSpecificRoot, "foo", "bar"));
        TestExpectedFailSubpaths(Path.Combine(OsSpecificRoot, "foo", "bar"),
            Path.Combine(OsSpecificRoot, "foo", "barbarbar"),
            Path.Combine(OsSpecificRoot, "foo", "barb"));
    }

    [TestMethod]
    public void IsSubpathOfRelativeFolderPathNegativeTests()
    {
        TestExpectedFailSubpaths(OsSpecificRoot,
            "d",
            Path.Combine("d", "c"),
            "c.txt");

        TestExpectedFailSubpaths(Path.Combine("a", "b"),
            Path.Combine(OsSpecificRoot, "a", "b"),
            "a",
            Path.Combine("a", "b.txt"),
            Path.Combine("ab", "b"),
            Path.Combine("a", "c"),
            Path.Combine("a", "ba"),
            Path.Combine("c", "a", "b"));

        TestExpectedFailSubpaths(Path.Combine("foo", "barbarbar"), Path.Combine("foo", "bar"));
        TestExpectedFailSubpaths(Path.Combine("foo", "bar"),
            Path.Combine("foo", "barbarbar"),
            Path.Combine("foo", "barb"));
    }

    [TestMethod]
    public void IsSubpathOfFilePathNegativeTests()
    {
        TestExpectedFailSubpaths(Path.Combine(OsSpecificRoot, "a.txt"),
            Path.Combine(OsSpecificRoot, "a"),
            Path.Combine(OsSpecificRoot, "a.txtt"),
            Path.Combine(OsSpecificRoot, "a.tx"),
            Path.Combine(OsSpecificRoot, "ab.txt"),
            Path.Combine(OsSpecificRoot, "a", "a.txt"),
            @"d:\a.txt");

        TestExpectedFailSubpaths(Path.Combine(OsSpecificRoot, "a", "b", "a.txt"),
            Path.Combine(OsSpecificRoot, "a", "b"),
            Path.Combine(OsSpecificRoot, "a", "b", "a"),
            Path.Combine(OsSpecificRoot, "a", "b", "atxt"),
            Path.Combine(OsSpecificRoot, "a", "a.txt"),
            Path.Combine(OsSpecificRoot, "a", "b", "c", "a.txt"));
    }

    private static void TestExpectedSuccessSubpaths(string compareToNoTrailingPathSeparators, params string[] expectedNoTrailingPathSeparators)
    {
        TestSubpaths(compareToNoTrailingPathSeparators, expectedNoTrailingPathSeparators, Assert.IsTrue);

        // Further checks for the positive case.
        string compareToWithPathSeparator = compareToNoTrailingPathSeparators + Path.DirectorySeparatorChar;
        Assert.IsTrue(compareToNoTrailingPathSeparators.IsSubpathOf(compareToNoTrailingPathSeparators));
        Assert.IsTrue(compareToNoTrailingPathSeparators.IsSubpathOf(compareToWithPathSeparator));
        Assert.IsTrue(compareToWithPathSeparator.IsSubpathOf(compareToNoTrailingPathSeparators));
        Assert.IsTrue(compareToWithPathSeparator.IsSubpathOf(compareToWithPathSeparator));
    }

    private static void TestExpectedFailSubpaths(string compareToNoTrailingPathSeparators, params string[] expectedNoTrailingPathSeparators)
    {
        TestSubpaths(compareToNoTrailingPathSeparators, expectedNoTrailingPathSeparators, Assert.IsFalse);
    }

    private static void TestSubpaths(string compareToNoTrailingPathSeparators, string[] expectedNoTrailingPathSeparators, Action<bool, string> assert)
    {
        string compareToWithPathSeparator = compareToNoTrailingPathSeparators + Path.DirectorySeparatorChar;

        // Case 1: No trailing separator in both cases.
        foreach (string expected in expectedNoTrailingPathSeparators)
        {
            assert(expected.IsSubpathOf(compareToNoTrailingPathSeparators), $"'{compareToNoTrailingPathSeparators}'=>'{expected}'");
        }

        // Case 2: compareTo no separator, expected with trailing.
        foreach (string expected in expectedNoTrailingPathSeparators
            .Select(s => s + Path.DirectorySeparatorChar))
        {
            assert(expected.IsSubpathOf(compareToNoTrailingPathSeparators), $"'{compareToNoTrailingPathSeparators}'=>'{expected}'");
        }

        // Case 3: compareTo separator, expected with none.
        foreach (string expected in expectedNoTrailingPathSeparators)
        {
            assert(expected.IsSubpathOf(compareToWithPathSeparator), $"'{compareToWithPathSeparator}'=>'{expected}'");
        }

        // Case 4: Separator on both.
        foreach (string expected in expectedNoTrailingPathSeparators
            .Select(s => s + Path.DirectorySeparatorChar))
        {
            assert(expected.IsSubpathOf(compareToWithPathSeparator), $"'{compareToWithPathSeparator}'=>'{expected}'");
        }
    }
}
