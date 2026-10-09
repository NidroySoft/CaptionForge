using CaptionForge.Application.Services;
namespace CaptionForge.Tests;

public sealed class SubtitleFileReaderTests
{
    [Fact]
    public void ImportsSrtUnicodeMultilineAndPreservesIntervals()
    {
        var cues = SubtitleFileReader.Parse("\uFEFF1\r\n00:00:01,200 --> 00:00:03,400\r\n<b>Hola</b>, ¿qué tal?\r\nSegunda línea.\r\n\r\n2\r\n00:00:03,500 --> 00:00:04,500\r\nFish &amp; chips.\r\n", ".srt");
        Assert.Equal(2, cues.Count); Assert.Equal(1200000, cues[0].TimelineRange.StartUs); Assert.Equal(2200000, cues[0].TimelineRange.DurationUs);
        Assert.Equal("Hola, ¿qué tal? Segunda línea.", cues[0].Text); Assert.Equal("Fish & chips.", cues[1].Text);
        Assert.Equal(0, cues[0].Words[0].StartTimeMs); Assert.Equal(2200, cues[0].Words[^1].EndTimeMs);
    }
    [Fact]
    public void ImportsWebVttIdsSettingsAndMetadata()
    {
        var cues = SubtitleFileReader.Parse("WEBVTT\n\nNOTE annotation\nignored\n\nSTYLE\n::cue {color: red}\n\nintro\n00:01.000 --> 00:02.500 align:start\n<v Lumina>Hello <c.blue>world</c>.</v>\n\n00:00:03.000 --> 00:00:04.000\nSecond cue.", ".vtt");
        Assert.Equal(2, cues.Count); Assert.Equal("Hello world.", cues[0].Text); Assert.Equal(1000000, cues[0].TimelineRange.StartUs);
    }
    [Theory]
    [InlineData("1\n00:00:02,000 --> 00:00:01,000\nWrong.", ".srt")]
    [InlineData("1\n00:99:02,000 --> 00:99:03,000\nWrong.", ".srt")]
    [InlineData("1\n00:00:00,000 --> 00:00:00,001\nToo many words.", ".srt")]
    [InlineData("WEBVTT", ".vtt")]
    [InlineData("Not subtitles", ".srt")]
    [InlineData("[Script Info]", ".ass")]
    public void RejectsBrokenOrUnrecognizedFiles(string text, string extension)
        => Assert.Throws<InvalidDataException>(() => SubtitleFileReader.Parse(text, extension));
}
