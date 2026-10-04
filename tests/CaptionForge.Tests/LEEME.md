# CaptionForge.Tests — pruebas xUnit v1

Pruebas para los archivos **Core v1 + Application v1 + Infrastructure v1** entregados en esta conversación. Se instalan dentro del proyecto xUnit existente. No se incluye ni se reemplaza tu `.csproj`, solución o código de producción.

## 1. Instalación

Extrae **el contenido** del ZIP directamente en:

```text
C:\Proyectos\CaptionForge\tests\CaptionForge.Tests
```

Deben quedar allí `Core`, `Application`, `Infrastructure`, `Integration`, `Support`, `Fixtures` y este LEEME. Conserva tu `CaptionForge.Tests.csproj`. Puedes eliminar `UnitTest1.cs` si sigue siendo el ejemplo vacío de la plantilla; no elimines pruebas propias.

Tu proyecto ya tiene las referencias a las tres capas. Infrastructure necesita los paquetes Whisper.net y Whisper.net.Runtime **1.9.1**, indicados en su entrega.

Las pruebas se han compilado con las versiones generadas por la plantilla xUnit del SDK .NET 10.0.401: xunit 2.9.3, xunit.runner.visualstudio 3.1.4, Microsoft.NET.Test.Sdk 17.14.1, coverlet.collector 6.0.4. Si tu plantilla tiene versiones distintas o faltan paquetes, puedes fijarlas con estos comandos desde la raíz de la solución:

```powershell
dotnet add .\tests\CaptionForge.Tests\CaptionForge.Tests.csproj package xunit --version 2.9.3
dotnet add .\tests\CaptionForge.Tests\CaptionForge.Tests.csproj package xunit.runner.visualstudio --version 3.1.4
dotnet add .\tests\CaptionForge.Tests\CaptionForge.Tests.csproj package Microsoft.NET.Test.Sdk --version 17.14.1
dotnet add .\tests\CaptionForge.Tests\CaptionForge.Tests.csproj package coverlet.collector --version 6.0.4
```

No necesitas un proyecto de consola ni añadir otro proyecto a la solución.

## 2. Primera ejecución

Desde `C:\Proyectos\CaptionForge`:

```powershell
dotnet test .\tests\CaptionForge.Tests\CaptionForge.Tests.csproj -c Release --logger "console;verbosity=normal"
```

Las pruebas de archivos trabajan en carpetas nuevas bajo `%TEMP%\CaptionForge.Tests` y las eliminan al terminar. No buscan ni modifican tus proyectos reales por defecto. Los fixtures incluidos son copias del JSON y las transcripciones que usamos como referencia.

Las pruebas de FFmpeg se ejecutan si `ffmpeg.exe` y `ffprobe.exe` están en PATH. Si faltan, aparecen como **Skipped**, con el motivo. Whisper nativo y el proyecto real también se omiten cuando no has configurado sus recursos. Una prueba omitida no demuestra que esa integración funcione.

La localización de `Fixtures` busca desde el directorio de ejecución hacia el proyecto/solución. Extrae siempre esa carpeta. Si un runner utiliza una ubicación distinta, configura su ruta explícita:

```powershell
$env:CAPTIONFORGE_TEST_FIXTURES = 'C:\Proyectos\CaptionForge\tests\CaptionForge.Tests\Fixtures'
```

## 3. FFmpeg y Whisper reales

Comprueba primero:

```powershell
ffmpeg -version
ffprobe -version
```

La prueba nativa de referencia requiere un modelo **GGML en inglés (.en)** y un archivo con **voz en inglés de 11 segundos o más**. No se distribuyen modelos ni audios descargados en este ZIP. El modelo tiny.en sirve para comprobar rápidamente la integración; tus fixtures siguen validando los tiempos de referencia del modelo medium.en.

```powershell
$env:CAPTIONFORGE_TEST_MODEL = 'C:\Herramientas\whisper\models\ggml-tiny.en.bin'
$env:CAPTIONFORGE_TEST_VOICE = 'C:\Pruebas\voz_ingles.wav'
dotnet test .\tests\CaptionForge.Tests\CaptionForge.Tests.csproj -c Release --filter 'FullyQualifiedName~WhisperNativeTests' --logger "console;verbosity=normal"
```

