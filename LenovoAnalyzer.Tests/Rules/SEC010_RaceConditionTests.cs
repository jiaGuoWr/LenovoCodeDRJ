// Rules/SEC010_RaceConditionTests.cs
// TDD tests for rule SEC010 – "线程同步/竞争条件"
//
// RED phase: These tests define expected behavior for detecting race condition patterns.
// The analyzer should detect:
// 1. Check-then-use patterns on shared fields without synchronization
// 2. Non-atomic operations (++, --, +=, etc.) on shared fields
// 3. Using non-thread-safe collections in potential multi-threaded scenarios

using FluentAssertions;
using LenovoAnalyzer.Tests.Infrastructure;
using Xunit;

namespace LenovoAnalyzer.Tests.Rules;

public sealed class SEC010_RaceConditionTests
{
    private const string RuleId = "SEC010";

    // =========================================================================
    // Positive cases – must trigger SEC010
    // =========================================================================

    [Fact(DisplayName = "SEC010: check-then-use on field without lock triggers")]
    public async Task CheckThenUse_WithoutLock_Triggers()
    {
        var source = """
            public class BankAccount
            {
                private int _balance;
                
                public void Withdraw(int amount)
                {
                    if (_balance >= amount)
                    {
                        _balance -= amount;
                    }
                }
            }
            """;

        var result = await AnalyzerTestHarness.AnalyzeAsync(source);

        result.CountForRule(RuleId).Should().BeGreaterThanOrEqualTo(1,
            because: "check-then-use pattern on field without lock is a race condition");
    }

    [Fact(DisplayName = "SEC010: increment on shared field without synchronization triggers")]
    public async Task FieldIncrement_WithoutSync_Triggers()
    {
        var source = """
            public class Counter
            {
                private int _counter;
                
                public void Increment()
                {
                    _counter++;
                }
            }
            """;

        var result = await AnalyzerTestHarness.AnalyzeAsync(source);

        result.CountForRule(RuleId).Should().BeGreaterThanOrEqualTo(1,
            because: "++ on field without synchronization is not atomic");
    }

    [Fact(DisplayName = "SEC010: decrement on shared field without synchronization triggers")]
    public async Task FieldDecrement_WithoutSync_Triggers()
    {
        var source = """
            public class Counter
            {
                private int _counter;
                
                public void Decrement()
                {
                    _counter--;
                }
            }
            """;

        var result = await AnalyzerTestHarness.AnalyzeAsync(source);

        result.CountForRule(RuleId).Should().BeGreaterThanOrEqualTo(1,
            because: "-- on field without synchronization is not atomic");
    }

    [Fact(DisplayName = "SEC010: compound assignment on field without lock triggers")]
    public async Task CompoundAssignment_WithoutLock_Triggers()
    {
        var source = """
            public class Counter
            {
                private int _value;
                
                public void Add(int delta)
                {
                    _value += delta;
                }
            }
            """;

        var result = await AnalyzerTestHarness.AnalyzeAsync(source);

        result.CountForRule(RuleId).Should().BeGreaterThanOrEqualTo(1,
            because: "+= on field without synchronization is not atomic");
    }

    [Fact(DisplayName = "SEC010: Dictionary field modification without lock triggers")]
    public async Task DictionaryModification_WithoutLock_Triggers()
    {
        var source = """
            using System.Collections.Generic;
            public class Cache
            {
                private Dictionary<string, object> _cache = new();
                
                public void Add(string key, object value)
                {
                    _cache[key] = value;
                }
            }
            """;

        var result = await AnalyzerTestHarness.AnalyzeAsync(source);

        result.CountForRule(RuleId).Should().BeGreaterThanOrEqualTo(1,
            because: "Dictionary is not thread-safe and needs synchronization");
    }

    [Fact(DisplayName = "SEC010: List field Add without lock triggers")]
    public async Task ListAdd_WithoutLock_Triggers()
    {
        var source = """
            using System.Collections.Generic;
            public class ItemStore
            {
                private List<string> _items = new();
                
                public void AddItem(string item)
                {
                    _items.Add(item);
                }
            }
            """;

        var result = await AnalyzerTestHarness.AnalyzeAsync(source);

        result.CountForRule(RuleId).Should().BeGreaterThanOrEqualTo(1,
            because: "List<T> is not thread-safe and needs synchronization");
    }

    // =========================================================================
    // Negative cases – must NOT trigger SEC010
    // =========================================================================

    [Fact(DisplayName = "SEC010: check-then-use with lock does not trigger")]
    public async Task CheckThenUse_WithLock_NoViolation()
    {
        var source = """
            public class BankAccount
            {
                private readonly object _lock = new();
                private int _balance;
                
                public void Withdraw(int amount)
                {
                    lock (_lock)
                    {
                        if (_balance >= amount)
                        {
                            _balance -= amount;
                        }
                    }
                }
            }
            """;

        var result = await AnalyzerTestHarness.AnalyzeAsync(source);

        result.CountForRule(RuleId).Should().Be(0,
            because: "lock provides proper synchronization");
    }

