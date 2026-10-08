# Módulos independientes de CaptionForge

CaptionForge conoce únicamente el contrato común y su herramienta integrada de subtítulos. Las herramientas opcionales se descubren en `Modules/<Nombre>/module.json`. El anfitrión no referencia sus proyectos ni sus motores, dependencias, configuración o reproductor.

## Distribuir o quitar un módulo

1. Publica el proyecto del módulo en una carpeta independiente.
2. Copia la carpeta completa bajo `Modules`, junto al ejecutable del anfitrión.
3. Al abrir CaptionForge aparecerá el nombre declarado en el manifest.
4. Para quitarlo, cierra el programa y retira su carpeta. Los subtítulos siguen funcionando. Sus preferencias y modelos locales se conservan por separado.

`module.json` contiene `id`, `displayName`, `assembly`, `entryType` y `contractVersion: 1`. El ensamblado debe permanecer dentro de su carpeta. El tipo de entrada implementa `IApplicationModule` y ofrece una vista WPF. Los errores del manifest se registran sin impedir abrir el programa; errores al crear una vista se muestran al seleccionar esa herramienta.

Los módulos son código local de confianza y tienen los permisos del proceso. El contrato no proporciona una caja de aislamiento de seguridad. Las dependencias administradas se resuelven desde las carpetas de los módulos, compartiendo el contrato y el runtime de WPF. Si dos módulos requieren versiones incompatibles de una biblioteca, su backend debe ejecutarse en otro proceso.

## Responsabilidades

- `CaptionForge.Modularity`: contrato, registro y descubrimiento genérico de módulos. No conoce texto a voz.
- `CaptionForge.Desktop`: muestra pestañas dentro de la misma ventana, conserva una sola instancia por módulo y solicita cierre. Se puede cambiar de pestaña durante una operación; la pestaña que está trabajando no se puede cerrar hasta terminar o cancelar.
- `Desktop/Modules/SubtitleModule`: adapta la herramienta integrada de subtítulos.
- `CaptionForge.Modules.TextToSpeech`: pantalla, preferencias, reproducción y forma de onda.
- `CaptionForge.Modules.TextToSpeech.Core`: motores, validaciones, proceso Python, instalación privada y lectura de WAV. No depende del núcleo, Application o Infrastructure de CaptionForge.
- `Modules.TextToSpeech/Backend`: generación Python, instalación de dependencias y descarga de modelos oficiales.

El registro crea las herramientas al seleccionarlas por primera vez. `Deactivate()` detiene reproducción; `ShutdownAsync()` cancela y espera la tarea activa. La vista notifica sus cambios de estado mediante `INotifyPropertyChanged`. Los recursos visuales compartidos del anfitrión permiten conservar colores, fuentes y controles, mientras cada módulo controla su interfaz.

## Crear una herramienta

Crea un proyecto WPF con referencia al contrato `CaptionForge.Modularity`, implementa `IApplicationModule` y añade el manifest. Mantén la lógica y dependencias específicas dentro del módulo. No añadas una referencia ni un registro específico en Desktop. Los backends y recursos usan `CopyToOutputDirectory` y `CopyToPublishDirectory`; resuelve su ubicación desde el ensamblado del módulo, no desde el ejecutable anfitrión.

El script de validación encuentra los proyectos con `module.json`, los publica en `artifacts/modules/<Nombre>` y después construye Desktop. El anfitrión copia esas carpetas de forma genérica en Build y Publish. El módulo de voz se puede entregar como carpeta o ZIP con `TextToSpeech` como carpeta raíz. No se incluye el runtime Python ni los pesos en ese paquete.

## Instalación de motores de voz

El botón **Instalar motor** prepara un Python 3.11 portable de Windows x64 procedente de Astral, verifica SHA256 y crea un entorno virtual privado por motor. Instala dependencias CPU y pesos oficiales de Hugging Face. No modifica PATH, el registro ni el Python del equipo, y no requiere que el usuario instale Python o Git.

Los entornos y modelos quedan bajo `%LOCALAPPDATA%\CaptionForge\modules\text-to-speech\engines`. La descarga puede ocupar varios GB según el motor. El progreso muestra etapas y archivos; Cancelar detiene el árbol del proceso. Una instalación incompleta se puede reintentar: se reutilizan archivos de modelo ya verificados y no se marca el motor como instalado hasta comprobar sus imports.

**Detectar instalaciones** reutiliza también los entornos existentes del laboratorio bajo `%LOCALAPPDATA%\Wondecode`. Las rutas avanzadas permiten importar otra instalación. Las voces se descubren de los archivos locales para el idioma seleccionado y la pantalla informa cuántas encontró o qué falta.

Fuentes: [Python Build Standalone](https://github.com/astral-sh/python-build-standalone), [Kokoro](https://github.com/hexgrad/kokoro), [Pocket TTS](https://github.com/kyutai-labs/pocket-tts), [Chatterbox](https://github.com/resemble-ai/chatterbox). Cada dependencia, modelo y voz conserva sus términos propios.

## Generación y reproductor

El backend CPU trabaja en un proceso supervisado con peticiones JSON y eventos de progreso. Los WAV se guardan con nombres únicos junto con un registro y parámetros de generación. Cancelar termina el árbol de Python; los pesos se cargan localmente y el modelo se libera al salir del proceso.

La vista calcula barras de amplitud del WAV PCM16 sin cargar el modelo. Permite reproducir, pausar, detener, avanzar con la barra deslizante o pulsar y arrastrar sobre la forma de onda. Las flechas mueven cinco segundos cuando la forma de onda tiene el foco. El audio se puede guardar en otra carpeta sin cambiar el original.

## Comprobaciones

```powershell
dotnet publish src/CaptionForge.Modules.TextToSpeech/CaptionForge.Modules.TextToSpeech.csproj --configuration Release --output artifacts/modules/TextToSpeech
dotnet test tests/CaptionForge.Tests/CaptionForge.Tests.csproj
dotnet run --project checks/CaptionForge.ModuleChecks/CaptionForge.ModuleChecks.csproj --configuration Release -- artifacts/module-checks
```

Las comprobaciones WPF validan carga, navegación, contexto de subtítulos, contenido del selector de voces y renderizado. Las pruebas xUnit cubren manifiestos inválidos, rutas fuera de la instalación, lectura de WAV, errores y cancelación de Python. Las comprobaciones de reproducción nativa son adicionales a las de renderizado.

`--real-speech` genera muestras reales en inglés y español con Kokoro y Pocket instalados. `--install-kokoro` prueba el recorrido de instalación privada y una generación real; descarga dependencias y modelos. No se ejecuta en CI por defecto. `--no-modules` comprueba el anfitrión en una copia sin carpeta Modules.
