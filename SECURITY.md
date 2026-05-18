# Políticas de Seguridad y Privacidad

En **TecniStudio**, la transparencia es nuestra prioridad absoluta. Al ser un proyecto 100% Open Source, invitamos a toda la comunidad a auditar nuestro código para garantizar que no hay malware, *stealers* ni mineros ocultos. 

Este documento detalla cómo protegemos tu equipo y tu información.

---

## Parte 1: Política de Seguridad

### Medidas de Seguridad Implementadas
* **Autenticación Segura:** Utilizamos Supabase para el manejo de sesiones en la nube. No guardamos contraseñas en texto plano ni las enviamos a terceros.
* **Cifrado Local (DPAPI):** Los datos de sesión (como tus credenciales recordadas) se guardan localmente de forma segura en tu PC utilizando la API de Protección de Datos de Windows (DPAPI), lo que impide que otros programas puedan leerlos.
* **Actualizaciones Blindadas:** A partir de la v1.4.1, nuestro sistema de auto-actualización incluye validación criptográfica (Hash SHA256) para asegurar la integridad de las descargas y proteger la cadena de suministro contra inyecciones maliciosas.
* **Código Transparente:** Usamos librerías conocidas y no ofuscamos nuestro código, facilitando su revisión por parte de la comunidad.

### Cómo reportar vulnerabilidades
Si encuentras un fallo de seguridad en el launcher, la API o la web, por favor **NO abras un "Issue" público** para evitar que usuarios malintencionados lo aprovechen.
Contáctanos directamente para que podamos parchearlo rápidamente a través de:
* **Correo:** tecnistudio.soporte@gmail.com
* **Discord:** Abriendo un ticket privado en [nuestro servidor](https://discord.com/invite/adhVwrHbrJ).

---

## Parte 2: Política de Privacidad

Recopilamos **únicamente lo mínimo necesario** para que el ecosistema de TecniLauncher funcione, puedas jugar optimizado y lucir tu skin en multijugador.

### ¿Qué datos recopilamos y a dónde van?
* **Base de Datos (Supabase):** Solo guardamos tu nombre de usuario, correo electrónico, contraseña encriptada (Hash) y el identificador de tu perfil (UUID).
* **Almacenamiento (Cloudflare R2):** Aquí alojamos públicamente tu archivo de skin (`.png`) para que el juego pueda cargarlo.

### Acceso Local a tu Equipo
El launcher únicamente crea, lee y modifica archivos dentro de las carpetas de las instancias del juego (`.minecraft` o la carpeta de TecniLauncher). Esto es necesario para descargar los recursos originales, inyectar tu skin y aplicar las optimizaciones de rendimiento (Aikar's Flags).

### Lo que NUNCA hacemos
* **NO** tocamos, leemos ni exfiltramos tus archivos personales fuera del juego.
* **NO** robamos tokens de navegadores, Discord ni cuentas externas (incluidas cuentas de Microsoft).
* **NO** instalamos mineros de criptomonedas ni procesos en segundo plano ajenos al juego.
* **NO** vendemos tu información personal a terceros.

### Servicios de Terceros
Realizamos conexiones legítimas a servicios públicos oficiales:
* **Modrinth API:** Para explorar y descargar mods/modpacks de la comunidad.
* **Mojang / Microsoft:** Para validar cuentas premium y descargar las versiones originales del juego.

### Eliminación de Datos
Eres dueño de tu información. Si en algún momento deseas que eliminemos tu cuenta y tus skins de nuestra base de datos, contáctanos a soporte y lo procesaremos de inmediato.

---
*Última actualización: Mayo de 2026 (v1.4.1)*
