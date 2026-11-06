# Data Model: Entornos aislados de API

Este feature no añade entidades persistidas a la API. El modelo siguiente describe los
objetos operativos que deben inventariarse, configurarse y auditarse para administrar
los tres entornos.

**Destino Blob vigente (2026-11-04)**: `cekideblobaccount`, CEKIDE/Brazil South,
StorageV2, Standard_LRS + Hot, contenedor privado `cekidecontainer`, sin shared key,
HTTPS/TLS 1.2. Creado con aprobacion expresa mediante `cekide-blob-replacement`.
Cada entorno paso 18 comprobaciones reales (54 total) con su identidad propia,
incluidas system-assigned desde SCM staging/production. Objetos sinteticos y carpetas
SCM eliminados correctamente. Configuracion remota y flujo de API aun pendientes.
El inventario anterior de `cekidecontainer` se conserva como historico: esa cuenta
todavia aparecia en ARM al preparar el reemplazo y no fue modificada.

## Entorno

Representa un destino de desarrollo o ejecución.

| Campo | Descripción | Regla |
|---|---|---|
| `name` | Identificador: `development`, `staging` o `production` | Debe ser único y uno de los tres valores permitidos |
| `purpose` | Desarrollo local, validación previa a release o servicio real | Obligatorio y consistente con el nombre |
| `location` | Equipo/local o región de Azure | Staging y production deben identificar región y recursos Azure |
| `resourceGroup` | Grupo lógico de recursos | Todos los recursos Azure de esta API pertenecen al grupo existente `CEKIDE`; no crear grupos adicionales |
| `dataPolicy` | Tipo de datos permitidos | Production permite datos reales; staging solo sintéticos/anonimizados |
| `configuration` | Ajustes no sensibles asociados al entorno | No debe incluir credenciales |
| `runtimeIdentity` | Identidad con la que se ejecuta la aplicación | Staging y production deben usar identidades distintas |

### Relaciones

- Un entorno contiene uno o más recursos de ejecución y persistencia.
- Cada entorno tiene exactamente un destino de base de datos propio: LocalDB para
  development y una base Azure SQL distinta para cada uno de staging y producción.
- Staging y producción ejecutan instancias independientes de la API en dos Azure App
  Service Web Apps distintas, ambas dentro del grupo `CEKIDE`.
- Las bases de staging y producción no se comparten ni reciben conexiones desde el otro
  entorno; cada una tiene una cadena de conexión y credenciales propias.
- Cada entorno de Azure se asocia a un vault e identidad propios.
- Los recursos de staging y production están dentro del mismo grupo `CEKIDE`, pero son
  recursos distintos con identidades, permisos, datos y configuración aislados.
- Los tres entornos usan una única cuenta Azure Blob Storage. Cada entorno escribe y lee
  bajo su propio prefijo fijo; el prefijo identifica la ruta, pero la autorización debe
  restringir el acceso cruzado mediante un ámbito de permisos efectivo.
- Un entorno puede tener varias publicaciones; solo una versión de aplicación está activa
  en un momento dado.
- Antes de crear o modificar recursos se verifican precio, elegibilidad y cargos posibles.
  Los recursos nuevos o cambios que puedan costar dinero requieren aviso y aprobación
  explícita del usuario antes de continuar.

## Recurso de entorno

Inventario de cada componente necesario para operar la API.

| Campo | Descripción | Regla |
|---|---|---|
| `resourceName` | Nombre Azure/local del recurso | Único en `CEKIDE`, identificable por entorno y sin datos secretos |
| `resourceType` | App Service, SQL Database, Storage Account, Key Vault, Application Insights, etc. | Debe corresponder a una necesidad documentada |
| `environment` | Entorno propietario o `shared` | Un único entorno, salvo recursos declarados explícitamente compartidos; solo Blob Storage puede ser compartido |
| `purpose` | Función del recurso | Obligatorio |
| `databaseTarget` | Base de datos asignada al entorno | No debe coincidir con la base asignada a otro entorno; development es local |
| `connectionConfiguration` | Ubicación de la cadena de conexión y credenciales | Debe ser exclusiva del entorno y nunca contener credenciales versionadas |
| `identityOrAccessPolicy` | Identidad/rol que permite acceso | Privilegio mínimo y alcance restringido |
| `retentionAndRecovery` | Retención, respaldo y restauración si aplica | Obligatorio para persistencia/telemetría |
| `costAssessment` | Precio, oferta, región, suscripción, cuotas, consumo y cargos indirectos | Verificar antes de crear/modificar; informar y esperar aprobación explícita si hay costo posible |
| `freeOfferConditions` | Elegibilidad, cuota, duración y costo de excedentes | No asumir que una oferta gratuita aplica ni que garantiza factura de $0 |

