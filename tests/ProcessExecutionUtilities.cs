// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Text;

#nullable enable

namespace Microsoft.CopyOnWrite.Tests
{
    /// <summary>
    /// Shared process execution methods used by client and agent.
    /// </summary>
    public static class ProcessExecutionUtilities
    {
        /// <summary>
        /// Synchronously executes a process without timeout or stdout/err callbacks. Throws on a nonzero exit code.
        /// </summary>
        public static ProcessRunResult RunAndCaptureOutput(
            string fileName,
            string? arguments = null,
            string? workingDirectory = null,
            bool throwOnError = true)
        {
            arguments ??= string.Empty;
            workingDirectory ??= Environment.CurrentDirectory;

            var startInfo = new ProcessStartInfo(fileName, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                ErrorDialog = false,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = workingDirectory,
            };

            var errors = new StringBuilder(1024);
            var output = new StringBuilder(1024);

            using var process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = false,
            };

            process.ErrorDataReceived += (_, args) =>
            {
                if (args.Data is not null)
                {
                    errors.AppendLine(args.Data);
                }
            };

            process.OutputDataReceived += (_, args) =>
            {
                if (args.Data is not null)
                {
                    output.AppendLine(args.Data);
                }
            };

            bool processStarted = process.Start();
            if (!processStarted)
            {
                throw new InvalidOperationException($"Failed to launch '{fileName} {arguments}'");
            }

            // Make sure to read both streams asynchronously to avoid deadlocking when tools write to both streams simultaneously!
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            process.WaitForExit();

            var result = new ProcessRunResult(fileName, arguments, output.ToString(), errors.ToString(), process.ExitCode, timedOut: false);

            if (result.ExitCode != 0 && throwOnError)
            {
                throw new InvalidOperationException(
                    $"Process terminated with non-zero exit code: {result.ToLogString()}");
            }

            return result;
        }
    }

    public class ProcessRunResult
    {
        public ProcessRunResult(
            string fileName,
            string? arguments,
            string output,
            string errors,
            int exitCode,
            bool timedOut)
        {
            FileName = fileName;
            Arguments = arguments;
            Output = output;
            Errors = errors;
            ExitCode = exitCode;
            TimedOut = timedOut;
        }

        public string FileName { get; }
        public string? Arguments { get; }
        public string Output { get; }
        public string Errors { get; }
        public int ExitCode { get; }
        public bool TimedOut { get; }

        public string ToLogString()
        {
            if (TimedOut)
            {
                return $"Command '{FileName} {Arguments}' timed out";
            }

            return
                $"Completed command '{FileName} {Arguments ?? "'No Args'"}' exited with code {ExitCode}{Environment.NewLine}" +
                $"Output={Output}{Environment.NewLine}" +
                $"Errors={Errors}";
        }
    }
}
