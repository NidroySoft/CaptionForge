# CaptionForge.Infrastructure — v1

## Instalación en tu solución

Extrae **el contenido del ZIP** en:

`C:\Proyectos\CaptionForge\src\CaptionForge.Infrastructure`

Las carpetas `CapCut`, `Media`, `Transcription`, `Workspace`, `Settings`, `Configuration`, `Internal` y `Assets` deben quedar al lado de tu `.csproj`. Conserva el proyecto existente y sus referencias a Core y Application; no se reemplaza ninguno de esos proyectos. `Class1.cs` puede permanecer.

Este bloque sí necesita dos paquetes. Desde `C:\Proyectos\CaptionForge`, ejecuta:

```powershell
dotnet add .\src\CaptionForge.Infrastructure\CaptionForge.Infrastructure.csproj package Whisper.net --version 1.9.1
dotnet add .\src\CaptionForge.Infrastructure\CaptionForge.Infrastructure.csproj package Whisper.net.Runtime --version 1.9.1
dotnet build .\src\CaptionForge.Infrastructure\CaptionForge.Infrastructure.csproj -c Release
```

Se fija la versión **1.9.1**, que se utilizó para compilar y ejecutar las pruebas. Los paquetes agregan las dependencias transitivas necesarias; no hay que añadirlas manualmente. El molde golden está incluido como constante C# en `Assets/GoldenV3Mold.cs`, así que no necesitas configurar `EmbeddedResource`, copias adicionales ni editar el `.csproj` para incluir el JSON.

El paquete contiene **22 archivos C# implementados**, documentación, informe de comprobaciones y un ZIP separado con la suite reproducible. No incluye WPF, modelos GGML, ejecutables FFmpeg, DLL de Whisper ni los recursos/font de CapCut.

## Qué está implementado

| Adaptador                        | Función                                                                                                         |
| -------------------------------- | --------------------------------------------------------------------------------------------------------------- |
| `CapCutCatalog`                  | Descubre proyectos válidos, lista timelines del registro y resuelve medios desde los materiales de la timeline. |
| `JsonWorkspaceStore`             | Ejecuciones por draft_id, manifiesto versionado, resultados, SRT, estados e identidades propias.                |
| `JsonApplicationSettingsStore`   | Preferencias locales persistidas con escritura por reemplazo.                                                   |
| `FfmpegAudioPreparationService`  | Probe, audio desde audio/vídeo, recorte, mono PCM 16 kHz/16 bits y duración correspondiente al destino.         |
| `WhisperNetTranscriptionService` | Transcripción directa con Whisper.net, CPU y DTW; reutiliza el modelo entre fragmentos.                         |
| `GoldenV3SubtitleWriter`         | Plan validado, grafo v3, preservación de datos ajenos, backups, commit, rollback y restauración explícita.      |
| `CapCutTemplateAssetResolver`    | Localiza los cuatro recursos golden en la caché o permite suministrar rutas explícitas.                         |

Los seis contratos de Application tienen implementaciones concretas. El frontend todavía debe componerlas, presentar las selecciones y llamar a generar/aplicar. Estas bibliotecas no son una aplicación ejecutable por sí solas.

## Preparar dependencias del equipo

- **FFmpeg y FFprobe:** deben estar instalados o acompañar al futuro ejecutable. El constructor acepta las dos rutas; si se omiten, busca `ffmpeg` y `ffprobe` mediante PATH. Se usan procesos con `ArgumentList`, sin shell ni concatenación de comandos. FFprobe devuelve JSON; Whisper no se ejecuta mediante CLI.
- **Modelo Whisper GGML:** se suministra su archivo local. `medium.en` reproduce la arquitectura de la referencia inglesa; para español se necesita un modelo multilingüe. Se reconoce la cabecera para seleccionar el preset DTW, en lugar de adivinarlo solo por el nombre. Tiny/Base/Small/Medium, sus variantes inglesas y Large v3/turbo están mapeados; Large v1/v2 necesitan la versión en el nombre o preset explícito.
- **Runtime CPU:** se utilizan `Whisper.net` y `Whisper.net.Runtime` 1.9.1. Para Windows, consulta los requisitos del runtime oficial: Windows 11/Server 2022 o posterior, Visual C++ Redistributable 2022 y las instrucciones CPU indicadas por el proyecto. Para hardware sin AVX existe un runtime específico; esta entrega se probó con el CPU runtime normal.
- **Recursos de CapCut:** la plantilla golden, Bebas Neue y sus recursos de efecto/animación deben existir en la caché local de CapCut. No se distribuyen ni descargan automáticamente. La validación de rutas comprueba presencia y contenido no vacío; la compatibilidad visual final requiere abrir CapCut en tu PC.

