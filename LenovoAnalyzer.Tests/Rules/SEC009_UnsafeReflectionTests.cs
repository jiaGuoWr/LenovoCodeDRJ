// Rules/SEC009_UnsafeReflectionTests.cs
// TDD tests for rule SEC009 – "不安全的反射使用"
//
// RED phase: These tests define expected behavior for detecting unsafe reflection patterns.
// The analyzer should detect:
// 1. Type.GetType() with user-controlled input
// 2. Activator.CreateInstance() with user-controlled type
// 3. GetMethod() with BindingFlags.NonPublic (accessing private members)

using FluentAssertions;
using LenovoAnalyzer.Tests.Infrastructure;
using Xunit;

namespace LenovoAnalyzer.Tests.Rules;

public sealed class SEC009_UnsafeReflectionTests
{
    private const string RuleId = "SEC009";

    // =========================================================================
    // Positive cases – must trigger SEC009
    // =========================================================================

    [Fact(DisplayName = "SEC009: Type.GetType with variable input triggers")]
    public async Task TypeGetType_WithVariableInput_Triggers()
    {
        var source = """
            using System;
            public class C
            {
                public void M(string userInput)
                {
                    var type = Type.GetType(userInput);
                }
            }
            """;

        var result = await AnalyzerTestHarness.AnalyzeAsync(source);

        result.CountForRule(RuleId).Should().BeGreaterThanOrEqualTo(1,
            because: "Type.GetType with user-controlled input can load arbitrary types");
    }

    [Fact(DisplayName = "SEC009: Type.GetType with Console.ReadLine triggers")]
    public async Task TypeGetType_WithConsoleReadLine_Triggers()
    {
        var source = """
            using System;
            public class C
            {
                public void M()
                {
                    string typeName = Console.ReadLine();
                    var type = Type.GetType(typeName);
                }
            }
            """;

        var result = await AnalyzerTestHarness.AnalyzeAsync(source);

        result.CountForRule(RuleId).Should().BeGreaterThanOrEqualTo(1,
            because: "Type.GetType with user input from Console.ReadLine is dangerous");
    }

    [Fact(DisplayName = "SEC009: Activator.CreateInstance with Type.GetType triggers")]
    public async Task ActivatorCreateInstance_WithTypeGetType_Triggers()
    {
        var source = """
            using System;
            public class C
            {
                public object M(string typeName)
                {
                    var type = Type.GetType(typeName);
                    return Activator.CreateInstance(type);
                }
            }
            """;

        var result = await AnalyzerTestHarness.AnalyzeAsync(source);

        result.CountForRule(RuleId).Should().BeGreaterThanOrEqualTo(1,
            because: "Activator.CreateInstance with user-controlled type can instantiate arbitrary types");
    }

    [Fact(DisplayName = "SEC009: GetMethod with BindingFlags.NonPublic triggers")]
    public async Task GetMethod_WithNonPublicFlag_Triggers()
    {
        var source = """
            using System;
            using System.Reflection;
            public class C
            {
                public void M(Type type)
                {
                    var method = type.GetMethod("SecretMethod", BindingFlags.NonPublic | BindingFlags.Instance);
                }
            }
            """;

        var result = await AnalyzerTestHarness.AnalyzeAsync(source);

        result.CountForRule(RuleId).Should().BeGreaterThanOrEqualTo(1,
            because: "accessing non-public methods via reflection bypasses encapsulation");
    }

    [Fact(DisplayName = "SEC009: GetField with BindingFlags.NonPublic triggers")]
    public async Task GetField_WithNonPublicFlag_Triggers()
    {
        var source = """
            using System;
            using System.Reflection;
            public class C
            {
                public void M(Type type)
                {
                    var field = type.GetField("_privateField", BindingFlags.NonPublic | BindingFlags.Instance);
                }
            }
            """;

        var result = await AnalyzerTestHarness.AnalyzeAsync(source);

        result.CountForRule(RuleId).Should().BeGreaterThanOrEqualTo(1,
            because: "accessing non-public fields via reflection bypasses encapsulation");
    }

    [Fact(DisplayName = "SEC009: GetProperty with BindingFlags.NonPublic triggers")]
    public async Task GetProperty_WithNonPublicFlag_Triggers()
    {
        var source = """
            using System;
            using System.Reflection;
            public class C
            {
                public void M(Type type)
                {
                    var prop = type.GetProperty("InternalProp", BindingFlags.NonPublic | BindingFlags.Static);
                }
            }
            """;

        var result = await AnalyzerTestHarness.AnalyzeAsync(source);

        result.CountForRule(RuleId).Should().BeGreaterThanOrEqualTo(1,
            because: "accessing non-public properties via reflection bypasses encapsulation");
    }

    [Fact(DisplayName = "SEC009: Method.Invoke on user-controlled method triggers")]
    public async Task MethodInvoke_OnUserControlledMethod_Triggers()
    {
        var source = """
            using System;
            using System.Reflection;
            public class C
            {
                public object M(string methodName, Type type)
                {
                    var method = type.GetMethod(methodName);
                    return method.Invoke(null, null);
                }
            }
            """;

        var result = await AnalyzerTestHarness.AnalyzeAsync(source);

        result.CountForRule(RuleId).Should().BeGreaterThanOrEqualTo(1,
            because: "invoking methods by user-controlled name is dangerous");
    }

