using System;
using System.Configuration;
using AutoChem.Core.AppInterop;
using AutoChem.Core.Generics;
using CommandLine;

namespace AutoChemBroker
{
    internal class Program
    {
        private static AutoChemBrokerEntryPoint program;

        public class Options
        {
            [Option("port", HelpText = "Listening port", Default = 8809)]
            public int Port { get; set; }
        }

        public static void Main(string[] args)
        {
            Parser.Default.ParseArguments<Options>(args).WithParsed(options =>
            {
                StartProgram(options);
            });
            Console.ReadKey();
            program.Dispose();
        }


        private static void StartProgram(Options options)
        {
            var config = ConfigurationManager.AppSettings;
            program = new AutoChemBrokerEntryPoint(config);
        }
    }
}