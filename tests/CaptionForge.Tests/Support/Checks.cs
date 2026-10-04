using Xunit;
using Xunit.Abstractions;
using CaptionForge.Tests.Support;
using CaptionForge.Application.Enums;
using CaptionForge.Application.Exceptions;

namespace CaptionForge.Tests.Support;

public sealed class Checks(ITestOutputHelper output)
{
    public void Check(bool condition, string name) { Assert.True(condition, name); output.WriteLine("PASS: " + name); }
    public void Throws<T>(Action action, string name) where T : Exception
    { var e = Record.Exception(action); Check(e is T, name + "; excepción: " + e?.GetType().Name); }
    public async Task ThrowsAsync<T>(Func<Task> action, string name) where T : Exception
    { var e = await Record.ExceptionAsync(action); Check(e is T, name + "; excepción: " + e?.GetType().Name); }
    public async Task<CaptionForgeOperationException> ErrorAsync(Func<Task> action, OperationErrorCode code, string name)
    {
        var e = await Assert.ThrowsAsync<CaptionForgeOperationException>(action);
        Check(e.Code == code, name + " (" + e.Code + ")"); return e;
    }
}