    // =========================================================================
    // Negative cases – must NOT trigger SEC009
    // =========================================================================

    [Fact(DisplayName = "SEC009: Type.GetType with string literal does not trigger")]
    public async Task TypeGetType_WithStringLiteral_NoViolation()
    {
        var source = """
            using System;
            public class C
            {
                public void M()
                {
                    var type = Type.GetType("System.String");
                }
            }
            """;

        var result = await AnalyzerTestHarness.AnalyzeAsync(source);

        result.CountForRule(RuleId).Should().Be(0,
            because: "Type.GetType with hardcoded string literal is safe");
    }

    [Fact(DisplayName = "SEC009: typeof operator does not trigger")]
    public async Task TypeofOperator_NoViolation()
    {
        var source = """
            using System;
            public class C
            {
                public void M()
                {
                    var type = typeof(string);
                    var instance = Activator.CreateInstance(type);
                }
            }
            """;

        var result = await AnalyzerTestHarness.AnalyzeAsync(source);

        result.CountForRule(RuleId).Should().Be(0,
            because: "typeof() with compile-time type is safe");
    }

    [Fact(DisplayName = "SEC009: GetMethod with BindingFlags.Public only does not trigger")]
    public async Task GetMethod_WithPublicFlagOnly_NoViolation()
    {
        var source = """
            using System;
            using System.Reflection;
            public class C
            {
                public void M(Type type)
                {
                    var method = type.GetMethod("PublicMethod", BindingFlags.Public | BindingFlags.Instance);
                }
            }
            """;

        var result = await AnalyzerTestHarness.AnalyzeAsync(source);

        result.CountForRule(RuleId).Should().Be(0,
            because: "accessing public methods only is safe");
    }

    [Fact(DisplayName = "SEC009: GetMethod without BindingFlags does not trigger")]
    public async Task GetMethod_WithoutBindingFlags_NoViolation()
    {
        var source = """
            using System;
            public class C
            {
                public void M(Type type)
                {
                    var method = type.GetMethod("ToString");
                }
            }
            """;

        var result = await AnalyzerTestHarness.AnalyzeAsync(source);

        result.CountForRule(RuleId).Should().Be(0,
            because: "GetMethod without BindingFlags defaults to public members only");
    }

    [Fact(DisplayName = "SEC009: Activator.CreateInstance with typeof does not trigger")]
    public async Task ActivatorCreateInstance_WithTypeof_NoViolation()
    {
        var source = """
            using System;
            public class C
            {
                public void M()
                {
                    var instance = Activator.CreateInstance(typeof(List<int>));
                }
            }
            """;

        var result = await AnalyzerTestHarness.AnalyzeAsync(source);

        result.CountForRule(RuleId).Should().Be(0,
            because: "Activator.CreateInstance with compile-time type is safe");
    }

    [Fact(DisplayName = "SEC009: Activator.CreateInstance<T> generic does not trigger")]
    public async Task ActivatorCreateInstance_Generic_NoViolation()
    {
        var source = """
            using System;
            public class C
            {
                public T M<T>() where T : new()
                {
                    return Activator.CreateInstance<T>();
                }
            }
            """;

        var result = await AnalyzerTestHarness.AnalyzeAsync(source);

        result.CountForRule(RuleId).Should().Be(0,
            because: "generic Activator.CreateInstance<T> is compile-time safe");
    }

    [Fact(DisplayName = "SEC009: empty source does not trigger")]
    public async Task EmptySource_NoViolation()
    {
        var result = await AnalyzerTestHarness.AnalyzeAsync(string.Empty);

        result.CountForRule(RuleId).Should().Be(0);
    }

    // =========================================================================
    // Diagnostic metadata
    // =========================================================================

    [Fact(DisplayName = "SEC009: diagnostic has Warning severity")]
    public async Task Diagnostic_HasWarningSeverity()
    {
        var source = """
            using System;
            public class C
            {
                public void M(string input)
                {
                    var type = Type.GetType(input);
                }
            }
            """;

        var result = await AnalyzerTestHarness.AnalyzeAsync(source);

        result.ForRule(RuleId)
              .Should().HaveCountGreaterThanOrEqualTo(1)
              .And.AllSatisfy(d => d.Severity.Should().Be(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning));
    }

    [Fact(DisplayName = "SEC009: diagnostic message contains method name")]
    public async Task Diagnostic_MessageContainsMethodName()
    {
        var source = """
            using System;
            public class C
            {
                public void M(string input)
                {
                    var type = Type.GetType(input);
                }
            }
            """;

        var result = await AnalyzerTestHarness.AnalyzeAsync(source);

        result.ForRule(RuleId)
              .Should().HaveCountGreaterThanOrEqualTo(1)
              .And.AllSatisfy(d => d.GetMessage().Should().Contain("GetType"));
    }
}