### Inventario reportado y verificacion de solo lectura (2026-11-03)

| Entorno | Recurso | Nombre reportado |
|---|---|---|
| staging | Base de datos | `cekide-db-staging`, verificada en servidor `cekide`, `CEKIDE`, Brazil South |
| producción | Base de datos | `cekide-db`, verificada en servidor `cekide`, `CEKIDE`, Brazil South |
| producción | API/App Service | `cekide-api`, verificada en `CEKIDE`, West US 3, estado Running |
| staging | API/App Service | `cekide-api-staging`, no aparece en el inventario de `CEKIDE`; disponibilidad global no verificada |
| todos | Blob Storage | Cuenta `cekidecontainer` en `CEKIDE`, Brazil South, `StorageV2`, `Standard_RAGRS`, tier `Cool`, `Succeeded`; contenedor privado `cekidecontainer` creado y verificado el 2026-11-04 |

La cuenta Storage fue creada por el usuario, no por esta implementacion. A peticion del
usuario, se creo el contenedor `cekidecontainer` en esa cuenta con acceso publico deshabilitado
y se verifico su existencia. La cuenta permite shared key, pero la API no la utiliza. No se
consultaron claves ni contenido. Se usara este contenedor compartido por decision del usuario. La separacion por prefijos
requiere condiciones Azure ABAC independientes por identidad y no queda garantizada por el
nombre del blob ni por la validacion de la API. No se confirmo oferta gratuita aplicable a
esta cuenta ni a Key Vault; considerar costos de almacenamiento y operaciones antes de
habilitar consumo. Ver [research.md](research.md).

El grupo `CEKIDE` esta verificado, con ubicacion de metadatos Brazil South y estado
Succeeded. La consulta de recursos tambien confirma la identidad user-assigned
`cekide-api-id-8230` en West US 3; no se verifico su asignacion ni permisos.
No aparecen Key Vaults en el inventario del grupo.

Las bases tienen SKU `GP_S_Gen5`, capacidad 2, `useFreeLimit=true`,
`freeLimitExhaustionBehavior=AutoPause`, pausa automatica a los 60 minutos y estado Paused
en la consulta. Ambas tienen backup Local y retencion de corto plazo de 7 dias,
diferenciales cada 12 horas. Esto verifica configuracion, no factura ni restauracion.

La Web App `cekide-api` referencia el plan Windows `ASP-AuthCraft-bc2b` en el grupo
`AuthCraft`, SKU `F1` / Free, West US 3, con dos sitios. No modificarlo ni moverlo:
es compartido con otra carga y no cumple la topologia productiva propuesta en `CEKIDE`.
La respuesta consultada no informa identidad administrada asignada a la Web App.
Queda pendiente decidir un plan productivo apto y revisar costos sin afectar AuthCraft.

IDs relativos de recursos verificados (sin credenciales):

- `CEKIDE/Microsoft.Web/sites/cekide-api`.
- `CEKIDE/Microsoft.Sql/servers/cekide/databases/cekide-db-staging`.
- `CEKIDE/Microsoft.Sql/servers/cekide/databases/cekide-db`.
- `AuthCraft/Microsoft.Web/serverfarms/ASP-AuthCraft-bc2b` (dependencia existente;
  no autoriza crear recursos nuevos fuera de `CEKIDE`).

La cuenta Blob `cekidecontainer` ya existe; se creo en ella el contenedor privado
`cekidecontainer` a peticion del usuario el 2026-11-04. No se verifico oferta gratuita.
La primera consulta ARM no encontro entonces la Web App de staging en `CEKIDE`; T004 se
completo con el inventario de ese momento. No se imprimieron valores secretos ni se
consultaron datos de las bases.

