using System.Globalization;
using CaptionForge.Application.Enums;
using CaptionForge.Application.Exceptions;
using CaptionForge.Core.Models.Media;
using CaptionForge.Infrastructure.Media;
using CaptionForge.Tests.Support;
using Xunit;
using Xunit.Abstractions;
using static CaptionForge.Tests.Infrastructure.InfrastructureTools;

namespace CaptionForge.Tests.Infrastructure;

[Collection("External engines")]
public sealed class FfmpegEdgeTests(ITestOutputHelper output) : InfrastructureTestBase(output)
{
    [FfmpegTheory]
    [InlineData("outside-media")] [InlineData("start-at-end")] [InlineData("duration-mismatch")]
    [InlineData("missing-source")] [InlineData("corrupt-source")] [InlineData("speed")]
    [InlineData("variable")] [InlineData("reverse")] [InlineData("mute")]
    public async Task AudioInvalidoNoPublicaWav(string flaw)
    {
        var s=await SessionAsync();var run=await s.Store.CreateRunAsync(s.Request());var source=Path.Combine(work,"audio with spaces.wav");WriteWave(source,16000);
        if(flaw=="missing-source")File.Delete(source);if(flaw=="corrupt-source")File.WriteAllText(source,"invalid-media");
        var segment=new MediaSegment("slice","t","m",source,new(flaw=="start-at-end"?1_000_000:0,flaw=="outside-media"?1_200_000:1_000_000),
            new(0,flaw=="duration-mismatch"?800_000:flaw=="outside-media"?1_200_000:1_000_000),flaw=="speed"?2:1,flaw=="variable",flaw=="reverse",flaw=="mute");
        var error=await Record.ExceptionAsync(()=>new FfmpegAudioPreparationService().PrepareAsync(new(run,segment,source)));
        Assert.NotNull(error);
        if(flaw=="missing-source")Assert.IsType<FileNotFoundException>(error);
        else if(flaw is "outside-media" or "start-at-end" or "corrupt-source")Assert.IsType<InvalidDataException>(error);
        else Assert.Equal(OperationErrorCode.UnsupportedMedia,Assert.IsType<CaptionForgeOperationException>(error).Code);
        Assert.Empty(Directory.GetFiles(Path.Combine(run.RunDirectory,"audio"),"*.wav"));Assert.Empty(Directory.GetFiles(Path.Combine(run.RunDirectory,"audio"),"*.tmp"));
    }
    [FfmpegTheory]
    [InlineData("es-ES")] [InlineData("en-US")] [InlineData("fr-FR")]
    public async Task RecortesDecimalesNoDependenDeCulturaYNoEditanFuente(string culture)
    {
        var previous=CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo(culture);var s=await SessionAsync();var run=await s.Store.CreateRunAsync(s.Request());
            var source=Path.Combine(work,"声 & audio $ original.wav");WriteWave(source,32000,440);var before=File.ReadAllBytes(source);
            var seg=new MediaSegment("s","t","m",source,new(250_000,500_031),new(10_000_000,500_031));
            var audio=await new FfmpegAudioPreparationService().PrepareAsync(new(run,seg,source));Assert.InRange(Math.Abs(audio.DurationUs-audio.MeasuredDurationUs),0,63);
            Assert.Equal(before,File.ReadAllBytes(source));Assert.Equal(8000,PcmWaveInfo.Read(audio.Path).SampleFrames);
        }
        finally{CultureInfo.CurrentCulture=previous;}
    }
}
