# Dragonic Mate: comprobación breve del envío durante Voice

Esta es una compilación experimental del adaptador. La interfaz todavía se llama
OpenGameMate. No incluye captura exclusiva de Kenshi ni contexto de campaña.

## Qué está validado y qué falta

GitHub Actions compila en Windows y prueba reglas, estados y páginas sintéticas.
Esas pruebas no demuestran compatibilidad con el ChatGPT real, Voice, el foco del
juego, el rendimiento ni la refrigeración de tu computadora. El resultado sigue
pendiente hasta la comprobación manual siguiente.

El paquete es autocontenido para .NET 8: no requiere instalar su SDK ni su runtime.
WebView2 Runtime debe existir previamente. Si falta, detén la comprobación; no hace
falta instalar herramientas para continuar esta investigación.

## Preparación

1. Descarga el artefacto `DragonicMate-voice-diagnostic-win-x64` de una ejecución
   correcta de CI en la rama de reparación. Extrae el ZIP fuera del repositorio.
2. No ejecutes scripts de instalación, compilación ni pruebas de estrés.
3. Abre `Start-Voice-Check.cmd`. Este lanzador solo inicia la aplicación con
   `--portable --manual-only`: datos aislados en esa carpeta y automático desactivado.
   No instala herramientas. Si abres el EXE directamente, no se aplica ese modo.
4. La captura actual abarca **todo el monitor principal**. Cierra documentos,
   conversaciones y notificaciones privadas. Usa primero una escena inocua con
   formas de colores que puedas describir; todavía no necesitas iniciar Kenshi.
5. Abre ChatGPT desde la aplicación, inicia sesión tú mismo, abre Voice y confirma
   Voice en el panel de la aplicación. El perfil aislado puede pedir otro inicio
   de sesión aunque tu navegador habitual ya esté conectado.

## Una imagen, con control del usuario

1. Antes de activar el modo compañero, deja una frase de prueba en el compositor
   y comprueba que el envío de rol se detiene sin reemplazarla. Retírala tú mismo.
2. Inicia el modo compañero y confirma que la actividad indica `Manual validation
   mode`. Usa una vez el atajo configurado cuando Voice esté en silencio. Detén el
   modo compañero al terminar el intento. En este modo no hay envíos automáticos.
3. Comprueba visualmente que hay exactamente una imagen en la conversación y
   pregunta por las formas o colores de la escena. Escucha la respuesta y confirma
   que Voice sigue abierto. El programa no lee ni registra esa respuesta.
4. Si funciona, repite en dos intentos breves separados, deteniendo el modo
   compañero después de cada uno. No prolongues la sesión ni pruebes límites.
5. Si muestra `Send unconfirmed`, `Web adapter failed`, cuota o un borrador/adjunto
   residual, detente y revisa la conversación antes de volver a enviar. No pulses
   repetidamente Enviar. Retira tú mismo cualquier borrador residual.

Si el equipo presenta problemas de refrigeración, interrumpe la comprobación. No
se requiere una sesión de juego ni una prueba prolongada para este primer paso.

## Información útil para el siguiente diagnóstico

Comunica solo: si la imagen aparece, si Voice continúa, si el foco cambió y el
código de error. El botón de exportación genera diagnósticos de estados y controles;
revisa su contenido antes de compartirlos. No publiques imágenes, conversaciones,
documentos de Dragonic Army ni el directorio `data/WebView2`.

Los eventos nuevos son `adapter.input-preparation`, `adapter.image-preparation`,
`adapter.text-preparation` y `adapter.role-preparation`. Distinguen borrador,
compositor, entradas ambiguas, espera de adjunto y resultado desconocido.

No se instalan modelos locales ni se utilizan servicios de IA facturados por API.
Se aplican las capacidades y límites normales de tu cuenta ChatGPT.
