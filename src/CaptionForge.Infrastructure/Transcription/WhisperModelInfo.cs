using Whisper.net;

namespace CaptionForge.Infrastructure.Transcription;

public sealed record WhisperModelInfo(bool IsEnglishOnly,int AudioLayers,int TextLayers,int AudioState,int MelBins,WhisperAlignmentHeadsPreset AlignmentPreset)
{
    public static WhisperModelInfo Read(string path,WhisperAlignmentHeadsPreset? explicitPreset=null)
    {
        using var stream=File.OpenRead(path);using var reader=new BinaryReader(stream);
        if (stream.Length<48 || reader.ReadUInt32()!=0x67676d6c) throw new InvalidDataException("El modelo no tiene cabecera GGML de Whisper válida.");
        int vocab=reader.ReadInt32();reader.ReadInt32();int state=reader.ReadInt32();reader.ReadInt32();int layers=reader.ReadInt32();
        reader.ReadInt32();reader.ReadInt32();reader.ReadInt32();int textLayers=reader.ReadInt32();int mels=reader.ReadInt32();
        if (vocab is < 51864 or > 51866 || mels is not (80 or 128)) throw new InvalidDataException("Metadatos de modelo Whisper no admitidos.");
        bool english=vocab==51864;
        WhisperAlignmentHeadsPreset preset=(layers,state,textLayers,mels) switch
        {
            (4,384,4,80) => english?WhisperAlignmentHeadsPreset.TinyEn:WhisperAlignmentHeadsPreset.Tiny,
            (6,512,6,80) => english?WhisperAlignmentHeadsPreset.BaseEn:WhisperAlignmentHeadsPreset.Base,
            (12,768,12,80) => english?WhisperAlignmentHeadsPreset.SmallEn:WhisperAlignmentHeadsPreset.Small,
            (24,1024,24,80) => english?WhisperAlignmentHeadsPreset.MediumEn:WhisperAlignmentHeadsPreset.Medium,
            (32,1280,4,128) when !english => WhisperAlignmentHeadsPreset.LargeV3Turbo,
            (32,1280,32,128) when !english => WhisperAlignmentHeadsPreset.LargeV3,
            (32,1280,32,80) when !english => explicitPreset is WhisperAlignmentHeadsPreset.LargeV1 or WhisperAlignmentHeadsPreset.LargeV2 ? explicitPreset.Value
                : Path.GetFileName(path).Contains("large-v1",StringComparison.OrdinalIgnoreCase)?WhisperAlignmentHeadsPreset.LargeV1
                : Path.GetFileName(path).Contains("large-v2",StringComparison.OrdinalIgnoreCase)?WhisperAlignmentHeadsPreset.LargeV2
                : throw new InvalidDataException("Large v1/v2 requiere su versión explícita para DTW."),
            _ => throw new InvalidDataException("Arquitectura de modelo sin alineación DTW verificada.")
        };
        if (explicitPreset is not null && explicitPreset!=preset) throw new InvalidDataException("El preset DTW no corresponde a la arquitectura del modelo.");
        return new(english,layers,textLayers,state,mels,preset);
    }
}
