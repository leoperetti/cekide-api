# Feature Specification: Entornos aislados de API

**Feature Branch**: `001-environment-separation`

**Created**: 2026-11-03

**Status**: Draft

**Input**: User description: "Quiero separar la API en tres entornos: development para desarrollo, staging en Azure para validar cambios y producción en Azure con datos reales. Necesito definir recursos, configuración, secretos y el flujo de despliegue. No quiero implementar todavía. Usar recursos gratuitos; si algún recurso puede tener costo, avisarme antes de crearlo. Development usa User Secrets y su base local; staging y producción usan Key Vault y bases separadas. Staging y producción ejecutarán instancias independientes de la API en Azure App Service; se promoverá a producción el mismo artefacto validado en staging, sin reconstruirlo. Es obligatorio usar GitHub Actions. Cada cambio publicado desde main va primero a staging y luego requiere aprobación manual para producción. Las migraciones se ejecutan automáticamente después del despliegue de la API y deben proteger los datos reales ante fallos. Los tres entornos compartirán Blob Storage con un directorio por entorno. Los recursos Azure van en `CEKIDE`. Ya existen la base de staging `cekide-db-staging`, la base de producción `cekide-db` y la API de producción `cekide-api`; crear la API de staging `cekide-api-staging` y la cuenta Blob, tras verificar costos."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Entornos separados (Priority: P1)

El equipo de la API necesita disponer de development para desarrollar, staging para validar cambios y producción para atender el uso real, con los dos últimos alojados en Azure.

**Why this priority**: La separación es la base para validar cambios sin poner en riesgo el servicio ni los datos reales.

**Independent Test**: Revisar el inventario de entornos y confirmar que identifica los tres propósitos, ubicaciones y recursos asignados, manteniendo aislados los datos, permisos y directorios de archivos aunque la cuenta de Blob Storage sea compartida.

**Acceptance Scenarios**:

1. **Given** el inventario de entornos aprobado, **When** un miembro del equipo consulta dónde desarrollar, **Then** identifica development como el entorno de desarrollo y distingue sus recursos de los de Azure.
2. **Given** los entornos staging y producción definidos, **When** el equipo revisa su ubicación y propósito, **Then** confirma que ambos están en Azure y que staging sirve para validar cambios mientras producción atiende operaciones reales.
3. **Given** los tres entornos configurados, **When** se revisan sus destinos de persistencia, **Then** development utiliza su base local y staging y producción utilizan bases de datos Azure distintas, sin compartir base ni cadena de conexión.
4. **Given** que los tres entornos guardan archivos en Blob Storage, **When** se inspecciona su configuración, **Then** todos usan la cuenta compartida, pero cada entorno opera únicamente bajo su directorio/prefijo asignado.

---

### User Story 2 - Configuración y datos protegidos (Priority: P1)

El equipo necesita configurar cada entorno sin mezclar credenciales, datos ni ajustes, para que las pruebas no afecten la operación real.

**Why this priority**: Una separación incompleta podría exponer secretos o modificar datos reales durante el desarrollo y la validación.

**Independent Test**: Revisar la configuración prevista para los tres entornos y comprobar que las credenciales y los datos de producción no se comparten con development ni staging.

**Acceptance Scenarios**:

1. **Given** que se configura cualquiera de los entornos, **When** se revisan sus ajustes y credenciales, **Then** se puede identificar a qué entorno pertenecen y no se reutilizan credenciales entre entornos.
2. **Given** que staging requiere datos para validar cambios, **When** se prepara ese entorno, **Then** se usan datos sintéticos o anonimizados y no datos reales de producción.
3. **Given** que un usuario autorizado necesita gestionar un secreto, **When** lo consulta o actualiza, **Then** puede hacerlo mediante el mecanismo protegido previsto y el secreto no aparece en código fuente ni registros.

---

### User Story 3 - Promoción controlada a producción (Priority: P2)

