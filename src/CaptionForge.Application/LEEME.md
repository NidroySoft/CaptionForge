# CaptionForge.Application — v1

## Añadir a la solución existente

1. Extrae **el contenido de este ZIP** directamente en:

   `C:\Proyectos\CaptionForge\src\CaptionForge.Application`

2. Las carpetas `Abstractions`, `Models`, `Services`, `Enums`, `Exceptions` e `Internal` deben quedar junto a tu `CaptionForge.Application.csproj`. No añadas otra carpeta intermedia.
3. Conserva tu `.csproj` actual: `net10.0`, `ImplicitUsings=enable`, `Nullable=enable` y la referencia a `CaptionForge.Core` que ya creaste. **No hacen falta paquetes NuGet ni modificar Core v1**.
4. Compila desde `C:\Proyectos\CaptionForge`:

   ```powershell
   dotnet build .\src\CaptionForge.Application\CaptionForge.Application.csproj -c Release
   ```

Los proyectos SDK incluyen automáticamente los `.cs`. Tu `Class1.cs` puede permanecer; no interviene. El ZIP no contiene otro `.csproj` de Application ni reemplaza la solución. Incluye **33 archivos C# de producción**, documentación, el informe de comprobaciones y una suite opcional empaquetada por separado.

## Qué está implementado

| Parte | Responsabilidad |
|---|---|
| `CaptionGenerationService` | Coordina preparación de WAV, transcripción, construcción de captions, preparación del plan, persistencia y aplicación explícita. |
| `SubtitleCuePlanner` | Reutiliza las reglas de Core: cuantiza una vez, limita al fragmento, construye palabras locales y coloca el caption en la timeline. |
| `SrtFormatter` | Produce SRT ordenado, con tiempos globales y CRLF; no escribe archivos. |
| `CpuThreadRecommendation` | Sugiere floor(75 %), mínimo un hilo, y revalida la preferencia cuando cambia el equipo. |
| Modelos y errores | Entradas y resultados inmutables, huellas SHA-256, opciones, ejecución, IDs propios, plan y recibo. |
| Seis interfaces | Definen los adaptadores de catálogo, FFmpeg, Whisper.net, escritor CapCut, espacio propio y ajustes. |

La coordinación es código ejecutable. Los adaptadores concretos se implementarán en **CaptionForge.Infrastructure**: esta biblioteca por sí sola todavía no descubre carpetas, extrae audio, ejecuta Whisper ni modifica un JSON de CapCut. No depende de WPF, FFmpeg, Whisper.net, un contenedor DI o una biblioteca de JSON.

## Flujo previsto

1. El frontend usa `ICapCutCatalog` para listar proyectos y timelines, y obtiene una `TimelineSnapshot` coherente de la seleccionada.
2. Construye `GenerateCaptionsRequest` con IDs incluidos, opciones de modelo/idioma/hilos, raíz propia y rutas alternativas opcionales.
3. Llama a `GenerateAsync`. Application valida antes de crear la ejecución, ordena los fragmentos por destino y los procesa secuencialmente. Cada adaptador guarda sus artefactos dentro del run.
4. El resultado contiene captions, transcripciones y plan durable. Queda en `ReadyToApply`. **Generar no aplica el plan en CapCut.**
5. El frontend presenta resultado y advertencias. Cuando el usuario decide aplicar, llama a `ApplyAsync(result)`.
6. El escritor revalida originales y plan, verifica que CapCut esté cerrado, crea backups propios y realiza el commit recuperable. Application comprueba el recibo y pide guardar el estado `Completed`.

Ejemplo de coordinación cuando existan los adaptadores:

```csharp
var service = new CaptionGenerationService(audio, transcription, writer, workspace);
var options = new TranscriptionOptions(
    modelName: "medium.en",
    modelPath: modelPath,
    language: "en",
    cpuThreads: CpuThreadRecommendation.Suggest(logicalProcessorCount),
    logicalProcessorCount: logicalProcessorCount);

var request = new GenerateCaptionsRequest(snapshot, selectedIds, options, workspaceRoot);
var result = await service.GenerateAsync(request, progress, cancellationToken);

// El frontend muestra result.Captions y result.Plan.Warnings.
// Esta segunda llamada corresponde a la acción explícita de aplicar.
var applied = await service.ApplyAsync(result, cancellationToken);
string srt = SrtFormatter.Format(result.Captions);
```

Los nombres `audio`, `writer`, etc. representan las futuras implementaciones de las interfaces; el ejemplo no es un programa completo. `medium.en` reproduce la referencia inglesa y **no establece el modelo definitivo para todos los idiomas**. `auto` permite detectar el idioma; DTW se exige en este bloque para conservar el contrato de alineación v3.

## Reglas incluidas

