using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

// Minimal stand-in for a test runner: loads the compiled test assembly and invokes every [Test]
// method, with its [SetUp] and [TearDown]. A test counts as failed only on an NUnit assertion,
// as inconclusive or ignored when it says so itself (Assert.Inconclusive, Assert.Ignore).
// Any other exception means the test needs the Unity engine and cannot run here.
//
// It follows NUnit, and so Unity's Test Runner, in two things that decide whether a test passes:
// one fixture object serves all the tests of a class, in the order of their names, so state a
// test leaves in a field is there for the next one; and a parameter with [Values] makes one
// test per value (every combination, if several parameters have values).
static class Program
{
    static string[] dirs;

    // NUnit's TestCase takes "params object[]", so its arguments arrive either one by one or as one array.
    static object[] CaseArguments(CustomAttributeData data)
    {
        var values = new List<object>();
        foreach (CustomAttributeTypedArgument argument in data.ConstructorArguments)
        {
            if (argument.Value is System.Collections.ObjectModel.ReadOnlyCollection<CustomAttributeTypedArgument> list)
            {
                values.AddRange(list.Select(item => item.Value));
            }
            else
            {
                values.Add(argument.Value);
            }
        }

        return values.ToArray();
    }

    // Every combination of the values that [Values] gives the parameters of a test. A parameter
    // without values makes the test one this runner cannot call; it is then reported as such.
    static List<object[]> ValueCombinations(MethodInfo method)
    {
        var combinations = new List<object[]> { new object[0] };
        foreach (ParameterInfo parameter in method.GetParameters())
        {
            CustomAttributeData values = parameter.GetCustomAttributesData().FirstOrDefault(a => a.AttributeType.Name == "ValuesAttribute");
            if (values == null)
            {
                return new List<object[]> { null };
            }

            var next = new List<object[]>();
            foreach (object[] sofar in combinations)
            {
                foreach (object value in CaseArguments(values))
                {
                    next.Add(sofar.Append(Convert.ChangeType(value, parameter.ParameterType)).ToArray());
                }
            }

            combinations = next;
        }

        return combinations;
    }

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
        int pass = 0, fail = 0, skipped = 0, inconclusive = 0, ignored = 0;
        var lines = new List<string>();

        foreach (Type type in tests.GetTypes().Where(t => t.IsClass && !t.IsAbstract && !t.IsNested).OrderBy(t => t.Name))
        {
            MethodInfo[] methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance);
            var testMethods = methods.Where(m => m.GetCustomAttributesData().Any(a =>
                a.AttributeType.Name == "TestAttribute" || a.AttributeType.Name == "TestCaseAttribute")).ToArray();
            if (testMethods.Length == 0) continue;
            var setUp = methods.Where(m => m.GetCustomAttributesData().Any(a => a.AttributeType.Name == "SetUpAttribute")).ToArray();
            var tearDown = methods.Where(m => m.GetCustomAttributesData().Any(a => a.AttributeType.Name == "TearDownAttribute")).ToArray();

            var cases = new List<(MethodInfo Method, object[] Arguments)>();
            foreach (MethodInfo method in testMethods.OrderBy(m => m.Name))
            {
                bool any = false;
                foreach (CustomAttributeData data in method.GetCustomAttributesData())
                {
                    if (data.AttributeType.Name == "TestCaseAttribute")
                    {
                        cases.Add((method, CaseArguments(data)));
                        any = true;
                    }
                }

                if (!any && method.GetParameters().Length > 0)
                {
                    foreach (object[] combination in ValueCombinations(method))
                    {
                        cases.Add((method, combination));
                    }
                }
                else if (!any)
                {
                    cases.Add((method, null));
                }
            }

            // One object for the whole class, as NUnit does it.
            object fixture = null;
            Exception fixtureProblem = null;
            try
            {
                fixture = Activator.CreateInstance(type);
            }
            catch (Exception ex)
            {
                fixtureProblem = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
            }

            foreach (var (test, arguments) in cases)
            {
                string id = type.Name + "." + test.Name +
                            (arguments == null ? "" : "(" + string.Join(", ", arguments.Select(a => a == null ? "null" : a.ToString())) + ")");
                string outcome;
                try
                {
                    if (fixtureProblem != null) throw fixtureProblem;
                    foreach (MethodInfo m in setUp) m.Invoke(fixture, null);
                    test.Invoke(fixture, arguments);
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
                    else if (kind == "InconclusiveException")
                    {
                        outcome = "INCONCLUSIVE  " + inner.Message.Replace("\n", " ").Trim();
                        inconclusive++;
                    }
                    else if (kind == "IgnoreException")
                    {
                        outcome = "IGNORED  " + inner.Message.Replace("\n", " ").Trim();
                        ignored++;
                    }
                    else if (kind == "SuccessException")
                    {
                        outcome = "PASS";
                        pass++;
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
        Console.WriteLine($"\nTOTAL {pass + fail + skipped + inconclusive + ignored}: passed {pass}, failed {fail}, " +
                          $"inconclusive {inconclusive}, ignored {ignored}, need the Unity engine {skipped}");
        return fail == 0 ? 0 : 1;
    }
}
