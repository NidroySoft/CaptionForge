# Resultado de ejecución

Compilación Release: **0 errores, 0 advertencias**.

| Ejecución | Aprobadas | Fallidas | Omitidas | Total |
|---|---:|---:|---:|---:|
| FFmpeg + Whisper nativo configurado | 225 | 0 | 1 | 226 |
| Sin modelo/voz ni proyecto real configurados, con FFmpeg | 224 | 0 | 2 | 226 |

La prueba omitida en la primera ejecución requiere el proyecto real de tu Windows. La segunda omite además Whisper nativo.

| Assembly | Líneas | Ramas |
|---|---:|---:|
| CaptionForge.Core | 96.95% | 92.62% |
| CaptionForge.Infrastructure | 96.79% | 79.80% |
| CaptionForge.Application | 96.00% | 82.37% |
| Total | 96.56% | 81.38% |

Cobertura medida con coverlet.collector: 1516/1570 líneas y 1128/1386 ramas. La cobertura de código no mide calidad visual, precisión del reconocimiento para cualquier audio ni recuperación frente a todo fallo de Windows.

FFmpeg real recortó audio y extrajo voz de vídeo. Whisper.net 1.9.1 ejecutó tiny.en en CPU con DTW, preparó captions y aplicó el JSON en copias. CapCut y WPF no se ejecutaron. Los modelos, voz descargada y recursos de caché no se incluyen.

VERIFICACION.json contiene los nombres de cada caso y hashes del código de producción usado. CASOS_Y_LIMITES.md explica el alcance y lo pendiente.
