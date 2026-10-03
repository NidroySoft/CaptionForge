using System.Text;
using CaptionForge.Infrastructure.Media;
using CaptionForge.Infrastructure.Transcription;
using Whisper.net;
using Xunit;

namespace CaptionForge.Tests.Infrastructure;

public sealed class BinaryFormatTests : IDisposable
{
    private readonly string work=Path.Combine(Path.GetTempPath(),"CaptionForge.Tests",Guid.NewGuid().ToString("N"));
    public BinaryFormatTests()=>Directory.CreateDirectory(work);
    public void Dispose(){Directory.Delete(work,true);GC.SuppressFinalize(this);}
    [Theory]
    [InlineData("riff")] [InlineData("wave")] [InlineData("fmt-short")] [InlineData("float")]
    [InlineData("stereo")] [InlineData("rate")] [InlineData("bits")] [InlineData("align")]
    [InlineData("empty")] [InlineData("odd-data")] [InlineData("multiple-data")] [InlineData("truncated")]
    [InlineData("no-fmt")] [InlineData("no-data")] [InlineData("rf64")]
    public void WavIncompatibleOCorruptoSeRechaza(string scenario)
    {
        var path=Path.Combine(work,"invalid.wav");using(var writer=new BinaryWriter(File.Create(path),Encoding.ASCII))
        {
            writer.Write(Encoding.ASCII.GetBytes(scenario=="riff"?"NOPE":scenario=="rf64"?"RF64":"RIFF"));writer.Write(0);
            writer.Write(Encoding.ASCII.GetBytes(scenario=="wave"?"NOPE":"WAVE"));
            if(scenario!="no-fmt")
            {
                writer.Write(Encoding.ASCII.GetBytes("fmt "));writer.Write(scenario=="fmt-short"?8:16);
                writer.Write((short)(scenario=="float"?3:1));writer.Write((short)(scenario=="stereo"?2:1));writer.Write(scenario=="rate"?44100:16000);
                if(scenario!="fmt-short"){writer.Write(32000);writer.Write((short)(scenario=="align"?4:2));writer.Write((short)(scenario=="bits"?8:16));}
            }
            if(scenario!="no-data")
            {
                writer.Write(Encoding.ASCII.GetBytes("data"));int count=scenario=="empty"?0:scenario=="odd-data"?3:4;
                writer.Write(scenario=="truncated"?100:count);writer.Write(new byte[count]);
                if(scenario=="multiple-data"){writer.Write(Encoding.ASCII.GetBytes("data"));writer.Write(4);writer.Write(new byte[4]);}
            }
        }
        Assert.Throws<InvalidDataException>(()=>PcmWaveInfo.Read(path));
    }
    [Fact]
    public void WavAceptaChunkDesconocidoConPaddingYCuentaMuestras()
    {
        var path=Path.Combine(work,"valid.wav");InfrastructureTools.WriteWave(path,16000);
        var bytes=File.ReadAllBytes(path);using(var writer=new BinaryWriter(File.Create(path),Encoding.ASCII))
        {writer.Write(bytes[..12]);writer.Write(Encoding.ASCII.GetBytes("JUNK"));writer.Write(3);writer.Write(new byte[]{1,2,3,0});writer.Write(bytes[12..]);}
        var info=PcmWaveInfo.Read(path);Assert.Equal(16000,info.SampleFrames);Assert.Equal(1_000_000,info.DurationUs);
    }
    [Theory]
    [InlineData(51864,4,384,4,80,WhisperAlignmentHeadsPreset.TinyEn)]
    [InlineData(51865,4,384,4,80,WhisperAlignmentHeadsPreset.Tiny)]
    [InlineData(51864,6,512,6,80,WhisperAlignmentHeadsPreset.BaseEn)]
    [InlineData(51865,6,512,6,80,WhisperAlignmentHeadsPreset.Base)]
    [InlineData(51864,12,768,12,80,WhisperAlignmentHeadsPreset.SmallEn)]
    [InlineData(51865,12,768,12,80,WhisperAlignmentHeadsPreset.Small)]
    [InlineData(51864,24,1024,24,80,WhisperAlignmentHeadsPreset.MediumEn)]
    [InlineData(51865,24,1024,24,80,WhisperAlignmentHeadsPreset.Medium)]
    [InlineData(51866,32,1280,32,128,WhisperAlignmentHeadsPreset.LargeV3)]
    [InlineData(51866,32,1280,4,128,WhisperAlignmentHeadsPreset.LargeV3Turbo)]
    public void CabeceraGgmlSeleccionaPresetDtwCorrecto(int vocab,int layers,int state,int textLayers,int mels,WhisperAlignmentHeadsPreset expected)
    {
        var path=Path.Combine(work,"model.bin");WriteHeader(path,vocab,layers,state,textLayers,mels);
        var info=WhisperModelInfo.Read(path);Assert.Equal(expected,info.AlignmentPreset);Assert.Equal(vocab==51864,info.IsEnglishOnly);
    }
    internal static void WriteHeader(string path,int vocab=51864,int layers=4,int state=384,int textLayers=4,int mels=80)
    {
        using var w=new BinaryWriter(File.Create(path));w.Write(0x67676d6c);w.Write(vocab);w.Write(1500);w.Write(state);w.Write(6);w.Write(layers);
        w.Write(448);w.Write(state);w.Write(6);w.Write(textLayers);w.Write(mels);w.Write(0);
    }
    [Theory]
    [InlineData("magic")] [InlineData("short")] [InlineData("vocab")] [InlineData("mels")]
    [InlineData("architecture")] [InlineData("large-ambiguous")] [InlineData("preset-mismatch")]
    public void ModeloInvalidoOAmbiguoNoSeCarga(string scenario)
    {
        var path=Path.Combine(work,"model.bin");WriteHeader(path,
            scenario=="vocab"?123:scenario=="large-ambiguous"?51865:51864,
            scenario=="architecture"?13:scenario=="large-ambiguous"?32:4,
            scenario=="large-ambiguous"?1280:384,scenario=="large-ambiguous"?32:4,scenario=="mels"?90:80);
        if(scenario=="magic")File.WriteAllBytes(path,new byte[48]);
        if(scenario=="short")File.WriteAllBytes(path,new byte[47]);
        Assert.Throws<InvalidDataException>(()=>WhisperModelInfo.Read(path,scenario=="preset-mismatch"?WhisperAlignmentHeadsPreset.MediumEn:null));
    }
    [Theory]
    [InlineData("large-v1.bin",WhisperAlignmentHeadsPreset.LargeV1)]
    [InlineData("ggml-large-v2-q5.bin",WhisperAlignmentHeadsPreset.LargeV2)]
    public void LargeV1V2ExigeIdentificarSuVersion(string name,WhisperAlignmentHeadsPreset expected)
    {
        var path=Path.Combine(work,name);WriteHeader(path,51865,32,1280,32,80);
        Assert.Equal(expected,WhisperModelInfo.Read(path).AlignmentPreset);
        var ambiguous=Path.Combine(work,"renamed.bin");File.Copy(path,ambiguous);
        Assert.Equal(expected,WhisperModelInfo.Read(ambiguous,expected).AlignmentPreset);
    }
    [Theory]
    [InlineData(true,50256)] [InlineData(false,50257)]
    public void TokenControlIgnoraOffsetsNegativosPeroTokenLexicoNo(bool english,int controlId)
    {
        var segment=new SegmentData("voice",TimeSpan.Zero,TimeSpan.FromSeconds(1),0,0,0,0,"en",new[]{new WhisperToken{Id=controlId,Text="<|control|>",Start=-1,End=-1}});
        Assert.True(Assert.Single(WhisperTokenNormalizer.Normalize(segment,english).Tokens).IsControl);
        segment=new SegmentData("voice",TimeSpan.Zero,TimeSpan.FromSeconds(1),0,0,0,0,"en",new[]{new WhisperToken{Id=controlId-1,Text=" voice",Start=-1,End=-1}});
        Assert.Throws<InvalidDataException>(()=>WhisperTokenNormalizer.Normalize(segment,english));
    }
}
