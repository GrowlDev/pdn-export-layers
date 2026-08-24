using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace ExportLayersTests
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            // Same knob the .csproj files use, so one environment variable covers everything.
            string pdnRoot = Environment.GetEnvironmentVariable("PdnRoot");
            if (string.IsNullOrEmpty(pdnRoot))
            {
                pdnRoot = @"C:\Program Files\paint.net";
            }

            // The PDN assemblies live in the install directory rather than next to this exe,
            // so the loader needs pointing at them by hand.
            AssemblyLoadContext.Default.Resolving += (ctx, name) =>
            {
                string candidate = Path.Combine(pdnRoot, name.Name + ".dll");
                return File.Exists(candidate) ? ctx.LoadFromAssemblyPath(candidate) : null;
            };

            try
            {
                if (args.Length == 2 && args[0] == "makepdn")
                {
                    TestPdnMaker.Make(args[1]);
                    return 0;
                }
                if (args.Length == 2 && args[0] == "verifypdn")
                {
                    return TestPdnMaker.Verify(args[1]) ? 0 : 1;
                }
                return TestRunner.RunAll();
            }
            catch (Exception ex)
            {
                Console.WriteLine("FATAL: " + ex);
                return 2;
            }
        }
    }
}
