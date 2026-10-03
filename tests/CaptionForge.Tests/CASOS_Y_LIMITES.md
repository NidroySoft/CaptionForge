# Casos cubiertos y límites de la verificación

La suite comprueba el contrato del MVP y sus casos límite conocidos. Ninguna colección finita garantiza todos los fallos posibles; los límites de entorno y comportamiento pendiente se indican al final. No se cambió el backend para que las pruebas pasaran.

| Área | Casos y comportamiento comprobado |
|---|---|
| Golden v3 | Los 15 captions: rangos, texto y arrays de palabras exactos; DTW 15/15 y transcripción regular 13/15. Grafo de plantilla, fuente, attachment, animación y efecto; duración D-1; IDs nuevos y referencias. |
| Unidades y límites temporales | µs/ms, redondeo de medio milisegundo, long.MaxValue, conversiones con overflow, valores negativos, duración vacía, FPS cero/default, FPS racionales y enteros; 1.000 rangos reproducibles en cada uno de siete FPS. |
| Recortes y montaje | Origen y destino distintos, desplazamiento al destino, huecos conservados al excluir un fragmento, recorte final limitado, subframe omitido, palabras y frases solapadas rechazadas, selección fuera de timeline. |
| Palabras | Controles y espacios ignorados, subpalabras y apóstrofos unidos, puntuación, pausas con espacios de duración cero, palabras sin duración, offsets inválidos, endpoints y alternancia palabra/espacio, tokens que contienen frases. |
| Unicode | Acentos, chino, árabe, emoji con secuencias UTF-16, caracteres combinados, signos y texto que requiere escaping JSON; texto conservado, rango de estilo en unidades UTF-16. No se valida tipografía ni disposición RTL en CapCut. |
| Contratos | Copia defensiva y colecciones de solo lectura, IDs y claves duplicadas, overrides ajenos, proyecto/timeline distintos, huellas inválidas, archivo ausente con hash, recibos sin backup, WAV con medición fuera de tolerancia. |
| CPU y exportación | Sugerencia de hilos 75%, límites de hardware, preferencias revalidadas, overflow del cálculo; SRT vacío, orden temporal, CRLF y horas superiores a 99. |
| Orquestación | Estados y orden de puertos, generación separada de aplicación, fragmentos secuenciales, resultados de adaptadores de otro ID/duración/modelo/idioma, silencio, planes y recibos incoherentes, excepción del observador, errores de persistencia con ambas causas. |
| Cancelación | Antes de crear run, preparando audio, transcribiendo, precommit, tras commit; commit iniciado termina y confirma aunque se solicite cancelación. Lock ocupado cancelable. |
| Concurrencia | Dos Apply del mismo run; dos planes con la misma captura; CAS de estados real; finalización tardía que no sobrescribe registro nuevo; guardado concurrente de settings; IDs de runs únicos incluso con reloj fijo. |
| Catálogo | Tres identidades separadas, nombres/covers, audio resuelto por material_id, medios ausentes diagnosticados; metadata corrupta, raíz inexistente, draft_id duplicado, IDs de timeline duplicados/diferentes, timeline eliminada, JSON ausente/corrupto, esquema futuro. |
| FPS en JSON | FPS negativos/cero/excesivos y precisión no admitida; decimal 29.97 preservado como 2997/100, sin convertirlo silenciosamente en 30000/1001. |
| Materiales no interpretables | Material no resuelto, sin ruta, compuesto, rango negativo, velocidad inconsistente: se omiten con diagnóstico. Velocidad modificada, curvas, reversa, mute y solapamientos de selección se rechazan en generación. |
| Preservación de JSON | IDs y datos del proyecto real, pistas y materiales ajenos, campos raíz y campos desconocidos dentro de objetos propios y content; SRT/plantilla manual ajenos conservados con advertencia. |
| Autoría e IDs | Actualización conserva IDs; reduce/amplía cantidad de captions sin duplicar pista; pista propia eliminada, material propio ausente, segmento ajeno dentro de pista propia y referencias externas a propios bloquean actualización; objetos duplicados se rechazan. |
| Raíz y timelines | Raíz equivalente sincronizada junto con su .bak; diferencia flotante mínima real tolerada; mismo ID con estado discordante rechazado; raíz de otra timeline preservada; segunda timeline simulada conserva registro y principal. |
| Huellas y staging | Cambio de original, raíz, metadata, registro, .bak aparecido y original eliminado; plan o staging modificados; todas las huellas deben mantenerse antes de escribir. |
| Backups y commit | Byte a byte del estado previo, incluidos .bak existentes; nativos actualizados al contenido nuevo; fallo inyectado en reemplazos 1/2/3/4 recupera todos los documentos y registro; fallo de rollback deja RecoveryRequired. No se afirma atomicidad del conjunto de archivos. |
| Restauración | Primera ejecución y actualización, recuperación de .bak anteriores, eliminación de archivos inicialmente ausentes, registry anterior; restauración repetida idempotente; cambios posteriores, ejecución posterior, backup/plan/journal alterados bloquean restauración. |
| Rutas y aislamiento | IDs con traversal/separadores/nombres reservados; workspace dentro de proyectos/ancestro o distinto de petición rechazado; rutas de staging, backup y registry fuera del run rechazadas incluso con hash del plan recalculado. |
| Assets | Recursos/fuente ausentes y carpetas vacías; fuente desaparecida después de preparar; alternativas únicas y hashes ambiguos. Los assets de los fixtures son marcadores, no fuentes/efectos utilizables. |
| Settings y manifests | Defaults solo si no hay archivo, roundtrip, schema incorrecto, JSON corrupto, cero hilos; manifests de identidad/versión/estado incorrectos no se reescriben; estados terminales no reanudan generación/aplicación. |
| WAV | RIFF/WAVE/fmt incorrectos, RF64, float/stereo/rate/bits/align incompatibles, datos vacíos/impares/múltiples/truncados, falta fmt/data; chunk desconocido de tamaño impar con padding aceptado. |
| Cabecera de modelos | Tiny/Base/Small/Medium en/multi, Large v3/turbo, v1/v2 por nombre o preset explícito; magic, tamaño, vocabulario, mel bins, arquitectura y preset incompatibles. Son pruebas de metadatos, no modelos completos. |
| Normalización Whisper.net | Unidades de 10 ms a µs, controles EOT en/multi, offsets negativos léxicos rechazados, t_dtw no se usa como offset. |
| Validación antes de motor | WAV fuera del run, medición alterada, modelo ausente/inválido, .en con español, cancelación y servicio disposed; no se carga motor con los modelos sintéticos. |
| FFmpeg real | WAV recortado, vídeo con audio, vídeo sin audio, padding pequeño, muestra exacta, mapping, fuente inalterada, paths con espacios/Unicode/caracteres de shell y culturas con coma decimal; medio ausente/corrupto, recorte fuera de duración, ejecutables ausentes, modos pendientes. |
| Whisper.net real | Voz inglesa, CPU y DTW, traslado temporal, JSON v3 en copias, fábrica reutilizada, idioma auto con .en. Requiere modelo y muestra de voz configurados. |
| Proyecto real opcional | Copia de los documentos de una carpeta que indiques, medios/resources reales en lectura, timeline/fragmento explícitos, generación, commit, .bak, restauración y hashes de originales. No ejecutado aquí con tu Windows. |