### Actualizacion de inventario de solo lectura (2026-11-04)

La enumeracion ARM del grupo `CEKIDE` confirmo su estado `Succeeded` y estos recursos:

| Recurso | Tipo | Ubicacion |
|---|---|---|
| `cekide-api` | `Microsoft.Web/sites` | West US 3 |
| `cekide-api-id-8230` | `Microsoft.ManagedIdentity/userAssignedIdentities` | West US 3 |
| `cekide` (servidor) y las bases `cekide-db-staging`, `cekide-db` | `Microsoft.Sql` | Brazil South |
| `cekidecontainer` | `Microsoft.Storage/storageAccounts` | Brazil South |

Una consulta ARM GET directa de `cekide-api-staging` devolvio `ResourceNotFound` en
`CEKIDE`; `az keyvault list` no devolvio vaults para el grupo. Una consulta ARM GET
confirmo que `cekide-api` esta Running, en West US 3, y referencia el plan
`AuthCraft/ASP-AuthCraft-bc2b`; la Web App no reporta identidad asignada. La cuenta `cekidecontainer`
ya existe, pero no se verifico nombre libre para otra cuenta porque no se necesita crearla.
No se consultaron claves, valores de secretos ni filas SQL.

La consulta ARM GET del plan `ASP-AuthCraft-bc2b` confirmo `F1` / `Free`, Windows,
West US 3 y dos sitios asociados; el plan esta en `AuthCraft`, fuera de `CEKIDE`. El
usuario eligio no moverlo y preparar hosting independiente de Cekide en `CEKIDE`.
Produccion queda bloqueada: F1 no se considera apto para produccion y no se aprobo
ningun costo. Las consultas fueron de lectura; no se aprovisiono ni cambio ningun recurso.

### Actualizacion de staging y aprobacion Blob (2026-11-04)

Despues de que el usuario informara que creo `cekide-api-staging`, una consulta ARM GET
de solo lectura confirmo que la Web App existe en `CEKIDE`, esta `Running` y esta en West
US 3. Su `serverFarmId` referencia el plan compartido `ASP-AuthCraft-bc2b` del grupo
`AuthCraft`, fuera de `CEKIDE`; no se modifico ni se movio ese plan. La existencia de la
Web App no acredita identidad administrada, configuracion .NET, secretos, permisos,
aislamiento de datos ni recuperabilidad. Esta dependencia compartida deja pendiente T006
y no habilita despliegues.

Una consulta de configuración no secreta confirmó `httpsOnly=true`, TLS mínimo 1.2,
`publicNetworkAccess=Enabled`, identidades system-assigned y user-assigned, y reglas
`Allow all` para la aplicación y SCM. FTPS está limitado a `FtpsOnly`. La ruta de red
desde GitHub Actions aún no se verificó.

El usuario acepto posibles cargos de Blob Storage asociados a la cuenta y al contenedor
existentes y posibles cargos de operaciones de Key Vault. No se modifico la cuenta Blob,
no se configuraron permisos RBAC/ABAC y no se realizaron pruebas de acceso; estas siguen
bloqueadas hasta validar ABAC por prefijo. Posteriormente el usuario autorizó la reanudación
y el uso de SQL para vincular staging; esa aprobación no se extiende a hosting u otros
recursos.

El usuario indicó que staging ya está creado, rechazó planes de hosting con costo
adicional y aceptó el F1 compartido actual como excepción temporal solo para staging; no
se modificará el plan existente. Para recuperación, eligió reutilizar los backups
automáticos/PITR SQL existentes. La configuración observada registra retención local de
corto plazo de 7 días; no se ha ensayado una restauración. Cualquier ensayo requiere
aprobación separada y debe restaurar a una base nueva, sin sobrescribir la original.
El usuario solicitó vincular `cekide-api-staging` a `cekide-db-staging` mediante identidad
administrada. Una lectura previa no encontró identidades; una verificación posterior
encontró identidades system-assigned y user-assigned en la Web App. No se concedieron
permisos a la identidad user-assigned. Tras autorización expresa, se creó en
`cekide-db-staging` el usuario `cekide-api-staging` asociado al principal system-assigned
de la Web App y se asignaron solo los roles `db_datareader` y `db_datawriter`, sin permisos
DDL. La base estaba pausada, se reanudó con autorización de posibles cargos y quedó
`Online`; tiene auto-pausa de 60 minutos. Para la operación se añadió una regla SQL
temporal de firewall limitada a una IP y se eliminó al terminar. La regla preexistente
`AllowAllWindowsAzureIps` sigue habilitada. La ruta de red desde Actions sigue pendiente.

