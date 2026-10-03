# Contratos para implementar CaptionForge.Infrastructure

Este documento fija el comportamiento que Application v1 exige. Distingue la coordinación ya implementada de las operaciones que deben resolver los adaptadores. No es autorización para eliminar textos ajenos, sustituir timelines o sobrescribir documentos no capturados.

## Dependencias y composición

Infrastructure referencia Core y Application, como en la solución existente. Implementa las seis interfaces de `CaptionForge.Application.Abstractions`. El futuro arranque WPF crea estos adaptadores y los pasa al constructor de `CaptionGenerationService`; Application no contiene un service locator ni obliga a usar un framework DI.

| Interfaz | Adaptador concreto pendiente |
|---|---|
| `ICapCutCatalog` | Carpetas de CapCut, metadatos, registro de timelines, materiales y captura coherente. |
| `IAudioPreparationService` | FFmpeg/probe, recorte y WAV normalizado con correspondencia temporal. |
| `ITranscriptionService` | Whisper.net, opciones DTW y tokens/frases normalizados. |
| `ICapCutSubtitleWriter` | Molde golden v3, grafo, recursos, plan validado, backups, commit/journal/rollback. |
| `IWorkspaceStore` | Espacio propio versionado, estados, resultados, recibos y registro de identidades. |
| `IApplicationSettingsStore` | Preferencias locales; rutas, modelo, idioma e hilos. |

## Catálogo y lectura coherente

La ruta por defecto es candidata, no una constante garantizada:

`%LOCALAPPDATA%\CapCut\User Data\Projects\com.lveditor.draft`

La UI puede ofrecer otra raíz si la validación falla. Listar debe usar metadatos reales y omitir/diagnosticar entradas inválidas. Leer timelines debe usar `Timelines/project.json`, excluir entradas marcadas como borradas y no confundir `main_timeline_id` con la pestaña actualmente activa.

`TimelineSnapshot` exige que `Timeline.ProjectId == Project.Id`, IDs de pistas/fragmentos únicos, archivos únicos y el estado del contenido seleccionado **y su `.bak`**, aunque el segundo no exista. `SourceFileStamp` guarda ruta, existencia y SHA-256 de los bytes si existe. Las rutas se normalizan en el adaptador Windows; las comparaciones de ruta en Application no distinguen mayúsculas, pero los IDs se conservan exactamente.

Capturar todos los documentos que puedan participar en el plan. El lector debe verificar que los bytes no cambiaron mientras los leyó y que su propia representación es coherente. El escritor vuelve a comprobar esa captura al preparar. No fabricar hashes ni confiar solo en fecha/tamaño.

Resolver medios a través de `segment.material_id` y los materiales de audio/vídeo. Mantener `source_timerange` y `target_timerange` separados, además de velocidad, reversa y silencio. Un vídeo sin audio, material compuesto o estructura no interpretada necesita diagnóstico; no inventar una ruta o asumir que el audio está en la carpeta del draft.

## Preparación y transcripción

El contrato MVP acepta velocidad 1 y reproducción normal. Application rechaza selecciones solapadas; aún no mezcla pistas. La normalización debe usar el recorte del origen y producir WAV PCM mono, 16 kHz y 16 bits, con origen local cero.

`AudioPreparationRequest.RequiredDurationUs` es la duración lógica del destino. `PreparedAudio.DurationUs` conserva esa duración y `MeasuredDurationUs` registra la medida real; ambas pueden diferir como máximo 63 µs por la rejilla de muestras. El pequeño desfase del fixture entre duración importada y timeline debe resolverse en preparación, no desplazando captions ni afirmando medidas falsas. Un recorte imposible/medio ausente devuelve un diagnóstico.

Los WAV y el mapeo origen/destino quedan en el run. Una ruta alternativa es solo para la ejecución. No reescribir `materials.*.path` de CapCut por localizar un archivo.

Whisper.net debe activar DTW compatible con el modelo escogido. Tokens y frases se expresan en **microsegundos desde el comienzo del WAV**; no en tiempo global ni relativos a cada frase. Conservar espacios iniciales, separar tokens especiales con `IsControl` y mantener unidades del tokenizador. No entregar una frase entera como un token con tiempo inventado. Persistir la transcripción normalizada, diagnóstico y modelo/idioma efectivos. Una entrada `Language="auto"` devuelve el idioma detectado; una explícita debe respetarse.

Application verifica identidad, duración lógica, modelo e idioma de cada resultado. `SubtitleCuePlanner` ejecuta las reglas Core una sola vez: cuantizar inicio/duración local, acotar, construir palabras, trasladar al destino. La alineación por palabras de la referencia usa offsets normalizados; no interpretar `t_dtw` crudo como milisegundos. No hay fallback de distribución uniforme ni correcciones editoriales hardcodeadas.

## Preparar el plan de escritura

`PrepareAsync` no modifica CapCut. Guarda un plan durable en el run y devuelve ruta/hash del plan, número de captions, archivos esperados, IDs que quedarán administrados y advertencias. El plan debe contener/copiar toda la información necesaria para validar y aplicar sin depender de objetos temporales desaparecidos.

Usar la estructura del golden v3, con recurso `7535399757947161873`, fuente `7517426090072149264` y demás constantes verificadas del paquete de contexto. La muestra manual reciente no sustituye ese golden. El JSON destino sigue siendo el documento real: preservar propiedades desconocidas, IDs, medios, canvas/FPS, pistas y textos ajenos. Generar IDs nuevos solo para objetos nuevos. Mantener los IDs administrados que correspondan al actualizar.

