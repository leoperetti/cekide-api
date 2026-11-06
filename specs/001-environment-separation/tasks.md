---
description: "Tareas de implementacion para separar development, staging y production"
---

# Tasks: Entornos aislados de API

**Input**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [quickstart.md](quickstart.md) y
[Constitution](../../.specify/memory/constitution.md).

**Prerequisites**: Diseno disponible; no hay contratos de API nuevos ni cambios de dominio.
Los nombres y costos Azure siguen sin verificar. Este documento solo genera trabajo futuro:
no autoriza aprovisionamiento, modificaciones de recursos, migraciones ni despliegues.

**Actualizacion Blob ABAC 2026-11-04**: con autorizacion expresa se creo la cuenta
`cekideblobaccount` en CEKIDE/Brazil South, Standard_LRS + Hot, con contenedor privado
`cekidecontainer`, HTTPS/TLS 1.2, sin shared key ni acceso publico a blobs.
`infra/blob-access.bicep` compone `infra/modules/storage.bicep` y
`infra/modules/environment-access.bicep`: rol minimo y tres condiciones por prefijo.
Development tiene una aplicacion Entra exclusiva con certificado local protegido, sin
permisos administrativos; staging/production usan sus identidades system-assigned.
Build Bicep, verificacion estructural y build .NET del probe pasaron; what-if y grants
persistidos se revisaron. Los tres entornos pasaron 18 comprobaciones reales cada uno
de acceso propio y denegacion ajena/raiz (54 total). Staging/production se ejecutaron,
con aprobacion separada, desde carpetas SCM temporales con sus identidades system-assigned;
el probe verifico principal/tenant del token. Blobs y carpetas nuevas eliminados correctamente.
El HTTP 500 previo no se reprodujo; su causa y limpieza de la cuenta anterior siguen
sin confirmar. No se modifico esa cuenta. Pasaron 55 tests unitarios y 9 de integracion
enfocados, mas 4 del launcher. T020/T022/T026/T028 siguen pendientes
(faltan las pruebas runtime SQL/Key Vault, configuracion remota, resto de superficies,
composicion main y dependencias). No se publicaron archivos a GitHub ni se desplegaron
APIs. Evidencia y limpieza pendiente en [quickstart.md](quickstart.md).

**Tests**: Se incluyen las pruebas de aislamiento, autorizacion y fallos exigidas por
SC-011, SC-013 y la Constitution. Escribir primero las pruebas automatizables, comprobar
su fallo por la capacidad faltante y preservar las pruebas existentes sin secretos Azure.
Las pruebas con servicios reales requieren recursos aprobados y datos sinteticos.

**Organization**: Setup, fundamentos, US1 (P1), US2 (P1), US3 (P2) y cierre transversal.

## Format: `[ID] [P?] [Story] Description`

- Todas las tareas empiezan con una casilla pendiente y un ID secuencial.
- `[P]` identifica trabajo en archivos distintos que puede ejecutarse en paralelo una vez
  satisfechos sus requisitos indicados en Dependencies.
- `[US1]`, `[US2]` y `[US3]` corresponden a las historias de la especificacion.
- Las rutas son relativas a la raiz del repositorio y usan separadores Windows.

## Path Conventions

