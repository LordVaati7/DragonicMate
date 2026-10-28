# Dragonic Mate: prueba de captura exclusiva de Kenshi

## Capturas periódicas opcionales

Abre **Start-Kenshi.cmd** para usar el nuevo control **Periodic captures (optional)**.
Está apagado al abrir la aplicación y la elección no se guarda. Los lanzadores
Start-Voice-Check.cmd y Start-Image-Check.cmd mantienen forzado el modo manual.

Antes de Start puedes activar la casilla y elegir 1, 2, 5 o 10 minutos. El valor
inicial es 2 minutos; no hace falta decidir ahora el intervalo definitivo. Cada
vencimiento espera además 10 segundos de página lista y silencio de salida de
ChatGPT. Si continúa ocupado, esa ocasión caduca sin acumular capturas.

**Send now** y el atajo siguen disponibles: una solicitud manual reinicia la espera
completa, evitando una captura periódica justo después. Pause suspende el programa;
Resume reinicia el intervalo completo. Stop detiene todo envío. Detén el modo antes
de cambiar el intervalo o la casilla. Si una captura ya está en proceso no se duplica
ni se interrumpe para iniciar otra; los pedidos simultáneos se rechazan sin cola.

El programa observa audio de salida y controles permitidos de la página, no tu
micrófono: no puede garantizar que detecte que tú estás hablando. Activa el modo
solo si quieres probar este comportamiento. Conserva resolución y ajustes del juego.
Para la primera comprobación, espera una captura periódica y luego pulsa Stop;
verifica la imagen y continuidad de Voice. No son pruebas térmicas ni de carga.

Esta versión experimental añade selección de una ventana de Kenshi. La interfaz
se llama OpenGameMate y el título incluye `Kenshi window`. El envío de imagen y
la conversación por Voice juntos ya tienen una primera validación del usuario
en la versión anterior. La captura de Kenshi, foco y rendimiento siguen pendientes.

## Preparación

1. Extrae el nuevo ZIP en una carpeta nueva. Con ambas versiones cerradas puedes
   copiar `data` de la versión anterior, en el mismo usuario y equipo, para conservar
   la sesión. No compartas ni publiques esa carpeta.
2. Abre `Start-Voice-Check.cmd`. No requiere instalar herramientas ni compilar.
3. Abre Kenshi en ventana o sin bordes. Usa una escena tranquila que ya tengas
   disponible; no cambies ajustes gráficos ni hagas pruebas de carga. Mantén la
   ventana abierta, sin minimizar. Esta prueba es opcional si el portátil está caliente.
4. En GameMate pulsa **Select Kenshi window**. Debe aparecer **Kenshi selected**.
   Solo se admiten nombres de proceso exactos `kenshi_x64` o `kenshi`. Si no encuentra
   exactamente una ventana válida, se detiene; no elige otra aplicación ni el escritorio.
5. Abre ChatGPT, activa Voice, conversa brevemente y confirma Voice en el panel.
6. Pulsa **Start** y acepta la confirmación de captura exclusiva de Kenshi.

## Una captura desde GameMate

1. Deja GameMate delante, cubriendo parte del juego. No minimices Kenshi.
2. Pulsa **Send now** una sola vez y espera hasta 40 segundos.
3. Confirma que la imagen enviada contiene el juego y no GameMate ni el escritorio.
   Pregunta por voz qué aparece; confirma que puedes seguir conversando.
4. Pulsa **Stop**. Para esta prueba manual deja apagada la casilla de capturas
   periódicas; los lanzadores de comprobación la mantienen desactivada.

Si el juego se minimiza al cambiar de aplicación, usa ventana o sin bordes antes de
seleccionarlo otra vez. El programa no restaura ventanas ni fuerza el foco. Una
imagen negra puede indicar que el juego dejó de renderizar o una limitación de captura;
no se intentará eludir protecciones. Detén la prueba y exporta el diagnóstico si falla.

## Comprobación posterior del foco, solo si la anterior funciona

Con Voice activo y Kenshi delante, solicita una captura con el atajo configurado
(ahora Ctrl+Alt+F10; puede cambiarse con Change). Comprueba que GameMate no aparece
delante y que los controles del juego siguen funcionando. Esto es distinto de pulsar
Send now: al pulsar ese botón tú mismo das el foco a GameMate. Después pulsa Stop.
No hace falta repetir capturas ni mantener una sesión larga para esta comprobación.

## Límites y privacidad

- Selección válida solo para la ejecución actual. Detén el modo antes de reseleccionar.
- Ventana ausente, cerrada, minimizada o con identidad distinta: falla sin capturar el monitor.
- La captura usa Windows Graphics Capture sobre la ventana, no memoria del juego,
  inyección, entradas simuladas ni una API de IA adicional.
- La imagen temporal conserva proporción y tiene como máximo 1920 x 1080 píxeles.
- Cualquier contenido privado que el juego dibuje dentro de su ventana puede incluirse.
- El programa no lee respuestas, historial, credenciales ni audio. Los diagnósticos no
  incluyen imágenes, títulos de ventana, rutas de procesos ni documentos de campaña.
- CI valida compilación, estados y páginas sintéticas; no ejecuta Kenshi ni demuestra
  refrigeración, foco o captura real del juego. No se hicieron compilaciones o pruebas
  de carga en tu portátil.
- Si reaparece Silero VAD, detén la prueba. No se modifican ni eluden sus recursos.
- `Start-Image-Check.cmd` permite el mismo ensayo de ventana sin Voice, con confirmación
  explícita de prueba de imagen. No verifica Voice.
