# Audio y transcripción

Módulo independiente para CaptionForge. Abre audio o vídeo, elige una pista, selecciona un tramo sobre la forma de onda o escribe sus tiempos y extrae WAV/MP3 o transcribe a TXT/SRT/VTT.

Instalación: descomprime el paquete en `Modules/MediaTools` junto a CaptionForge.exe y reinicia. El host descubre `module.json`; no necesita referencias a este módulo.

Requiere FFmpeg/FFprobe y un modelo GGML de Whisper para transcribir. Usa inicialmente las rutas ya configuradas en CaptionForge, después conserva su propia configuración en `%LOCALAPPDATA%/CaptionForge/modules/media-tools/settings.json`. No requiere Python, proyectos de CapCut ni el módulo de voces.

La vista previa temporal es PCM mono 16 kHz; WAV exportado conserva frecuencia y canales de la pista, MP3 se codifica con calidad VBR. El original nunca se sobrescribe. Los subtítulos comienzan en cero para el fragmento; puede activarse «Tiempos del archivo original» al exportar. Las marcas de Whisper corresponden a segmentos, no a alineación por palabra.

Whisper.net / whisper.cpp: licencias MIT; ver THIRD_PARTY_NOTICES del programa y las licencias incluidas. La separación de voz y música queda fuera de este módulo.
