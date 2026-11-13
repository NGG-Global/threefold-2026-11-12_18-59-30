using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

// Minimal stand-in for a test runner: loads the compiled test assembly and invokes every [Test]
// method, with its [SetUp] and [TearDown]. A test counts as failed only on an NUnit assertion.
// Any other exception means the test needs the Unity engine and cannot run here.
static class Program
{
    static string[] dirs;

    static int Main(string[] args)
    {
        dirs = args.Skip(1).ToArray();
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            string name = new AssemblyName(e.Name).Name + ".dll";
            foreach (string d in dirs)
            {
                string p = Path.Combine(d, name);
                if (File.Exists(p)) return Assembly.LoadFrom(p);
            }
            return null;
        };

        Assembly tests = Assembly.LoadFrom(args[0]);
        int pass = 0, fail = 0, skipped = 0;
        var lines = new List<string>();

        foreach (Type type in tests.GetTypes().Where(t => t.IsClass && !t.IsAbstract && !t.IsNested).OrderBy(t => t.Name))
        {
            MethodInfo[] methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance);
            var testMethods = methods.Where(m => m.GetCustomAttributesData().Any(a => a.AttributeType.Name == "TestAttribute")).ToArray();
            if (testMethods.Length == 0) continue;
            var setUp = methods.Where(m => m.GetCustomAttributesData().Any(a => a.AttributeType.Name == "SetUpAttribute")).ToArray();
            var tearDown = methods.Where(m => m.GetCustomAttributesData().Any(a => a.AttributeType.Name == "TearDownAttribute")).ToArray();

            foreach (MethodInfo test in testMethods.OrderBy(m => m.Name))
            {
                string id = type.Name + "." + test.Name;
                object fixture = null;
                string outcome;
                try
                {
                    fixture = Activator.CreateInstance(type);
                    foreach (MethodInfo m in setUp) m.Invoke(fixture, null);
                    test.Invoke(fixture, null);
                    outcome = "PASS";
                    pass++;
                }
                catch (Exception ex)
                {
                    Exception inner = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                    string kind = inner.GetType().Name;
                    if (kind == "AssertionException")
                    {
                        outcome = "FAIL  " + inner.Message.Replace("\n", " ").Trim();
                        fail++;
                    }
                    else
                    {
                        outcome = "NEEDS-UNITY  (" + kind + ")";
                        skipped++;
                    }
                }

                try { if (fixture != null) foreach (MethodInfo m in tearDown) m.Invoke(fixture, null); } catch { }
                lines.Add(outcome.PadRight(14).Substring(0, Math.Min(outcome.Length, 140)) + "  " + id);
            }
        }

        foreach (string l in lines) Console.WriteLine(l);
        Console.WriteLine($"\nTOTAL {pass + fail + skipped}: passed {pass}, failed {fail}, need the Unity engine {skipped}");
        return fail == 0 ? 0 : 1;
    }
}
