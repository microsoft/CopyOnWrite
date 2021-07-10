// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using CommandLine;
using CommandLine.Text;

namespace Microsoft.CopyOnWrite
{
    public static class CoWCloneFileProgram
    {
        internal static int Main(string[] args)
        {
            int retCode = (int)ReturnCode.Success;
            try
            {
                using var parser = new Parser(
                    settings =>
                    {
                        settings.AutoHelp = true;
                        settings.CaseSensitive = false;
                        settings.CaseInsensitiveEnumValues = true;
                    });
                ParserResult<CommandLineOptions> parserResult = parser.ParseArguments<CommandLineOptions>(args);
                parserResult
                    .WithParsed(opts => retCode = Run(opts))
                    .WithNotParsed(_ => retCode = (int)DisplayHelp(parserResult));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected top-level exception: {ex}");
                retCode = (int)ReturnCode.GeneralException;
            }

            return retCode;
        }

        private static int Run(CommandLineOptions opts)
        {
            ICopyOnWriteFilesystem cow = CopyOnWriteFilesystemFactory.GetInstance();
            if (!cow.CopyOnWriteLinkSupportedBetweenPaths(opts.Source!, opts.Destination!))
            {
                int retCode = (int)ReturnCode.CopyOnWriteNotSupported;
                Console.Error.WriteLine("Warning: Copy-on-write linking is not allowed between the source and destination.");
                Console.Error.WriteLine($"Returning exit code {retCode}");
                return retCode;
            }

            cow.CloneFile(opts.Source!, opts.Destination!);
            return (int)ReturnCode.Success;
        }

        private static ReturnCode DisplayHelp(ParserResult<CommandLineOptions> parserResult)
        {
            HelpText helpText = HelpText.AutoBuild(parserResult, maxDisplayWidth: ConsoleUtils.GetConsoleWidthForHelpText());
            Console.Error.WriteLine(helpText);
            return ReturnCode.InvalidArguments;
        }

        private enum ReturnCode
        {
            Success = 0,
            InvalidArguments = 1,
            GeneralException = 2,
            CopyOnWriteNotSupported = 3,
        }

        internal class CommandLineOptions
        {
            public const string DestinationParamName = "Dest";

            [Option(nameof(Source), Required = true,
                HelpText = "Path to the file to clone")]
            public string? Source { get; set; }

            [Option(DestinationParamName, Required = true,
                HelpText = "Path to the file to create or overwrite as a copy-on-write link referring to the file specified in --" + nameof(Source))]
            public string? Destination { get; set; }
        }
    }
}
