<p align="center">
  <img src="src/CaptionForge.Desktop/Resources/CaptionForge.png" alt="CaptionForge" width="128" />
</p>

<h1 align="center">CaptionForge</h1>

<p align="center">
  <strong>Tu audio. Tus subtítulos. La plantilla que elegiste en CapCut.</strong><br />
  Transcripción local con Whisper y aplicación de subtítulos animados sobre proyectos reales de CapCut.
</p>

<p align="center">
  <a href="https://github.com/NidroySoft/CaptionForge/actions/workflows/ci.yml"><img src="https://github.com/NidroySoft/CaptionForge/actions/workflows/ci.yml/badge.svg?branch=main" alt="Estado de compilación y pruebas" /></a>
  <img src="https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white" alt=".NET 10" />
  <img src="https://img.shields.io/badge/Windows-x64-0078D4" alt="Windows x64" />
  <img src="https://img.shields.io/badge/UI-WPF%20%2B%20MVVM-7C3AED" alt="WPF y MVVM" />
  <img src="https://img.shields.io/badge/Transcripci%C3%B3n-local%20en%20CPU-16A34A" alt="Transcripción local en CPU" />
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-Custom%20Source%20Available-CA8A04" alt="Licencia personalizada de código disponible" /></a>
</p>

<p align="center">
  <a href="#instalación">Instalación</a> ·
  <a href="#guía-de-uso">Guía de uso</a> ·
  <a href="#módulos-y-texto-a-voz">Texto a voz</a> ·
  <a href="#pruebas-y-calidad">Pruebas</a> ·
  <a href="https://github.com/NidroySoft/CaptionForge/releases">Releases</a> ·
  <a href="https://github.com/NidroySoft/CaptionForge/issues">Reportar un problema</a>
</p>

## Qué es CaptionForge

**CaptionForge es una aplicación de escritorio para Windows que genera subtítulos desde los clips de audio o vídeo de una timeline de CapCut y les aplica el formato de una plantilla ya presente en el proyecto.**

Elige la plantilla en CapCut; CaptionForge se encarga de transcribir, preparar los bloques y escribirlos en el borrador correspondiente. Conserva los recursos de esa plantilla —fuente, colores, posición, efectos y animación— y utiliza tiempos de palabras para acompañar el texto.

La transcripción se ejecuta localmente con **Whisper.net y alineación DTW en CPU**. La aplicación no necesita interpretar la salida de `whisper-cli.exe` ni enviar el audio a un servicio de transcripción.

> **Estado: MVP funcional validado.** Última ejecución de xUnit confirmada en GitHub Actions: **272 pruebas aprobadas, 0 fallidas y 2 omitidas**, de un total de 274. El badge de CI refleja el estado actual del workflow; este recuento documenta la ejecución verificada del MVP.

CaptionForge es un proyecto independiente y no está afiliado a CapCut ni a sus desarrolladores.

## Funciones principales

| Área | Qué puedes hacer |
| --- | --- |
| Proyectos | Detectar la carpeta habitual de CapCut, indicar otra ubicación, filtrar los proyectos y elegir el que quieres procesar. |
| Timelines | Seleccionar la timeline por nombre y portada, identificar la fijada y trabajar sin cambiar el pin de CapCut. |
| Clips | Incluir o excluir fragmentos individualmente, seleccionar todos, escuchar sus recortes y localizar medios movidos o renombrados. |
| Transcripción | Elegir un modelo Whisper GGML, el idioma y la cantidad de hilos de CPU. |
| Plantillas | Usar una plantilla existente en el draft y conservar sus recursos visuales y animación. |
| Resultado | Revisar todos los bloques generados y sus tiempos antes de aplicar los cambios. |
| Exportación | Exportar el resultado completo a SRT, incluso sin aplicarlo al proyecto. |
| Escritura | Sustituir los subtítulos de la pista seleccionada y conservar las demás pistas. |
| Recuperación | Guardar el estado anterior de los archivos afectados, consultar las ejecuciones y restaurar un backup válido. |
| Interfaz | Usar modo claro, oscuro o del sistema, paletas compatibles con cada modo y preferencias persistentes. |
| Idiomas | Añadir traducciones mediante JSON y descubrirlas al abrir el selector. |
| Ayuda | Consultar la guía integrada y seguir un tutorial sobre los controles reales de la aplicación. |
| Módulos | Cambiar de herramienta conservando el estado del trabajo. Los módulos se crean al abrirlos por primera vez. |
| Texto a voz | Generar, escuchar y exportar WAV locales con Kokoro, Pocket TTS, Chatterbox Nano o Chatterbox Multilingual V3. |

