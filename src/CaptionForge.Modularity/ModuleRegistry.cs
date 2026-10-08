namespace CaptionForge.Modularity;

/// <summary>UI-independent lifecycle contract. The desktop host expects a FrameworkElement view.</summary>
public interface IApplicationModule
{
    object View { get; }
    bool IsBusy { get; }
    void Deactivate();
    Task ShutdownAsync();
}

public sealed record ModuleDefinition(string Id, string DisplayName, Func<IApplicationModule> Create)
{
    public override string ToString() => DisplayName;
}

/// <summary>Explicit registration; modules are constructed only on first use.</summary>
public sealed class ModuleRegistry
{
    private readonly List<ModuleDefinition> _definitions = [];
    private readonly Dictionary<string, IApplicationModule> _loaded = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<ModuleDefinition> Definitions => _definitions.AsReadOnly();
    public bool IsBusy => _loaded.Values.Any(m => m.IsBusy);

    public void Register(ModuleDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.DisplayName);
        ArgumentNullException.ThrowIfNull(definition.Create);
        if (_definitions.Any(d => string.Equals(d.Id, definition.Id, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"El módulo '{definition.Id}' ya está registrado.");
        _definitions.Add(definition);
    }

    public IApplicationModule Get(string id)
    {
        if (_loaded.TryGetValue(id, out var module)) return module;
        var definition = _definitions.SingleOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Módulo desconocido: {id}");
        module = definition.Create() ?? throw new InvalidOperationException($"El módulo '{id}' no pudo crearse.");
        _loaded.Add(definition.Id, module);
        return module;
    }

    public async Task ShutdownAsync()
    {
        List<Exception> errors = [];
        foreach (var module in _loaded.Values.Reverse())
            try { await module.ShutdownAsync(); } catch (Exception e) { errors.Add(e); }
        if (errors.Count > 0) throw new AggregateException(errors);
    }
}
