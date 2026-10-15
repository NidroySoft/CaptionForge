using System.Text.Json;
using CaptionForge.Application.Services;
using CaptionForge.Core.Models.Media;
using CaptionForge.Core.Models.Transcription;
using CaptionForge.Core.Rules;
using CaptionForge.Core.ValueObjects;
using CaptionForge.Infrastructure.Transcription;
using CaptionForge.Tests.Support;
using Whisper.net;

namespace CaptionForge.Tests.Infrastructure;

public sealed class WhisperAlignmentEdgeTests
{
    static WhisperToken T(string text,long start,long end,long dtw=-1,int id=1)
        => new(){Id=id,Text=text,Start=start,End=end,DtwTimestamp=dtw};
    static SegmentData P(long start,long end,WhisperToken[] tokens,string language="en",string? text=null)
        => new(text??string.Concat(tokens.Where(t=>t.Id<50256).Select(t=>t.Text)),TimeSpan.FromMilliseconds(start*10),TimeSpan.FromMilliseconds(end*10),0,0,0,0,language,tokens);
    static void Valid(TranscriptionSegment s,string expected)
    {
        var words=WordTimingBuilder.Build(s.Tokens,s.Range);
        Assert.Equal(expected,string.Concat(words.Select(w=>w.Text)));
        long previous=0;
        foreach(var w in words.Where(w=>!w.IsSpace)) {
            Assert.True(w.EndTimeMs>w.StartTimeMs);
            Assert.True(w.StartTimeMs>=previous);
            Assert.InRange(w.EndTimeMs,1,TimeRangeUs.RoundToMilliseconds(s.Range.DurationUs));
            previous=w.EndTimeMs;
        }
    }

    [Theory]
    [InlineData(".")][InlineData(",")][InlineData("!")][InlineData("?")][InlineData(";")][InlineData(":")]
    public void TrailingPunctuationDoesNotHideCollapsedBoundaryWord(string punctuation)
    {
        var first=P(0,100,[T(" pulls",0,80),T(" air",100,100,102),T(punctuation,100,105)]);
        var next=P(100,200,[T(" through",110,180)]);
        var repaired=Assert.IsType<TranscriptionSegment>(WhisperTokenNormalizer.RecoverWithFollowing(first,next,true));
        Valid(repaired,"pulls air"+punctuation+" through");
    }

    [Theory]
    [InlineData("plane")][InlineData(".")][InlineData("s")][InlineData("’s")]
    public void RetryMustNotAcceptOnlyAPrefixOfALongerTokenSequence(string continuation)
    {
        var original=P(0,100,[T(" air",10,30)]);
        var retry=P(0,100,[T(" air",10,30),T(continuation,30,50)]);
        Assert.Null(WhisperTokenNormalizer.RecoverFromRetry(original,[retry],true));
    }

    [Theory]
    [InlineData(0)][InlineData(100)][InlineData(12345)]
    public void RetryAcrossSegmentsPreservesAbsoluteAudioOrigin(long offset)
    {
        var original=P(offset,offset+100,[T(" one",offset+5,offset+30),T(" two",offset+50,offset+80)]);
        var parts=new[]{P(offset,offset+40,[T(" one",offset+5,offset+30)]),P(offset+40,offset+100,[T(" two",offset+50,offset+80),T(" extra",offset+85,offset+95)])};
        var repaired=Assert.IsType<TranscriptionSegment>(WhisperTokenNormalizer.RecoverFromRetry(original,parts,true));
        Valid(repaired,"one two");Assert.Equal(offset*10_000,repaired.Range.StartUs);
    }