Rutas candidatas por defecto, obtenidas desde `LocalApplicationData`:

```text
CapCut\User Data\Projects\com.lveditor.draft
CaptionForge\Projects
CaptionForge\settings.json
```

Son configurables. El almacén rechaza un espacio propio dentro del árbol de proyectos de CapCut. Los proyectos con el mismo draft_id en varias carpetas se diagnostican y no se listan como dos proyectos independientes con un espacio propio compartido.

## Composición para el futuro frontend

```csharp
using CaptionForge.Infrastructure.CapCut;
using CaptionForge.Infrastructure.Media;
using CaptionForge.Infrastructure.Settings;
using CaptionForge.Infrastructure.Transcription;
using CaptionForge.Infrastructure.Workspace;
using CaptionForge.Application.Services;

var catalog = new CapCutCatalog();
var settings = new JsonApplicationSettingsStore(settingsPath);
var workspace = new JsonWorkspaceStore(workspaceRoot);
var audio = new FfmpegAudioPreparationService(ffmpegPath, ffprobePath);
var assets = CapCutTemplateAssetResolver.Resolve(capCutUserDataPath);
var writer = new GoldenV3SubtitleWriter(assets);
await using var transcription = new WhisperNetTranscriptionService();
var generation = new CaptionGenerationService(audio, transcription, writer, workspace);

var projects = await catalog.FindProjectsAsync(projectsRoot, cancellationToken);
var timelines = await catalog.GetTimelinesAsync(selectedProject, cancellationToken);
var snapshot = await catalog.ReadTimelineAsync(selectedProject, selectedTimeline, cancellationToken);

// Construir GenerateCaptionsRequest con selección, modelo, idioma e hilos.
var result = await generation.GenerateAsync(request, progress, cancellationToken);
// Mostrar captions y result.Plan.Warnings; después, acción explícita de aplicar:
var receipt = await generation.ApplyAsync(result, cancellationToken);
```

Las variables de rutas, selecciones y petición las proporciona el frontend. El servicio de transcripción debe mantenerse durante la sesión y liberarse con `DisposeAsync` al cerrar; crear uno por chunk volvería a cargar el modelo innecesariamente. La cantidad de hilos se configura en `TranscriptionOptions`, con la recomendación ya implementada en Application. Usa códigos de idioma como `en`, `es` o `auto`.

Si la caché no se puede resolver sin ambigüedad, se pueden proporcionar rutas verificadas de esos mismos recursos:

```csharp
var assets = new TemplateAssetPaths(
    templateDirectory, bebasNeueFontFile, textEffectDirectory, captionAnimationDirectory);
```

No debe apuntarse a la variante manual reciente: el escritor fija la plantilla `7535399757947161873`, fuente `7517426090072149264`, efecto `7340940260152447489` y animación `7289356246623195649` de la v3 correcta.

## JSON real, identidades y espejo

El catálogo usa `draft_meta_info.draft_id` para el proyecto y `Timelines/project.json` para las timelines. El contenido debe declarar el mismo ID que su entrada. El esquema admitido es **version 360000**, observado en los fixtures CapCut 9.5; otro esquema se diagnostica, en vez de aplicar el molde sin evidencia.

El patcher clona el documento real. Conserva propiedades raíz, canvas/FPS, pistas y materiales ajenos. Añade una pista propia y seis identidades por caption: segmento, texto, template, attachment, animación y efecto. Las relaciones siguen el golden, con rangos globales en µs, words relativos en ms y duración D-1 en attachment/animación. Los IDs de recursos/font se conservan y no se confunden con IDs de objetos administrados.

