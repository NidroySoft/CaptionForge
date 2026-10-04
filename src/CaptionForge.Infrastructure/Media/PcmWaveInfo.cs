using System.Text;

namespace CaptionForge.Infrastructure.Media;

/// <summary>Valida WAV RIFF PCM de tamaño normal; RF64 necesita un adaptador explícito.</summary>
public sealed record PcmWaveInfo(int SampleRate,int Channels,int BitsPerSample,long SampleFrames,long DurationUs)
{
    public static PcmWaveInfo Read(string path)
    {
        using var stream=File.OpenRead(path);using var reader=new BinaryReader(stream,Encoding.ASCII);
        if (Encoding.ASCII.GetString(reader.ReadBytes(4))!="RIFF") throw new InvalidDataException("El audio no es WAV RIFF.");
        reader.ReadUInt32();if (Encoding.ASCII.GetString(reader.ReadBytes(4))!="WAVE") throw new InvalidDataException("Cabecera WAV inválida.");
        int format=0,channels=0,rate=0,bits=0,align=0;long size=-1;
        while (stream.Position+8<=stream.Length)
        {
            string name=Encoding.ASCII.GetString(reader.ReadBytes(4));uint length=reader.ReadUInt32();long begin=stream.Position;
            if (begin+length>stream.Length) throw new InvalidDataException("Chunk WAV truncado.");
            if (name=="fmt ")
            {
                if (length<16) throw new InvalidDataException("Formato WAV incompleto.");
                format=reader.ReadUInt16();channels=reader.ReadUInt16();rate=reader.ReadInt32();reader.ReadInt32();align=reader.ReadUInt16();bits=reader.ReadUInt16();
            }
            else if (name=="data") { if (size>=0) throw new InvalidDataException("WAV con varios chunks data no admitido.");size=length; }
            stream.Position=begin+length+(length%2);
        }
        if (format!=1 || channels!=1 || rate!=16000 || bits!=16 || align!=2 || size<=0 || size%align!=0) throw new InvalidDataException("Se necesita PCM mono/16 kHz/16 bits con muestras completas.");
        long frames=size/align;long duration=checked((long)Math.Round((decimal)frames*1_000_000/rate,MidpointRounding.AwayFromZero));
        return new(rate,channels,bits,frames,duration);
    }
}
