# Licencia de CaptionForge: instalación y alcance

Este paquete contiene una licencia personalizada redactada conforme a las condiciones acordadas: uso profesional permitido, modificaciones y distribución gratuita, conservación de la atribución y prohibición de vender el programa o versiones que contengan su código cubierto.

**Es un borrador redactado con asistencia de IA; no ha recibido revisión de un abogado colegiado.** No se afirma su validez universal ni que cubra todas las situaciones posibles. No se ha elegido una jurisdicción o tribunal sin información específica del titular.

## Archivos

| Archivo                                                | Función                                                                    |
| ------------------------------------------------------ | -------------------------------------------------------------------------- |
| `LICENSE`                                              | Texto completo en inglés de CaptionForge Source-Available License 1.0.     |
| `NOTICE`                                               | Atribución de Yordin Isaac Garcia (NidroySoft) y enlace al proyecto.       |
| `README.md`                                            | README actualizado con resumen de condiciones, badge y autor.              |
| `THIRD_PARTY_NOTICES.md`                               | Alcance de los componentes de terceros; no sustituye sus licencias.        |
| `licenses/Whisper.net-MIT.txt`                         | Licencia original de Whisper.net, conservada íntegramente.                 |
| `licenses/WhisperCompat-NOTICE.md`                     | Descripción existente del parche de compatibilidad.                        |
| `src/CaptionForge.WhisperCompat/LICENSE` y `NOTICE.md` | Copias de los avisos originales en el proyecto correspondiente.            |
| `.github/workflows/release.yml`                        | Añade los documentos legales y `licenses/` al ZIP de los futuros releases. |

## Cómo añadirlos

Extrae el ZIP en `C:\Proyectos\CaptionForge`, junto a `src`, `tests` y `.github`. Reemplaza el README y el workflow de release que se entregaron anteriormente. Las copias de los avisos de WhisperCompat conservan su texto original; no cambian el código de ese proyecto.

No reemplaza Core, Application, Infrastructure, Desktop ni las pruebas. Tampoco publica cambios en GitHub por sí solo.

Después de revisar el texto, desde la raíz del repositorio:

```powershell
git add LICENSE NOTICE README.md THIRD_PARTY_NOTICES.md licenses .github/workflows/release.yml src/CaptionForge.WhisperCompat/LICENSE src/CaptionForge.WhisperCompat/NOTICE.md
git commit -m "docs: añadir licencia de uso comercial permitido sin venta y atribución"
git push origin main
```

La entrega anterior del workflow está preparada para estos mismos nombres y rutas. Si has editado el workflow por tu cuenta después, integra el bloque de copia de avisos sin perder esos cambios.

## Ejemplos de la política acordada

| Caso                                                                             | Resultado del borrador                                                    |
| -------------------------------------------------------------------------------- | ------------------------------------------------------------------------- |
| Usar CaptionForge para editar vídeos de una empresa                              | Permitido.                                                                |
| Subtitular vídeos monetizados                                                    | Permitido.                                                                |
| Cobrar a un cliente por un vídeo o un archivo de subtítulos                      | Permitido.                                                                |
| Crear un fork, modificarlo y distribuirlo gratis con la atribución               | Permitido.                                                                |
| Cobrar por separado por formación, soporte o ayuda de instalación                | Permitido si el programa y su uso no requieren contratarlo.               |
| Recibir una donación totalmente opcional                                         | Permitido.                                                                |
| Vender CaptionForge, cambiarle el nombre y venderlo, o cobrar por una activación | Prohibido.                                                                |
| Exigir una donación o contratar otro servicio para entregar una copia            | Prohibido.                                                                |
| Distribuir CaptionForge dentro de un paquete que obligatoriamente se compra      | Prohibido.                                                                |
| Omitir el autor original y la licencia en una distribución                       | Prohibido.                                                                |
| Vender un componente de terceros obtenido independientemente bajo MIT            | La licencia de CaptionForge no restringe sus derechos MIT independientes. |

El texto no exige publicar modificaciones privadas ni el código de las adiciones del distribuidor. Tampoco exige poner tu nombre en los vídeos o subtítulos que produzcan los usuarios. Su atribución debe acompañar al programa redistribuido.

Es una licencia de **código disponible (source available)**. No se presenta como licencia open source aprobada por OSI. GitHub puede mostrarla como una licencia no reconocida o personalizada.

## Versiones y releases

La licencia se aplicará al material propio que publiques bajo estos términos. No elimina permisos concedidos anteriormente bajo otra licencia. Tampoco cambia las licencias de código de terceros ni concede derechos sobre recursos de CapCut.

El workflow ahora incluye los avisos en las nuevas descargas; no modifica releases ya publicados. El historial que compartiste ya contiene `v0.1.0`, anterior a las muestras completas y al README. Por eso el ejemplo del README utiliza una etiqueta nueva, `v0.1.1`, que debes crear sobre el commit completo que quieras distribuir.

El archivo LICENSE contiene el texto normativo en inglés; los cuadros y resúmenes en español son explicaciones. La comprobación realizada es de coherencia documental, alcance del borrador, conservación de los avisos originales y sintaxis YAML, no una validación judicial.

## Referencias consultadas

- [Open Source Definition de OSI](https://opensource.org/osd): la restricción de venta impide presentar esta licencia como open source conforme a esa definición.
- [MIT y sus requisitos de conservación de avisos](https://choosealicense.com/licenses/mit/).
- [Whisper.net](https://github.com/sandrohanea/whisper.net): los avisos originales se conservaron a partir del proyecto de compatibilidad ya entregado.
