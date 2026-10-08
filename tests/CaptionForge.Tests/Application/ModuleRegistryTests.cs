using CaptionForge.Modularity;
namespace CaptionForge.Tests.Application;

public sealed class ModuleRegistryTests
{
    [Fact]
    public async Task Modules_are_lazy_reused_and_shutdown_only_if_loaded()
    {
        var registry = new ModuleRegistry(); var first = new FakeModule(); int creates = 0;
        registry.Register(new("first", "Primero", () => { creates++; return first; }));
        registry.Register(new("unused", "Sin cargar", () => throw new InvalidOperationException()));
        Assert.Equal(0, creates);
        Assert.Same(first, registry.Get("FIRST")); Assert.Same(first, registry.Get("first"));
        Assert.Equal(1, creates);
        first.Busy = true; Assert.True(registry.IsBusy);
        first.Busy = false; Assert.False(registry.IsBusy);
        await registry.ShutdownAsync(); Assert.True(first.Stopped);
    }

    [Fact]
    public void Duplicate_ids_are_rejected_case_insensitively()
    {
        var registry = new ModuleRegistry(); registry.Register(new("speech", "Voz", () => new FakeModule()));
        Assert.Throws<ArgumentException>(() => registry.Register(new("SPEECH", "Otra", () => new FakeModule())));
        Assert.Throws<KeyNotFoundException>(() => registry.Get("unknown"));
    }

    [Fact]
    public async Task A_shutdown_failure_does_not_leave_other_modules_running()
    {
        var registry = new ModuleRegistry(); var first = new FakeModule(); var failing = new FakeModule { FailShutdown = true };
        registry.Register(new("one", "Uno", () => first)); registry.Register(new("two", "Dos", () => failing));
        registry.Get("one"); registry.Get("two");
        await Assert.ThrowsAsync<AggregateException>(() => registry.ShutdownAsync()); Assert.True(first.Stopped);
    }

    private sealed class FakeModule : IApplicationModule
    {
        public object View { get; } = new();
        public bool Busy, Stopped, FailShutdown;
        public bool IsBusy => Busy;
        public void Deactivate() { }
        public Task ShutdownAsync() { Stopped = true; return FailShutdown ? Task.FromException(new IOException("failed")) : Task.CompletedTask; }
    }
}
