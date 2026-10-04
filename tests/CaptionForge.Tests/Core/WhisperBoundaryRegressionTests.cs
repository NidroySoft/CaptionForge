using System.Text.Json;
using CaptionForge.Application.Services;
using CaptionForge.Core.Models.Media;
using CaptionForge.Core.Models.Transcription;
using CaptionForge.Core.Rules;
using CaptionForge.Core.ValueObjects;
using CaptionForge.Infrastructure.Transcription;
using CaptionForge.Tests.Support;
using Whisper.net;
using Xunit;

namespace CaptionForge.Tests.Core;

public sealed class WhisperBoundaryRegressionTests
{
    [Fact]
    public void DiagnosticoNativoDelUsuarioSeNormalizaYRecortaSinColapsarPalabras()
    {
        using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(FixtureFiles.Directory,"3219BA9AFC48C4252E875CA8-native.json")));
        var phrases=new List<TranscriptionSegment>();
        foreach(var segment in doc.RootElement.GetProperty("segments").EnumerateArray())
        {
            var tokens=segment.GetProperty("tokens").EnumerateArray().Select(t=>new WhisperToken
            {
                Id=t.GetProperty("id").GetInt32(),Text=t.GetProperty("text").GetString()!,
                Start=t.GetProperty("start10Ms").GetInt64(),End=t.GetProperty("end10Ms").GetInt64(),DtwTimestamp=t.GetProperty("dtwTimestamp").GetInt64()
            }).ToArray();
            var native=new SegmentData(segment.GetProperty("text").GetString()!,TimeSpan.FromTicks(segment.GetProperty("startTicks").GetInt64()),TimeSpan.FromTicks(segment.GetProperty("endTicks").GetInt64()),0,0,0,0,"en",tokens);
            phrases.Add(WhisperTokenNormalizer.Normalize(native,true));
        }
        var source=new MediaSegment("s","t","m","audio.wav",new(0,11_000_000),new(20_000_000,11_000_000));
        var transcription=new TranscriptionResult("s","medium.en","en",11_000_000,phrases);
        var captions=new SubtitleCuePlanner().Build(source,transcription,new FrameRate(30));
        Assert.Equal(4,captions.Count);Assert.Equal("It",captions[^1].Text);
        Assert.Equal(30_733_333,captions[^1].TimelineRange.StartUs);Assert.Equal(31_000_000,captions[^1].TimelineRange.EndUs);
        Assert.Equal(267,Assert.Single(captions[^1].Words).EndTimeMs);
        Assert.All(captions,c=>Assert.InRange(c.TimelineRange.EndUs,20_000_000,31_000_000));
        Assert.Equal(phrases[0].Text.Trim(),captions[0].Text);Assert.Equal(phrases[1].Text.Trim(),captions[1].Text);Assert.Equal(phrases[2].Text.Trim(),captions[2].Text);
    }
    [Fact]
    public void ExcluyePalabraFueraDelFinalDespuesDeUnirSubpalabras()
    {
        var words=WordTimingBuilder.Build(new[]
        {
            new TranscriptionToken(" It",new(10_800_000,860_000)),
            new TranscriptionToken(" uses",new(11_730_000,160_000)),
            new TranscriptionToken("--",new(11_890_000,110_000))
        },new(10_733_333,266_667));
        Assert.Equal("It",Assert.Single(words).Text);Assert.Equal(267,words[0].EndTimeMs);
    }
    [Fact]
    public void ContinuacionDentroDelCaptionConservaSuPalabraCompleta()
    {
        var words=WordTimingBuilder.Build(new[]
        {
            new TranscriptionToken(" cap",new(900_000,50_000)),
            new TranscriptionToken("tion",new(950_000,150_000)),
            new TranscriptionToken(".",new(1_100_000,50_000))
        },new(1_000_000,200_000));
        Assert.Equal("caption.",Assert.Single(words).Text);Assert.Equal(200,words[0].EndTimeMs);
    }
    [Fact]
    public void TodasLasPalabrasFueraDevuelvenVacioYLasSolapadasDentroSiguenFallando()
    {
        Assert.Empty(WordTimingBuilder.Build(new[]{new TranscriptionToken(" late",new(1_000_000,100_000))},new(0,1_000_000)));
        Assert.Throws<InvalidOperationException>(()=>WordTimingBuilder.Build(new[]
        {
            new TranscriptionToken(" first",new(0,700_000)),
            new TranscriptionToken(" second",new(500_000,400_000)),
            new TranscriptionToken(" outside",new(1_100_000,100_000))
        },new(0,1_000_000)));
    }
}