El equipo necesita validar una versión en staging y promoverla a producción solo después de comprobar que es apta y obtener la aprobación requerida.

**Why this priority**: Un recorrido claro de cambios reduce errores y permite rastrear qué versión está atendiendo a usuarios reales.

**Independent Test**: Recorrer el flujo documentado con una versión de prueba y verificar las condiciones de validación, migración automática, aprobación, publicación y recuperación segura de datos.

**Acceptance Scenarios**:

1. **Given** una versión candidata, **When** se inicia su publicación, **Then** se despliega primero en staging y se ejecutan las validaciones acordadas antes de permitir su promoción.
2. **Given** un cambio fusionado en `main`, **When** se ejecuta GitHub Actions, **Then** se publica automáticamente en staging y las migraciones de esa versión se ejecutan después de desplegar la API.
3. **Given** que las validaciones o migraciones de staging fallan, **When** se evalúa la promoción, **Then** la versión no se publica en producción.
4. **Given** que la API está publicada, **When** se revisan los destinos en Azure, **Then** staging y producción ejecutan instancias independientes de la API en Azure App Service.
5. **Given** que una versión ha pasado staging, **When** se solicita promoverla, **Then** producción espera una aprobación manual y recibe exactamente el mismo artefacto validado en staging, sin reconstruirlo, y queda registrada la versión publicada.
6. **Given** que una migración o publicación en producción falla, **When** se activa el procedimiento de recuperación, **Then** se bloquean las siguientes etapas y se protege la integridad de los datos reales sin ejecutar una reversión destructiva automática.

---

### Edge Cases

