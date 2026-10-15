# CaptionForge v0.2.0

- Interfaz en español e inglés, con selector en Configuración y preferencia persistente.
- Traducción de las herramientas de audio/transcripción y texto a voz sin acoplarlas al host.
- Corrección de palabras sin duración al final de una frase: se recupera contexto de la frase siguiente cuando hay tiempos nativos válidos.
- Reintentos de alineación que conservan las palabras originales y rechazan coincidencias parciales y puntuación incompleta.
- Pruebas de regresión para tiempos, subpalabras, puntuación, idioma y conservación del estado de los módulos.

Descarga y extrae el ZIP completo para Windows x64. Incluye .NET; los modelos Whisper/TTS y FFmpeg se configuran por separado.
