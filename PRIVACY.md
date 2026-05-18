# Política de Privacidad de TecniLauncher

En **TecniStudio**, creemos en la transparencia absoluta. Nuestro objetivo es ofrecerte el mejor rendimiento y personalización en Minecraft, por lo que hemos diseñado nuestro ecosistema para recopilar **únicamente lo mínimo necesario** para que el servicio funcione. 

A continuación, te explicamos exactamente qué datos manejamos, dónde se guardan y a qué tenemos acceso.

---

## 📂 1. ¿Qué datos recopilamos y dónde se almacenan?

Para poder gestionar tu identidad en el juego y permitir que otros vean tu skin, utilizamos infraestructuras en la nube altamente seguras:

* **Base de Datos (Supabase):** Alojamos únicamente la información de tu cuenta. Esto incluye tu nombre de usuario, tu correo electrónico, tu contraseña (almacenada mediante un *Hash* encriptado de forma irreversible) y el identificador de tu perfil (UUID).
* **Almacenamiento de Skins (Cloudflare R2):** Cuando subes una skin a nuestro portal, el archivo de imagen (`.png`) se guarda en nuestros servidores de Cloudflare R2 para generar un enlace público. Este enlace es el que nuestro servidor Yggdrasil envía al juego para que los demás jugadores puedan verte.

##  2. Acceso Local a tu Equipo

TecniLauncher es un programa que se ejecuta en tu computadora. Para funcionar, interactúa con tu sistema de la siguiente manera:

* **Archivos del Juego:** El launcher únicamente crea, lee y modifica archivos dentro de las carpetas de las instancias del juego (típicamente relacionadas con la estructura de `.minecraft` o la carpeta local de TecniLauncher).
* **Inyección y Optimización:** Accedemos a los directorios del juego de forma local para inyectar el sistema de skins (mediante parcheo del `.jar` o *authlib-injector*) y aplicar configuraciones de rendimiento, como los argumentos de Java (Aikar's Flags).


##  3. Servicios de Terceros integrados

El launcher realiza conexiones legítimas a servicios públicos para descargar contenido oficial:
* **Modrinth API:** Para buscar, visualizar y descargar modpacks o mods.
* **Mojang / Microsoft:** Para validar cuentas premium y descargar los recursos originales del juego (`assets`, librerías y versiones).

##  4. Eliminación de Datos y Contacto

Eres dueño de tu información. Si en algún momento deseas que eliminemos tu cuenta, tus skins y todos los registros asociados de nuestra base de datos en Supabase, puedes contactarnos directamente y lo procesaremos de inmediato.

* **Soporte:** tecnistudio.soporte@gmail.com
* **Comunidad:** [Servidor de Discord](https://discord.com/invite/adhVwrHbrJ)

---
*Última actualización: Mayo de 2026 (v1.4.1)*
