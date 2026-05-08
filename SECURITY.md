# Política de Seguridad de TecniLauncher

En TecniStudio, la transparencia es nuestra prioridad. Al ser un proyecto 100% Open Source, invitamos a la comunidad a auditar nuestro código para garantizar que no hay malware, stealers ni mineros ocultos.

## Medidas de Seguridad Actuales
* **Autenticación Segura:** Utilizamos Supabase para el manejo de sesiones. No guardamos contraseñas en texto plano ni las enviamos a terceros.
* **Cifrado Local:** Los datos de sesión se guardan localmente de forma segura en tu PC usando la API de Protección de Datos de Windows (DPAPI).
* **Dependencias:** Usamos librerías conocidas y no ofuscamos nuestro código.

## Limitaciones Conocidas y Roadmap
Actualmente (v1.4.0), nuestro sistema de auto-actualización descarga el nuevo release directamente desde GitHub y reemplaza los archivos.
**Nuestra prioridad #1 para la v1.4.1:** Estamos trabajando en implementar validación criptográfica (SHA256) en el updater para asegurar la integridad de las descargas y blindar la cadena de suministro.

## Cómo reportar vulnerabilidades
Si encuentras un fallo de seguridad, por favor NO abras un "Issue" público. Contáctanos directamente a **tecnistudio.soporte@gmail.com** o abre un ticket privado en nuestro servidor de Discord para que podamos parchearlo rápidamente.