El registro propio vincula `(Kind, LogicalKey)` a los IDs; las claves de caption usan sourceSegmentId y ordinal dentro de ese fragmento. En una regeneración se reutilizan esos IDs. Las propiedades desconocidas añadidas a nuestros objetos y al JSON interior de `content` sobreviven a la actualización. Si la pista propia desapareció, contiene objetos ajenos o comparte referencias con material externo, se devuelve un diagnóstico y no se elimina a ciegas.

La política inicial conserva SRT/captions ajenos y advierte de posibles duplicados. El registro de timelines y metadatos de CapCut no se modifican. El espejo raíz solo se cambia cuando su ID corresponde a la timeline seleccionada y ambos documentos son equivalentes. Se admite la pequeña diferencia de representación decimal del fixture; una discrepancia real con el mismo ID se rechaza. Si la raíz corresponde a otra timeline, se conservan ella y su `.bak`.

Para canvas horizontal se conserva la configuración y se informa que la disposición del molde aún necesita verificación visual. No se calcula una adaptación nueva sin el fixture correspondiente. Los rangos de estilos usan longitud UTF-16; textos con emojis/alfabetos no probados necesitan comprobación en CapCut. La segmentación por palabra sigue el contrato Core validado con los fixtures ingleses, no una segmentación lingüística universal.

## Audio y transcripción

FFmpeg hace el recorte de origen, prepara muestras para la duración de destino y registra el mapa temporal. Se admite un margen pequeño de hasta 100 ms para desajustes de duración/recorte antes de rechazar una fuente incompatible; los WAV finales deben coincidir con el destino dentro de una muestra de 16 kHz (63 µs). El recorte de origen no se suma a los captions: Application suma el inicio de destino y conserva los huecos.

Se comprueba que exista un stream de audio en el medio. Un vídeo sin audio se diagnostica. Velocidades distintas de 1, curvas, reversa, fragmentos silenciados, compuestos no interpretados y selección solapada mantienen las limitaciones del MVP. No hay mezcla, edición de audio ni eliminación del archivo original.

Whisper.net recibe WAVs comprobados, utiliza CPU y DTW y mantiene un modelo cargado entre chunks. Con un modelo exclusivamente inglés, `auto` se resuelve a `en` según la cabecera; no se activa una detección multilingüe que ese modelo no puede realizar correctamente. Para modelos multilingües se utiliza la detección del motor. El adaptador convierte `WhisperToken.Start/End`, que son unidades de 10 ms, a µs. `DtwTimestamp` queda en el diagnóstico; no se interpreta como un offset de milisegundos. Los controles se identifican por el rango de IDs del tokenizador. Se conservan los espacios iniciales; tokens léxicos sin tiempos válidos fallan y no reciben una distribución artificial.

Dentro del run se guardan WAV, mapa/probe, argumentos y stderr de FFmpeg, transcripción normalizada y tokens nativos con timestamps para diagnóstico. Los datos no se envían a un servicio de transcripción.

## Backup, commit y restauración

`GenerateAsync` guarda el plan y sus JSON preparados dentro del espacio propio. No aplica cambios en CapCut. `ApplyAsync` exige CapCut cerrado, adquiere exclusión por timeline y comprueba hashes del plan, documentos leídos, registro propio y archivos preparados. Los backups propios incluyen los bytes inmediatamente anteriores de cada archivo existente, también los `.bak` nativos.

Antes de escribir se persiste el journal. Durante commit se reemplazan los documentos y `.bak` con el JSON nuevo; se comprueba el hash de cada destino y se guarda recibo durable. El registro de IDs se publica bajo la misma exclusión del commit, para que otra ejecución no vea captions aplicados sin autoría. Finalizar un manifiesto anterior no pisa el registro de una ejecución posterior.

Si falla un reemplazo, se intenta restaurar todo el conjunto con los backups y devolver el registro anterior. Si rollback falla, el journal queda `RecoveryRequired`. Application mantiene su protocolo conservador de recuperación incluso cuando el writer logró rollback: no se reintenta ese run a ciegas. No se promete atomicidad de varios archivos ni éxito garantizado ante pérdida de disco.

Restauración explícita:

```csharp
await writer.RestoreAsync(receipt.JournalPath, cancellationToken);
```