La prueba prepara los primeros 11 segundos, los sitúa en la timeline a partir del segundo 20, usa Whisper.net con CPU y DTW, aplica el JSON en una copia temporal y comprueba `auto` con el mismo modelo .en. No compara una frase concreta: exige captions, rangos válidos y coherencia del pipeline. Para que detecte correctamente el recurso, define las variables antes de abrir el runner o ejecuta el comando de PowerShell.

## 4. Tu proyecto real, sobre una copia temporal

Prueba opcional para detectar las particularidades de tu instalación: lee los JSON de una carpeta que tú indiques, copia los documentos necesarios a una carpeta temporal, utiliza los medios y recursos reales **en lectura**, genera, aplica a la copia y restaura. Finalmente comprueba que los documentos originales copiados no cambiaron. **Nunca aplica ni restaura sobre el proyecto original.**

Cierra CapCut: esta prueba usa su guardia de procesos real. El proyecto debe tener el esquema soportado (version 360000), un fragmento de audio/vídeo compatible, su medio disponible y voz reconocible. Usa un proyecto sencillo; un silencio completo o un recorte/velocidad aún no soportado produce un fallo explicativo.

```powershell
$env:CAPTIONFORGE_TEST_MODEL = 'C:\Herramientas\whisper\models\ggml-medium.en.bin'
$env:CAPTIONFORGE_TEST_CAPCUT_PROJECT = 'C:\Users\yordi\AppData\Local\CapCut\User Data\Projects\com.lveditor.draft\1003'
$env:CAPTIONFORGE_TEST_TIMELINE = 'EFA8ACC8-F181-4059-96C5-6BC82353A0C6'
$env:CAPTIONFORGE_TEST_SEGMENT = 'AD298FE0-856F-4cef-BD60-839FC84C820B'
$env:CAPTIONFORGE_TEST_LANGUAGE = 'en'
dotnet test .\tests\CaptionForge.Tests\CaptionForge.Tests.csproj -c Release --filter 'FullyQualifiedName~CapCutRealProjectTests' --logger "console;verbosity=normal"
```

Si hay una sola timeline o un solo fragmento, puedes omitir sus variables. Si hay varios, debes indicar cuál quieres comprobar: la prueba no escoge por ti. Los IDs del ejemplo pertenecen al proyecto 1003 que compartiste; para otro proyecto cámbialos.

La caché se busca en `%LOCALAPPDATA%\CapCut\User Data`. Si tu instalación está en otro lugar:

```powershell
$env:CAPTIONFORGE_TEST_CAPCUT_USER_DATA = 'D:\CapCut\User Data'
```

Esta prueba requiere los recursos de la plantilla **7535399757947161873** y la fuente **7517426090072149264** (Bebas Neue), además de efecto/animación. Aplica/descarga la plantilla v3 en CapCut para que existan. El modelo .en debe usarse con `en` o `auto`; un audio en español necesita un modelo multilingüe y `es`/`auto`.

Para desactivar la prueba del proyecto real después de usarla:

```powershell
Remove-Item Env:\CAPTIONFORGE_TEST_CAPCUT_PROJECT -ErrorAction SilentlyContinue
```

La copia temporal se elimina. El test confirma la integración de archivos y motor; no abre CapCut ni certifica el aspecto visual de la plantilla.

## 5. Filtrar pruebas y recoger el resultado

```powershell
# Core y Application, sin dependencias de audio externas
dotnet test .\tests\CaptionForge.Tests\CaptionForge.Tests.csproj -c Release --filter 'FullyQualifiedName~CaptionForge.Tests.Core|FullyQualifiedName~CaptionForge.Tests.Application'

# Toda la suite con informe TRX y cobertura (si instalaste coverlet.collector)
dotnet test .\tests\CaptionForge.Tests\CaptionForge.Tests.csproj -c Release --logger trx --collect 'XPlat Code Coverage'
```

En Visual Studio puedes ejecutarlas desde Test Explorer. El número de casos cambia si un Theory se omite por falta de FFmpeg: xUnit puede representar la teoría omitida como un único caso. Consulta `VERIFICACION.json` para los resultados obtenidos aquí y `CASOS_Y_LIMITES.md` para el alcance.

Si algo falla en tu Windows, envíame el nombre del test, su mensaje y el resumen de ejecución. No es necesario pegar todo el TRX.
