using CaptionForge.Core.Models.Subtitles;
using CaptionForge.Core.Models.Transcription;
using CaptionForge.Core.Rules;
using CaptionForge.Core.ValueObjects;
using Xunit;

namespace CaptionForge.Tests.Core;

public sealed class TemporalAndUnicodeTests
{
    [Theory]
    [InlineData(24,1)] [InlineData(25,1)] [InlineData(30,1)] [InlineData(60,1)]
    [InlineData(24000,1001)] [InlineData(30000,1001)] [InlineData(60000,1001)]
    public void CuantizacionConservaLosLimitesSinDeriva(int numerator,int denominator)
    {
        var rate=new FrameRate(numerator,denominator);var random=new Random(8129);
        for(int i=0;i<1000;i++)
        {
            long start=random.NextInt64(0,36_000_000_000),duration=random.NextInt64(1,10_000_000);
            var raw=new TimeRangeUs(start,duration);var quantized=SubtitleTimingCalculator.QuantizeToFrames(raw,rate);
            Assert.InRange(quantized.StartUs,0,start);Assert.True(quantized.EndUs<=raw.EndUs);
            long maxFrameLength=rate.GetFrameStartUs(2)-rate.GetFrameStartUs(1)+2;
            Assert.InRange(start-quantized.StartUs,0,maxFrameLength);
            var clipped=SubtitleTimingCalculator.ConstrainToFragment(quantized,raw.EndUs);
            if(clipped is not null)
            {
                var placed=SubtitleTimingCalculator.PlaceOnTimeline(clipped.Value,new(1_000_000_000,raw.EndUs));
                Assert.Equal(1_000_000_000+clipped.Value.StartUs,placed.StartUs);
                Assert.Equal(clipped.Value.DurationUs,placed.DurationUs);
            }
        }
    }
    [Theory]
    [InlineData(0,0)] [InlineData(499,0)] [InlineData(500,1)] [InlineData(999,1)]
    [InlineData(1499,1)] [InlineData(1500,2)] [InlineData(1999,2)]
    public void RedondeoDeMicrosegundosEnLimitesDeMedioMilisegundo(long us,long ms)
        =>Assert.Equal(ms,TimeRangeUs.RoundToMilliseconds(us));

    [Theory]
    [InlineData("acción")] [InlineData("中文")] [InlineData("العربية")] [InlineData("👩🏽‍💻")]
    [InlineData("e\u0301")] [InlineData("can't")] [InlineData("¿Hola?")] [InlineData("<tag>&\"quoted\"")]
    public void UnicodeNoSeNormalizaNiSePierde(string text)
    {
        var cue=new SubtitleCue("s",new(0,1_000_000),new[]{new TimedWord(text,0,1000)});
        Assert.Equal(text,cue.Text);
        var words=WordTimingBuilder.Build(new[]{new TranscriptionToken(" "+text,new(0,1_000_000))},new(0,1_000_000));
        Assert.Equal(text,Assert.Single(words).Text);
    }
    [Theory]
    [InlineData("")] [InlineData("\t")] [InlineData("\n")] [InlineData("one two")] [InlineData("one\u00a0two")]
    public void PalabraVaciaOConSeparadoresSeRechaza(string text)
        =>Assert.ThrowsAny<ArgumentException>(()=>new TimedWord(text,0,10));

    [Theory]
    [InlineData("leading-space")] [InlineData("ending-space")] [InlineData("no-space")]
    [InlineData("overlap")] [InlineData("wrong-space-time")] [InlineData("beyond-duration")]
    public void CaptionRechazaPatronesDePalabrasInvalidos(string scenario)
    {
        TimedWord[] words=scenario switch
        {
            "leading-space"=>new[]{new TimedWord(" ",0,0),new TimedWord("Hello",0,1000)},
            "ending-space"=>new[]{new TimedWord("Hello",0,1000),new TimedWord(" ",1000,1000)},
            "no-space"=>new[]{new TimedWord("Hello",0,500),new TimedWord("world",500,1000)},
            "overlap"=>new[]{new TimedWord("Hello",0,600),new TimedWord(" ",600,600),new TimedWord("world",500,1000)},
            "wrong-space-time"=>new[]{new TimedWord("Hello",0,500),new TimedWord(" ",600,600),new TimedWord("world",700,1000)},
            _=>new[]{new TimedWord("Hello",0,1001)}
        };
        Assert.Throws<ArgumentException>(()=>new SubtitleCue("s",new(0,1_000_000),words));
    }
    [Fact]
    public void CaptionYTranscripcionCopianSusColecciones()
    {
        var words=new[]{new TimedWord("Hello",0,1000)};var cue=new SubtitleCue("s",new(0,1_000_000),words);
        words[0]=new("Changed",0,1000);Assert.Equal("Hello",cue.Text);
        Assert.Throws<NotSupportedException>(()=>((IList<TimedWord>)cue.Words).Clear());
        var tokens=new[]{new TranscriptionToken(" Hello",new(0,1_000_000))};var segment=new TranscriptionSegment(" Hello",new(0,1_000_000),tokens);
        tokens[0]=new(" Changed",new(0,1_000_000));Assert.Equal(" Hello",segment.Tokens[0].Text);
    }
    [Fact]
    public void DefaultsYParametrosNulosNoInventanTiempos()
    {
        Assert.True(default(TimeRangeUs).IsEmpty);
        Assert.Throws<InvalidOperationException>(()=>_ =default(FrameRate).FramesPerSecond);
        Assert.Throws<ArgumentNullException>(()=>WordTimingBuilder.Build(null!,new(0,1000)));
        Assert.Throws<ArgumentNullException>(()=>WordTimingBuilder.Build(new TranscriptionToken[]{null!},new(0,1000)));
        Assert.Throws<ArgumentException>(()=>WordTimingBuilder.Build(Array.Empty<TranscriptionToken>(),new(0,499)));
        Assert.Throws<ArgumentOutOfRangeException>(()=>new FrameRate(1,0));
        Assert.Throws<ArgumentOutOfRangeException>(()=>new FrameRate(30).ToFrameIndex(-1));
        Assert.Throws<ArgumentOutOfRangeException>(()=>new FrameRate(30).GetFrameStartUs(-1));
        Assert.Throws<ArgumentOutOfRangeException>(()=>SubtitleTimingCalculator.ConstrainToFragment(new(0,1),0));
    }
}
