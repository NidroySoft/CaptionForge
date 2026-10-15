namespace CaptionForge.Modularity;

/// <summary>UI language contract. Independent of the host and media content languages.</summary>
public static class InterfaceLanguage
{
    public static string Code { get; private set; } = "es";
    public static event EventHandler? Changed;
    public static void Select(string code)
    {
        if (Code == code) return;
        Code = code;
        Changed?.Invoke(null, EventArgs.Empty);
    }
}
