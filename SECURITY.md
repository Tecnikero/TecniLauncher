# Seguridad y Privacidad — TecniStudio

En **TecniStudio**, nos tomamos en serio la seguridad de nuestros usuarios y la protección de sus datos.

Este documento describe nuestras prácticas de seguridad, cómo reportar vulnerabilidades y qué información puede procesarse al utilizar TecniLauncher y los servicios relacionados.

La seguridad es un proceso continuo. Aunque aplicamos medidas para reducir riesgos, ningún software puede garantizar una protección absoluta frente a todas las amenazas.

## 1. Política de Seguridad

### Medidas de seguridad

TecniLauncher incorpora diferentes mecanismos de protección y mantenimiento:

* **Gestión de sesiones:** el ecosistema utiliza Supabase para determinados servicios de autenticación y gestión de usuarios. Las credenciales y los tokens deben tratarse como información confidencial.
* **Protección de datos locales:** el proyecto utiliza mecanismos de protección de Windows, como DPAPI, para determinados datos almacenados localmente. La protección depende de la cuenta de Windows, del contexto de ejecución y de la configuración del sistema.
* **Integridad de las actualizaciones:** el sistema de actualización incorpora verificaciones mediante hashes SHA-256 para comprobar la integridad de determinados archivos descargados. Esta comprobación no sustituye la firma digital ni garantiza por sí sola la autenticidad o seguridad del archivo.
* **Protección de archivos:** se han incorporado controles para reducir riesgos relacionados con rutas de archivos no autorizadas y la extracción de archivos comprimidos.
* **Mantenimiento del código:** continuamos revisando y mejorando el código, las dependencias y los procesos de instalación y actualización.

Las medidas anteriores describen mecanismos implementados en el proyecto; no constituyen una garantía de ausencia total de vulnerabilidades.

### Cómo reportar vulnerabilidades

Si encuentras una vulnerabilidad en TecniLauncher, la API o los servicios web de TecniStudio, te pedimos que **no publiques los detalles técnicos en un issue público antes de que podamos investigarlos**.

Puedes comunicarte con nosotros mediante los siguientes canales:

* **Correo:** [tecnistudio.soporte@gmail.com](mailto:tecnistudio.soporte@gmail.com)
* **Discord:** [Abrir un ticket privado en nuestro servidor](https://discord.com/invite/adhVwrHbrJ)

Cuando informes de un problema, incluye, si es posible:

1. El componente afectado y la versión correspondiente.
2. Los pasos necesarios para reproducir el problema.
3. El impacto potencial y las condiciones necesarias para explotarlo.
4. Evidencias técnicas que no expongan datos personales ni credenciales.

No incluyas contraseñas, tokens de acceso ni información privada de otros usuarios.

Investigaremos los reportes recibidos y procuraremos comunicar los avances y las medidas correctivas cuando corresponda. El tiempo de resolución dependerá de la gravedad, la complejidad y las condiciones del problema.

## 2. Política de Privacidad

TecniLauncher se integra con servicios de terceros para ofrecer funciones como autenticación, descarga de contenido, gestión de perfiles y personalización.

La información procesada depende de las funciones utilizadas y de los servicios con los que el usuario interactúe.

### Servicios y datos

Los siguientes servicios forman parte del ecosistema descrito por el proyecto:

* **Supabase:** utilizado para determinados servicios de autenticación y gestión de usuarios. Los datos concretos almacenados y sus periodos de conservación dependen de la configuración de la aplicación y de la base de datos.
* **Cloudflare R2:** utilizado para alojar archivos de skins cuando se utiliza la función correspondiente. La disponibilidad pública de estos archivos depende de la configuración del almacenamiento y de sus permisos.
* **Modrinth:** utilizado para consultar contenido y descargar mods y modpacks compatibles.
* **Microsoft y servicios de Minecraft:** utilizados para los flujos compatibles de autenticación y descarga de recursos oficiales.
* **TecniStudio:** utilizado para las funciones de autenticación y personalización compatibles que ofrece la aplicación.

Esta lista identifica servicios integrados, pero no constituye un inventario exhaustivo de todas las conexiones de red. Para publicar una relación completa, deben revisarse las conexiones efectuadas por cada componente y las políticas de los proveedores correspondientes.

### Archivos y acceso local

TecniLauncher necesita trabajar con archivos relacionados con las instalaciones de Minecraft para descargar recursos, administrar perfiles, instalar mods y modpacks, guardar configuraciones y ejecutar las funciones compatibles de personalización.

El alcance real del acceso a archivos depende de las funciones utilizadas y de los permisos concedidos a la aplicación.

No afirmamos que la aplicación esté técnicamente imposibilitada de acceder a cualquier archivo fuera de las carpetas del juego sin haber verificado ese comportamiento en todos sus componentes y escenarios de ejecución.

### Credenciales y datos confidenciales

Las contraseñas, los tokens de acceso y los datos de sesión deben tratarse como información sensible.

No compartas archivos de configuración, registros ni capturas que puedan contener credenciales. Si sospechas que un token o una sesión ha quedado expuesto, contacta con el soporte correspondiente y revoca la sesión o credencial afectada cuando sea posible.

### Lo que no pretendemos hacer

TecniLauncher está diseñado para ofrecer funciones relacionadas con Minecraft y los servicios de TecniStudio, no para recopilar indiscriminadamente información personal.

No pretendemos robar credenciales, extraer datos personales ajenos a las funciones del producto, instalar mineros de criptomonedas ni ejecutar procesos ocultos sin relación con sus funcionalidades.

Estas declaraciones reflejan el propósito del proyecto y deben acompañarse de revisiones del código y de sus dependencias.

### Conservación y eliminación de datos

Si deseas consultar, corregir o solicitar la eliminación de los datos asociados a tu cuenta, contacta con nosotros a través de:

* **Correo:** [tecnistudio.soporte@gmail.com](mailto:tecnistudio.soporte@gmail.com)

Las solicitudes se evaluarán según los datos afectados, las capacidades de los servicios utilizados y las obligaciones legales aplicables. La eliminación de una cuenta no implica necesariamente que todas las copias de seguridad o registros de terceros desaparezcan de forma inmediata.

## 3. Actualizaciones y divulgación responsable

Recomendamos utilizar versiones obtenidas desde los canales oficiales de TecniStudio y mantener TecniLauncher actualizado.

Los cambios relevantes de seguridad y las correcciones se comunicarán a través de los canales de publicación correspondientes cuando sea apropiado.

Si se identifica una vulnerabilidad que pueda afectar a los usuarios, priorizaremos su evaluación y la preparación de medidas correctivas según su gravedad.

## 4. Alcance de este documento

Esta política describe las prácticas y los objetivos de seguridad y privacidad del proyecto. No sustituye una auditoría independiente del código, de la infraestructura ni de los proveedores externos.

Los servicios de terceros pueden aplicar sus propias políticas de privacidad, condiciones de uso y periodos de conservación de datos.

Para consultas relacionadas con la seguridad o la privacidad, utiliza los canales de contacto indicados en este documento.

---

**Proyecto:** TecniLauncher / TecniStudio
**Última revisión:** Octubre de 2026

Esta fecha debe actualizarse cuando se revise nuevamente el contenido y se compruebe su correspondencia con la implementación vigente.
