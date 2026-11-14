using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework.Api;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;

// Runs an NUnit test assembly outside Unity. Used by script/test.
public static class TestRunner
{
    public static int Main(string[] args)
    {
        var runner = new NUnitTestAssemblyRunner(new DefaultTestAssemblyBuilder());
        runner.Load(Assembly.LoadFrom(args[0]), new Dictionary<string, object>());
        var result = runner.Run(TestListener.NULL, TestFilter.Empty);
        Print(result);
        Console.WriteLine($"{result.PassCount} passed, {result.FailCount} failed, {result.SkipCount} skipped, {result.InconclusiveCount} inconclusive");
        return result.FailCount == 0 && result.PassCount > 0 ? 0 : 1;
    }

    static void Print(ITestResult result)
    {
        if (!result.HasChildren && result.ResultState.Status != TestStatus.Passed)
            Console.WriteLine($"{result.ResultState} {result.FullName}\n  {result.Message}\n{result.StackTrace}");
        foreach (var child in result.Children) Print(child);
    }
}