### Key Vaults creados (2026-11-04)

| Entorno | Vault | Región / SKU | Configuración | Acceso y contenido |
|---|---|---|---|---|
| Staging | `cekide-key-vault-staging` | West US 3 / Standard | RBAC, soft delete de 90 días, purge protection | `Key Vault Secrets User` asignado a la identidad system-assigned de staging en el scope del vault |
| Producción | `cekide-key-vault-prod` | West US 3 / Standard | RBAC, soft delete de 90 días, purge protection | `Key Vault Secrets User` asignado a la identidad system-assigned de producción; contiene `CekideDatabaseConnectionString` |

El acceso público quedó habilitado. El usuario aceptó posibles cargos por operaciones de
Key Vault. Para staging, `KeyVault__Name=cekide-key-vault-staging` está configurado como
ajuste de App Service. Se guardó la cadena de conexión no secreta `CekideDatabaseConnectionString`
en el vault con autenticación Managed Identity y destino exclusivo a `cekide-db-staging`.
Una ruta protegida respondió HTTP 401 sin token; no se validó una operación SQL desde la API.
El inventario de nombres del vault muestra `Authcraft--BaseUrl`, `Brevo--ApiKey`,
`Brevo--FromEmail`, `CekideDatabaseConnectionString`, `Jwt--Key` y `Storage--AccountUrl`;
faltan `Jwt--Issuer` y `Jwt--Audience`. En producción aún no se configuró
`KeyVault__Name` en App Service. Cada entorno puede usar los mismos
nombres de secreto en su vault independiente; no hacen falta prefijos de entorno. En el proveedor de
configuración de Azure, `--` en un nombre de secreto representa `:` (por ejemplo,
`Email--ApiKey` corresponde a `Email:ApiKey`). Los valores deben cargarse por un canal
seguro y no documentarse aquí. No se ha autorizado un despliegue de producción. La API
productiva todavía no consume el vault.

### Vinculación SQL de producción (2026-11-04)

Con autorización expresa para posibles cargos e interrupción, se habilitó la identidad
system-assigned de `cekide-api` (`a71db939-b848-448f-b0e1-f11adfb616d0`) y se creó en
`cekide-db` el usuario externo `cekide-api`, con solo `db_datareader` y `db_datawriter`
(sin DDL). `cekide-db` quedó `Online`, con auto-pausa de 60 minutos. La cadena de conexión
de Managed Identity se guardó como `CekideDatabaseConnectionString` en el vault prod.
Se quitaron el rol temporal `Key Vault Secrets Officer` del operador y la regla de firewall
temporal de una IP; queda la regla preexistente `AllowAllWindowsAzureIps`.

El inventario inicial del vault prod no pudo leerse por falta de RBAC. No se configuró
`KeyVault__Name` ni se reinició `cekide-api`, para evitar dejar la app fuera de servicio:
al momento de esta verificación solo se añadió la clave de conexión y faltan las claves
requeridas de producción. La vinculación de identidad y permisos está preparada, pero la
API aún no consume esa conexión.

### Identidades de despliegue y migracion SQL (2026-11-04)

Por autorizacion expresa se reutilizan las identidades de despliegue, sin crear otras:

| Entorno | Identidad user-assigned | Base / usuario externo |
|---|---|---|
| Staging | `cekide-api-stagi-id-a925` | `cekide-db-staging` / `cekide-api-stagi-id-a925` |
| Produccion | `cekide-api-id-8230` | `cekide-db` / `cekide-api-id-8230` |