    [Theory]
    [InlineData("missing")][InlineData("reordered")][InlineData("changed")][InlineData("empty")][InlineData("outside")][InlineData("overlap")]
    public void RetryRejectsUnsafeTextOrTimings(string flaw)
    {
        var original=P(0,100,[T(" one",0,20),T(" two",30,50),T(" three",60,90)]);
        WhisperToken[] tokens=flaw switch {
            "missing"=>[T(" one",0,20),T(" three",60,90)],
            "reordered"=>[T(" two",0,20),T(" one",30,50),T(" three",60,90)],
            "changed"=>[T(" one",0,20),T(" too",30,50),T(" three",60,90)],
            "empty"=>[],
            "outside"=>[T(" one",0,20),T(" two",30,50),T(" three",100,120)],
            _=>[T(" one",0,60),T(" two",30,70),T(" three",80,90)]};
        Assert.Null(WhisperTokenNormalizer.RecoverFromRetry(original,[P(0,100,tokens)],true));
    }

    [Theory]
    [InlineData("language")][InlineData("gap")][InlineData("long")][InlineData("unordered")][InlineData("dtw-missing")][InlineData("dtw-outside")][InlineData("same-end")][InlineData("internal")]
    public void FollowingContextRejectsUnsafeCandidates(string flaw)
    {
        var first=P(0,100,[T(" pulls",0,80),T(" air",flaw=="internal"?90:100,flaw=="internal"?90:100,flaw=="dtw-missing"?-1:flaw=="dtw-outside"?300:102)]);
        var next=flaw switch {
            "language"=>P(100,200,[T(" through",110,180)],"es"),
            "gap"=>P(126,200,[T(" through",130,180)]),
            "long"=>P(100,1201,[T(" through",110,1180)]),
            "unordered"=>P(0,90,[T(" through",20,80)]),
            "same-end"=>P(90,100,[T(" through",90,100)]),
            _=>P(100,200,[T(" through",110,180)])};
        Assert.Null(WhisperTokenNormalizer.RecoverWithFollowing(first,next,true));
    }

    [Theory]
    [InlineData(-1,20)][InlineData(30,20)]
    public void InvalidNativeOffsetsAreRejected(long start,long end)
        =>Assert.Throws<InvalidDataException>(()=>WhisperTokenNormalizer.Normalize(P(0,100,[T(" word",start,end)]),true));

    [Fact]
    public void ConsecutiveCollapsedWordsWithNoSeparationAreRejected()
        =>Assert.Throws<InvalidDataException>(()=>WhisperTokenNormalizer.Normalize(P(0,100,[T(" one",0,20),T(" two",40,40,45),T(" three",40,40,50),T(" four",80,90)]),true));

    [Fact]
    public void SeparateCollapsedWordsKeepTheirOwnDtwEvidence()
        =>Valid(WhisperTokenNormalizer.Normalize(P(0,100,[T(" one",0,20),T(" two",30,30,35),T(" three",60,60,65),T(" four",80,90)]),true),"one two three four");

    [Fact]
    public void UnicodeSubwordsAndPunctuationPreserveText()
        =>Valid(WhisperTokenNormalizer.Normalize(P(0,100,[T(" don",0,20),T("'",20,20),T("t",20,30),T(" café",40,50),T(".",50,60)]),false),"don't café.");

    [Theory]
    [InlineData(true,50256)][InlineData(false,50257)]
    public void ControlTokensNeverBecomeSpokenWords(bool english,int control)
        =>Valid(WhisperTokenNormalizer.Normalize(P(0,100,[T(" hello",5,40),T("[_EOT_]",-1,-1,-1,control)]),english),"hello");

    [Fact]
    public void LastWordAtAudioEndCannotBeSilentlyDropped()
        =>Assert.Throws<InvalidDataException>(()=>WhisperTokenNormalizer.Normalize(P(0,100,[T(" last",0,80),T(" word",100,100,105)]),true));

