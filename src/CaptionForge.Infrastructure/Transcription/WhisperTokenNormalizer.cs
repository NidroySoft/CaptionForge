using CaptionForge.Core.Models.Transcription;
using CaptionForge.Core.ValueObjects;
using Whisper.net;

namespace CaptionForge.Infrastructure.Transcription;

/// <summary>Whisper.net expone Start/End en unidades de 10 ms; DtwTimestamp se guarda solo como diagnóstico.</summary>
public static class WhisperTokenNormalizer
{
    public static TranscriptionSegment Normalize(SegmentData segment,bool englishOnly)
    {
        ArgumentNullException.ThrowIfNull(segment);long from=segment.Start.Ticks/10,to=segment.End.Ticks/10;
        if (from<0 || to<from) throw new InvalidDataException("Frase de Whisper con tiempos inválidos.");
        int eot=englishOnly?50256:50257;var tokens=new List<TranscriptionToken>();
        foreach (var token in segment.Tokens)
        {
            bool control=token.Id>=eot;string text=token.Text ?? "";
            if (control) { tokens.Add(new(text,new TimeRangeUs(0,0),true));continue; }
            if (token.Start<0 || token.End<token.Start) throw new InvalidDataException("Token léxico sin offsets válidos; no se inventará su tiempo.");
            long start=checked(token.Start*10_000),end=checked(token.End*10_000);
            tokens.Add(new(text,new TimeRangeUs(start,end-start)));
        }
        return new(segment.Text,new(from,to-from),tokens);
    }
}
