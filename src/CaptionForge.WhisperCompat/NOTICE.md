# CaptionForge compatibility patch

Original work: Whisper.net by Sandro Hanea, MIT license (LICENSE).
Upstream repository: https://github.com/sandrohanea/whisper.net
Source commit from official NuGet Whisper.net 1.9.1 metadata: 98278acc38ae23590cdfa9859f78f089abae52a7.

Changes:
- WhisperProcessor.cs: after successful native processing, drain pending segments through OnNewSegment before freeing the native state, in synchronous and asynchronous paths.
- Packaging: net10.0 source project, unchanged assembly name Whisper.net and Microsoft.Extensions.AI.Abstractions 10.2.0 dependency. Full upstream public source API retained.

This is a local CaptionForge compatibility copy; it is not an official Whisper.net release.
Native runtime remains the official Whisper.net.Runtime 1.9.1 package. No native engine, decoding setting, waveform conversion or DTW preset is modified.
