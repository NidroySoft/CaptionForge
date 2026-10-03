# CaptionForge.Core — bloque inicial v1

## Instalación en tu solución existente

1. Extrae **el contenido del ZIP** directamente en:

   `C:\Proyectos\CaptionForge\src\CaptionForge.Core`

2. Conserva tu `CaptionForge.Core.csproj` actual: no es necesario cambiar referencias ni añadir paquetes NuGet. Debe seguir usando `net10.0`, `ImplicitUsings=enable` y `Nullable=enable`, como la plantilla que creaste.
3. Las carpetas `Models`, `ValueObjects`, `Enums` y `Rules` deben quedar al lado del `.csproj`, sin otra carpeta `CaptionForge.Core` intermedia.
4. Compila la solución. En un proyecto SDK los archivos `.cs` se incluyen automáticamente; no hay que registrar cada archivo en el `.csproj`.

El ZIP contiene 14 archivos C# implementados, este documento y el informe de verificación. No incluye otro proyecto, solución, binarios, `bin`, `obj` ni archivos de CapCut. Tu `Class1.cs` de la plantilla puede mantenerse; no interviene en este bloque.

## Qué incluye

- Identidades distintas de proyecto, registro y timeline.
- Pistas y fragmentos de medios con origen/destino independientes y datos de velocidad.
- Transcripciones y tokens propios, sin depender de Whisper.net.
- Caption y palabras con el patrón temporal de la referencia v3.
- Rangos validados en microsegundos y FPS como razón exacta.
- Cuantización de inicio y duración, traslado a la timeline y límite del fragmento.
- Agrupación de tokens, puntuación, espacios explícitos, pausas y extremos del caption.

Los modelos conservan IDs como cadenas sin reformatearlos. Las colecciones se copian y se exponen como solo lectura. `FrameRate` se construye explícitamente con numerador/denominador; su valor `default` se rechaza al calcular. `TimeRangeUs` admite duración cero para representar resultados vacíos; medios y captions exigen duración positiva.

## Contrato de tiempos

- Rangos y offsets de `TranscriptionToken`: **microsegundos desde el comienzo del WAV preparado**.
- `TimedWord.StartTimeMs/EndTimeMs`: **milisegundos relativos al caption**.
- `SubtitleCue.TimelineRange`: **microsegundos en el montaje**.
- `WordTimingBuilder.Build`: recibe el rango local del caption, no el rango global.
- `PlaceOnTimeline`: suma el inicio de destino y mantiene la duración; presupone que el WAV ya refleja cualquier velocidad admitida.

Uso previsto: cuantizar la frase local una sola vez, acotarla a la duración del fragmento, construir palabras usando ese rango local, trasladarlo a destino y crear el `SubtitleCue`. Si el rango queda vacío, omitirlo antes de construir palabras. Si no hay tokens léxicos, `Build` devuelve una colección vacía; no crear un caption a partir de ella.

El adaptador de Whisper.net debe marcar los tokens especiales con `IsControl=true`, mantener espacios iniciales y convertir sus unidades a microsegundos. Core no inspecciona formatos de control específicos de Whisper ni interpreta `t_dtw` crudo.

Las palabras inválidas, solapadas o sin duración no reciben tiempos artificiales: la operación informa un error. El patrón de palabras está basado en los fixtures ingleses de la v3; no es un segmentador lingüístico universal. No hay correcciones automáticas de «code words» ni «Not magic, math»: esas dos correcciones del ejemplo son editoriales y pertenecen solo a la comparación de referencia.

## Lo que sigue en otros proyectos

La lectura y modificación del JSON, el grafo de templates/animaciones/efectos, selección persistida, recursos, FFmpeg, Whisper.net, escritura, backups y UI se implementarán en Application, Infrastructure y WPF. Estos archivos no modifican CapCut.

Las propiedades `HasVariableSpeed`, `IsReversed` y `IsMuted` describen el medio; no declaran que ya exista soporte de extracción para esos casos. La cuantización a FPS racionales está implementada, pero su compatibilidad con montajes y velocidades especiales todavía requiere las pruebas de integración del blueprint.

## Verificación realizada

Compilación Release con SDK .NET 10.0.401, advertencias tratadas como errores: **0 errores y 0 advertencias**. Se ejecutaron **129 comprobaciones C#**, incluidas referencias v3, recortes desplazados, huecos, FPS racionales, unidades, desbordamientos, alineaciones inválidas e inmutabilidad de las colecciones.

Referencia DTW: 15/15 rangos, textos y conjuntos de tiempos por palabra. Referencia normal: 15/15 rangos/textos y 13/15 conjuntos de tiempos. Para esa comparación se aplicaron externamente las dos correcciones editoriales del ejemplo; no se incluyeron como reglas del producto.

La comprobación fue sobre Core en Linux. No se ejecutaron aquí Whisper.net, FFmpeg, WPF ni CapCut. Consulta `VERIFICACION.json` para el informe. El proyecto de consola utilizado para verificar no se incluye dentro de Core, para que no añada archivos de pruebas a la compilación de tu biblioteca.