- Si un entorno no está disponible, los otros entornos deben conservar su configuración y datos sin cambios.
- Si faltan credenciales o configuración requerida, la publicación debe detenerse antes de afectar al entorno de destino.
- Si se intenta usar una credencial o un destino de datos de otro entorno, el proceso debe impedir la operación o detectarla antes de la publicación.
- Si una migración o cambio de datos no puede revertirse de forma segura, el flujo de publicación debe indicar cómo recuperar el servicio y proteger los datos.
- Si una migración falla después del despliegue, no se debe promover el artefacto ni restaurar automáticamente una copia de seguridad que pueda eliminar escrituras válidas recientes.
- Si un recurso gratuito no está disponible o puede generar cargos por elegibilidad, cuota o consumo, se debe informar al usuario antes de crearlo o modificarlo y esperar aprobación explícita para cualquier costo.
- Si el aislamiento de Blob Storage se basa solo en nombres de prefijo y una identidad puede acceder a toda la cuenta, se debe tratar como separación lógica insuficiente para el control de acceso y detener el despliegue hasta definir un límite seguro.
- Si falla una validación de staging o no hay aprobación, producción debe permanecer en la versión estable actual.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: La solución MUST definir exactamente tres entornos: development, staging y producción, con el propósito de cada uno documentado.
- **FR-002**: Staging y producción MUST estar alojados en Azure; development MUST estar destinado al trabajo de desarrollo.
- **FR-003**: El plan MUST identificar los recursos requeridos para cada entorno, su propósito y sus dependencias.
- **FR-004**: Cada entorno MUST tener configuración y credenciales propias, sin reutilizar credenciales entre entornos.
- **FR-005**: En development, los secretos MUST gestionarse con .NET User Secrets local. En staging y producción, MUST gestionarse con Azure Key Vault Secrets y acceso de identidad administrada para las aplicaciones. En ningún entorno MUST almacenarse en código fuente ni exponerse en registros.
- **FR-006**: Staging MUST usar datos sintéticos o anonimizados; MUST NOT utilizar datos reales de producción.
- **FR-007**: El flujo de publicación MUST desplegar y validar primero en staging y MUST bloquear la promoción a producción si las validaciones fallan.
- **FR-008**: La promoción a producción MUST requerir una aprobación explícita después de validar staging.
- **FR-009**: El flujo MUST registrar qué versión fue publicada en cada entorno y quién autorizó una publicación a producción.
- **FR-010**: El plan MUST documentar cómo detectar una publicación fallida, recuperar una versión estable y proteger la integridad de los datos.
- **FR-011**: Antes de habilitar publicaciones reales, el equipo MUST poder revisar y aprobar los recursos, configuración, gestión de secretos y flujo de publicación propuestos.
- **FR-012**: El equipo MUST preferir recursos gratuitos elegibles y verificar precio, cuotas, elegibilidad y cargos indirectos antes de crear o modificar recursos Azure. Si un recurso cuesta o puede generar cargos, MUST informar al usuario antes de la acción y MUST NOT crearlo, modificarlo ni habilitar consumo de pago sin aprobación explícita.
- **FR-013**: Todos los recursos Azure de esta API MUST aprovisionarse dentro del grupo de recursos existente `CEKIDE`; el equipo MUST NOT crear grupos de recursos adicionales para esta API. El aislamiento entre entornos MUST implementarse con recursos, identidades, permisos, configuración y datos separados dentro de ese grupo.
- **FR-014**: Development MUST utilizar una base de datos local. Staging y producción MUST utilizar bases de datos Azure distintas y no compartidas; cada entorno MUST tener su propia cadena de conexión y credenciales, y staging MUST NOT conectarse a la base de producción.
- **FR-015**: Staging y producción MUST ejecutar instancias independientes de la API, cada una alojada en su propia aplicación Azure App Service dentro del grupo `CEKIDE`.
- **FR-016**: El flujo de despliegue MUST publicar primero un artefacto inmutable en staging y, tras validaciones satisfactorias y aprobación explícita, promover exactamente ese mismo artefacto a producción sin reconstruirlo.
- **FR-017**: El equipo MUST utilizar GitHub Actions para compilar, probar y desplegar la API en staging y producción; no se permite sustituirlo por otra plataforma de CI/CD.
- **FR-018**: Los tres entornos MUST utilizar una única cuenta Azure Blob Storage, bajo directorios/prefijos fijos e independientes `development/`, `staging/` y `production/`. Cada aplicación MUST limitar sus operaciones a su propio directorio. La separación por prefijo es lógica; la implementación MUST aplicar un límite de autorización que impida el acceso cruzado, por ejemplo contenedores separados con permisos RBAC por entorno o ACL de directorio cuando se use namespace jerárquico.
- **FR-019**: Cada actualización aprobada/publicada en la rama `main` MUST desplegarse primero automáticamente en staging. Producción MUST requerir aprobación manual posterior a las validaciones satisfactorias de staging y MUST recibir el mismo artefacto inmutable.
- **FR-020**: Después de desplegar la API en cada entorno Azure, el flujo MUST ejecutar automáticamente las migraciones pendientes de ese entorno antes de las validaciones posteriores. Las migraciones MUST proteger la compatibilidad y los datos reales; un fallo MUST detener el flujo y bloquear la promoción, sin ejecutar una restauración destructiva automática de producción.

### Key Entities

