using System;
using System.Reflection;
using NUnit.Framework;

// Runs the pure-rule/input-adapter NUnit cases without claiming an Editor or PlayMode run.
public static class RunChessTests
{
    public static int Main(string[] args)
    {
        int passed = 0, failed = 0;
        foreach (var type in Assembly.LoadFrom(args[0]).GetTypes())
        {
            foreach (var method in type.GetMethods())
            {
                var cases = method.GetCustomAttributes(typeof(TestCaseAttribute), false);
                if (cases.Length == 0 && !method.IsDefined(typeof(TestAttribute), false)) continue;
                object instance = Activator.CreateInstance(type);
                if (cases.Length == 0) cases = new object[] { null };
                foreach (TestCaseAttribute test in cases)
                {
                    string label = method.Name + (test == null ? "" : "(" + string.Join(",", test.Arguments) + ")");
                    try { method.Invoke(instance, test?.Arguments); Console.WriteLine("PASS " + label); passed++; }
                    catch (Exception error) { Console.WriteLine("FAIL " + label + "\n" + (error.InnerException ?? error)); failed++; }
                }
            }
        }
        Console.WriteLine($"Tests: {passed} passed, {failed} failed");
        return failed == 0 ? 0 : 1;
    }
}
