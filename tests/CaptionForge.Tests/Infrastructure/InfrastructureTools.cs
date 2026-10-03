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

internal static class InfrastructureTools
{
internal static bool EqualBytes(string path,byte[]? expected) => expected is null ? !File.Exists(path) : File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(expected);
internal static JsonObject Read(string path) => JsonNode.Parse(File.ReadAllBytes(path))!.AsObject();
internal static void Write(string path,JsonNode node) => File.WriteAllText(path,node.ToJsonString(new JsonSerializerOptions {WriteIndented=true}));
internal static void ValidateGraph(JsonObject draft,GenerationResult result,Checks tests)
{
 var track=draft["tracks"]!.AsArray().Single(t=>t!["id"]!.GetValue<string>()==result.Plan.ManagedSubtitles.Objects.Single(o=>o.Kind==SubtitleObjectKind.Track).Id)!;
 var materials=draft["materials"]!;
 foreach (var s in track["segments"]!.AsArray())
 {
  var template=materials["text_templates"]!.AsArray().Single(t=>t!["id"]!.GetValue<string>()==s!["material_id"]!.GetValue<string>())!;var a=template["text_info_resources"]![0]!;
  var text=materials["texts"]!.AsArray().Single(t=>t!["id"]!.GetValue<string>()==a["text_material_id"]!.GetValue<string>())!;var content=JsonNode.Parse(text["content"]!.GetValue<string>())!;
  var animation=materials["material_animations"]!.AsArray().Single(t=>t!["id"]!.GetValue<string>()==s!["extra_material_refs"]![0]!.GetValue<string>())!;
  tests.Check(template["resource_id"]!.GetValue<string>()=="7535399757947161873" && content["styles"]![0]!["font"]!["id"]!.GetValue<string>()=="7517426090072149264","Grafo golden: plantilla/fuente definitivas");
  tests.Check(a["attach_info"]!["duration"]!.GetValue<long>()==s!["target_timerange"]!["duration"]!.GetValue<long>()-1 && animation["animations"]![0]!["duration"]!.GetValue<long>()==a["attach_info"]!["duration"]!.GetValue<long>(),"Grafo golden: duraciones D-1 de attachment/animación");
  tests.Check(a["extra_material_refs"]![0]!.GetValue<string>()==s!["extra_material_refs"]![1]!.GetValue<string>() && a["extra_material_refs"]![1]!.GetValue<string>()==s["extra_material_refs"]![0]!.GetValue<string>(),"Grafo golden: referencias cruzadas efecto/animación");
 }
}
internal static void WriteWave(string path,int frames,int frequency=0)
{
 Directory.CreateDirectory(Path.GetDirectoryName(path)!);using var stream=File.Create(path);using var writer=new BinaryWriter(stream,Encoding.ASCII);
 writer.Write(Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+frames*2);writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(16000);writer.Write(32000);writer.Write((short)2);writer.Write((short)16);writer.Write(Encoding.ASCII.GetBytes("data"));writer.Write(frames*2);
 for(int i=0;i<frames;i++) writer.Write((short)(frequency==0?0:Math.Sin(i*2*Math.PI*frequency/16000)*10000));
}
internal static async Task RunFfmpegAsync(IEnumerable<string> arguments)
{
 var start=new ProcessStartInfo("ffmpeg") {UseShellExecute=false,RedirectStandardError=true,RedirectStandardOutput=true};start.ArgumentList.Add("-hide_banner");start.ArgumentList.Add("-loglevel");start.ArgumentList.Add("error");foreach(var a in arguments)start.ArgumentList.Add(a);
 using var p=Process.Start(start)!;var error=p.StandardError.ReadToEndAsync();var output=p.StandardOutput.ReadToEndAsync();await p.WaitForExitAsync();await output;if(p.ExitCode!=0)throw new IOException(await error);await error;
}
}