- **Entorno**: Destino con un propósito definido, ubicación, recursos, ajustes, credenciales y política de datos propios.
- **Publicación**: Versión de la API asociada a un entorno, resultado de validación, momento de publicación y, para producción, su aprobación.
- **Secreto**: Credencial sensible asignada a un entorno y accesible solo mediante el mecanismo protegido definido.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: El equipo puede identificar los tres entornos, su propósito y su ubicación en un inventario único aprobado.
- **SC-002**: El 100% de los recursos, ajustes y credenciales definidos para una publicación están asociados explícitamente a un entorno o declarados como compartidos bajo una excepción aprobada y con límites de acceso verificables.
- **SC-003**: El 100% de las validaciones de una versión candidata se completan en staging antes de autorizar su promoción a producción.
- **SC-004**: Ninguna publicación a producción puede completarse sin validaciones satisfactorias de staging y una aprobación registrada.
- **SC-005**: El equipo puede identificar la versión activa en cada entorno y ejecutar el procedimiento documentado para recuperar una versión estable.
- **SC-006**: El precio, elegibilidad y cargos posibles de cada recurso Azure están verificados antes de crear o modificarlo; cualquier posible costo se comunica al usuario y no se incurre en él sin aprobación explícita.
- **SC-007**: El 100% de los recursos Azure de esta API pertenece al grupo `CEKIDE`, sin grupos de recursos adicionales.
- **SC-008**: La configuración de los tres entornos referencia tres destinos de base de datos no compartidos: una base local de development y bases Azure distintas para staging y producción; ninguna cadena de conexión de staging referencia producción.
- **SC-009**: Staging y producción están configurados como dos aplicaciones Azure App Service distintas; el identificador o hash del artefacto publicado en producción coincide con el validado en staging.
- **SC-010**: El flujo de compilación, pruebas y despliegue de staging y producción está implementado y ejecutado mediante GitHub Actions.
- **SC-011**: Existe una única cuenta Blob Storage compartida, con el prefijo asignado a cada entorno y una prueba de autorización que demuestra que una identidad de entorno no puede leer, sobrescribir ni eliminar archivos de los otros entornos.
- **SC-012**: Una publicación desde `main` despliega primero a staging; producción solo avanza tras pruebas satisfactorias y aprobación manual, usando el hash idéntico del artefacto.
- **SC-013**: Las migraciones pendientes se ejecutan automáticamente después del despliegue de cada entorno Azure; un fallo de migración detiene el flujo, no promueve a producción y no elimina datos reales mediante rollback automático.

## Assumptions

- Development se usará como entorno de desarrollo del equipo; el lugar y los recursos concretos se decidirán durante la planificación.
- Staging y producción serán entornos de Azure separados lógicamente y tendrán recursos y configuración aislados.
- Staging utilizará datos sintéticos o anonimizados en lugar de datos reales.
- Una publicación a producción requerirá aprobación explícita; el rol responsable y los criterios específicos se definirán en el plan.
- Development utilizará .NET User Secrets para guardar secretos localmente; staging y producción utilizarán Azure Key Vault Secrets.
- Se priorizan recursos gratuitos, pero las ofertas dependen de la suscripción, la región, el plazo y el consumo. Si algún recurso o uso puede generar cargos, se debe avisar antes de la acción y obtener aprobación explícita; no se debe afirmar que "Free" garantiza factura cero.
- Todos los recursos Azure de la API se crearán dentro del grupo de recursos existente `CEKIDE`; no se crearán grupos adicionales para separar los entornos.
- Development usará una base local; staging y producción tendrán cada uno una base Azure distinta, con cadenas de conexión y credenciales propias y sin compartir datos.
- Staging y producción ejecutarán instancias independientes de la API en Azure App Service; producción recibirá el artefacto exacto validado en staging, sin reconstrucción.
- GitHub Actions es la plataforma obligatoria para compilar, probar y desplegar en staging y producción.
- Los tres entornos comparten una cuenta Azure Blob Storage bajo directorios/prefijos `development/`, `staging/` y `production/`; el acceso se debe restringir por entorno además del aislamiento lógico del nombre de ruta.
- Las migraciones se ejecutan automáticamente después del despliegue de la API en Azure. En producción se usan cambios compatibles, respaldo/restauración verificados y se evita cualquier rollback automático que pueda perder escrituras válidas.
- Según el usuario, ya existen las bases `cekide-db-staging` y `cekide-db` y la API de producción `cekide-api`; debe crearse la API staging `cekide-api-staging` y una cuenta Blob compartida. La existencia, tipo, ubicación y costo de los recursos reportados deben verificarse antes de operar; los recursos nuevos o las acciones con costo requieren aviso y aprobación.
- La especificación define necesidades y criterios de aceptación; la selección detallada de recursos, configuración, gestión de secretos y automatización queda para `/speckit-plan`.
