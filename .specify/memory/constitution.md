<!--
Sync Impact Report
Version change: 1.5.0 → 2.0.0
Modified principles:
- I. Responsabilidades por Capas y Contratos Co-localizados → I. Preserve Layered Responsibilities
- V. Configuración Segura, Legibilidad y Simplicidad → V. Configuración Segura y Explícita
- VI. Use Table-Specific Infrastructure Repositories → VII. Use Table-Specific Infrastructure Repositories
Added principles:
- VI. Código Legible
Added sections: None
Removed sections: None
Follow-up TODOs:
- Ninguno
-->

# Cekide API Constitution

## Core Principles

### I. Preserve Layered Responsibilities
El manejo HTTP MUST permanecer en los controladores de API, los conceptos de negocio en Domain y
las responsabilidades de persistencia en Infrastructure. Application MUST contener únicamente
servicios que implementan lógica de negocio. Todo el código de acceso a datos y sus abstracciones
MUST residir en Infrastructure; Application MUST NOT contener acceso a datos ni abstracciones de
acceso a datos. Las interfaces de servicios MUST declararse junto con las clases que las
implementan. Estos límites evitan patrones paralelos y mantienen responsabilidades claras.

### II. Seguridad en el Límite de la API
La API MUST aplicar autenticación, autorización, validación de entradas y, cuando corresponda,
aislamiento entre tenants en el servidor. MUST NOT confiar en controles del navegador ni en
identificadores o claims proporcionados por el cliente como prueba de autorización. Las
credenciales MUST NOT almacenarse en el repositorio; el desarrollo MUST usar el mecanismo de
secretos configurado y producción MUST usar el almacén de secretos aprobado para el entorno.

### III. Compatibilidad de Contratos de API y Datos
Las rutas, formas de solicitudes y respuestas, y semántica de los datos persistidos MUST
mantener compatibilidad, salvo que el cambio documente explícitamente una transición compatible.
Los cambios de esquema MUST gestionarse mediante migraciones versionadas de EF Core en
Infrastructure. MUST NOT realizarse cambios manuales de esquema en producción ni cambios que
alteren datos sin una ruta de migración repetible y revisada.

### IV. Cambios Verificables
Todo cambio de comportamiento MUST incluir o actualizar pruebas automáticas pertinentes, unitarias
o de integración. Antes de publicar, MUST ejecutarse el build y las pruebas impactadas disponibles;
un cambio funcional no está completo hasta verificar el comportamiento esperado y sus errores
principales. Los cambios de contratos, persistencia o integraciones MUST probarse en los límites
afectados.

### V. Configuración Segura y Explícita
El código MUST seguir los patrones de configuración existentes de ASP.NET Core. Los secretos MUST
permanecer fuera del control de versiones. Las nuevas claves de configuración MUST documentar su
propósito y las fuentes previstas para desarrollo y producción. Los cambios MUST preferir
soluciones simples y acotadas a refactors no solicitados.

### VI. Código Legible
Las variables MUST usar nombres semánticos y descriptivos. MUST NOT usarse nombres de una sola
letra, salvo contadores triviales en ciclos cortos. El código nuevo MUST seguir las convenciones y
patrones existentes de .NET 10.

### VII. Use Table-Specific Infrastructure Repositories
Para cada tabla de base de datos a la que acceda la aplicación, Infrastructure MUST proporcionar
una clase de repositorio dedicada a esa tabla. Toda abstracción para acceder a esos datos,
incluida la interfaz del repositorio, MUST residir en Infrastructure; la interfaz MUST declararse
dentro de su clase de repositorio. Todas las lecturas y escrituras de la tabla MUST pasar por ese
repositorio. Esto mantiene explícitos los accesos por tabla y su contrato junto al repositorio
responsable.

## Restricciones Técnicas y de Dominio

- La solución está orientada a .NET 10 y mantiene las capas API, Application, Domain e
  Infrastructure.
- Esta API cubre la operación de un gimnasio: planes, alumnos, pagos, vencimientos e invitaciones.
  El modelado MUST proteger la integridad de esos procesos, incluidos estados de pago consistentes,
  vencimientos auditables y relaciones claras entre alumnos y planes.
- Los entornos de development, staging y producción MUST mantener configuración, credenciales,
  datos y recursos aislados. Staging y producción MUST alojarse en Azure.
- Los recursos Azure de esta API MUST crearse dentro del grupo existente `CEKIDE`; esta API MUST
  NOT crear grupos de recursos adicionales.
- Los tres entornos MAY compartir una cuenta de Azure Blob Storage. Los archivos MUST guardarse
  bajo prefijos fijos y distintos (`development/`, `staging/` y `production/`), y cada aplicación
  MUST limitar sus operaciones al prefijo de su entorno. Los prefijos solo brindan separación
  lógica: las credenciales de una aplicación MUST NOT permitir acceso indiscriminado a los demás
  entornos. Las pruebas MUST verificar que no se lean, sobrescriban ni eliminen archivos de otro
  entorno.

## Flujo de Desarrollo

- Para nuevas funcionalidades, usar Spec Kit para definir especificación, plan y tareas antes de
  implementar; aclarar preguntas materiales de comportamiento y seguridad antes de planificar.
- Antes de diseñar un cambio, inspeccionar las capas, controladores, servicios y configuración de
  persistencia afectados.
- Ejecutar pruebas enfocadas y el build aplicable antes de considerar terminado un cambio.
  Revisar especialmente el impacto de migraciones, contratos y configuración de entornos.

## Governance

Esta constitution prevalece sobre prácticas ad hoc del proyecto. Las enmiendas requieren acuerdo
explícito del equipo, actualización de este documento y registro del impacto. Cada pull request
MUST declarar conformidad con estos principios o justificar explícitamente cualquier desviación
temporal e incluir un plan de reversión.

Versionado de la constitution:
- MAJOR: cambios incompatibles o redefinición de principios.
- MINOR: nuevos principios, secciones o ampliaciones materiales de reglas normativas.
- PATCH: aclaraciones sin cambio de alcance normativo.

Revisión de cumplimiento:
- Cada pull request MUST revisar límites de capas, ubicación de contratos, seguridad, compatibilidad
  de API y datos, acceso mediante repositorios específicos por tabla, pruebas y migraciones cuando
  correspondan.
- Los cambios de despliegue MUST revisar el aislamiento entre entornos, el uso del grupo Azure
  `CEKIDE` y la restricción de acceso a los prefijos de Blob Storage.
- Incumplimientos repetidos MUST generar una acción correctiva en la siguiente iteración.

**Version**: 2.0.0 | **Ratified**: 2026-11-03 | **Last Amended**: 2026-11-05