### Una interfaz pensada para trabajar

La aplicación usa WPF, MVVM y controles reutilizables. La ventana se adapta al espacio disponible y mantiene un tamaño mínimo de trabajo. Las portadas conservan sus proporciones tanto en proyectos verticales como horizontales.

En la pantalla de preparación, cada panel tiene su propio desplazamiento. El reproductor permanece encima de la lista de clips, accesible aunque recorras muchos fragmentos. El engranaje abre la configuración desde cualquier etapa del flujo.

## Módulos y texto a voz

El selector **Herramienta** permite alternar entre **Subtítulos de CapCut** y **Texto a voz**. El módulo de voz tiene su propia vista y configuración; su generación funciona en un proceso Python independiente. La navegación se bloquea durante una operación y cerrar CaptionForge solicita la cancelación del trabajo activo.

1. Selecciona **Texto a voz** y elige motor e idioma.
2. Para Kokoro o Pocket, elige una voz instalada. Para Chatterbox, selecciona un audio limpio de referencia de más de cinco segundos.
3. Pega el guion y pulsa **Generar voz**. Puedes cancelar la operación, reproducir el resultado y usar **Guardar WAV como…**.
4. Si cambias de ubicación los modelos, despliega **Configuración del motor seleccionado y archivos de salida** para indicar el Python, la carpeta de pesos y la salida. Guarda la configuración.

Se detectan las instalaciones del laboratorio existente bajo `%LOCALAPPDATA%\Wondecode`: Kokoro, PocketTTS, ChatterboxNano y ChatterboxMultilingual. Pocket usa su propio entorno Python. **El ejecutable no incluye ni descarga automáticamente Python, dependencias o pesos de voz**; las rutas se pueden configurar en cada equipo. La configuración se guarda en `%LOCALAPPDATA%\CaptionForge\modules\text-to-speech\settings.json` y la salida predeterminada en `%LOCALAPPDATA%\CaptionForge\Speech\Audio`.

| Motor | Idiomas expuestos | Entrada de voz | Ajustes |
| --- | --- | --- | --- |
| Kokoro | Inglés y español | Voces `.pt` locales | Velocidad y semilla |
| Pocket TTS público | Inglés y español | Voces `.safetensors` locales, sin clonación | Semilla |
| Chatterbox Nano | Inglés | Audio de referencia | Semilla; etiquetas compatibles con el motor |
| Chatterbox Multilingual V3 | Inglés y español | Audio de referencia | Expresividad y semilla |

Las generaciones se ejecutan de una en una en CPU, por fragmentos, y producen WAV PCM de 16 bits. El proceso libera el modelo al finalizar. Junto al WAV se conserva un registro de diagnóstico y los parámetros de generación. La calidad y el tiempo dependen del motor, de la voz y del equipo; Chatterbox puede tardar varios minutos en CPU. Los controles nuevos de voz están inicialmente en español.

Para añadir nuevas herramientas, consulta [la arquitectura de módulos](docs/Modules.md). Son módulos de código registrados en el proyecto; se compilan con la aplicación.

## Requisitos

| Requisito | Uso |
| --- | --- |
| Windows x64 | Plataforma de la distribución portable. |
| CapCut para Windows | Proyecto local con un draft JSON legible y una plantilla de subtítulos existente. |
| Modelo Whisper GGML `.bin` | Motor de reconocimiento local; se configura por separado. |
| FFmpeg y FFprobe | Lectura del medio y preparación del audio para Whisper. |
| Recursos de la plantilla | Fuentes y efectos referenciados por el draft disponibles en el equipo. |
| Espacio de trabajo | Audio preparado, transcripciones, resultados y backups de las ejecuciones. |

El modelo y la duración de los clips determinan buena parte del uso de memoria y del tiempo de transcripción. El MVP utiliza CPU; no requiere GPU y no ofrece aceleración GPU.

La integración se ha comprobado con muestras de proyectos y plantillas del desarrollo. No se afirma compatibilidad universal con todas las versiones o estructuras internas de CapCut. Los casos que no admite se rechazan con diagnóstico.