## Correcciones editoriales de la referencia

La referencia histórica contiene dos cambios editoriales conocidos: `code words.` → `codewords.` y `Not magic, math.` → `Not magic. Math.`. Solo se ajustan al leer los fixtures para compararlos con golden v3. Las pruebas no introducen esas reglas en el producto ni garantizan que Whisper produzca literalmente el texto editorial.

## Qué falta verificar en tu instalación

- Abrir el resultado en CapCut y comprobar animación por palabra, fuente/color, posición, sincronía perceptual y ausencia de recuperación desde un .bak anterior. Las aserciones JSON no sustituyen esa revisión.
- La apariencia horizontal sigue pendiente aunque el canvas se conserva y se emite advertencia. Tampoco se ejecutó WPF ni se validaron DPI/layout.
- La ejecución aquí es Linux/.NET 10; hay que ejecutar el ZIP en tu Windows para comprobar permisos, antivirus, locks, runtime nativo y nombres de procesos reales de CapCut. Los casos de CapCut abierto con fixtures usan una guardia controlada.
- No se simula un corte eléctrico o la muerte del proceso entre cada instrucción. Los fallos de reemplazo/rollback son inyectados mediante el puerto IFileCommitter; no garantizan recuperación ante cualquier fallo del sistema de archivos.
- No se generó un disco lleno real ni una ACL Windows denegada. Hay fallos de E/S inyectados y locks reales. Junctions/symlinks siguen protegidos por el backend, pero su comportamiento Windows necesita prueba local específica.
- La cabecera multilingüe y normalización están cubiertas; no se ejecutó aquí inferencia de un modelo multilingüe ni medium.en real. La prueba nativa rápida usa tiny.en.
- Compounds, cambios de velocidad/reversa, mezcla de fragmentos solapados, conversión de captions ajenos, otros esquemas/versiones de CapCut y apertura automática del editor siguen fuera del contrato MVP.
- Fixtures de varias timelines son construidos a partir del proyecto recibido; conviene añadir un proyecto real con varias timelines cuando lo tengas.

Estos límites se mantienen visibles para no convertir un resultado verde en una garantía que las pruebas no pueden dar.