El grafo y el estilo completos se validan en el escritor usando los fixtures: textos, templates, recursos de texto, attachments, animations, effects y sus referencias. Distinguir IDs de objetos propios de IDs de recursos/fuentes. `ManagedSubtitleSet` solo registra identidades propias, mediante pares únicos `(Kind, LogicalKey)` y un ID exacto. Las claves lógicas deben ser deterministas y persistirse; no deducir autoría por color, nombre de plantilla o tipo del texto.

La política de esta versión conserva subtítulos ajenos. Detectar/advertir duplicados visuales al existir captions o SRT ajenos. Conversión o sustitución necesita una futura decisión explícita; no eliminar por coincidencia de estilo.

El plan incluye `draft_content.json` de la timeline y su `.bak`. Incluye contenido raíz y su `.bak` **solo si corresponde a la timeline seleccionada**. La selección no modifica el registro ni `main_timeline_id`. Con varias timelines se necesita el fixture adicional del blueprint antes de asumir cómo se mantiene el espejo raíz. Application comprueba que cada ruta/huella del plan procede de la captura, pero esa comprobación no determina por sí sola la relación del espejo: corresponde al adaptador.

## Aplicación, recibo y recuperación

Antes de modificar archivos, el escritor comprueba que CapCut está cerrado, adquiere exclusión para esa timeline, verifica el plan por su SHA-256 y revalida existencia/hashes de los documentos originales. La exclusión por run que ofrece el almacén no sustituye la exclusión por timeline del escritor: dos runs diferentes pueden apuntar al mismo proyecto.

Respaldar los bytes inmediatamente anteriores de **cada archivo existente que vaya a cambiar**, incluidos `.bak` nativos. Registrar también archivos que no existían, para eliminarlos al revertir/restaurar. Los backups propios viven fuera de CapCut, bajo la ejecución; no reutilizar los `.bak` nativos como fuente de restauración propia.

Persistir journal, plan e identidades antes de iniciar commit. Preparar los reemplazos, validar y escribir los documentos y `.bak` correspondientes con la estructura nueva. El recibo final y las identidades deben quedar durables antes de retornar, porque guardar el manifiesto en `CompleteAsync` puede fallar. No prometer atomicidad entre múltiples archivos. Ante interrupción/fallo, terminar o revertir y conservar información para reconciliar el estado al reiniciar.

`SubtitleApplyResult` contiene runId, journal y un recibo por archivo del plan. Cada `AppliedFile` registra huella anterior, hash nuevo y ruta del backup propio si existía. Application comprueba las identidades/huellas del recibo y que el hash nuevo de cada `.bak` sea el del contenido nuevo. La verificación byte a byte y los archivos físicos corresponden al escritor, no a la comprobación del DTO.

| Resultado del escritor | Comportamiento de Application |
|---|---|
| Éxito con recibo válido | `CompleteAsync` guarda recibo/IDs y confirma `Completed`, sin usar el token cancelado. |
| `CapCutOpen` o `ResourceUnavailable` antes de escribir | Registra `ReadyToApply`, conserva resultado y devuelve el diagnóstico. |
| `SourceChanged` antes de escribir | Registra `Failed`; hay que leer/generar de nuevo. |
| Cancelación pedida antes de escribir | Registra `ReadyToApply` y propaga cancelación. |
| Otro fallo desde `Applying` o recibo incoherente | Registra `RecoveryRequired`, devuelve diagnóstico y bloquea reaplicación ciega. |
| Fallo al persistir además del error original | `PersistenceFailure`, con ambas causas conservadas. |

Los tres códigos de rechazo anteriores son **garantía de que no hubo escrituras**. `OperationCanceledException` solo puede salir del escritor antes de escribir. Durante commit, diferir cancelación hasta terminar o revertir; un estado incierto necesita otra excepción y journal. No reutilizar esos códigos para errores después del primer reemplazo.

## Espacio propio y estado

`CreateRunAsync` usa la raíz propia configurable, carpeta del **draft_id del proyecto** y subcarpeta única por ejecución; registra también timelineId. El manifiesto es versionado y conserva captura, selección, rangos, overrides, opciones, fechas, artefactos, hashes y estado. Validar que este espacio queda fuera del árbol de proyectos de CapCut.

El almacén crea una ejecución íntegra en `Created`. `TransitionAsync` compara estado esperado y cambia estado de forma indivisible por run, sin sobrescribir un estado incompatible; devuelve `RunStateConflict` si no coincide. Debe aceptar cancelación antes de persistir, y retornar normalmente una vez confirmada la transición. Una ejecución `Completed` no se reaplica.

Flujo de generación: `Created → PreparingAudio → Transcribing`; preparación/transcripción se repiten para los fragmentos; al terminar, `PreparingSubtitles → ReadyToApply`. Un fallo anterior al commit termina en `Failed` o `Cancelled`. El paso de aplicación es `ReadyToApply → Applying → Completed`, con los retornos/recovery de la tabla anterior. No hacen falta un framework de estados, CQRS o MediatR.

`SaveGenerationAsync` guarda el resultado revisable sin cambiar el registro de IDs aplicados. `CompleteAsync`, desde `Applying`, guarda recibo y registro propio de la timeline y completa el run de forma recuperable. Si falla, el journal del escritor sigue siendo la fuente para reconciliar. La restauración explícita y la UI de recuperación aún requieren implementar su caso de uso; no declarar que ya funcionan por existir estos contratos.

## Pendientes que siguen abiertos

Resolver instalaciones/modelos/recursos y prueba Whisper.net DTW real; pruebas de audio originales y padding; clips de vídeo con audio; varias timelines y espejo raíz; adaptación visual horizontal de la plantilla; política de SRT ajeno; reproducción, restauración explícita y recuperación al iniciar. El blueprint y los fixtures siguen siendo la referencia para esas decisiones. No ampliar velocidades, mezcla, reversa o documentos auxiliares sin evidencia y prueba concreta.