## Instalación

### Desde un release

1. Abre [Releases](https://github.com/NidroySoft/CaptionForge/releases) y descarga el ZIP de Windows x64 cuando haya una versión publicada.
2. Extrae **todo el ZIP** en una carpeta donde puedas mantener la aplicación y sus archivos.
3. Ejecuta `CaptionForge.exe`.
4. Abre Configuración para revisar las herramientas y la carpeta de trabajo.
5. Selecciona el modelo GGML en la pantalla de preparación.

Los releases automáticos incluyen .NET y las dependencias publicadas de la aplicación. **Los modelos Whisper y FFmpeg/FFprobe se proporcionan por separado.** La distribución es portable; no es un instalador.

### Configurar FFmpeg y FFprobe

Puedes indicar la ruta de cada ejecutable en Configuración. También puedes utilizar sus nombres si están disponibles en `PATH`.

La aplicación busca inicialmente `Tools/ffmpeg.exe` y `Tools/ffprobe.exe` junto al ejecutable; si no están, utiliza los nombres `ffmpeg` y `ffprobe`. Las rutas guardadas tienen prioridad en las siguientes sesiones.

Si los utilizas desde `PATH`, comprueba en PowerShell:

```powershell
ffmpeg -version
ffprobe -version
```

Obtén las herramientas desde [FFmpeg](https://ffmpeg.org/download.html). Para modelos GGML, consulta las [instrucciones de whisper.cpp](https://github.com/ggml-org/whisper.cpp/blob/master/models/README.md) y su [repositorio de modelos](https://huggingface.co/ggerganov/whisper.cpp/tree/main).

### Elegir un modelo y un idioma

El modelo de referencia utilizado durante las pruebas del flujo en inglés fue `ggml-medium.en.bin`. El lector de modelos reconoce las arquitecturas Tiny, Base, Small y Medium y sus variantes inglesas, además de variantes Large con presets DTW admitidos. Un archivo desconocido o con metadatos incompatibles se rechaza antes de transcribir.

- Los modelos **`.en` son sólo para inglés**. Selecciona inglés o automático; en ese modelo, automático utiliza inglés.
- Para español u otros idiomas, utiliza un **modelo multilingüe**.
- El selector de transcripción actual ofrece automático, español, inglés, portugués, francés, alemán e italiano.
- Large v1/v2 requiere una identificación inequívoca de su versión para elegir la alineación DTW correspondiente.

La sugerencia inicial es el **75 % de los hilos lógicos**, redondeado hacia abajo y con un mínimo de uno; puedes ajustarla. Seleccionar más hilos no garantiza una reducción proporcional del tiempo ni un uso constante del 100 % de CPU.

## Guía de uso

### 1. Prepara la plantilla en CapCut

Abre el proyecto y coloca un subtítulo con la plantilla que quieres utilizar. Ajusta allí su fuente, colores, posición y demás propiedades visuales. Puedes tener varias pistas con distintas plantillas.

Guarda los cambios y **cierra CapCut antes de aplicar o restaurar desde CaptionForge**.

La plantilla debe estar en la timeline que vas a procesar. CaptionForge la obtiene del draft real de ese proyecto; no usa el draft de otro proyecto como sustituto.

### 2. Selecciona el proyecto y la timeline

La carpeta habitual de proyectos es:

```text
%LOCALAPPDATA%\CapCut\User Data\Projects\com.lveditor.draft
```

Si no se encuentra o tu instalación utiliza otra ubicación, selecciona la carpeta correcta. Elige el proyecto y después la timeline. El pin indica cuál refleja el draft de la raíz.

Si acabas de modificar el proyecto en CapCut, vuelve a cargar la timeline antes de generar.

### 3. Elige los clips

Marca los fragmentos que quieres transcribir. Un audio dividido en CapCut aparece como clips independientes, aunque todos puedan apuntar al mismo archivo original.

La aplicación respeta:

- **Rango de origen:** el tramo del archivo original que corresponde al clip.
- **Rango de destino:** la posición y duración de ese clip en la timeline.

Por ejemplo, si un recorte utiliza los segundos 10–15 del archivo y comienza en el segundo 30 de la timeline, sus subtítulos se sitúan en el intervalo 30–35.

Excluir un clip sólo lo omite en esa generación: **no borra el clip en CapCut ni modifica el archivo original**. El botón de reproducción permite escuchar el recorte. Si moviste un medio, utiliza **Localizar** para indicar una ruta alternativa para esa ejecución.

En un vídeo con audio, FFmpeg prepara la pista de audio antes de transcribir. No se envía el vídeo directamente a Whisper.

### 4. Selecciona la plantilla y configura Whisper

Escoge la pista de plantilla que quieres utilizar, el modelo, el idioma de transcripción y los hilos. Si hay varios candidatos, verifica la pista seleccionada: ésa será la que se sustituya al aplicar.

Una plantilla puede tener varias capas internas. CaptionForge analiza su estructura; las que requieren una adaptación aún no admitida aparecen como no compatibles.

### 5. Genera y revisa

Pulsa **Generar subtítulos**. El flujo prepara los recortes, transcribe con Whisper, normaliza los tiempos de palabras y prepara los bloques y el plan de escritura.

Revisa el texto completo y los intervalos del resultado. El progreso cuenta clips completados: un clip largo puede tardar mientras la barra permanece en el mismo punto.

**Generar prepara el resultado; Aplicar escribe los cambios en CapCut.** Puedes exportar a SRT antes de aplicar. El SRT contiene los bloques y tiempos del resultado completo, pero no transporta la animación, los efectos ni las fuentes de la plantilla.

### 6. Aplica al proyecto

Con CapCut cerrado, pulsa **Aplicar**:

1. Se comprueba que el proyecto y los recursos necesarios siguen siendo válidos.
2. Se verifica que los archivos no cambiaron desde la lectura utilizada para preparar el resultado.
3. Se respalda su estado anterior.
4. Se escriben los subtítulos y se sincronizan los `.bak` afectados.
5. Se registra la ejecución y el resultado de la operación.

Si la pista contiene texto trabajado o subtítulos anteriores, se solicita confirmación para sustituirlos. Una muestra reconocida de la plantilla se sustituye directamente. Cancelar la confirmación conserva el resultado preparado y no modifica el proyecto.

Abre el proyecto en CapCut para comprobar los bloques, su sincronización y la animación.

## Cómo se integra con el draft de CapCut

### La pista seleccionada define el destino

CaptionForge utiliza el grafo de materiales y recursos de la plantilla del proyecto. Sustituye el texto, los tiempos de palabras y los rangos de los segmentos necesarios para los nuevos bloques, conservando las propiedades visuales del molde.

Los identificadores del proyecto y la timeline y las pistas ajenas a la selección se conservan. El bloque inicial reutiliza el molde seleccionado; los bloques adicionales reciben sus referencias dentro de la estructura del draft. No se reemplaza el proyecto por un JSON genérico creado en otro contexto.

Si hay dos pistas con distintas plantillas y eliges una, la otra se conserva. También puedes regenerar la seleccionada sin duplicar la pista.

### La raíz refleja la timeline fijada

En la estructura comprobada, `Timelines/project.json` identifica la timeline fijada mediante `main_timeline_id`. El draft de la raíz refleja esa timeline; no es la suma de todas las timelines.

| Timeline que procesas | Archivos de CapCut que se escriben |
| --- | --- |
| La fijada | `Timelines/<timeline-id>/draft_content.json`, su `.bak`, `draft_content.json` de la raíz y su `.bak`. |
| Otra timeline | Sólo `Timelines/<timeline-id>/draft_content.json` y su `.bak`; los dos archivos de la raíz se conservan. |

La aplicación no cambia qué timeline está fijada. Si la raíz no coincide con la timeline que debería reflejar, la escritura se detiene para que se revise el proyecto.

### Dos respaldos con funciones distintas

- **Backup de CaptionForge:** conserva los bytes del estado anterior y permite la restauración.
- **`.bak` de CapCut:** se sincroniza con el nuevo draft en los archivos afectados, para mantener consistente el par que utiliza CapCut al cargar.

El respaldo anterior permanece en la carpeta de trabajo de CaptionForge; no se utiliza el `.bak` actualizado como copia del estado previo.

## Backups y restauración

Cada ejecución tiene su propia carpeta y un registro de operación. Desde **Backups de esta timeline** puedes seleccionar una ejecución y restaurar los archivos respaldados con CapCut cerrado.

La restauración comprueba el journal, el plan y los archivos actuales. Si se detectan ediciones posteriores o una ejecución más reciente incompatible con esa restauración, se rechaza en lugar de sobrescribirlas.

El escritor dispone de recuperación ante fallos durante el commit. Si tampoco puede completar la recuperación, conserva el estado de diagnóstico en el journal. Consulta ese registro antes de intentar otra escritura.

## Archivos locales y privacidad

La transcripción del MVP es local. Los modelos y las herramientas deben estar disponibles en el equipo; descargar esas dependencias es un paso separado.

La ubicación predeterminada de trabajo es:

```text
%LOCALAPPDATA%\CaptionForge\Projects
```

La organización utiliza el ID real del proyecto: `<project-id>/runs/<run-id>`. No depende sólo del nombre visible del proyecto, que puede repetirse o cambiar.

| Ubicación | Contenido |
| --- | --- |
| `<project-id>/runs/<run-id>/run.json` | Identidad, petición y estado de la ejecución. |
| `audio/` dentro de la ejecución | Recortes WAV preparados y mapas entre el clip y el audio. |
| `transcription/` | Transcripciones y datos nativos de diagnóstico. |
| `result/` | Resultado, SRT, plan de escritura, documentos preparados y recibos. |
| `backup/` | Estado anterior de los archivos afectados al aplicar. |
| `journal.json` | Registro de aplicación y restauración. |
| `<project-id>/managed/` | Registro de subtítulos administrados por timeline y pista. |
| `%LOCALAPPDATA%\CaptionForge\Logs\desktop.log` | Diagnósticos del escritorio. |
| `%LOCALAPPDATA%\CaptionForge\settings.json` | Preferencias de proyectos, trabajo y transcripción. |
| `%LOCALAPPDATA%\CaptionForge\desktop-tools.json` | Rutas de FFmpeg y FFprobe. |
| `%LOCALAPPDATA%\CaptionForge\appearance.json` | Preferencias de apariencia. |
| `%LOCALAPPDATA%\CaptionForge\interface.json` | Idioma y seguimiento del tutorial. |

Puedes cambiar la carpeta de trabajo en Configuración. Los archivos correspondientes aparecen a medida que se ejecutan o completan sus etapas. Conserva las ejecuciones que quieras poder restaurar: borrar sus backups o journals elimina esa posibilidad.

## Temas, idiomas y ayuda

### Apariencia

El MVP incluye modo claro, oscuro y seguimiento del tema del sistema. El catálogo tiene **13 paletas**: cinco exclusivas para claro, cinco para oscuro y tres compartidas. El selector muestra las compatibles con el modo activo.

### Añadir un idioma

El español se incluye como idioma de respaldo. Para crear una traducción:

1. Copia `Languages/es.json` junto al ejecutable.
2. Nombra la copia con un código de dos letras minúsculas, por ejemplo `en.json`.
3. Cambia `code` a `en` y `name` al nombre que aparecerá en el selector, por ejemplo `English`.
4. Traduce los **valores** de `translations`, conservando las claves.
5. Mantén los marcadores de formato como `{0}` y `{1}`.
6. Abre el desplegable de idiomas en Configuración: la carpeta se vuelve a leer y el archivo válido aparece sin reiniciar.

Ejemplo de la estructura, con sólo algunas claves:

```json
{
  "code": "en",
  "name": "English",
  "translations": {
    "settings.title": "CaptionForge settings",
    "help.close": "Close"
  }
}
```

Las claves sin traducción muestran su texto en español. Los documentos inválidos, códigos duplicados y marcadores incompatibles se rechazan. El idioma de interfaz es **independiente del idioma de Whisper**. Los diagnósticos originales de los motores pueden conservar su idioma original.

### Tutorial integrado

La ayuda incluye temas sobre proyectos, audio, plantillas, generación, aplicación, restauración y configuración. El tutorial resalta controles reales y acompaña las etapas del trabajo. Puedes volver a iniciarlo desde Ayuda.

Sus pasos usan identificadores estables y un catálogo extensible, preparado para incorporar funciones futuras. El tutorial no ejecuta automáticamente transcripciones, aplicaciones ni restauraciones.

## Limitaciones actuales

El MVP se concentra en el flujo de subtítulos con plantillas existentes:

- No incluye editor de plantillas ni emulación de sus animaciones. Ajusta y comprueba la apariencia en CapCut.
- No incorpora edición manual del texto o de los tiempos desde el panel de resultados.
- No procesa clips silenciados, invertidos ni con velocidad modificada o variable.
- Procesa los clips seleccionados por separado; no reproduce ni mezcla todos los efectos de audio de la timeline como un render de CapCut.
- La vista previa depende de los códecs disponibles para el reproductor de Windows; un medio que FFmpeg puede leer puede no reproducirse allí.
- No crea un proyecto de CapCut desde cero ni reemplaza sus herramientas de edición.
- No descarga modelos automáticamente ni ofrece aceleración GPU en este MVP.

Whisper puede cometer errores de reconocimiento. Revisa el resultado y comprueba la aplicación en CapCut antes de exportar tu vídeo.

## Desarrollo

### Compilar desde el código

Necesitas Windows, Git y el **SDK de .NET 10**. Para trabajar con la interfaz desde Visual Studio, utiliza una versión compatible con .NET 10 y la carga de trabajo de desarrollo de escritorio .NET.

```powershell
git clone https://github.com/NidroySoft/CaptionForge.git
cd CaptionForge

dotnet restore src/CaptionForge.Desktop/CaptionForge.Desktop.csproj
dotnet build src/CaptionForge.Desktop/CaptionForge.Desktop.csproj --configuration Release
dotnet run --project src/CaptionForge.Desktop/CaptionForge.Desktop.csproj
```

Seleccionar los proyectos explícitamente evita depender de si la solución usa `.sln` o `.slnx`.

### Estructura y responsabilidades

| Ruta | Responsabilidad |
| --- | --- |
| `src/CaptionForge.Core` | Modelos de dominio, rangos temporales, palabras y reglas de subtítulos. |
| `src/CaptionForge.Application` | Casos de uso, contratos, planificación y coordinación de generación y aplicación. |
| `src/CaptionForge.Infrastructure` | Catálogo de CapCut, lectura y escritura JSON, FFmpeg, transcripción y almacenamiento. |
| `src/CaptionForge.WhisperCompat` | Integración compatible de Whisper.net utilizada para conservar la entrega final de datos DTW. |
| `src/CaptionForge.Desktop` | Aplicación WPF, MVVM, controles, reproducción, preferencias, idiomas y tutorial. |
| `tests/CaptionForge.Tests` | Pruebas xUnit del dominio, aplicación, infraestructura e integración. |
| `tests/CaptionForge.Tests/Fixtures` | Muestras completas de Whisper y CapCut utilizadas por las pruebas. |
| `checks/` | Comprobaciones adicionales de apariencia e interfaz, si están incluidas. |
| `.github/workflows` | Compilación, pruebas y releases automáticos. |
| `.github/scripts` | Validación compartida e instalación de FFmpeg en el runner. |

Core define el dominio; Application depende de Core; Infrastructure implementa los contratos; Desktop compone los servicios y presenta el flujo. La transcripción utiliza la biblioteca .NET y su runtime nativo; FFmpeg y FFprobe sí se invocan como procesos externos.

## Pruebas y calidad

### Resultado verificado del MVP

| Métrica de la ejecución confirmada | Resultado |
| --- | ---: |
| Total de pruebas xUnit | 274 |
| Aprobadas en GitHub Actions | **272** |
| Fallidas | **0** |
| Omitidas por dependencias externas | **2** |

Las dos omitidas requieren un modelo y audio de prueba o un proyecto real de CapCut. **No se presentan como pruebas aprobadas en CI.** Su ejecución local se configura por separado.

El badge superior consulta [CaptionForge CI](https://github.com/NidroySoft/CaptionForge/actions/workflows/ci.yml). Si cambia el número de casos, actualiza también esta tabla: el badge indica el estado del workflow, no calcula el recuento de pruebas.

### Qué verifica la suite

Las pruebas cubren reglas temporales y tiempos de palabras, normalización de datos Whisper, catálogo de proyectos, referencias entre materiales y segmentos, reproducción de estructuras verificadas por bloques, plantillas con varias capas, sustitución de la pista elegida, regeneración, aislamiento entre timelines, sincronización de `.bak`, backups, commit, restauración y conflictos de archivos.

Las pruebas nativas opcionales ejercitan Whisper con voz real. La integración opcional con un proyecto real de CapCut trabaja sobre una **copia temporal** y comprueba los archivos respaldados y restaurados. Esto no sustituye la revisión visual de cada plantilla en CapCut ni las pruebas de interfaz en Windows.

### Ejecutar xUnit localmente

Mantén la carpeta `Fixtures` completa, incluidos sus JSON y `.bak`. Instala FFmpeg/FFprobe y asegúrate de que están en `PATH` para las pruebas de audio.

```powershell
dotnet test tests/CaptionForge.Tests/CaptionForge.Tests.csproj --configuration Release
```

También puedes utilizar el Visor de pruebas de Visual Studio.

Para ejecutar el caso nativo de Whisper desde PowerShell, adapta estas rutas:

```powershell
$env:CAPTIONFORGE_TEST_MODEL = "C:\Modelos\ggml-medium.en.bin"
$env:CAPTIONFORGE_TEST_VOICE = "C:\Pruebas\voz-en-11s.wav"

dotnet test tests/CaptionForge.Tests/CaptionForge.Tests.csproj --configuration Release --filter "FullyQualifiedName~WhisperNativeTests"
```

Ese caso necesita un modelo GGML inglés `.en` y voz en inglés de al menos 11 segundos. Para la integración con CapCut, configura además `CAPTIONFORGE_TEST_CAPCUT_PROJECT`; si el proyecto tiene varios candidatos, puedes indicar `CAPTIONFORGE_TEST_TIMELINE` y `CAPTIONFORGE_TEST_SEGMENT`. Los recursos y medios referenciados deben existir.

Las variables de la sesión de PowerShell se aplican a `dotnet test` iniciado desde ella. Para ejecutar esos casos en el Visor de Visual Studio, configura las variables en un archivo `.runsettings` seleccionado por el IDE.

### Integración continua

[CI](.github/workflows/ci.yml) se ejecuta en Windows con .NET 10:

1. Obtiene el código y restaura las dependencias.
2. Instala FFmpeg/FFprobe para las pruebas.
3. Compila Desktop y ejecuta xUnit.
4. Ejecuta las comprobaciones adicionales si sus proyectos están presentes.
5. Conserva resultados TRX y cobertura Cobertura durante 14 días, incluso si las pruebas fallan.

La cobertura se guarda como artifact de Actions; este README no muestra un porcentaje sin un informe verificado. Los casos externos se omiten en el runner alojado porque no tiene los archivos personales necesarios.

## Releases automáticos

El [workflow de release](.github/workflows/release.yml) se activa al subir una etiqueta `vX.Y.Z`. Repite la compilación y las pruebas antes de publicar: si una comprobación falla, no se ejecuta la creación del release.

Desde el commit que quieras distribuir, después de subir sus cambios:

```powershell
git tag -a v0.1.1 -m "CaptionForge MVP v0.1.1"
git push origin v0.1.1
```

El workflow adjunta el ZIP portable de Windows x64 y su checksum SHA-256, e incluye la etiqueta y el commit en `VERSION.txt`. Una etiqueta como `v0.1.0-beta.1` crea un prerelease. Utiliza una etiqueta nueva para cada versión: el workflow no sobrescribe releases existentes.

## Solución de problemas

| Situación | Qué revisar |
| --- | --- |
| No aparecen proyectos | La carpeta configurada debe contener proyectos locales válidos de CapCut. |
| No aparece la plantilla | Añádela a la timeline en CapCut, guarda y vuelve a cargarla en CaptionForge. |
| Se indica que faltan medios | El archivo original pudo moverse o renombrarse; usa Localizar en el clip afectado. |
| La plantilla aparece no compatible | Consulta su diagnóstico; su reparto de capas o estructura puede requerir una adaptación específica. |
| Faltan fuentes o efectos | Comprueba que los recursos de la plantilla siguen disponibles en la instalación de CapCut. |
| FFmpeg o FFprobe no se encuentra | Revisa su ruta en Configuración o su disponibilidad en `PATH`. |
| El audio no se reproduce | Revisa el diagnóstico del reproductor y los códecs de Windows. |
| No puede aplicar con CapCut abierto | Cierra CapCut y vuelve a aplicar el resultado preparado. |
| El proyecto cambió después de generar | Vuelve a cargar y generar sobre el estado actual; el plan anterior ya no corresponde. |
| Un backup no se puede restaurar | Consulta el journal: puede haber ediciones posteriores o una ejecución más reciente. |
| La generación tarda sin avanzar la barra | El progreso registra clips completados; el clip actual puede seguir transcribiéndose. |
| El idioma añadido no aparece | Revisa el código, el nombre de archivo, la estructura JSON y los marcadores de formato. |
| CI falla por archivos ausentes | Incluye todos los fixtures y comprueba que `.gitignore` no excluya sus `.bak`. |

## Reportar problemas y proponer mejoras

Utiliza [Issues](https://github.com/NidroySoft/CaptionForge/issues). Para un error, incluye:

- Versión o commit de CaptionForge y versión de CapCut.
- Modelo GGML e idioma seleccionados, si el problema afecta a la transcripción.
- Pasos para reproducirlo y resultado esperado frente al observado.
- Mensaje completo y, cuando corresponda, extracto del log o journal.
- Una muestra mínima del draft o de los datos de transcripción si es necesaria.

Los drafts y los logs pueden contener rutas, nombres de archivos y textos de tu proyecto. Revisa esos datos antes de publicarlos. Una muestra mínima ayuda a reproducir el problema sin subir todos tus medios.

Para cambios de código, describe el caso que resuelven y su validación. Las modificaciones del escritor deben comprobar especialmente la pista seleccionada, las referencias de materiales, la timeline fijada y la restauración. Las traducciones pueden aportarse mediante archivos JSON siguiendo el formato de `es.json`.

## Posibles siguientes pasos

El alcance futuro se discutirá a partir del uso del MVP. Entre las líneas consideradas están ampliar la compatibilidad con estructuras de plantillas, incorporar más traducciones, ofrecer una vista previa de animaciones y explorar un módulo opcional de énfasis de palabras.

Estas posibilidades no están implementadas ni tienen una fecha de entrega comprometida. El objetivo actual es mantener fiable el flujo de generación, aplicación y recuperación.

## Licencia

El material original de CaptionForge se distribuye bajo **[CaptionForge Source-Available License 1.0](LICENSE)**, una licencia personalizada de código disponible con uso comercial permitido y prohibición de venta del programa.

| Actividad | Condición |
| --- | --- |
| Usarla para trabajo profesional, vídeos monetizados o encargos para clientes | Permitido. |
| Cobrar por vídeos, subtítulos o trabajos realizados con la herramienta | Permitido. |
| Modificarla y compartir copias de la aplicación | Permitido gratuitamente, conservando la licencia y atribución. |
| Renombrar una versión modificada | Permitido, manteniendo el reconocimiento del creador original. |
| Vender copias, licencias, activaciones o versiones modificadas del programa | Prohibido. |
| Incluirla como parte de un paquete cuya compra sea obligatoria para recibirla | Prohibido. |
| Ofrecer soporte, formación o asistencia de instalación por separado | Permitido si obtener y usar el programa no exige contratar esos servicios. |

**Autor original: Yordin Isaac Garcia (NidroySoft).** Las distribuciones deben conservar el crédito del archivo [NOTICE](NOTICE). No se exige añadirlo a los vídeos o subtítulos producidos.

Esta licencia no cumple la definición de *open source* de OSI por su restricción de venta. La descripción del proyecto es **source available**; la publicación del código permite estudiarlo y colaborar bajo las condiciones indicadas.

Las dependencias conservan sus licencias. En particular, el código de Whisper.net en `CaptionForge.WhisperCompat` mantiene MIT. Consulta [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). El texto completo de `LICENSE` prevalece sobre este resumen.

## Créditos

Creado por **Yordin Isaac Garcia · NidroySoft**.

CaptionForge utiliza o integra tecnologías de estos proyectos:

- [.NET y WPF](https://github.com/dotnet/wpf): aplicación de escritorio y plataforma C#.
- [Whisper](https://github.com/openai/whisper): reconocimiento de voz.
- [whisper.cpp](https://github.com/ggml-org/whisper.cpp): motor nativo y formato de modelos utilizado.
- [Whisper.net](https://github.com/sandrohanea/whisper.net): integración .NET del motor.
- [FFmpeg](https://ffmpeg.org/): preparación y lectura de audio y vídeo.
- [xUnit](https://xunit.net/) y [Coverlet](https://github.com/coverlet-coverage/coverlet): pruebas y cobertura.
- [GitHub Actions](https://github.com/features/actions): automatización de compilación, pruebas y releases.

Las licencias de esas dependencias corresponden a sus respectivos proyectos. CapCut y las demás marcas mencionadas pertenecen a sus respectivos titulares.