Cada usuario usa el client ID de la identidad como SID y pertenece al rol
`cekide_schema_migrator` en su base. Este concede `CREATE TABLE` y
`ALTER`, `REFERENCES`, `SELECT`, `INSERT`, `UPDATE`, `DELETE` en `SCHEMA::dbo`.
No se otorgaron `db_owner`, `db_ddladmin`, gestion de usuarios ni acceso a la base ajena.
Se verificaron grants persistentes y permisos efectivos por impersonacion; no se
autentico aun como migrador desde Actions ni se ejecutaron migraciones.

La asignacion user-assigned de despliegue se retiro de `cekide-api-staging`, con
autorizacion, antes de conceder DDL. La Web App conserva unicamente su identidad
system-assigned y el mismo principal runtime; `keyVaultReferenceIdentity=SystemAssigned`.
Sus roles SQL y los de produccion siguen siendo solo `db_datareader`/`db_datawriter`.
Se conservaron OIDC por environment y las variables `AZURE_CLIENT_ID` existentes.

Se agrego a pedido del usuario la regla SQL permanente `CekideOperatorPublicIp`, limitada
a su IP publica indicada; si cambia debe actualizarse. No se documenta la IP en el repo.
La regla preexistente `AllowAllWindowsAzureIps` no se modifico. Las bases se reanudaron
al conectar, sin cambios de SKU, esquema de negocio ni datos.

Verificacion posterior desde Actions: `41900175393` paso en staging y production, esta
ultima aprobada manualmente por el usuario. Cada identidad conecto a su base con permisos
esperados y recibio SQL 18456 en la base contraria. Un fallo inicial 40615 demostro que la
regla Azure existente no cubre todos los runners; con aprobacion expresa se agrego
`Cekide SQL Runner Firewall Operator` a ambas identidades de despliegue, solo en el servidor
`cekide`, con firewallRules read/write/delete y sin DataActions.
El diagnostico crea reglas de una IP exacta con nombre unico por job y las elimina al
terminar; se confirmo en ARM que no quedan reglas `gha-` tras esta ejecucion.
No se ejecutaron migraciones ni despliegues; las identidades runtime no recibieron
permisos nuevos. Ver [quickstart.md](quickstart.md) para repetir y limpiar reglas huerfanas.

### Acceso a Blob Storage compartido

**Actualizacion aplicada (2026-11-04)**: deployment incremental `cekide-blob-replacement`
reutilizo el rol `Cekide Blob Prefix Operator`
(`155f895d-a0eb-56a7-8897-e227e1152304`) y tres asignaciones condicionadas 2.0 al
contenedor privado de `cekideblobaccount`. Solo DataActions Blob read/write/delete/add,
sin Actions de gestion.

| Entorno | Principal runtime | Asignacion |
|---|---|---|
| development | `d4c38e0f-5597-4ce4-b6b7-ccb19ac49aae` | `3d47f79b-b3c2-5d64-99d9-0b86d77426e5` |
| staging | `446b2b59-9468-4814-b273-a15a8b44bcbc` | `a6480c8b-b15a-58a1-8236-a9557fb25365` |
| production | `a71db939-b848-448f-b0e1-f11adfb616d0` | `71c085d7-f101-5873-b6fd-de048637af9c` |

Development usa la aplicacion `cekide-blob-development`, client ID
`c16ef1d0-ae1b-4e2e-8794-af93f7841989`, con certificado local fuera del repo
(vence 2027-05-04), sin roles administrativos. Staging/production conservan sus
identidades system-assigned; no se otorgo Blob a las identidades de despliegue.
Cada condicion limita `path` al prefijo fijo propio y `Blob.List` al `prefix` solicitado.
Los grants se confirmaron por ARM; los tres entornos pasaron 18 comprobaciones reales
cada uno con limpieza correcta. Staging/production se ejecutaron desde SCM con sus
identidades system-assigned y verificacion de principal/tenant del token. No se
actualizo configuracion ni se desplegaron/reiniciaron APIs. El 500 y la limpieza incierta
corresponden a la cuenta anterior, no a este destino; ver [quickstart.md](quickstart.md).
Shared keys estan deshabilitadas en la cuenta nueva; el Owner del operador no se
elimino y no se usa para probar aislamiento.

