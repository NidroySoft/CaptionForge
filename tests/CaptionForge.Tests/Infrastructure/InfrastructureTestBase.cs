using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Diagnostics;
using CaptionForge.Application.Abstractions;
using CaptionForge.Application.Enums;
using CaptionForge.Application.Exceptions;
using CaptionForge.Application.Models.Generation;
using CaptionForge.Application.Models.Media;
using CaptionForge.Application.Models.Settings;
using CaptionForge.Application.Models.Workspace;
using CaptionForge.Application.Models.Writing;
using CaptionForge.Application.Services;
using CaptionForge.Core.Models.Subtitles;
using CaptionForge.Core.Models.Transcription;
using CaptionForge.Core.Models.Media;
using CaptionForge.Core.ValueObjects;
using CaptionForge.Infrastructure.CapCut;
using CaptionForge.Infrastructure.Configuration;
using CaptionForge.Infrastructure.Media;
using CaptionForge.Infrastructure.Settings;
using CaptionForge.Infrastructure.Transcription;
using CaptionForge.Infrastructure.Workspace;
using Whisper.net;

using Xunit;
using Xunit.Abstractions;
using CaptionForge.Tests.Support;
using static CaptionForge.Tests.Infrastructure.InfrastructureTools;

namespace CaptionForge.Tests.Infrastructure;

public abstract class InfrastructureTestBase : IDisposable
{
    protected readonly string fixturePath = FixtureFiles.Directory;
    protected readonly string work = Path.Combine(Path.GetTempPath(), "CaptionForge.Tests", Guid.NewGuid().ToString("N"));
    protected readonly Checks tests;
    protected InfrastructureTestBase(ITestOutputHelper output)
    { tests = new Checks(output); Directory.CreateDirectory(work); }
    private protected Task<Session> SessionAsync(string fixture = "snapshot_audio_only.json", string? rootFixture = null)
        => Session.CreateAsync(work, fixturePath, fixture, rootFixture);
    public void Dispose()
    {
        // Solo la carpeta creada para este caso. El fallo del test conserva su propia excepción.
        try { if (Directory.Exists(work)) Directory.Delete(work, recursive: true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }
}