- API y configuracion: `src\CekideApi\`.
- Persistencia, Blob Storage y migraciones: `src\CekideApi.Infrastructure\`.
- Pruebas existentes: `tests\CekideApi.UnitTests\` y `tests\CekideApi.IntegrationTests\`.
- Infraestructura propuesta: `infra\`, con Bicep a scope del grupo existente `CEKIDE`.
- Automatizacion operativa propuesta: `scripts\environments\` y `scripts\deployment\`.
- Documentacion operativa y evidencia no sensible: documentos existentes de esta feature.
- No crear tablas de negocio para las entidades operativas del modelo de datos.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirmar el estado inicial sin desplegar ni sobrescribir cambios ajenos.

- [X] T001 Revisar configuracion, migraciones y ambos workflows existentes en `src\CekideApi\Program.cs`, `src\CekideApi.Infrastructure\Storage\RoutineStorage.cs`, `.github\workflows\deploy-cekide-api.yml` y `.github\workflows\main_cekide-api.yml`; registrar en `specs\001-environment-separation\quickstart.md` los comandos disponibles, diferencias detectadas y archivos nuevos previstos, sin alterar los workflows activos.
- [X] T002 [P] Ejecutar la validacion inicial `dotnet test cekide-api.slnx` y registrar resultado y fallos preexistentes en `specs\001-environment-separation\quickstart.md`; mantener `specs\001-environment-separation\checklists\requirements.md` sin cambios por ser un gate de solo lectura durante implementacion; no instalar dependencias nuevas salvo fallo por dependencia faltante.
- [X] T003 Confirmar herramientas disponibles para Bicep/Azure CLI, .NET 10 y EF Core, y documentar sus comandos de validacion en `specs\001-environment-separation\quickstart.md`; usar el SDK existente y fijar la herramienta EF compatible en `.config\dotnet-tools.json` solo si falta una configuracion reutilizable.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Resolver inventario, permisos, costos y proteccion de datos antes de aplicar
cambios en los entornos. La implementacion local no autoriza ejecutar operaciones remotas.

**Estado historico de fundamentos 2026-11-04** (Blob reemplazado segun actualizacion
superior): T004 completada y actualizada con inventario de solo lectura:
`cekide-api-staging` ahora existe y está `Running` en `CEKIDE`, pero referencia el plan
compartido `ASP-AuthCraft-bc2b` de `AuthCraft`, fuera de `CEKIDE`. La cuenta y el contenedor privado
`cekidecontainer` existen; el contenedor se creó a petición del usuario y no se configuró
todavía aislamiento por entorno. El usuario mantiene producción bloqueada y no autoriza
mover ni cambiar el plan compartido. La aceptación actual de posibles cargos cubre Blob
Storage y operaciones de Key Vault; no aprueba costos de hosting ni otros recursos. Con su
aprobación se crearon `cekide-key-vault-staging` y `cekide-key-vault-prod` en `CEKIDE`, West US 3, SKU Standard,
RBAC y purge protection. Staging ya tenía identidades system-assigned y user-assigned;
no se modificaron. Se asignó `Key Vault Secrets User` a la identidad system-assigned solo
en el vault de staging y se configuró `KeyVault__Name`. Se creó el principal SQL
`cekide-api-staging` con `db_datareader` y `db_datawriter` (sin DDL) en `cekide-db-staging`;
la cadena de conexión con autenticación Managed Identity quedó guardada como
`CekideDatabaseConnectionString` en el vault de staging. Para el acceso administrativo se
añadió y retiró una regla de firewall temporal de una sola IP; solo permanece la regla
preexistente `AllowAllWindowsAzureIps`. La base está `Online`, con auto-pausa de 60 minutos.
Para producción, con autorización explícita, se habilitó la identidad system-assigned de
`cekide-api`, se asignó `Key Vault Secrets User` a su vault y se creó el principal SQL
`cekide-api` con `db_datareader` y `db_datawriter`, sin DDL. La cadena Managed Identity se
guardó como `CekideDatabaseConnectionString` en `cekide-key-vault-prod`; también se retiraron
el rol temporal del operador y la regla temporal de una sola IP. `cekide-db` está `Online`
con auto-pausa de 60 minutos. No se configuró `KeyVault__Name` ni se reinició la API:
el vault de producción estaba vacío y faltan secretos requeridos para arrancar.
El usuario eligió usar el único contenedor `cekidecontainer` compartido, por lo que la
autorización final requiere condiciones ABAC por prefijo; el código local fue alineado a
ese contenedor, pero las condiciones aún no están implementadas ni validadas.
Los costos posibles de Blob fueron aceptados, pero no se iniciarán pruebas hasta validar
ABAC. El usuario rechazó planes de hosting con costo adicional e indicó que staging ya
está creado; aceptó el F1 compartido actual como excepción temporal, sin crear ni modificar
un plan. Para recuperación, eligió los backups/PITR SQL existentes, con restauraciones
solo a una base nueva y autorización separada para ensayos. T006 sigue abierta para definir
la ruta de red de Actions; el vínculo runtime de SQL está aplicado y verificado. El usuario acepta detener staging durante
la migración y que se interrumpan solicitudes de prueba. Solicitó vincular `cekide-api-staging`
a `cekide-db-staging` mediante identidad administrada. Tras autorización expresa, se usó
la identidad system-assigned existente (principal `446b2b59-9468-4814-b273-a15a8b44bcbc`)
y se concedieron solo permisos de lectura/escritura de datos. El primer preflight encontró
la base pausada; con autorización para posibles cargos se reanudó, conectó y creó el
principal SQL. Una regla administrativa temporal para una IP fue eliminada al terminar.
T005 sigue abierta para costos/eligibilidad del resto de recursos; la validación y
activación funcional de producción permanecen pendientes. T001-T003, T008,
T013 y T014 se completaron con
validación local. Las 11 pruebas de `RoutineTests` también pasan; T017 permanece abierta
hasta completar T015 y verificar los archivos existentes sin acceder a datos SQL/Blob no
autorizados. El vault contiene los nombres `Authcraft--BaseUrl`, `Brevo--ApiKey`,
`Brevo--FromEmail`, `CekideDatabaseConnectionString`, `Jwt--Key` y `Storage--AccountUrl`;
no se leyeron sus valores. Aún faltan `Jwt--Issuer` y `Jwt--Audience`. Un GET a una ruta
protegida devolvió 401 sin token, confirmando respuesta HTTP pero no una operación SQL
desde la aplicación. El vault de producción recibió solo `CekideDatabaseConnectionString`;
no se consultaron valores. La API prod aún no tiene `KeyVault__Name` ni se reinició porque
faltan las demás claves requeridas.
T021/T022/T026 y la validación funcional siguen pendientes.
Preparacion parcial de T037 aplicada con autorizacion expresa: environments `staging` y
`production` limitados a `main`, aprobacion manual de `leoperetti` sin bypass administrativo
(autoaprobacion permitida), PR/check `test` obligatorio en `main`, variables no sensibles y
confianzas OIDC de las dos identidades de despliegue restringidas a sus environments.
Se desactivaron los tres workflows de despliegue existentes en GitHub. El PR #1 agrega
solo CI y retira una referencia gitlink invalida que impedia checkout. T037/T038 siguen
abiertas: faltan prerrequisitos, validacion OIDC y pipeline de promocion.
Posteriormente se autorizaron y aplicaron permisos SQL a las identidades de despliegue
existentes, sin crear otras: `cekide-api-stagi-id-a925` solo en `cekide-db-staging` y
`cekide-api-id-8230` solo en `cekide-db`, mediante el rol `cekide_schema_migrator`
(`CREATE TABLE` y ALTER/REFERENCES/DML en dbo, sin db_owner ni gestion de usuarios).
Se verificaron permisos efectivos y persistencia en nuevas conexiones; los usuarios
runtime conservan lectura/escritura sin DDL. Con aprobacion se retiro la identidad
user-assigned de despliegue de la Web App staging, preservando la system-assigned.
Por pedido expreso se dejo `CekideOperatorPublicIp` permanente para una unica IP del
operador. No se ejecutaron migraciones, despliegues ni reinicios explicitos.
Posteriormente, con autorizacion expresa, se publicaron/fusionaron PR #2 y #3 tras CI
correcto, agregando un diagnostico manual aislado de los otros cambios locales.
La ejecucion `41900175393` paso OIDC, conexion SQL y permisos propios en ambos entornos,
y denegacion SQL 18456 al contrario; production requirio aprobacion manual del usuario.
El primer diagnostico fallo en production por firewall 40615: el usuario autorizo reglas
temporales por IP exacta del runner y permisos minimos de read/write/delete de reglas
en el servidor `cekide`. Ambas reglas temporales se eliminaron y ARM confirmo su ausencia.
No se ejecutaron migraciones/despliegues. Esto no completa T006 ni T037/T038:
faltan el mantenimiento real, el pipeline unico y los restantes gates.
El script T007 y las pruebas de seleccion del proveedor Key Vault tienen preparacion
local; el preflight solo se ejercito con respuestas Azure CLI simuladas. T007 permanece
abierta por los gates T005/T006 y la ausencia de autorizacion para operar.
T019 y T023 se completaron localmente: pruebas de arranque Development/Testing, proveedor
User Secrets registrado, rechazo de vault ajeno antes de pedir tokens, precedencia real
del proveedor Key Vault con transporte HTTP sintetico y validacion temprana de destinos
SQL/Blob e identidad. La suite Release paso con 233 pruebas unitarias y 202 de integracion.
Esto no acredita permisos Azure ni habilita despliegues. El PR #1 ya fue fusionado por
el usuario; CI de `main` paso en la ejecucion `41895748105`.
Continuacion local posterior al diagnostico: se extrajo `Set-RunnerSqlFirewall.ps1`
para reutilizacion desde diagnostico y futura migracion. Se actualizo el diagnostico
local y CI para usar/probar el helper. Tras autorizacion expresa se publico/fusiono
solo esa extraccion en PR #4, sin incluir los otros cambios locales ni activar despliegues.
Las 16 pruebas de `Test-RunnerSqlFirewall.ps1` pasaron con dobles Azure/IP: validan
scope/identidad/IPv4, regla exacta, errores explicitos, no sobrescritura, limpieza
repetible, preservacion de reglas permanentes y limpieza tras fallo sintetico SQL.
El build Release del diagnostico paso sin advertencias/errores. CI `41902390425`
paso las 16 pruebas offline en PowerShell 7 mas las 172/193 pruebas .NET de `main`.
El diagnostico `41902573356` paso en ambos entornos con aprobacion manual productiva:
identidad, acceso SQL propio, denegacion ajena y creacion/eliminacion verificada de reglas.
ARM confirmo que solo quedan las dos reglas permanentes; no se ejecutaron migraciones.
Es preparacion parcial de T032/T034/T038, no una implementacion completa de esas tareas:
faltan pruebas SQL efimeras, artefacto/bundle, flujo unico, mantenimiento real y gates
de US1/US2. No se cambiaron marcadores de tareas incompletas ni de checklists.
Ver
[data-model.md](data-model.md), [research.md](research.md) y [quickstart.md](quickstart.md).

- [X] T004 Consultar Azure en modo solo lectura y registrar inventario no sensible en `specs\001-environment-separation\data-model.md`: verificar `CEKIDE`, tipo, IDs de recurso, region, servidor SQL, plan/SKU y estado de `cekide-db-staging`, `cekide-db`, `cekide-api` y posible `cekide-api-staging`; identificar disponibilidad del nombre de cuenta Blob y nombres de vaults, sin listar claves ni secretos; aplicar las reglas "Obligatorio", "Único en `CEKIDE`, identificable por entorno y sin datos secretos" y "Todos los recursos Azure de esta API pertenecen al grupo existente `CEKIDE`; no crear grupos adicionales".
- [ ] T005 Evaluar precios, elegibilidad, cuotas, vencimiento, excedentes, operaciones, transferencia, respaldos, auditoria, Actions y dependencias en `specs\001-environment-separation\research.md`; conservar las reglas "Verificar antes de crear/modificar; informar y esperar aprobación explícita si hay costo posible" y "No asumir que una oferta gratuita aplica ni que garantiza factura de $0"; avisar al usuario y esperar aprobacion explicita de cada costo potencial antes de cualquier accion afectada, sin recomendar Free/Shared para produccion.
- [ ] T006 Resolver y registrar en `specs\001-environment-separation\plan.md` y `specs\001-environment-separation\quickstart.md` el mecanismo compatible con la App Service existente para desplegar la API sin atender trafico ni ejecutar jobs dependientes de esquema antes de migrar; no crear ni modificar planes con costo. El usuario aceptó temporalmente el F1 compartido actual y una ventana de mantenimiento con interrupción de solicitudes solo para staging, y autorizó usar la regla SQL existente `AllowAllWindowsAzureIps` para el runner hospedado de GitHub, aceptando su alcance de red global a Azure. Definir compuerta stop/deploy sin reinicio/migracion/start, identidades OIDC y DDL separadas por entorno, y acceso de red desde Actions; documentar backups/PITR y restauracion a base nueva ensayada solo con aprobacion separada, sin fijar RPO/RTO numericos no solicitados. No marcar completa hasta probar el acceso desde Actions y validar el ciclo en staging.
- [ ] T007 Implementar verificaciones previas sin efectos de escritura en `scripts\environments\Test-EnvironmentPrerequisites.ps1`: exigir el inventario verificado, scope `CEKIDE`, identidad y base esperadas, aprobacion de costos aplicable y mecanismo de recuperacion antes de permitir operaciones remotas; rechazar datos faltantes con error explicito y nunca imprimir cadenas de conexion, tokens o valores de secretos.

**Checkpoint**: Costos y decisiones documentados. Si faltan acceso, aprobacion o un
mecanismo seguro, las tareas remotas quedan bloqueadas; no marcar esta fase completada.
Se puede preparar codigo y pruebas locales sin crear recursos ni activar despliegues.

---

## Phase 3: User Story 1 - Entornos separados (Priority: P1) - MVP

**Goal**: Disponer de tres destinos diferenciados, dos aplicaciones Azure independientes,
bases distintas y una cuenta Blob compartida con rutas de archivos por entorno.

**Independent Test**: Con inventario verificado, comprobar API local y LocalDB, Web Apps
`cekide-api-staging` / `cekide-api`, bases `cekide-db-staging` / `cekide-db`, y una cuenta
Blob con el contenedor privado compartido `cekidecontainer` y prefijos `development/`,
`staging/` y `production/`.
Validar rutas con dobles de prueba; la comprobacion remota requiere infraestructura
aprobada. No necesita el flujo de promocion de US3.

### Tests for User Story 1

- [X] T008 [US1] Agregar pruebas en `tests\CekideApi.UnitTests\EnvironmentStorageTests.cs` para mapeo fijo de entorno/contenedor/prefijo, aislamiento de nombres y rechazo de rutas absolutas, traversal y prefijos ajenos, preservando rutas relativas ya guardadas; comprobar "Debe ser único y uno de los tres valores permitidos" y "Un único prefijo fijo por entorno; no se aceptan rutas provistas sin validación", incluidos `development`, `staging` y `production`.

### Implementation for User Story 1

- [ ] T009 [P] [US1] Definir `infra\modules\app-service.bicep` para configurar sin reemplazo las Web Apps existentes `cekide-api-staging` y `cekide-api`, con identidades distintas, .NET 10 y entorno ASP.NET correcto; registrar que staging referencia el plan compartido F1 `AuthCraft/ASP-AuthCraft-bc2b`, no cambiar ni mover ese plan y detener la aplicación de infraestructura hasta resolver T006; no crear un plan con costo sin revisión y aprobación explícitas; mantener "Staging y production deben identificar región y recursos Azure", "Obligatorio y consistente con el nombre" y "Staging y production deben usar identidades distintas".
- [X] T010 [P] [US1] Definir `infra\modules\storage.bicep` para la nueva cuenta compartida `cekideblobaccount` aprobada por el usuario y el contenedor privado `cekidecontainer`; separar provisioning de uso runtime y no crear contenedores por entorno; mantener prefijos fijos y coordinar condiciones ABAC por identidad en T022 para impedir acceso cruzado.
- [ ] T011 [US1] Componer `infra\main.bicep` a scope de resource group y referenciar las bases y servidor SQL verificados como existentes, sin crear/recrear bases o grupos; modelar las reglas "Debe corresponder a una necesidad documentada", "No debe coincidir con la base asignada a otro entorno; development es local" y "Obligatorio para persistencia/telemetría".
- [ ] T012 [US1] Crear parametros no secretos en `infra\environments\staging.bicepparam` y `infra\environments\production.bicepparam`, y actualizar `src\CekideApi\appsettings.Development.json` para LocalDB y el Blob compartido; establecer cuenta/contenedor/prefijo por entorno, sin credenciales versionadas, preservando "No debe incluir credenciales" y la regla de destinos de base independientes.
- [X] T013 [US1] Adaptar `src\CekideApi.Infrastructure\Storage\RoutineStorage.cs` para usar endpoint Blob y TokenCredential/Entra, anteponer exactamente una vez el prefijo del entorno a las rutas relativas de rutinas y validarlas en cada lectura/escritura; retirar la dependencia runtime de claves compartidas y de crear contenedores, conservar contenido/tipo y manejo existente de 404 y propagar explicitamente errores 403/configuracion.
- [X] T014 [US1] Integrar las opciones de storage y seleccion de identidad por entorno en `src\CekideApi.Infrastructure\DependencyInjection.cs`, `src\CekideApi\appsettings.json` y `tests\CekideApi.IntegrationTests\Infrastructure\CekideApiFactory.cs`; mantener el reemplazo por almacenamiento en memoria en Testing y Azurite solo como opcion explicita de pruebas, sin fallback silencioso desde Azure.
- [ ] T015 [US1] Inventariar referencias `RoutineBlobPath` y archivos existentes sin revelar datos personales, y documentar en `specs\001-environment-separation\quickstart.md` el corte al contenedor/prefijo nuevo: copiar y verificar antes de cambiar destino, preservar originales y no borrar ni reescribir datos reales automaticamente; si no existen blobs previos, registrar esa evidencia en vez de inventar una migracion.
- [ ] T016 [US1] Validar Bicep y ejecutar what-if de `infra\main.bicep`; despues de T005 y T007, aplicar solo cambios aprobados de staging/storage a `CEKIDE`, crear `cekide-api-staging` solo si sigue sin existir y configurar sin reemplazar la cuenta/contenedor Blob existentes; verificar que las bases y la API productiva no fueron reemplazadas ni reconfiguradas; diferir cualquier cambio runtime productivo hasta preparar permisos/secretos en T026 y su corte autorizado; registrar IDs/ubicaciones y resultado no sensible en `specs\001-environment-separation\data-model.md`, sin marcar US2 como completada.
- [ ] T017 [US1] Ejecutar las pruebas de T008 y regresiones de `tests\CekideApi.IntegrationTests\RoutineTests.cs`, verificar que el flujo de rutinas preserva rutas relativas y objetos existentes conforme a T015 y registrar resultados en `specs\001-environment-separation\quickstart.md`.
- [ ] T018 [US1] Validar la topologia de `infra\main.bicep` con consultas de solo lectura y comprobar que development conserva LocalDB y cada aplicacion Azure tiene su propio destino SQL y prefijo dentro del contenedor compartido; registrar en `specs\001-environment-separation\quickstart.md` cualquier acceso runtime pendiente de US2, sin habilitar produccion ni afirmar aislamiento Azure antes de las pruebas de autorizacion.

**Checkpoint**: Topologia y adaptador de storage listos. El MVP es un incremento tecnico,
no una habilitacion productiva: permisos y secretos deben completarse con US2.

---

## Phase 4: User Story 2 - Configuracion y datos protegidos (Priority: P1)

**Goal**: Aislar secretos, permisos y datos; desarrollo usa User Secrets y las aplicaciones
Azure usan vaults/identidades distintos y no pueden acceder a datos de otros entornos.

**Independent Test**: Arrancar Development sin consultar Key Vault; comprobar error visible
por configuracion faltante o destino ajeno en Azure; verificar con cada identidad que
funcionan sus recursos y se deniegan el vault, base y blobs de otros entornos, sin mostrar
secretos ni copiar datos reales a staging. No necesita promocion automatica de US3.

### Tests for User Story 2

- [X] T019 [P] [US2] Agregar pruebas de proveedores, precedencia y fallo de inicio en `tests\CekideApi.IntegrationTests\EnvironmentConfigurationTests.cs`: Development usa User Secrets/no Key Vault, Staging/Production requieren su vault y valores obligatorios, valores de secretos no aparecen en errores y Testing sigue funcionando con configuracion falsa sin conexiones Azure.
- [ ] T020 [P] [US2] Preparar `scripts\environments\Test-EnvironmentIsolation.ps1` para pruebas reales por identidad con objetos sinteticos: lectura/escritura/borrado permitidos solo bajo el prefijo propio y denegados bajo los otros dos, vault ajeno y SQL ajeno denegados; cumplir "Debe impedir leer, sobrescribir o borrar blobs de otros entornos" y "No reutilizar claves de cuenta ni identidad con acceso global entre aplicaciones Azure"; fallar si un acceso cruzado funciona y no imprimir valores secretos.

### Implementation for User Story 2

- [ ] T021 [P] [US2] Definir `infra\modules\key-vault.bicep` para un vault por entorno Azure, RBAC, soft delete/purge protection y auditoria con destino/costo revisados; runtime solo tiene `Key Vault Secrets User` en su vault y la administracion de secretos usa otra identidad; preservar "Runtime solo lectura sobre el almacén de su entorno" y "Privilegio mínimo y alcance restringido".
- [ ] T022 [US2] Definir asignaciones de autorizacion en `infra\modules\environment-access.bicep`: cada identidad runtime Azure y la identidad Entra de development reciben una condicion ABAC en el contenedor `cekidecontainer` limitada a su prefijo, sin roles heredados/globales que permitan acceso cruzado; cubrir lectura, escritura, listado y borrado; separar identidades de despliegue, migracion y runtime, verificar permisos SQL de cada base y no conceder DDL a la API; integrar los modulos en `infra\main.bicep`.
- [X] T023 [US2] Exigir vault/configuracion valida en `src\CekideApi\Program.cs`, eliminar `KeyVault:Name` de `src\CekideApi\appsettings.Development.json` y aplicar validacion temprana de opciones requeridas sin consultas desde Application a DbContext; conservar precedencia documentada del proveedor, fallar visiblemente sin secreto/vault o ante destino SQL/Blob ajeno y mantener los contratos nuevos junto a sus implementaciones si fueran necesarios.
- [ ] T024 [US2] Documentar claves, clasificacion y rotacion en `specs\001-environment-separation\quickstart.md`: User Secrets local, vault por entorno, `:` como `--`, `KeyVault:Name`/endpoint/contenedor/prefijo no secretos, responsables y reinicio/recarga verificado; conservar "No incluye el valor del secreto", "Obligatorio; sin reutilizar valor o credencial entre entornos", "Development usa User Secrets; staging/production usan Key Vault", "En Key Vault, `:` se representa como `--`" y "Debe existir antes de producción".
- [ ] T025 [US2] Crear `scripts\environments\Test-EnvironmentConfiguration.ps1` para verificar nombres de claves requeridas, ajustes de entorno, destino SQL y permisos sin volcar valores; rechazar referencias a recursos de otro entorno, vault ausente y uso de claves de cuenta Blob; usar errores explicitos y ejecutar el gate de T007 antes de acciones de configuracion remota.
- [ ] T026 [US2] Validar/ejecutar what-if y aplicar los modulos de T021-T022 mediante `infra\main.bicep` solo tras aprobacion de costos y permisos; poblar secretos distintos por canales protegidos fuera de logs/archivos/artefactos, comprobar que no se reutilizan credenciales y registrar solo nombres, scopes y resultado en `specs\001-environment-separation\data-model.md`, cumpliendo "Debe ser exclusiva del entorno y nunca contener credenciales versionadas".
- [ ] T027 [US2] Preparar datos sinteticos o anonimizados en staging mediante un procedimiento documentado en `specs\001-environment-separation\quickstart.md`; cumplir "Production permite datos reales; staging solo sintéticos/anonimizados", sin copiar produccion ni sobrescribir datos reales existentes sin una decision explicita de proteccion.
- [ ] T028 [US2] Ejecutar `scripts\environments\Test-EnvironmentIsolation.ps1` con cada identidad real y `scripts\environments\Test-EnvironmentConfiguration.ps1` para los tres entornos, usando blobs sinteticos propios y pruebas no destructivas sobre SQL/vault; registrar denegaciones comprobadas, sin eliminar objetos ajenos ni usar credenciales administrativas para simular una identidad runtime.
- [ ] T029 [US2] Ejecutar T019 y las pruebas impactadas de storage/configuracion, comprobar rotacion con secretos de prueba y reinicio/recarga controlado y registrar resultados en `specs\001-environment-separation\quickstart.md`; no alterar secretos productivos como prueba.
- [ ] T030 [US2] Completar la matriz entorno-recurso-identidad de `specs\001-environment-separation\data-model.md` con configuracion no sensible, permisos, retencion y evidencia de aislamiento; verificar las reglas "Obligatorio y consistente con el nombre", "Staging y production deben usar identidades distintas" y la separacion de tres destinos SQL, dejando cualquier comprobacion pendiente explicitamente bloqueada.

**Checkpoint**: US1 y US2 verificadas; no hay credenciales de cuenta compartidas, acceso
cruzado ni necesidad de secretos productivos para las pruebas locales.

---

## Phase 5: User Story 3 - Promocion controlada a produccion (Priority: P2)

**Goal**: GitHub Actions construye una vez, despliega `main` a staging, migra despues del
despliegue, valida y promueve el mismo paquete solo con aprobacion manual; los fallos
bloquean etapas posteriores y protegen los datos reales.

**Independent Test**: Un PR no publica. Un push a `main` publica y migra staging.
Fallo de migracion/smoke bloquea produccion. Un candidato valido espera aprobacion
manual; tras aprobar, produccion recibe el hash identico, migra y valida.
En un destino de ensayo, simular fallo de migracion y confirmar que no atiende trafico
incompatible ni ejecuta `Down` o restauracion automatica sobre datos reales.

### Tests for User Story 3

- [ ] T031 [P] [US3] Crear pruebas automatizadas SQL Server en `tests\CekideApi.IntegrationTests\DeploymentMigrationTests.cs` usando una base efimera explicitamente configurada y con guard contra nombres productivos: aplicar las migraciones versionadas, comprobar preservacion de datos sinteticos, repeticion sin cambios y fallo controlado; no usar SQLite como sustituto del motor SQL Server ni saltar pruebas silenciosamente si falta el destino requerido.
- [ ] T032 [P] [US3] Crear `scripts\deployment\Test-DeploymentPipeline.ps1` para verificar el flujo unico, triggers PR/main, dependencia de staging, referencias al environment production, ausencia de rebuild/re-publish entre destinos, verificacion de hash y prohibicion de rollback destructivo; preparar pruebas de rechazo de artefacto alterado y fallo de migracion con recursos falsos sin desplegar.

### Implementation for User Story 3

- [ ] T033 [US3] Implementar `scripts\deployment\New-ReleaseArtifact.ps1` para construir/publicar una sola vez API y migration bundle desde el mismo commit y generar manifiesto sin secretos con hash SHA-256, version, commit y destino runtime del bundle; conservar "El mismo artefacto validado en staging debe promoverse a production", "Obligatorio para trazabilidad" y "Debe conservarse y coincidir en staging y producción".
- [ ] T034 [P] [US3] Implementar `scripts\deployment\Invoke-EnvironmentMigration.ps1` con base/servidor esperados obligatorios y credencial/identidad segura de DDL del entorno, sin fallback a LocalDB ni secretos en argumentos registrados; ejecutar solo el bundle verificado, serializar por entorno, inspeccionar historial/estado antes de reintentar y propagar fallos; cumplir "Las migraciones se ejecutan automáticamente tras el despliegue de la API en Azure; fallo bloquea los pasos siguientes".
- [ ] T035 [P] [US3] Implementar `scripts\deployment\Test-ReleaseReadiness.ps1` con comprobaciones acotadas de arranque, autenticacion, version/entorno, SQL y Blob mediante datos sinteticos y endpoints existentes; usar timeout/error explicito, no revelar secretos y no modificar datos reales; si hace falta una superficie nueva de salud, documentar primero su contrato no sensible y aprobar el cambio de alcance antes de agregarla.
- [ ] T036 [US3] Implementar el control de trafico/jobs acordado en T006 en `scripts\deployment\Invoke-EnvironmentDeployment.ps1`, integrando paquete verificado, despliegue, migracion post-despliegue y validacion antes de liberar la aplicacion; mantener trafico y trabajos dependientes de esquema bloqueados si la migracion falla y permitir recuperar binarios previos solo tras verificar compatibilidad del esquema.
- [ ] T037 [US3] Documentar y configurar environments GitHub `staging` y `production`, OIDC separado por destino, scopes minimos, variables no sensibles, proteccion de `main` y revisores requeridos de produccion en `specs\001-environment-separation\quickstart.md`; verificar que el plan/visibilidad del repositorio soporta aprobacion manual y que no existe bypass no autorizado; bloquear si falta esta capacidad y conservar "Obligatorio antes de publicación a production".
- [ ] T038 [US3] Consolidar `.github\workflows\deploy-cekide-api.yml` y retirar el despliegue competidor de `.github\workflows\main_cekide-api.yml`: PR restaura/compila/prueba sin secretos ni despliegue, push de `main` genera un unico release, staging despliega/migra/valida y production depende de exito y aprobacion manual; usar OIDC, descargar artefacto por ID inmutable y comprobar hash, sin `build`/`publish` en los jobs de despliegue; incorporar la suite SQL de T031 sin crear recursos facturables implicitamente.
- [ ] T039 [US3] Completar en `.github\workflows\deploy-cekide-api.yml` serializacion de toda la secuencia despliegue-migracion por destino, sin cancelar una migracion en curso, y rechazo de promociones obsoletas que desordenen releases; registrar manifiesto/resumen con entorno, Web App, commit, version, hash, validacion, migracion, fecha y referencia a aprobacion, cumpliendo "Production solo tras staging aprobado", "Staging y production deben identificar aplicaciones distintas", "Fallo bloquea la siguiente transición", "Obligatoria para publicaciones completadas" y "Obligatorio para production".
- [ ] T040 [US3] Implementar la comprobacion previa de recuperacion en `scripts\deployment\Test-DatabaseRecovery.ps1` y conectarla al job productivo de `.github\workflows\deploy-cekide-api.yml`: verificar backups/PITR, retencion, restaurabilidad ensayada, identidad y referencia temporal antes de cambios de riesgo; cumplir "Debe existir antes de migraciones que puedan afectar datos reales y verificarse su restaurabilidad" y bloquear si falta evidencia, sin crear automaticamente copias facturables.
- [ ] T041 [US3] Documentar recuperacion operativa en `specs\001-environment-separation\quickstart.md`: retener ultimo artefacto estable, inspeccionar migracion parcial, preferir correccion hacia delante, prohibir `Down`/restore automaticos, restaurar a nueva base solo con aprobacion y reconciliar escrituras antes del corte; incluir recuperacion de archivos conforme a T015 y mantener la trazabilidad de la publicacion fallida.
- [ ] T042 [US3] Ejecutar `scripts\deployment\Test-DeploymentPipeline.ps1` y `tests\CekideApi.IntegrationTests\DeploymentMigrationTests.cs`, verificar falta de configuracion, hash alterado, reejecucion y fallo sin siguiente etapa; registrar en `specs\001-environment-separation\quickstart.md` resultados y limitaciones, sin declarar aprobacion manual real validada mediante analisis estatico de YAML.
- [ ] T043 [US3] Ejecutar una publicacion de ensayo desde `main` por `.github\workflows\deploy-cekide-api.yml` tras completar gates: comprobar staging, migracion post-despliegue, pruebas fallidas que bloquean y espera de aprobacion manual; ensayar fallo productivo en destino aislado con datos sinteticos y sin apuntar a `cekide-db`, preservando evidencias no sensibles en `specs\001-environment-separation\quickstart.md`.
- [ ] T044 [US3] Con ventana y aprobacion manual explicitas, realizar la primera promocion real por `.github\workflows\deploy-cekide-api.yml` a `cekide-api`, verificar hash identico al de staging, bloqueo de trafico/jobs hasta migracion y salud satisfactoria, backup y preservacion de datos; registrar el resultado en `specs\001-environment-separation\quickstart.md` y mantener bloqueado si no existe autorizacion, sin inyectar fallos deliberados en produccion.

**Checkpoint**: Estados operativos:
`Built -> DeployedToStaging -> StagingMigration -> StagingValidated ->
AwaitingProductionApproval -> DeployedToProduction -> ProductionMigration ->
ProductionValidated`. Cualquier fallo detiene la secuencia; una aprobacion de codigo o
de costos no sustituye la aprobacion manual de cada release.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Cerrar evidencia, documentacion y validacion sin ampliar el alcance.

- [ ] T045 [P] Actualizar instrucciones de desarrollo y operacion en `specs\001-environment-separation\quickstart.md` con los comandos reales de provisionamiento aprobado, User Secrets, identidad Entra, rotacion, CI/CD y recuperacion; distinguir recursos verificados, tareas locales y validaciones remotas pendientes.
- [ ] T046 [P] Revisar `infra\main.bicep`, `infra\modules\key-vault.bicep` y `.github\workflows\deploy-cekide-api.yml` para confirmar costos aprobados, scopes/identidades, ausencia de secretos versionados o en artefactos, permisos minimos y auditoria visible; registrar evidencia no sensible y pendientes en `specs\001-environment-separation\checklists\requirements.md`.
- [ ] T047 Ejecutar `dotnet test cekide-api.slnx`, compilacion/validacion Bicep y pruebas de scripts/pipeline disponibles; recorrer todos los escenarios de `specs\001-environment-separation\quickstart.md`, limpiando solo objetos sinteticos creados por estas pruebas, sin borrar recursos compartidos o datos reales.
- [ ] T048 Actualizar trazabilidad FR-001 a FR-020 y SC-001 a SC-013 en `specs\001-environment-separation\checklists\requirements.md` y estado de `specs\001-environment-separation\tasks.md`; no marcar tareas remotas completas sin evidencia persistente ni eliminar bloqueos de costo/aprobacion, y dejar los documentos de diseno consistentes con lo realmente entregado.

---

## Dependencies & Execution Order

### Phase Dependencies

```text
Setup T001-T003
       |
