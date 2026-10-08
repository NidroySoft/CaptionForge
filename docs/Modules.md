# Módulos de CaptionForge

La separación previa Core / Application / Infrastructure se conserva. La ventana de escritorio actúa como anfitrión de herramientas y el flujo de subtítulos se integra mediante un adaptador para conservar el trabajo existente.

## Responsabilidades

- `CaptionForge.Modularity`: contrato `IApplicationModule`, definición y registro, sin dependencia de WPF ni del backend.
- `CaptionForge.Desktop`: registra las herramientas en `MainWindow.InitializeModules`, muestra su `FrameworkElement` y controla navegación y cierre.
- `Desktop/Modules/SubtitleModule`: adapta el flujo de CapCut existente.
- `CaptionForge.Modules.TextToSpeech`: vista WPF, estado, preferencias y reproducción de voz. No depende de Desktop.
- `Application/Models/Speech` y `ISpeechSynthesisService`: petición, resultado, progreso y validaciones compartidas.
- `Infrastructure/Speech/PythonSpeechSynthesisService`: ejecuta y supervisa el backend CPU por JSON y eventos, sin shell.
- `Modules.TextToSpeech/Backend`: implementación Python y configuraciones locales de Pocket.

El registro crea cada módulo al abrirlo por primera vez y reutiliza esa instancia al volver. `IsBusy` impide cambiar de herramienta durante una tarea. `Deactivate()` detiene recursos de la vista, como la reproducción. `ShutdownAsync()` cancela y espera el trabajo antes de cerrar; el registro intenta cerrar todos los módulos cargados aunque uno falle.

## Añadir una herramienta

1. Crea un proyecto `CaptionForge.Modules.Nombre` con referencia a Modularity. Si usa WPF, el destino es `net10.0-windows` y `UseWPF=true`.
2. Implementa `IApplicationModule`. `View` entrega un `FrameworkElement` cuya `DataContext` notifica cambios mediante `INotifyPropertyChanged`, incluido el estado ocupado. Conserva sus datos en el módulo.
3. Usa Application para contratos y modelos de operaciones compartidas; coloca acceso a disco, procesos o servicios en Infrastructure o en un backend propio. Evita referencias de un módulo a Desktop u otro módulo.
4. Añade la referencia al proyecto anfitrión y una definición al registro de `MainWindow.InitializeModules`. Usa un identificador único y un nombre visible.
5. Copia los archivos de backend al output y al publish mediante `Content` con `CopyToOutputDirectory` y `CopyToPublishDirectory`; conserva rutas bajo `Modules/Nombre`.
6. Prueba carga, cambio de herramienta, estado conservado, error, cancelación y cierre. Añade documentación y avisos de terceros si corresponde.

Este mecanismo usa registro explícito y compilación. No ejecuta DLL arbitrarias ni introduce un instalador de plugins; permite agregar funciones con límites claros sin ampliar el ViewModel de subtítulos.

## Backend de voz

El servicio .NET crea un directorio temporal exclusivo, un JSON de petición y un WAV con nombre único. Python escribe eventos `TTS_EVENT {json}` por stdout; stderr se conserva en el `.log`. El resultado incluye duración, frecuencia y tiempo de generación. Se comprueba que el archivo devuelto sea el solicitado y que no esté vacío.

Al cancelar se termina el árbol del proceso Python, incluido el lanzador del entorno virtual. Los archivos temporales del trabajo se limpian y un WAV parcial cancelado se elimina. Los registros quedan disponibles para diagnóstico. Los pesos se abren desde las rutas configuradas, con Hugging Face en modo offline. El módulo comparte el código de voz del laboratorio anterior, pero no depende de su servidor Gradio ni lo modifica.

Para agregar otro motor se amplían `SpeechEngine`, el catálogo de capacidades y voces, las rutas iniciales y las opciones de la vista, y se implementa su rama del worker (o un servicio alternativo). Un modelo que requiera dependencias incompatibles puede usar otro Python, como Pocket.

## Comprobaciones

```powershell
dotnet test tests/CaptionForge.Tests/CaptionForge.Tests.csproj
dotnet run --project checks/CaptionForge.ModuleChecks/CaptionForge.ModuleChecks.csproj -- artifacts/module-checks
```

La segunda comprobación crea las vistas WPF sin abrir ventanas, procesa los enlaces de datos, valida navegación y estados y renderiza ambas herramientas. Está incluida en la validación CI para Windows y no necesita modelos de voz. No sustituye una prueba manual de los diálogos o del reproductor.

Para comprobar las instalaciones de voz del laboratorio en este equipo:

```powershell
dotnet run --project checks/CaptionForge.ModuleChecks/CaptionForge.ModuleChecks.csproj -- artifacts/module-checks --real-speech
```

Esta opción genera muestras de Kokoro y Pocket en inglés y español mediante el servicio .NET real; requiere sus entornos y pesos ya descargados. Las pruebas xUnit del puente utilizan un worker pequeño y pueden seleccionar su Python mediante `CAPTIONFORGE_TEST_PYTHON`; se omiten explícitamente si no hay Python disponible. Los pesos y audios de prueba no se añaden al repositorio.