Comprueba CapCut cerrado, integridad de plan/backups, rutas y que no haya una edición/ejecución posterior incompatible. Recupera bytes anteriores, elimina archivos que no existían y devuelve el registro propio anterior. Registra `Restored` en el journal y un campo `restoration` en el manifiesto, sin añadir un estado nuevo al enum de Application. El frontend de restauración/recuperación al iniciar todavía debe construirse. No se borran backups/journals automáticamente.

Las escrituras mediante symlinks/junctions se rechazan para que no eludan la exclusión ni el confinamiento de rutas; debe indicarse la ruta física. La cancelación se acepta antes de commit. Una vez iniciado, se termina o revierte el conjunto sin cancelación intermedia.

## Pruebas realizadas

Release, SDK **.NET 10.0.401**, advertencias tratadas como errores: **0 errores y 0 advertencias**. Consulta `VERIFICACION.json` para la cantidad final y la lista completa de comprobaciones.

Se probaron catálogo real contra los fixtures, los tres IDs, medios ausentes, metadatos y covers, grafo de 15 captions v3, arrays words exactos, preservación de todos los materiales ajenos, actualización de IDs, campos desconocidos, espejo raíz, `.bak` viejos/nuevos, restauración exacta, hashes alterados, fallos parciales/rollback y finalización concurrente de ejecuciones.

Se ejecutó **FFmpeg real** con audio, padding pequeño y vídeo con/sin audio. También **Whisper.net 1.9.1 real con CPU, DTW y tiny.en** sobre una muestra de voz: generó captions, los colocó en el destino 20 s y aplicó el JSON a copias. Se probó `auto` y reutilización del modelo. No se ejecutó medium.en real ni se abrió CapCut/WPF aquí. Los assets visuales de las pruebas fueron marcadores ficticios; las pruebas de grafo no certifican el aspecto final de los recursos en tu instalación.

Los dos ajustes editoriales del fixture QR se aplican únicamente dentro del transcriptor de prueba. No están incluidos en el producto. La suite se ejecutó en Linux, no en tu Windows.

### Suite reproducible opcional

El ZIP `Verificacion/CaptionForge_Infrastructure_Verificacion_v1.zip` contiene el proyecto de consola, su código y los fixtures. Extrae **ese segundo ZIP en la raíz de la solución** (`C:\Proyectos\CaptionForge`). No añade su código a Infrastructure ni modifica tu proyecto xUnit o la solución.

Con FFmpeg/FFprobe en PATH:

```powershell
dotnet run --project .\tests\CaptionForge.Infrastructure.Verification\CaptionForge.Infrastructure.Verification.csproj -c Release -- .\tests\CaptionForge.Infrastructure.Verification\Fixtures .\tests\CaptionForge.Infrastructure.Verification\VERIFICACION_local.json
```

Las pruebas estructurales usan un audio/transcriptor controlado; las de FFmpeg usan procesos reales. Para añadir la prueba nativa de Whisper, usa el mismo comando con dos argumentos extra: ruta de modelo GGML inglés y muestra de voz inglesa de al menos 11 segundos. El test procesa sus primeros 11 s en una copia de proyecto.

La suite crea exclusivamente carpetas temporales nuevas y muestra su ubicación en el informe. Conserva los artefactos para revisión. No consulta ni modifica tus proyectos reales de CapCut; el guard de procesos de prueba es controlado. No incluye el modelo ni la muestra de voz descargados para nuestra ejecución.

## Referencias de implementación

- [Whisper.net y requisitos del runtime](https://github.com/sandrohanea/whisper.net)
- [Paquete Whisper.net 1.9.1](https://www.nuget.org/packages/Whisper.net/1.9.1)
- [API exacta del paquete: commit 98278acc](https://github.com/sandrohanea/whisper.net/tree/98278acc38ae23590cdfa9859f78f089abae52a7)
- [FFmpeg: filtros atrim, apad y aresample](https://ffmpeg.org/ffmpeg-filters.html)
- Blueprint y fixtures de `CaptionForge_Contexto_v1.zip`, y contratos de `CaptionForge_Application_v1.zip`.

El siguiente bloque será WPF/composición y la prueba visual en CapCut de tu equipo. Antes de ampliar velocidades, mezcla, formato de proyecto o adaptación del estilo a otros canvas, hacen falta sus fixtures y pruebas correspondientes.