Foundational T004 -> T005 -> T006 -> T007
       |
US1 T008-T018 -> US2 T019-T030 -> US3 T031-T044
                                         |
                              Polish T045-T048
```

La secuencia muestra dependencias de cierre, no obliga a esperar para preparar archivos
locales independientes. Ninguna tarea con Azure debe eludir T005/T007.

### Task Dependencies

| Tarea(s) | Requisitos reales |
|---|---|
| T001, T002 | Independientes |
| T003 | T001; comprobar herramientas disponibles antes de cambiar su configuracion |
| T004 | T001-T003; acceso de lectura a la suscripcion |
| T005 | T004; comunicar riesgos antes de cualquier aprobacion de costos |
| T006 | T004-T005; confirmar mecanismo y decisiones operativas |
| T007 | T004-T006 |
| T008-T010 | T007; T008 define pruebas antes de implementar storage |
| T011 | T009-T010 |
| T012 | T011 |
| T013 | T008, T012 |
| T014 | T013 |
| T015 | T004, T013-T014 |
| T016 | T007, T009-T012, T015; permiso/costo aprobados y what-if revisado |
| T017 | T008, T013-T015; puede ejecutarse antes de T016 con dobles locales |
| T018 | T016-T017; no sustituye pruebas de autorizacion de US2 |
| T019-T021 | T018; preparacion local posible despues de T007 sin tocar archivos de US1 |
| T022 | T021 y recursos/identidades de US1 definidos |
| T023 | T019; coordinar edicion de configuracion con T012-T014 ya completadas |
| T024 | T021-T023 |
| T025 | T007, T023-T024 |
| T026 | T005, T007, T021-T025; aprobaciones y permisos administrativos |
| T027 | T026; autorizar cualquier preparacion que pueda afectar datos existentes |
| T028 | T020, T025-T027 |
| T029 | T019, T023-T028 |
| T030 | T018, T028-T029 |
| T031-T032 | T030; preparacion local permitida antes sin activar workflows |
| T033 | T031-T032 |
| T034-T035 | T033; manifiesto y formatos del paquete/bundle definidos antes de consumirlos |
| T036 | T006, T033-T035 |
| T037 | T005, T007, T030, T036; acceso administrativo a GitHub/Entra y aprobador definido |
| T038 | T031-T037; no activar un camino transitorio que salte staging o aprobacion |
| T039 | T038 |
| T040 | T006, T039 |
| T041 | T015, T036, T040 |
| T042 | T031-T041 |
| T043 | T042; autorizacion/costos de destinos de ensayo |
| T044 | T043, T005/T007 vigentes, evidencia T040 y aprobacion manual de release |
| T045-T046 | T044 o cierre documentado como bloqueado; archivos de salida distintos |
| T047 | T045-T046 |
| T048 | T047; registrar bloqueos en vez de simular un cierre |

### User Story Dependencies

- **US1 (P1)**: requiere fundamentos; entrega infraestructura y adaptador local testeable.
- **US2 (P1)**: integra los scopes e identidades de US1; puede preparar pruebas/modulos
  propios en paralelo, pero su aceptacion exige los recursos y rutas de US1.
- **US3 (P2)**: requiere US1 y US2 verificadas para publicar con seguridad. Las pruebas SQL
  y scripts pueden prepararse antes; no habilitar despliegues mientras los gates fallen.
- Las pruebas de cada historia son independientes del flujo de historias posteriores,
  pero no de sus recursos/prerrequisitos operativos.

### Parallel Opportunities

- Setup: T001 y T002; T003 sigue a T001.
- US1: T009 y T010; T008 tambien es independiente de esos modulos.
- US2: T019, T020 y T021 en archivos distintos.
- US3: T031 y T032; despues de T033, T034 y T035 en archivos distintos.
- Cierre: T045 y T046.
- No ejecutar dos tareas que escriban `infra\main.bicep`, el mismo appsettings, el mismo
  workflow o la misma guia en paralelo. Las operaciones de despliegue/migracion del mismo
  destino nunca son paralelas, aunque existan distintos workflow runs.

## Parallel Example: User Story 1

```text
Tras T007:
T008: pruebas de rutas en tests\CekideApi.UnitTests\EnvironmentStorageTests.cs
T009: Web Apps en infra\modules\app-service.bicep
T010: cuenta y contenedores en infra\modules\storage.bicep
Unir resultados en T011; T013 debe esperar las pruebas y parametros.
```

## Parallel Example: User Story 2

```text
Tras cierre US1:
T019: pruebas de configuracion en tests\CekideApi.IntegrationTests\EnvironmentConfigurationTests.cs
T020: pruebas de permisos en scripts\environments\Test-EnvironmentIsolation.ps1
T021: vaults en infra\modules\key-vault.bicep
T022 integra scopes despues de T021; T028 ejecuta autorizacion real despues de T026.
```

## Parallel Example: User Story 3

```text
Tras cierre US2:
T031: pruebas SQL en tests\CekideApi.IntegrationTests\DeploymentMigrationTests.cs
T032: validacion de flujo en scripts\deployment\Test-DeploymentPipeline.ps1
Tras ambas: T033 define el artefacto; luego T034 y T035 preparan migracion y smoke en paralelo.
T036 los integra; T038 consolida workflows solo despues de configurar protecciones.
```

## Implementation Strategy

### MVP First (User Story 1)

1. Completar Setup y fundamentos, sin suponer aprobacion de costos.
2. Entregar US1: topologia declarativa, destinos identificados y storage con prefijos.
3. Ejecutar sus pruebas locales y comprobaciones de infraestructura autorizadas.
4. Detenerse ante bloqueos; el MVP no habilita produccion antes de US2/US3.

### Incremental Delivery

1. US1 entrega separacion de entornos e integracion de storage.
2. US2 completa secretos y autorizacion y prueba denegacion cruzada.
3. US3 agrega promocion, migraciones post-despliegue y recuperacion verificable.
4. Finalizar pruebas transversales y evidencia; dejar pendientes reales visibles.

### Parallel Team Strategy

Preparar modulos/pruebas de una misma historia en archivos distintos segun las dependencias.
Mantener un responsable para los archivos compartidos y otro para las operaciones
remotas aprobadas. No usar paralelismo para saltar costos, controles o aprobacion manual.

## Notes

- No crear recursos Azure durante `/speckit-tasks`; todas las casillas quedan pendientes.
- Las restricciones entre comillas conservan el texto original de `data-model.md`.
- No introducir nuevas interfaces dispersas ni cambiar endpoints o dominio por conveniencia.
- Los IDs Azure, nombres de claves y referencias de evidencia son metadatos; nunca almacenar
  contrasenas, connection strings completas, tokens, datos reales o valores de secretos.
- La validacion de YAML no demuestra que las protecciones GitHub esten activas: T037/T043
  deben comprobarlo efectivamente.
- Una restauracion requiere decision operativa; no existe promesa de perdida cero por
  tener backups. No probar fallos deliberados contra bases con datos reales.