| Campo | Descripción | Regla |
|---|---|---|
| `storageAccount` | Cuenta única de Blob Storage | Compartida por los tres entornos |
| `environmentPrefix` | `development/`, `staging/` o `production/` | Un único prefijo fijo por entorno; no se aceptan rutas provistas sin validación |
| `authorizationScope` | Contenedor/ACL/identidad que limita operaciones | Debe impedir leer, sobrescribir o borrar blobs de otros entornos |
| `identity` | Identidad autorizada para operar en storage | No reutilizar claves de cuenta ni identidad con acceso global entre aplicaciones Azure |

El usuario eligio un unico contenedor compartido `cekidecontainer` con prefijos fijos. Cada
identidad debe tener una condicion Azure ABAC propia para su prefijo, aplicada a lectura,
escritura, listado y borrado. El filtrado de rutas solo en la aplicacion no constituye
autorizacion. La condicion y los intentos de acceso cruzado deben validarse antes de
habilitar los entornos.

## Secreto

Valor sensible necesario para configurar la API o una integración.

| Campo | Descripción | Regla |
|---|---|---|
| `configurationKey` | Clave jerárquica de la API, por ejemplo `Jwt:Key` | No incluye el valor del secreto |
| `environment` | Entorno al que pertenece | Obligatorio; sin reutilizar valor o credencial entre entornos |
| `store` | User Secrets local o Azure Key Vault Secrets | Development usa User Secrets; staging/production usan Key Vault |
| `secretName` | Nombre no sensible en el almacén | En Key Vault, `:` se representa como `--` |
| `readerIdentity` | Identidad autorizada a leer | Runtime solo lectura sobre el almacén de su entorno |
| `rotationPolicy` | Responsable y proceso de rotación | Debe existir antes de producción |

El modelo nunca registra ni persiste el contenido del secreto en archivos versionados,
logs, inventarios ni artefactos CI.

## Publicación

Representa la promoción de un artefacto inmutable hacia un entorno y la instancia
independiente de API que lo ejecuta.

| Campo | Descripción | Regla |
|---|---|---|
| `version` | Identificador inmutable del artefacto | El mismo artefacto validado en staging debe promoverse a production |
| `sourceCommit` | Commit de origen | Obligatorio para trazabilidad |
| `targetEnvironment` | Staging o production | Production solo tras staging aprobado |
| `targetApplication` | App Service Web App de destino | Staging y production deben identificar aplicaciones distintas |
| `validationStatus` | Pendiente, aprobada o fallida | Fallo bloquea la siguiente transición |
| `migrationStatus` | No requerida, pendiente, ejecutada o fallida | Las migraciones se ejecutan automáticamente tras el despliegue de la API en Azure; fallo bloquea los pasos siguientes |
| `backupReference` | Identificador/fecha de respaldo previo a migración productiva | Debe existir antes de migraciones que puedan afectar datos reales y verificarse su restaurabilidad |
| `artifactHash` | Hash criptográfico del artefacto construido | Debe conservarse y coincidir en staging y producción |
| `approvedBy` | Aprobador de producción | Obligatorio antes de publicación a production |
| `publishedAt` | Marca temporal de publicación | Obligatoria para publicaciones completadas |
| `recoveryReference` | Artefacto estable y/o respaldo asociado | Obligatorio para production |

### Estados y transiciones

`Built -> DeployedToStaging -> StagingMigration -> StagingValidated -> AwaitingProductionApproval -> DeployedToProduction -> ProductionMigration -> ProductionValidated`

- Una validación o migración fallida termina en estado fallido/bloqueado; no se permite
  transición a producción ni se continúa con etapas posteriores.
- La falta de aprobación mantiene la publicación en `AwaitingProductionApproval`.
- La promoción a producción reutiliza el artefacto validado en staging; no vuelve a
  construirlo ni publicarlo.
- Las migraciones son compatibles hacia atrás y se ejecutan como un paso único controlado,
  no simultáneamente al arrancar réplicas de la API. En producción, un fallo detiene la
  publicación y no ejecuta una restauración destructiva automática; se decide entre
  corrección hacia delante o recuperación aprobada para evitar perder escrituras válidas.
- La recuperación registra la versión/restauración aplicada y no elimina la trazabilidad
  de la publicación original.