    [Fact(DisplayName = "SEC010: Interlocked.Increment does not trigger")]
    public async Task InterlockedIncrement_NoViolation()
    {
        var source = """
            using System.Threading;
            public class Counter
            {
                private int _counter;
                
                public void Increment()
                {
                    Interlocked.Increment(ref _counter);
                }
            }
            """;

        var result = await AnalyzerTestHarness.AnalyzeAsync(source);

        result.CountForRule(RuleId).Should().Be(0,
            because: "Interlocked.Increment is atomic");
    }

    [Fact(DisplayName = "SEC010: Interlocked.Decrement does not trigger")]
    public async Task InterlockedDecrement_NoViolation()
    {
        var source = """
            using System.Threading;
            public class Counter
            {
                private int _counter;
                
                public void Decrement()
                {
                    Interlocked.Decrement(ref _counter);
                }
            }
            """;

        var result = await AnalyzerTestHarness.AnalyzeAsync(source);

        result.CountForRule(RuleId).Should().Be(0,
            because: "Interlocked.Decrement is atomic");
    }

    [Fact(DisplayName = "SEC010: Interlocked.Add does not trigger")]
    public async Task InterlockedAdd_NoViolation()
    {
        var source = """
            using System.Threading;
            public class Counter
            {
                private int _value;
                
                public void Add(int delta)
                {
                    Interlocked.Add(ref _value, delta);
                }
            }
            """;

        var result = await AnalyzerTestHarness.AnalyzeAsync(source);

        result.CountForRule(RuleId).Should().Be(0,
            because: "Interlocked.Add is atomic");
    }

    [Fact(DisplayName = "SEC010: ConcurrentDictionary does not trigger")]
    public async Task ConcurrentDictionary_NoViolation()
    {
        var source = """
            using System.Collections.Concurrent;
            public class Cache
            {
                private ConcurrentDictionary<string, object> _cache = new();
                
                public void Add(string key, object value)
                {
                    _cache[key] = value;
                }
            }
            """;

        var result = await AnalyzerTestHarness.AnalyzeAsync(source);

        result.CountForRule(RuleId).Should().Be(0,
            because: "ConcurrentDictionary is thread-safe");
    }

    [Fact(DisplayName = "SEC010: local variable increment does not trigger")]
    public async Task LocalVariableIncrement_NoViolation()
    {
        var source = """
            public class C
            {
                public int Calculate()
                {
                    int counter = 0;
                    counter++;
                    return counter;
                }
            }
            """;

        var result = await AnalyzerTestHarness.AnalyzeAsync(source);

        result.CountForRule(RuleId).Should().Be(0,
            because: "local variables don't need synchronization");
    }

    [Fact(DisplayName = "SEC010: readonly field does not trigger")]
    public async Task ReadonlyField_NoViolation()
    {
        var source = """
            public class Config
            {
                private readonly int _maxRetries = 3;
                
                public int GetMaxRetries()
                {
                    return _maxRetries;
                }
            }
            """;

        var result = await AnalyzerTestHarness.AnalyzeAsync(source);

        result.CountForRule(RuleId).Should().Be(0,
            because: "readonly fields cannot be modified after construction");
    }

    [Fact(DisplayName = "SEC010: const field does not trigger")]
    public async Task ConstField_NoViolation()
    {
        var source = """
            public class Config
            {
                private const int MaxRetries = 3;
                
                public int GetMaxRetries()
                {
                    return MaxRetries;
                }
            }
            """;

        var result = await AnalyzerTestHarness.AnalyzeAsync(source);

        result.CountForRule(RuleId).Should().Be(0,
            because: "const fields are compile-time constants");
    }

    [Fact(DisplayName = "SEC010: volatile field read does not trigger")]
    public async Task VolatileFieldRead_NoViolation()
    {
        var source = """
            public class Flag
            {
                private volatile bool _isRunning;
                
                public bool IsRunning => _isRunning;
            }
            """;

        var result = await AnalyzerTestHarness.AnalyzeAsync(source);

        result.CountForRule(RuleId).Should().Be(0,
            because: "volatile ensures visibility across threads");
    }

    [Fact(DisplayName = "SEC010: empty source does not trigger")]
    public async Task EmptySource_NoViolation()
    {
        var result = await AnalyzerTestHarness.AnalyzeAsync(string.Empty);

        result.CountForRule(RuleId).Should().Be(0);
    }

    // =========================================================================
    // Diagnostic metadata
    // =========================================================================

    [Fact(DisplayName = "SEC010: diagnostic has Warning severity")]
    public async Task Diagnostic_HasWarningSeverity()
    {
        var source = """
            public class Counter
            {
                private int _counter;
                public void Inc() { _counter++; }
            }
            """;

        var result = await AnalyzerTestHarness.AnalyzeAsync(source);

        result.ForRule(RuleId)
              .Should().HaveCountGreaterThanOrEqualTo(1)
              .And.AllSatisfy(d => d.Severity.Should().Be(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning));
    }
}
