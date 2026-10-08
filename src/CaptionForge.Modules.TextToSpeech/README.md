# Texto a voz · módulo independiente

Conserva esta carpeta completa bajo `Modules/TextToSpeech`, junto al ejecutable de CaptionForge. El programa descubre `module.json` y carga la herramienta al seleccionarla. Puedes retirar la carpeta con el programa cerrado sin afectar a los subtítulos.

En **Texto a voz**, elige motor e idioma. **Detectar instalaciones** reutiliza los modelos del laboratorio. **Instalar motor** prepara un entorno privado y descarga lo necesario; no tienes que instalar Python manualmente. La configuración avanzada permite indicar otras rutas.

Kokoro y Pocket utilizan voces predefinidas. Nano (inglés) y Chatterbox Multilingual utilizan una referencia de audio. Los WAV se generan localmente, se pueden escuchar con forma de onda, pausar y recorrer, y exportar a cualquier carpeta.

La instalación requiere conexión y espacio libre para las dependencias y modelos. Las generaciones posteriores utilizan los archivos locales. Cada motor guarda su registro de instalación en `%LOCALAPPDATA%\CaptionForge\modules\text-to-speech\engines`.

**Normalizar automáticamente** mide el pico del WAV y ajusta el nivel a −1 dBFS, sin saturación. Es normalización de pico, no de sonoridad LUFS. **Ganancia manual · experto** permite aplicar entre −60 y +30 dB e informa si hay saturación. Ambos crean un WAV nuevo desde el original; repetir el ajuste no acumula ganancia. **Original** permite volver al audio generado. La reproducción, las barras y **Guardar WAV como…** utilizan el resultado seleccionado.

El módulo y su núcleo no dependen de Application, Infrastructure o Desktop de CaptionForge. Comparten únicamente el contrato `CaptionForge.Modularity` y los recursos visuales que ofrece el anfitrión.