- Los tres IDs siguen separados: proyecto `draft_id`, registro `project.id` y timeline `id`.
- Solo se procesan IDs presentes en la captura. Excluir un fragmento no modifica CapCut ni desplaza los posteriores.
- Las rutas alternativas se usan en esta ejecución; no cambian el medio del proyecto original.
- El WAV parte de `SourceRange`, tiene origen local cero y duración lógica de `TargetRange`. Los captions suman **el inicio de destino**, sin sumar el recorte de origen ni cerrar huecos.
- El audio preparado distingue duración lógica y duración medida del WAV. Se admite hasta una muestra de diferencia a 16 kHz (63 µs); el adaptador debe recortar o rellenar cuando haga falta.
- Se comprueba que audio y transcripción correspondan al mismo fragmento, duración y opciones de modelo/idioma.
- Las frases menores que un frame, fuera del fragmento o con solo tokens de control se omiten. Las alineaciones inválidas fallan con diagnóstico; no se inventan tiempos de palabras.
- El molde requerido es `7535399757947161873` y la fuente `7517426090072149264`, los de la v3 correcta.
- La política inicial conserva subtítulos ajenos y pasa al escritor los IDs propios registrados para actualizar sus objetos. La conversión/reemplazo de SRT ajeno queda pendiente de la decisión del producto.
- Un plan debe incluir el contenido seleccionado y su `.bak`. El recibo debe incluir todos los archivos del plan y demostrar que cada `.bak` nuevo coincide con su `draft_content.json` nuevo.

Como política conservadora del MVP, se rechazan antes de trabajar los fragmentos silenciados, invertidos, con velocidad distinta de 1 o curva de velocidad; también una selección con fragmentos solapados o fuera de la duración de timeline. Son diagnósticos para corregir la selección, no soporte simulado de mezcla/velocidades. La reproducción de medios y sus controles pertenecen al futuro frontend/adaptador de reproducción.

## Estados, errores y cancelación

`GenerateAsync` llega a `ReadyToApply`, `Failed` o `Cancelled`. La transición persistida usa un estado esperado: el almacén debe comprobarlo y cambiarlo de forma indivisible. Así, dos llamadas a `ApplyAsync` para el mismo run no pueden entrar simultáneamente al escritor ni reaplicar un resultado ya completado.

Los rechazos del escritor **antes de escribir** por CapCut abierto o recursos ausentes devuelven el run a `ReadyToApply`; se puede corregir el motivo y aplicar después. Si cambiaron los originales, queda `Failed` y hay que volver a generar desde una lectura nueva. Un fallo de commit, recibo incoherente o fallo del manifiesto después de escribir deja `RecoveryRequired`. Ese estado no permite reintentar a ciegas.

La cancelación preparando/transcribiendo se persiste sin reutilizar el token cancelado. El escritor solo puede aceptar cancelación antes de commit: una vez iniciada la escritura, debe terminar o revertir coherentemente y conservar el journal. Una cancelación que llega después de un commit exitoso no evita guardar `Completed`.

Si falla la operación y también falla guardar el error, `PersistenceFailure` conserva ambas causas. `CaptionForgeOperationException` expone código y runId cuando existe. Los errores de una notificación de progreso no rompen el procesamiento. Las operaciones usan `ConfigureAwait(false)`; el frontend debe recibir el progreso en su contexto de UI.

## Verificación entregada

Compilación Release con SDK **.NET 10.0.401** y advertencias tratadas como errores: **0 errores, 0 advertencias**. Se ejecutaron **199 comprobaciones C#**, incluidas pruebas con adaptadores controlados del flujo, fallos, concurrencia y cancelación. La reconstrucción DTW obtuvo **15/15 rangos y arrays de palabras exactos** frente al fixture v3.

En la comparación se aplican únicamente en los datos del test las dos correcciones editoriales conocidas («code words» y «Not magic, math»). Application/Core no las incluyen como reglas del producto. Este resultado demuestra la reconstrucción temporal; no afirma que ya exista un escritor JSON v3 concreto.

La verificación se ejecutó en Linux con Application real y Core v1. No se ejecutaron CapCut, FFmpeg, Whisper.net ni WPF. El informe completo, con cada comprobación, está en `VERIFICACION.json`. Las obligaciones del siguiente proyecto están en `CONTRATOS_INFRASTRUCTURE.md`.

### Repetir las comprobaciones, opcional

Dentro de `Verificacion` hay un segundo ZIP con el proyecto de consola, el código completo de pruebas y los dos fixtures necesarios. Está empaquetado para que **sus `.cs` no entren en la compilación de Application**.

Extrae ese ZIP en `C:\Proyectos\CaptionForge`, la raíz de tu solución. Añadirá `tests\CaptionForge.Application.Verification` sin modificar tu proyecto xUnit actual ni la solución. Luego ejecuta:

```powershell
dotnet run --project .\tests\CaptionForge.Application.Verification\CaptionForge.Application.Verification.csproj -c Release -- .\tests\CaptionForge.Application.Verification\Fixtures .\tests\CaptionForge.Application.Verification\VERIFICACION_local.json
```

Ese proyecto no requiere paquetes NuGet; referencia tu Application existente. Si alguna comprobación falla, termina con error. Si pasa, muestra la cantidad y guarda un informe local. No toca proyectos de CapCut: sus puertos son falsos controlados.
