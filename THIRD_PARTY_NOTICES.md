# Third-party notices

CaptionForge contains and integrates independently licensed components. The root `LICENSE` applies to original material owned and offered by Yordin Isaac Garcia; it does not replace the licenses of those components.

## Whisper.net compatibility source

`src/CaptionForge.WhisperCompat` is a compatibility copy of Whisper.net 1.9.1. The original work is copyright (c) 2024 sandrohanea and is licensed under MIT.

- Upstream: https://github.com/sandrohanea/whisper.net
- Source commit identified in the original package metadata: `98278acc38ae23590cdfa9859f78f089abae52a7`
- Complete license: [licenses/Whisper.net-MIT.txt](licenses/Whisper.net-MIT.txt)
- Patch description: [licenses/WhisperCompat-NOTICE.md](licenses/WhisperCompat-NOTICE.md)

Keep the original `LICENSE` and `NOTICE.md` in the compatibility source project. Its independent MIT permissions, including commercial permissions, remain available. A redistribution of the full CaptionForge application that also contains CaptionForge-covered material must satisfy both sets of applicable terms.

## Pocket TTS configurations and local speech engines

`src/CaptionForge.Modules.TextToSpeech/Backend/config/english.yaml` and `spanish.yaml` are Pocket TTS configurations from Kyutai's upstream package, distributed under MIT. The complete upstream license is retained in [licenses/Pocket-TTS-MIT.txt](licenses/Pocket-TTS-MIT.txt). Upstream: https://github.com/kyutai-labs/pocket-tts.

The local speech module integrates separately installed Kokoro, Pocket TTS and Chatterbox Python packages and model weights. Python runtimes, packages, model weights, preset voices and reference recordings are not included in the CaptionForge executable or release ZIP. Their respective terms govern their use and redistribution; the CaptionForge license grants no rights to those assets.

## Other components

.NET/WPF, the native Whisper runtime and its dependencies, Microsoft.Extensions.AI.Abstractions, and other restored packages are governed by their own notices and licenses. Preserve notices included in the packages and published runtime. This file is not a complete inventory of all transitive dependencies or a substitute for their license texts.

FFmpeg/FFprobe and Whisper model files are configured separately and are not added to CaptionForge's release ZIP by the current workflow. Their licenses must be checked for the particular distribution or model obtained, especially if a future release bundles them.

CapCut template resources, fonts, effects, and media belong to their respective rights holders. CaptionForge's root license grants no rights to those assets or to CapCut itself.