    [Theory]
    [InlineData(24,1)][InlineData(25,1)][InlineData(30,1)][InlineData(60,1)][InlineData(30000,1001)]
    public void RealFailureSurvivesCaptionFrameQuantization(int numerator,int denominator)
    {
        using var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(FixtureFiles.Directory,"whisper_english_phrase_edge_native.json")));
        var pair=json.RootElement.GetProperty("segments").EnumerateArray().Select(s=>new SegmentData(s.GetProperty("text").GetString()!,TimeSpan.FromTicks(s.GetProperty("startTicks").GetInt64()),TimeSpan.FromTicks(s.GetProperty("endTicks").GetInt64()),0,0,0,0,"en",s.GetProperty("tokens").EnumerateArray().Select(t=>T(t.GetProperty("text").GetString()!,t.GetProperty("start10Ms").GetInt64(),t.GetProperty("end10Ms").GetInt64(),t.GetProperty("dtwTimestamp").GetInt64(),t.GetProperty("id").GetInt32())).ToArray())).ToArray();
        var repaired=Assert.IsType<TranscriptionSegment>(WhisperTokenNormalizer.RecoverWithFollowing(pair[0],pair[1],true));
        var source=new MediaSegment("s","t","m","voice.wav",new(0,20_000_000),new(5_000_000,20_000_000));
        var result=new TranscriptionResult("s","medium.en","en",20_000_000,[repaired]);
        var cues=new SubtitleCuePlanner().Build(source,result,new FrameRate(numerator,denominator));
        var cue=Assert.Single(cues);Assert.Equal((pair[0].Text+pair[1].Text).Trim(),cue.Text);
        Assert.All(cue.Words.Where(w=>!w.IsSpace),w=>Assert.True(w.EndTimeMs>w.StartTimeMs));
    }

    [Fact]
    public void PreviousRealInternalWordFailureStillAcceptsItsRecordedNativeRetry()
    {
        using var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(FixtureFiles.Directory,"whisper_english_internal_retry.json")));
        var s=json.RootElement.GetProperty("original");
        var original=new SegmentData(s.GetProperty("text").GetString()!,TimeSpan.FromTicks(s.GetProperty("startTicks").GetInt64()),TimeSpan.FromTicks(s.GetProperty("endTicks").GetInt64()),0,0,0,0,"en",s.GetProperty("tokens").EnumerateArray().Select(t=>T(t.GetProperty("text").GetString()!,t.GetProperty("start10Ms").GetInt64(),t.GetProperty("end10Ms").GetInt64(),t.GetProperty("dtwTimestamp").GetInt64(),t.GetProperty("id").GetInt32())).ToArray());
        Assert.Throws<InvalidDataException>(()=>WhisperTokenNormalizer.Normalize(original,true));
        var tokens=json.RootElement.GetProperty("recoveredTokens").EnumerateArray().Select(t=>T(t.GetProperty("text").GetString()!,t.GetProperty("range").GetProperty("startUs").GetInt64()/10_000,t.GetProperty("range").GetProperty("endUs").GetInt64()/10_000)).ToArray();
        var retry=new SegmentData(original.Text,original.Start,original.End,0,0,0,0,"en",tokens);
        var recovered=Assert.IsType<TranscriptionSegment>(WhisperTokenNormalizer.RecoverFromRetry(original,[retry],true));
        Valid(recovered,original.Text.Trim());
    }

    [Fact]
    public void DeterministicStressPreservesTextAndOrderingAcross2000Variations()
    {
        var random=new Random(20261015);
        for(int sample=0;sample<2000;sample++) {
            int count=random.Next(3,14),offset=random.Next(0,30000);var tokens=new List<WhisperToken>();
            for(int i=0;i<count;i++) {
                long start=offset+i*20,end=start+10;bool collapsed=i>0&&i<count-1&&random.Next(3)==0;
                tokens.Add(T(" word"+i,start,collapsed?start:end,start+5));
            }
            var original=P(offset,offset+count*20,tokens.ToArray());
            var normalized=WhisperTokenNormalizer.Normalize(original,sample%2==0);
            Valid(normalized,original.Text.Trim());
            for(int i=0;i<count;i++) if(tokens[i].Start!=tokens[i].End) {
                Assert.Equal(tokens[i].Start*10_000,normalized.Tokens[i].Range.StartUs);
                Assert.Equal(tokens[i].End*10_000,normalized.Tokens[i].Range.EndUs);
            }
        }
    }
}
