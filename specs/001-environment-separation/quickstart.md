# Quickstart: Validación de entornos aislados

Esta guía define las comprobaciones para la implementación futura. No crea recursos ni
realiza despliegues. Completar las operaciones Azure solo cuando los recursos y permisos
hayan sido aprobados.

## Estado de ejecucion

### Cuenta nueva y Blob ABAC validado en los tres entornos (2026-11-04)

El usuario solicito crear `cekideblobaccount`, eligio `Standard_LRS` + `Hot` y
autorizo cargos, provisioning, ABAC y pruebas sinteticas. El deployment incremental
`cekide-blob-replacement`, tras revisar what-if, creo la cuenta en `CEKIDE`, Brazil South,
y el contenedor privado `cekidecontainer`. No modifico la cuenta anterior ni las APIs.
La cuenta nueva exige HTTPS/TLS 1.2, impide acceso publico a blobs y deshabilita
autenticacion por claves compartidas. El endpoint vigente es
`https://cekideblobaccount.blob.core.windows.net`.
LRS no proporciona replica geografica; no se afirma costo cero ni recuperacion geografica.

[blob-access.bicep](../../infra/blob-access.bicep) ahora compone
[storage.bicep](../../infra/modules/storage.bicep) y
[environment-access.bicep](../../infra/modules/environment-access.bicep), con dependencia
explicita antes de asignar permisos al contenedor. Se reutilizan las tres identidades
y el rol minimo, con condiciones ABAC por prefijo verificadas por ARM.

La identidad de development con certificado paso **18 comprobaciones reales**:
crear/leer/sobrescribir/listar/borrar en su prefijo; lectura/escritura/borrado/listado
ajenos rechazados con 403 `AuthorizationPermissionMismatch`; listados de raiz y
limites de prefijo rechazados. Probe `d7d44dcfde544417a4e5c0dbc0985ec0`,
eliminado correctamente, sin errores de limpieza. No se accedio a datos de negocio.
El HTTP 500 no se reprodujo en esta cuenta; la causa de la cuenta anterior sigue sin
determinar. La consulta ARM previa todavia mostraba la cuenta anterior como existente;
no se afirma su eliminacion ni la limpieza de sus intentos previos.
El what-if posterior no fue completamente NoChange: Azure representa defaults de
retencion/cifrado omitidos en la plantilla y normaliza el ID del rol a scope de
suscripcion. Se revisaron esos deltas; no implican cambios de prefijos o permisos.
No se afirma un what-if sin diferencias.

Validacion local: Bicep compilo; test estructural de permisos y 4 pruebas offline del
launcher pasaron; 55 pruebas unitarias y 9 de integracion enfocadas pasaron en Release.
El launcher fija tambien `Storage__AccountUrl` al nuevo endpoint y restaura el valor
previo al terminar. La validacion de arranque rechaza el endpoint de la cuenta anterior.

Con autorizaciones separadas para staging y production se ejecuto el probe dentro
de Kudu/SCM de cada Web App, en una carpeta aleatoria bajo `C:\home\data\abac-probe-<id>`,
fuera de `site\wwwroot`. Se verificaron sitio, identidad ARM y disponibilidad de
.NET 10 y del endpoint Managed Identity antes de subir el diagnostico framework-dependent.
La autenticacion administrativa a SCM fue Bearer por Azure CLI, sin obtener publishing
passwords; el acceso a Blob uso exclusivamente `ManagedIdentityCredential(SystemAssigned)`.
El probe comprobo `oid` y `tid` del token devuelto por esa credencial contra los IDs
esperados antes de operar, sin imprimir el token. Invocarlo como staging desde el
equipo local fue rechazado antes de autenticar por no ser el App Service asignado.

| Entorno | Principal verificado | Probe sintetico | Resultado |
|---|---|---|---|
| development | `d4c38e0f-5597-4ce4-b6b7-ccb19ac49aae` | `5bb77e0d725e47dea82c1535a2a99603` | 18 PASS; blob eliminado |
| staging | `446b2b59-9468-4814-b273-a15a8b44bcbc` | `6bf2d4a8efb649b79856cbab30116594` | 18 PASS; blob y carpeta SCM eliminados |
| production | `a71db939-b848-448f-b0e1-f11adfb616d0` | `5ea516d9a5834c34bf2fa30bac2a7bfd` | 18 PASS; blob y carpeta SCM eliminados |

Son **54 comprobaciones Blob**: CRUD/list propio y denegaciones 403
`AuthorizationPermissionMismatch` a los otros prefijos y listados raiz/limite.
La carpeta remota se comprobo ausente tras la limpieza de cada ejecucion; los paquetes
locales temporales tambien se retiraron. No se uso la identidad de despliegue para Blob.
No se leyo contenido de negocio ni se consultaron SQL o Key Vault durante estos probes.

**Pendiente**: configurar el endpoint y verificar el flujo de rutinas de las APIs
staging/production, ademas de las pruebas runtime de SQL y Key Vault. No se cambiaron
ajustes remotos, reiniciaron ni desplegaron APIs; no hubo migraciones ni publicacion GitHub.
Los User Secrets locales tampoco fueron modificados.
T020/T022/T026/T028 no se dan por completadas; falta ademas la composicion general.
Las secciones siguientes que nombran la cuenta anterior y sus HTTP 500 son evidencia
historica, no instrucciones para reutilizar ese endpoint.

### Estado Blob ABAC anterior al reemplazo (2026-11-04, historico)

Con autorizacion expresa se creo la aplicacion Entra `cekide-blob-development`,
client ID `c16ef1d0-ae1b-4e2e-8794-af93f7841989`, principal
`d4c38e0f-5597-4ce4-b6b7-ccb19ac49aae`. No tiene permisos administrativos, secretos
de cliente ni roles globales de Storage. Usa un certificado local con vencimiento
2027-05-04; Azure solo registra la clave publica.
El PEM privado esta fuera del repositorio en
`%LOCALAPPDATA%\CekideApi\BlobDevelopment\blob-development.pem`, con ACL sin herencia
y acceso unicamente del usuario Windows actual y SYSTEM. No copiarlo a GitHub,
repositorio, logs ni artefactos. Renovar antes del vencimiento: registrar una nueva
clave publica, cambiar el certificado local por canal protegido, verificar el acceso
y retirar la clave publica anterior; no borrar la anterior antes de verificar la nueva.

Se compilo [blob-access.bicep](../../infra/blob-access.bicep) y se reviso what-if:
solo un rol personalizado y tres asignaciones al contenedor existente. El deployment
`cekide-blob-abac` termino `Succeeded` en modo Incremental. No cambio la cuenta,
contenedor, SQL, vaults, APIs o planes. El modulo es independiente de `infra/main.bicep`,
todavia pendiente; no supone completada toda la infraestructura de T022/T026.

| Entorno | Identidad | Prefijo condicionado |
|---|---|---|
| development | Aplicacion Entra dedicada indicada arriba | `development/` |
| staging | System-assigned `446b2b59-9468-4814-b273-a15a8b44bcbc` | `staging/` |
| production | System-assigned `a71db939-b848-448f-b0e1-f11adfb616d0` | `production/` |

El rol `Cekide Blob Prefix Operator` concede exclusivamente las DataActions Blob
`read`, `write`, `delete`, `add/action`; no administra contenedores, obtiene claves,
genera delegation keys ni modifica permisos. Las condiciones 2.0 son sensibles a
mayusculas y exigen `/` tras el nombre: recursos por `path` y listado por el `prefix`
de la solicitud, usando el patron de implicaciones documentado por Microsoft.
Un listado de raiz, sin prefijo o con otro entorno debe fallar, no devolver un listado
filtrado. Las asignaciones persistidas y sus prefijos se releyeron por ARM.

La revision de roles encontro solo el Owner administrativo del operador heredado
en la cuenta, sin roles Blob globales/heredados de las tres identidades runtime.
Owner no se usa para la prueba: ABAC no restringe al administrador que puede cambiar
roles u obtener claves. `allowSharedKeyAccess=true` sigue sin modificar por alcance;
las aplicaciones no deben usar esas claves. No se afirma aislamiento frente al administrador.

**Bloqueo de validacion real**: la identidad dedicada logro autenticar, pero Blob devolvio
HTTP 500 `OperationTimedOut` al crear el objeto sintetico y al listar solo
`development/_abac-probe/`. Se reintento con espera/reintentos acotados, se alineo la
condicion con el ejemplo documentado y se probo REST independiente con API `2023-11-03`:
el mismo HTTP 500. No se encontro aun la causa; no atribuirlo a permisos ni contar un
timeout como denegacion ABAC. ARM reporta cuenta `available`, red publica habilitada
y defaultAction Allow; eso no demuestra salud del data plane.
Referencia REST: solicitud `4c1e9169-101e-002c-2b9a-6c0221000000`, 2026-11-04
20:27:36 UTC (listado SDK); la comprobacion REST independiente tambien fallo.

No se leyo contenido de negocio. La limpieza del intento sintetico
`development/_abac-probe/288dd2d4dd794b4396db0c59c00e91ae.txt` tambien recibio 500:
su ausencia no pudo confirmarse. Un primer upload con timeout, previo al registro del
ID de prueba, tampoco permite asegurar si se persistio algun objeto. Cuando el servicio
responda, inventariar solo `development/_abac-probe/`, verificar los objetos sinteticos de
esta operacion y retirar solo esos objetos; no borrar por prefijo indiscriminadamente.
La cuenta Cool RA-GRS puede cobrar operaciones y borrado anticipado; el usuario aprobo
ese consumo sintetico, no una modificacion de SKU o acceso compartido.
Los intentos posteriores de listado fallaron antes de crear objetos.

Validaciones locales correctas:

```powershell
az bicep build --file .\infra\modules\environment-access.bicep --outfile <archivo-temporal-fuera-del-repo>
& .\scripts\environments\Test-BlobAccessTemplate.ps1 -CompiledTemplatePath <archivo-temporal-fuera-del-repo>
dotnet build .\scripts\diagnostics\BlobIsolation\BlobIsolation.csproj --configuration Release
```

El test estructural comprueba DataActions exactas, scope, identidades y guardas separadas
de listado/ruta, incluida sustitucion del prefijo en el JSON compilado. No simula Azure
ni sustituye una prueba efectiva de autorizacion. Build sin advertencias/errores.
Tambien se comprobo que rechaza plantillas alteradas con DataActions `*` o sin guarda
de prefijo de listado. Las 4 pruebas offline de
`scripts/environments/Test-DevelopmentLauncher.ps1` pasaron: seleccion de identidad,
probe development, restauracion ante fallo y rechazo antes de iniciar si falta certificado.
Los tres scripts PowerShell nuevos pasaron el parser de Windows PowerShell.
El probe espera CRUD/list propio y 403 `AuthorizationPermissionMismatch` ajeno; 404,
problemas de red, autenticacion o 500 hacen fallar, no pasan silenciosamente.

Para ejecutar development con la identidad dedicada (sin cambiar tu login administrativo):

```powershell
& .\scripts\environments\Start-Development.ps1
# Solo la prueba sintetica, una vez investigado el 500:
& .\scripts\environments\Start-Development.ps1 -TestBlobIsolation
```

El launcher configura las variables del certificado solo para el proceso y restaura
las anteriores al terminar. No lo ejecutamos para arrancar/reiniciar la API.
User Secrets sigue guardando la configuracion local habitual, no la clave de la cuenta.
Iniciar la API de otra forma sin esas variables puede volver a seleccionar tu sesion
administrativa mediante DefaultAzureCredential; no usar esa ruta para validar ABAC.
Las identidades system-assigned solo se pueden probar desde su recurso Azure:
las pruebas staging/production y la denegacion cruzada completa siguen pendientes,
sin suplantarlas con identidades de despliegue. No habilitar el release por estos grants.
Los archivos de esta continuacion son locales y no se publicaron a GitHub.
T020/T022/T026/T028 siguen abiertas.

### Estado historico previo a Blob ABAC

Setup completado (T001-T003); fundamentos bloqueados antes de cambios Azure.
Los checklists se consultaron como gate de solo lectura y no se modificaron.

### Actualizacion de implementacion (2026-11-04)

T001-T003, T008, T013 y T014 completados. El trabajo local de storage ahora requiere un endpoint
HTTPS sin credenciales en `Storage:AccountUrl`, usa el contenedor compartido `cekidecontainer`
y selecciona el prefijo fijo segun el entorno. Usa `DefaultAzureCredential` en Development y Managed Identity en
Staging/Production. No crea contenedores ni vuelve a usar connection strings de cuenta.
Development no carga Key Vault; Staging/Production requieren `KeyVault:Name`. Fuera de
Testing, la API valida temprano configuracion obligatoria de JWT, SQL, Storage, Brevo y
Authcraft. Testing sigue sustituyendo storage por `InMemoryRoutineStorage`; `Storage:Provider`
permite seleccionar `Azurite` solo en Testing con `Storage:AzuriteConnectionString`
configurada explicitamente. El contenedor local `cekidecontainer` debe existir antes de usar
Azurite; el adaptador no crea contenedores. No existe fallback de Azure a Azurite.

La suite completa se ejecuto con `dotnet test cekide-api.slnx --no-restore`: 198 pruebas
unitarias y 195 de integracion pasaron. No se modificaron workflows ni se crearon plantillas
Bicep, identidades, aplicaciones o planes en Azure. A petición del usuario se crearon los
vaults separados de staging y producción; ambos siguen sin roles ni secretos. También se creó
el contenedor privado `cekidecontainer` en la cuenta Blob homónima. El usuario confirmó
usar un solo contenedor compartido con condiciones ABAC por prefijo; el adaptador local
ahora usa ese contenedor, pero las condiciones ABAC todavía no están configuradas ni
verificadas. El usuario aceptó posibles cargos de Blob Storage para la cuenta y el
contenedor existentes y de operaciones de Key Vault; la aprobación no incluye cambios
RBAC/ABAC, hosting ni otros recursos. No se iniciaron pruebas antes de validar ABAC.
El precio público de Cool RA-GRS Brazil South es 0.0443 USD/GB-mes, más operaciones,
recuperación y posibles cargos de transferencia; los detalles están en [research.md](research.md).
T014 se completo con pruebas locales enfocadas (23 pruebas de storage pasaron).
En la continuación del loop, las 11 pruebas de `RoutineTests` pasaron con SQLite y storage
en memoria. T017 no se cierra porque su verificación de archivos existentes depende de
T015, que requiere autorización para inspeccionar datos de SQL/Blob.
T004 quedó completada y se actualizó con una consulta ARM GET: `cekide-api-staging` existe,
está `Running` en `CEKIDE` y usa el plan compartido F1 `ASP-AuthCraft-bc2b` en `AuthCraft`,
fuera de `CEKIDE`. El usuario aceptó posibles costos de Blob Storage para la cuenta y el
contenedor existentes, operaciones de Key Vault y la reanudación/uso de SQL para este
vínculo; no incluye hosting ni otros recursos. Con aprobación expresa se crearon
`cekide-key-vault-staging` y
`cekide-key-vault-prod` (West US 3, Standard, RBAC, soft delete de 90 días y purge
protection). Staging tiene `KeyVault__Name` y el rol `Key Vault Secrets User` asignados
a su identidad system-assigned. El principal `cekide-api-staging` fue creado en
`cekide-db-staging` con `db_datareader` y `db_datawriter`, sin DDL. La cadena de conexión
de Managed Identity se guardó como `CekideDatabaseConnectionString` en el vault.
La base está `Online` con auto-pausa de 60 minutos. Una regla temporal para la IP del
administrador se retiró al completar el cambio.

Para producción, con autorización expresa para posibles cargos y cortes, se habilitó la
identidad system-assigned de `cekide-api`, se asignó `Key Vault Secrets User` en el vault
prod y se creó el principal SQL `cekide-api` con `db_datareader` y `db_datawriter`, sin DDL.
Se guardó la cadena Managed Identity como `CekideDatabaseConnectionString` en
`cekide-key-vault-prod`. La base productiva está `Online` y tiene auto-pausa de 60 minutos.
Se retiraron el rol temporal del operador y la regla de firewall de una sola IP. No se
configuró `KeyVault__Name` ni se reinició la API: el vault estaba vacío antes de guardar
la conexión, y faltan las demás claves requeridas para arrancar. La app aún no usa la base.
El usuario rechazó planes de hosting con costo adicional e indicó que staging ya está
creado; aceptó el F1 compartido actual como excepción temporal para staging, sin crear ni
modificar un plan. La Web App usa un plan fuera de `CEKIDE`. El usuario eligió usar
backups/PITR existentes; cualquier ensayo requiere autorización separada y restaurará a
una base nueva. T005-T007 siguen pendientes. Los costos públicos B1/S1 investigados son
solo comparación y no están aprobados.
El usuario acepta detener staging durante la migración y que se interrumpan solicitudes de
prueba. La vinculación SQL indicada arriba se aplicó con autorización posterior expresa.

La lectura de configuración de staging encontró HTTPS obligatorio, TLS mínimo 1.2, acceso
público habilitado, identidades system-assigned y user-assigned, y reglas `Allow all` para la app y SCM.
FTPS está restringido a FTPS. Se encontró una regla de firewall SQL, pero no se verificó
que permita a GitHub Actions alcanzar la base. La regla temporal de acceso administrativo
fue eliminada; la ruta de red desde Actions sigue pendiente.

El usuario mantiene producción bloqueada y no autoriza crear ni cambiar planes de hosting;
acepta temporalmente el plan compartido de AuthCraft solo para staging, sin moverlo ni
cambiarlo. La aprobación de costos para Blob no configura ABAC ni autoriza su modificación,
por lo que no se iniciaron pruebas Blob. El inventario de nombres del vault de staging
muestra `Authcraft--BaseUrl`, `Brevo--ApiKey`, `Brevo--FromEmail`,
`CekideDatabaseConnectionString`, `Jwt--Key` y `Storage--AccountUrl`; no se leyeron los
valores. `Jwt--Issuer` y `Jwt--Audience` aún faltan. Una llamada sin autenticación a la
ruta protegida devolvió HTTP 401; no se validó una consulta SQL ejecutada desde la API.
Producción tiene solo la clave de conexión en su vault; faltan `Jwt--Key`, `Jwt--Issuer`,
`Jwt--Audience`, `Brevo--ApiKey`, `Authcraft--BaseUrl` y `Storage--AccountUrl` para el
inicio validado. `Brevo--FromEmail` se requiere para enviar correo. No se deben agregar
valores de secretos a este documento.

### Herramientas y validacion inicial

- .NET SDK `10.0.403`; pruebas xUnit v2 con VSTest, sin configuracion MTP.
- `dotnet test cekide-api.slnx --logger "trx;LogFilePrefix=environment-baseline"`:
  172 unitarias y 193 de integracion correctas; cero fallidas u omitidas.
  Los TRX se conservan en `tests\CekideApi.UnitTests\TestResults\` y
  `tests\CekideApi.IntegrationTests\TestResults\`, ignorados por Git.
- Azure CLI `2.91.0`, autenticada; EF CLI global `10.0.12` disponible.
  El proyecto referencia EF `10.0.14`; revisar/alinear la herramienta antes de generar
  bundles, sin modificar la instalacion global como parte de este setup.
- Bicep faltaba (`az bicep version` fallo); se instalo mediante `az bicep install`.
  Version verificada `0.48.1`; validar archivos futuros con `az bicep build --file ...`.
- PowerShell 7 (`pwsh`) no esta instalado; los scripts futuros deben considerar el
  Windows PowerShell disponible o exigir explicitamente la herramienta necesaria.
- Algunos comandos `az webapp` fallan al cargar una extension local con acceso denegado.
  Las consultas siguientes se hicieron con `az rest --method get`, sin alterar la
  extension ni permisos locales.

### Diferencias verificadas al inicio (2026-11-03)

- Ambos workflows actuales publican desde `main` a produccion por caminos distintos:
  uno usa publish profile y otro OIDC; no implementan la promocion staging/production.
  No se modificaron ni ejecutaron workflows.
- En el estado inicial, `Program.cs` permitia omitir Key Vault; la actualizacion del
  2026-11-04 ahora requiere `KeyVault:Name` en Staging/Production.
- El storage actual usa connection string, contenedor `routines`, rutas relativas
  y creacion automatica del contenedor; T013 reemplazo ese comportamiento localmente
  por identidad Entra y rutas aisladas por entorno.
- Hay cuatro migraciones EF versionadas en Infrastructure. No se aplicaron migraciones;
  la fabrica de diseno puede caer a LocalDB si falta configuracion y no debe reutilizarse
  como fallback de despliegue remoto.
- Las bases Azure y la Web App productiva existen; faltaban staging y vaults en el
  inventario inicial. El usuario creó después `cekide-api-staging`, verificada por ARM como
  `Running` en `CEKIDE`, West US 3 y asociada al plan compartido F1 de `AuthCraft`. Una
  consulta posterior verifica la cuenta Storage `cekidecontainer`,
  creada por el usuario en Brazil South, SKU `Standard_RAGRS`, sin contenedores en la
  respuesta ARM de ese momento. Después, a petición del usuario, se creó el contenedor
  privado homónimo. No se confirmo la gratuidad de la cuenta. Ver [data-model.md](data-model.md).
- El plan F1 de produccion pertenece a `AuthCraft`, no a `CEKIDE`, y aloja dos sitios.
  No mover, actualizar ni reemplazar ese plan compartido: podria afectar otra aplicacion.
  La topologia final y su costo requieren una decision del usuario.

### Archivos futuros previstos y bloqueo

La implementacion pendiente agrega `infra\main.bicep`, modulos y parametros por entorno,
scripts de despliegue, pruebas de aislamiento y otros cambios de configuracion/storage,
como detalla [tasks.md](tasks.md). Se preparo localmente el preflight de solo lectura
`scripts\environments\Test-EnvironmentPrerequisites.ps1`; aun no se ejecuto contra Azure.
No se aplico infraestructura, no se habilitaron publicaciones ni se modificaron recursos
remotos en esta ejecucion.

T004 tiene inventario actualizado por consultas ARM de solo lectura. La asociación de
staging al plan compartido externo y la ruta SQL desde GitHub Actions requieren resolver
T006 antes de despliegues. Las identidades de runtime y permisos SQL de lectura/escritura
por base están configurados; siguen pendientes las condiciones Blob ABAC, las decisiones
de costo restantes de T005 y la evidencia de restauración. No se consultaron valores
secretos ni se conectó a datos SQL. Backups configurados no equivalen a restaurabilidad
ensayada.

### Preflight de operaciones remotas

El script siguiente consulta Azure de solo lectura y falla si la suscripcion, Web App,
identidad administrada o base no coinciden con el entorno esperado. Requiere referencias
no vacias a la aprobacion de costo aplicable y a la evidencia de recuperacion; no imprime
ni consulta valores de secretos o cadenas de conexion:

```powershell
.\scripts\environments\Test-EnvironmentPrerequisites.ps1 `
  -Environment Staging `
  -SubscriptionId "<subscription-guid>" `
  -ExpectedIdentityObjectId "<managed-identity-object-guid>" `
  -CostApprovalReference "<approval-reference>" `
  -RecoveryEvidenceReference "<recovery-evidence-reference>"
```

La referencia de recuperacion no acredita por si sola una restauracion ensayada, y este
preflight no verifica la cadena SQL activa de la aplicacion ni concede autorizacion para
cambiar recursos. T006, T005 y la evidencia de recuperacion requerida por T040 mantienen
bloqueadas las operaciones pendientes.

Validacion local de la implementacion actual:
`dotnet test cekide-api.slnx --no-restore --configuration Release`
paso con 233 pruebas unitarias y 202 de integracion, sin fallos ni omisiones. El preflight se valido con una
respuesta Azure CLI simulada, incluyendo el estado `Stopped` de staging; no se ejecuto
contra Azure ni se modifico ningun recurso.

Por eleccion del usuario se evaluaron alternativas con costo antes de aprobar:
Windows B1 (54.75 USD/mes a 730 horas) y Linux B1 (12.41 USD/mes a 730 horas),
mas dependencias/consumo y cargos no incluidos. Ver supuestos, medidores y limites en
[research.md](research.md). La opcion Linux requiere decidir una migracion de la Web App
Windows existente. La aprobación posterior se limita a Blob Storage; no hay aprobación de
costos de hosting ni autorización para cambiar o mover el plan compartido.

La verificacion posterior fue exclusivamente de lectura: no acredito oferta gratuita
aplicable a Storage/Key Vault. Se detienen las operaciones remotas segun lo solicitado.
La cuenta y el contenedor privado se llaman `cekidecontainer`. No se cambio la redundancia
ni otra configuracion de la cuenta.

## Verificación de costo y aprobación

Antes de ejecutar cualquier comando de creación/provisionamiento:

1. Priorizar ofertas gratuitas elegibles y consultar el precio del SKU y todas sus
   dependencias para la región y suscripción reales. Confirmar que los recursos se crearían
   dentro del grupo existente `CEKIDE`; no crear grupos de recursos adicionales.
2. Documentar elegibilidad, duración, cuotas, excedentes y cargos indirectos por
   almacenamiento, operaciones, transferencia, monitoreo y respaldos.
3. Si un recurso cuesta o puede generar cargos, informar al usuario antes de crearlo,
   modificarlo o habilitar consumo de pago. Esperar aprobación explícita para continuar;
   no asumir que créditos, alertas presupuestarias o una etiqueta “Free” limitan la factura.
4. No usar App Service Free/Shared para producción: Azure los describe para pruebas y
   aprendizaje, sin SLA, y no soportados para cargas productivas.
5. Si no hay oferta gratuita apta para producción o no se conoce el costo, dejar la
   operación pendiente y pedir decisión; no hacer afirmaciones de costo cero garantizado.

**Estado según investigación**: no se ha verificado el costo real de los recursos
reportados ni de los recursos nuevos solicitados. App Service F1 no es apto para
producción y Key Vault, SQL, Storage y operaciones asociadas pueden generar cargos; avisar
y obtener aprobación antes de cualquier acción con costo posible.

## Prerrequisitos

- .NET SDK 10 instalado.
- Acceso de desarrollo al repositorio y Azure CLI solo para comprobaciones de Azure.
- Aplicación registrada con User Secrets para development.
- GitHub Actions configurado para que `main` publique a staging y que el entorno protegido
  de producción solicite aprobación manual.
- Entornos de GitHub protegidos y recursos staging/production aprobados y aprovisionados
  solo después de la verificación de costo y la aprobación requerida.
- Secretos de prueba y datos sintéticos; no pegar valores reales en comandos, tickets o logs.

## 0. Verificar recursos reportados como existentes

Antes de desplegar o cambiar configuración, consultar en Azure (solo lectura) el inventario
dentro del grupo `CEKIDE` y confirmar los siguientes nombres reportados por el usuario:

- Base de staging: `cekide-db-staging`
- Base de producción: `cekide-db`
- API/App Service de producción: `cekide-api`
- API/App Service de staging solicitada: `cekide-api-staging`
- Cuenta Blob Storage compartida `cekideblobaccount`: creada en `CEKIDE`, Brazil South,
  Standard_LRS + Hot; contenedor privado `cekidecontainer`, ABAC aplicado y development
  validado desde las tres identidades reales. Pendiente configurar y probar el flujo de las APIs Azure.

Confirmar tipo, estado, ubicación y configuración del recurso sin consultar ni imprimir
valores secretos. Si un recurso no existe o no pertenece a `CEKIDE`, no crear ni moverlo
sin revisar el plan y el costo. Verificar costos antes de desplegar o modificar recursos
existentes; cualquier costo posible requiere aviso y aprobación explícita.

## 1. Comprobar pruebas del repositorio

Desde la raíz:

```powershell
dotnet test cekide-api.slnx
```

**Esperado**: todas las pruebas existentes pasan sin necesitar secretos de producción.

## 2. Comprobar User Secrets en development

Definir localmente los valores necesarios usando los secretos de desarrollo asignados al
desarrollador (no reutilizar credenciales de staging o production):

```powershell
dotnet user-secrets set "Jwt:Key" "<development-jwt-key>" --project src\CekideApi
dotnet user-secrets set "Jwt:Issuer" "<development-issuer>" --project src\CekideApi
dotnet user-secrets set "Jwt:Audience" "<development-audience>" --project src\CekideApi
dotnet user-secrets set "Brevo:ApiKey" "<development-brevo-key>" --project src\CekideApi
```

`Storage:AccountUrl` es un endpoint no secreto y debe configurarse localmente despues de
verificar que el uso del storage es aceptable. No usar claves de cuenta ni SAS:

```powershell
$env:Storage__AccountUrl = "https://cekideblobaccount.blob.core.windows.net"
```

El contenedor privado compartido `cekidecontainer` debe existir antes de ejecutar la API;
el runtime no crea contenedores. La identidad Entra local necesita una condicion ABAC que
limite sus operaciones al prefijo `development/`. Sin endpoint, base, JWT, Brevo o Authcraft
requeridos, la aplicacion falla
al iniciar con un error que identifica la clave faltante, nunca su valor.
No usar `dotnet user-secrets list` en una salida compartida: muestra valores sensibles.

Ejecutar localmente:

```powershell
dotnet run --project src\CekideApi
```

**Esperado**: la API arranca con configuración `Development`, usa LocalDB y User Secrets,
usa `DefaultAzureCredential` y accede solo al prefijo `development/` del contenedor
compartido. Azure ABAC debe aplicar la restriccion efectiva, pero su configuracion sigue
pendiente. No intenta autenticarse contra Azure Key Vault ni crea contenedores. No aparecen valores
secretos en consola.

El arranque ahora valida destinos antes de registrar servicios o ejecutar jobs:

- Development requiere un servidor `(localdb)\...` con base explicita; rechaza Azure SQL.
- Staging acepta solo `cekide-db-staging` y production solo `cekide-db`, ambas en
  `cekide.database.windows.net` (con o sin `tcp:` y puerto explicito `1433`).
- Azure SQL requiere `Authentication=Active Directory Managed Identity`, sin usuario,
  contrasena ni destino de failover, con cifrado y validacion de certificado habilitados.
- Staging requiere `cekide-key-vault-staging` y production `cekide-key-vault-prod`; un nombre
  ajeno se rechaza antes de consultar Key Vault o solicitar tokens.
- Fuera de Testing, Blob requiere el endpoint HTTPS de `cekideblobaccount`, puerto estandar
  y provider `AzureBlob` (predeterminado si no se especifica); no permite SAS, claves
  `Storage:ConnectionString` ni `Storage:AzuriteConnectionString`.

Los errores mencionan claves, nunca valores ni cadenas SQL. Se conserva la precedencia:
Key Vault se agrega al final y sobrescribe JSON, variables de entorno y argumentos.
Las pruebas usan el proveedor Key Vault real con transporte HTTP sintetico y no requieren
servicios Azure. Testing mantiene sus sustitutos SQLite/almacenamiento en memoria.
Estas validaciones son defensa adicional; no sustituyen los permisos SQL ni Blob ABAC.
La rotacion remota y la denegacion Azure entre identidades siguen pendientes.

Verificar el destino de base local sin mostrar la cadena de conexión ni secretos:

```powershell
dotnet ef dbcontext info --project src\CekideApi.Infrastructure --startup-project src\CekideApi
```

**Esperado**: development se configura con la base LocalDB local; no apunta a las bases
Azure de staging o producción.

## 3. Comprobar límites de Azure Key Vault

Para cada aplicación staging/production, verificar el nombre de vault y la asignación RBAC
sin imprimir valores secretos:

```powershell
az keyvault secret list --vault-name $env:KEY_VAULT_NAME --query "[].name" --output tsv
az role assignment list --scope $env:KEY_VAULT_RESOURCE_ID --query "[].{role:roleDefinitionName,principal:principalName}" --output table
```

**Esperado**:

- Staging y production usan vaults separados con las claves requeridas para su entorno.
- Todos los recursos de ambos entornos pertenecen al grupo `CEKIDE`; el aislamiento se
  comprueba en recursos, identidades, datos y permisos, no mediante grupos distintos.
- Staging y producción configuran bases de datos Azure distintas, cadenas de conexión
  independientes y credenciales propias; staging no puede conectarse a la base productiva.
- El inventario Azure confirma `cekide-db-staging`, `cekide-db`, `cekide-api` y
  `cekide-api-staging` en `CEKIDE` antes de habilitar el workflow.
- Staging y producción son dos Azure App Service Web Apps distintas, ambas pertenecientes
  al grupo `CEKIDE`; confirmar que cada aplicación informa su propio entorno.
- Development utiliza su base local y no comparte base de datos con ninguno de los entornos
  Azure.
- La identidad de runtime de cada App Service solo puede leer su propio vault.
- La identidad de staging no puede leer secretos de production y viceversa.
- Las comprobaciones solo enumeran nombres/roles; nunca solicitan ni imprimen los valores.

## 4. Validar publicación y promoción

### Ruta de red y migracion de staging

Decision vigente, validada en Actions: permitir temporalmente la IP IPv4 exacta del
runner en una regla `gha-<run>-<attempt>-<environment>` y eliminarla en un paso `always()`.
El usuario autorizo este cambio despues de comprobar que `AllowAllWindowsAzureIps` no
cubre todos los runners asignados por GitHub. Esa regla existente sigue habilitada y
permite conexiones desde cualquier recurso Azure, incluso de otras suscripciones.
No se crean runners ni se abren rangos generales. No guardar contrasenas SQL ni publish
profiles en GitHub. La regla de red no sustituye autenticacion/autorizacion SQL.

Antes de habilitar el workflow:

- Configurar OIDC federado para environments GitHub `staging` y `production`, con
  identidades distintas; no usar secretos de Azure de larga duracion.
- Permisos SQL preparados: se reutilizan las identidades de despliegue existentes, una
  por entorno, sin identidades nuevas. Cada una tiene DDL solo en su propia base.
  Las identidades runtime de las APIs siguen separadas y conservan solo lectura/escritura.
- OIDC, acceso SQL propio y denegacion a la otra base ya se verificaron desde Actions
  para ambas identidades en la ejecucion `41900175393`; conservar la compuerta production.
- En staging, detener la Web App, publicar con reinicio automatico deshabilitado,
  ejecutar la migration bundle y arrancar la aplicacion solo tras exito. Si falla una
  etapa, no arrancar ni continuar. El comportamiento de `az webapp deploy --restart false`
  debe confirmarse en staging; la disponibilidad de esa opcion en la CLI no es prueba de
  que la app quede detenida durante toda la operacion.
- Mantener production bloqueada hasta completar sus secretos, identidad de migracion,
  evidencia de restauracion ensayada y environment protegido con aprobacion manual.

### Configuracion GitHub aplicada y comprobada

Con autorizacion expresa del usuario y GitHub CLI autenticada:

- Los tres workflows de despliegue anteriores estan `disabled_manually`, incluido el de
  staging creado en GitHub que aun no existe en el checkout local. No reactivarlos.
- `staging` y `production` permiten despliegues solo desde la rama `main`.
- `production` requiere aprobacion manual de `leoperetti`, sin bypass administrativo.
  Se permite que el usuario apruebe sus propios releases por decision expresa.
- `main` exige PR y check `test` de GitHub Actions, base actualizada y conversaciones
  resueltas; las reglas incluyen administradores. Para trabajo individual no se exige
  aprobacion de otro usuario.
- Las identidades de despliegue existentes usan confianza OIDC exclusiva de su environment
  con subjects inmutables de GitHub, no `ref:refs/heads/main`. Sus permisos existentes son
  Website Contributor limitados a su Web App. Posteriormente, con autorizacion expresa,
  recibieron permisos SQL de migracion solo en su base mediante `cekide_schema_migrator`.
- Cada environment tiene variables no secretas `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`,
  `AZURE_SUBSCRIPTION_ID`, `AZURE_RESOURCE_GROUP` y `AZURE_WEBAPP_NAME`.

El [PR #1](https://github.com/leoperetti/cekide-api/pull/1) agrega CI sin Azure ni despliegue
y retira una referencia gitlink invalida que rompia `actions/checkout`. No contiene los
cambios locales de esta feature. El usuario lo fusiono el 2026-11-04 a las 19:18:55 UTC;
CI en `main` paso en la ejecucion
[41895748105](https://github.com/leoperetti/cekide-api/actions/runs/41895748105).
Esta implementacion no hizo el merge ni actualizo el checkout local, para preservar el
arbol de trabajo con cambios sin confirmar.
La ejecucion [41895547091](https://github.com/leoperetti/cekide-api/actions/runs/41895547091)
completo el check `test` correctamente (44 segundos), tras retirar el gitlink invalido.
Los permisos SQL de migracion y la prueba OIDC real estan comprobados; sigue pendiente
el pipeline unico de despliegue/migracion.
Ver [research.md](research.md) para subjects y evidencia exacta.
El usuario acepto costos bajos hasta USD 10; no se crearon runners ni recursos nuevos.

### Permisos SQL de migracion comprobados

| Identidad existente | Base autorizada |
|---|---|
| `cekide-api-stagi-id-a925` | `cekide-db-staging` |
| `cekide-api-id-8230` | `cekide-db` |

En cada base, `cekide_schema_migrator` concede `CREATE TABLE` y permisos
`ALTER`, `REFERENCES`, `SELECT`, `INSERT`, `UPDATE`, `DELETE` sobre `dbo`.
No concede `db_owner`, `db_ddladmin` ni gestion de usuarios. Las migraciones futuras
que necesiten otros esquemas/operaciones requeriran revisar permisos.
Se verificaron permisos efectivos con impersonacion SQL y persistencia desde conexiones
nuevas; las APIs conservan lectura/escritura sin DDL. No se ejecutaron migraciones.
La prueba OIDC y de denegacion cruzada desde Actions paso para ambos entornos.

La identidad de despliegue staging se desvinculo de la Web App con aprobacion expresa,
para que la API no pueda solicitar sus credenciales DDL. La identidad system-assigned
runtime se conservo sin cambios, igual que los subjects OIDC y `AZURE_CLIENT_ID`.
No se necesita `AZURE_MIGRATION_CLIENT_ID` adicional.

La regla permanente `CekideOperatorPublicIp`, solicitada por el usuario, permite acceso
administrativo solo desde su IP indicada. Si cambia, actualizarla; el firewall no sustituye
autenticacion ni concede permisos SQL. Es independiente de las reglas temporales del runner.

### Diagnostico SQL desde GitHub Actions

Workflow manual: [sql-connectivity.yml](../../.github/workflows/sql-connectivity.yml).
No despliega, no ejecuta migraciones ni consulta datos de negocio.
El programa comprueba token de la identidad/tenant esperados, usuario/base SQL,
permisos efectivos requeridos y ausencia de `db_owner`/gestion de usuarios.
Solo acepta errores SQL de autenticacion/autorizacion como denegacion cruzada;
un fallo de red/firewall no equivale a aislamiento correcto.

Los PR [#2](https://github.com/leoperetti/cekide-api/pull/2) y
[#3](https://github.com/leoperetti/cekide-api/pull/3) se publicaron/fusionaron con
autorizacion expresa y CI correcto, sin incluir el resto de cambios locales.
La primera ejecucion `41899312406` paso staging pero fallo production con 40615
(runner fuera de Azure). Por eso no se debe confiar solo en `AllowAllWindowsAzureIps`.

La [ejecucion 41900175393](https://github.com/leoperetti/cekide-api/actions/runs/41900175393)
paso staging y production, esta ultima tras aprobacion manual del usuario:

| Identidad | Base propia | Base ajena |
|---|---|---|
| `cekide-api-stagi-id-a925` | Conexion y permisos de migracion correctos | `cekide-db`: denegado, SQL 18456 |
| `cekide-api-id-8230` | Conexion y permisos de migracion correctos | `cekide-db-staging`: denegado, SQL 18456 |

El rol [sql-firewall-role.json](../../scripts/diagnostics/sql-firewall-role.json)
se asigno a ambas identidades solo en el servidor SQL `cekide`. Permite unicamente
`firewallRules/read`, `write`, `delete`, sin administracion SQL ni nuevos permisos de datos.
Azure RBAC permite administrar todas las reglas de ese servidor, no solo el prefijo `gha-`;
el workflow limita las operaciones a su nombre unico y no altera reglas permanentes.
Ambos pasos de limpieza pasaron; una lectura ARM final confirmo que solo quedan
`AllowAllWindowsAzureIps` y `CekideOperatorPublicIp`.

Para repetir: ejecutar manualmente `SQL connectivity diagnostic` sobre `main` y aprobar
production en GitHub cuando corresponda. Hay reintentos acotados de reanudacion SQL y
propagacion de firewall. Ante perdida del runner/cancelacion forzada puede quedar una
regla huerfana: revisar y eliminar solo `gha-<run>-<attempt>-<environment>` de esa ejecucion.
Los workflows de despliegue antiguos siguen deshabilitados; T006 sigue abierta hasta
probar el mantenimiento y la secuencia real de despliegue/migracion.

### Firewall reutilizable preparado y verificado

La continuacion de implementacion extrae el manejo de reglas a
[Set-RunnerSqlFirewall.ps1](../../scripts/deployment/Set-RunnerSqlFirewall.ps1).
El diagnostico lo usa en ambos environments; CI agrega las pruebas offline.
Con autorizacion expresa, el [PR #4](https://github.com/leoperetti/cekide-api/pull/4)
publico solo los dos scripts y los cambios de CI/diagnostico, y se fusiono tras CI.
No incluyo el resto de cambios locales de la feature.

El script exige subscription, environment, run ID y attempt explicitos. Verifica que
Azure CLI este autenticada con el client ID de despliegue de ese entorno y el tenant
esperado. Open valida una IPv4 unica, rechaza redes privadas/rangos/loopback y comprueba
la persistencia de la regla. No sobrescribe una regla existente con otra IP.
Close no depende del servicio externo de IP, es repetible y comprueba que la regla
desaparecio. Ambos limitan las escrituras al nombre `gha-<run>-<attempt>-<environment>`;
los fallos CLI/JSON/permisos se propagan, no se confunden con ausencia de regla.

Validacion local, sin Pester ni dependencias nuevas:

```powershell
& .\scripts\deployment\Test-RunnerSqlFirewall.ps1
dotnet build .\scripts\diagnostics\SqlConnectivity\SqlConnectivity.csproj --configuration Release --no-restore
```

Resultado: 16 pruebas offline pasaron en Windows PowerShell; parser PowerShell sin errores
y build del diagnostico sin advertencias/errores. Incluyen fallo sintetico de migracion
con limpieza `finally`, error de create/delete, regla persistente tras delete, IP invalida,
identidad/tenant/subscription incorrectos, colision de IP, repeticion y preservacion de
las dos reglas permanentes. No se contacto Azure ni se ejecuto SQL para estas pruebas.
CI `41902390425` tambien paso en PowerShell 7/Ubuntu: 16 pruebas offline y las
172 unitarias / 193 de integracion existentes en `main`, sin fallos ni omisiones.
Esos totales remotos difieren de los 233/202 locales porque la feature local aun no fue
publicada; no confundir ambos conjuntos.

La [ejecucion 41902573356](https://github.com/leoperetti/cekide-api/actions/runs/41902573356)
verifico el helper real en ambos entornos: Open creo/verifico cada regla, SQL permitio
la base propia y denego la ajena con 18456, y Close elimino/verifico la ausencia.
El usuario aprobo production manualmente. Una lectura ARM final confirmo solo
`AllowAllWindowsAzureIps` y `CekideOperatorPublicIp`; no quedaron reglas de runners.
Esta evidencia verifica la extraccion, no un pipeline completo de release.

Al implementar T034/T038, usar Open antes de conectar SQL y Close en un paso `always()`
posterior a login, con los mismos run/attempt/environment. En una invocacion PowerShell
compuesta, envolver la operacion SQL en `try/finally` y llamar Close en `finally`.
Nunca iniciar la Web App desde ese bloque de limpieza: el inicio requiere migracion exitosa.
El helper no autentica, no ejecuta migraciones ni administra Web Apps; no elude el
preflight, las aprobaciones ni los gates de Blob/secretos/recuperacion.
T032, T034 y T038 siguen abiertas: faltan el validador completo del flujo, el bundle y
la integracion de release verificada. Ningun workflow de despliegue fue habilitado.

En un cambio de prueba aprobado:

1. Abrir un pull request y verificar que compilación/pruebas corren sin publicar.
2. Fusionar el cambio en `main` y confirmar que GitHub Actions publica automáticamente
   primero en `cekide-api-staging`.
3. Confirmar que, después de desplegar la API de staging, se ejecutan automáticamente las
   migraciones de la base `cekide-db-staging` como paso único; luego ejecutar validaciones
   smoke/funcionales y comprobar conectividad con la base y el prefijo Blob de staging.
4. Probar una migración o validación fallida en staging y confirmar que producción no se
   publica. Probar un fallo en la migración productiva y confirmar que los pasos posteriores
   se detienen sin restauración destructiva ni pérdida automática de escrituras.
5. Probar el camino exitoso y confirmar que el entorno protegido de producción espera
   aprobación manual.
6. Confirmar que production recibe exactamente el artefacto validado en staging: el hash
   criptográfico del paquete debe coincidir y no debe haber una nueva ejecución de build o
   publish entre staging y production. Después del despliegue productivo, ejecutar
   automáticamente las migraciones de `cekide-db`, validar salud y registrar commit,
   versión, hash, aprobador y resultado. Verificar que staging y producción son dos App
   Service Web Apps distintas.

**Esperado**: no hay ruta alternativa que publique directamente a production; un fallo o
falta de aprobación bloquea la promoción. Las migraciones se ejecutan una sola vez por
entorno después de desplegar la API; un fallo bloquea etapas posteriores. Las dos
aplicaciones permanecen independientes, pero reciben el mismo artefacto inmutable tras su
validación.

## 5. Validar Blob Storage compartido

Confirmar que development, staging y production usan la cuenta y el contenedor privado
compartidos `cekidecontainer`, con los prefijos `development/`, `staging/` y `production/`
en sus rutas de blobs. Cada aplicación Azure debe usar su identidad administrada y
development una identidad Entra de desarrollador; no usar claves de cuenta ni SAS de
cuenta. En cada entorno, guardar y leer un objeto de prueba en su propio prefijo.
Con cada identidad, intentar leer, sobrescribir y borrar objetos de los otros entornos.

**Esperado**: las operaciones propias funcionan y las operaciones cruzadas son denegadas
por Azure ABAC en lectura, escritura, listado y borrado, no solo ocultadas por la aplicación.
Las condiciones siguen pendientes de implementacion y validacion para cada identidad.
No aceptar una clave de cuenta compartida con acceso total como aislamiento suficiente.

## 6. Validar recuperación

Ejecutar el procedimiento aprobado en staging y, con aprobación operativa, en una ventana
de prueba de producción:

- confirmar que se puede identificar el último artefacto estable;
- confirmar un respaldo restaurable antes de cualquier migración productiva de riesgo;
- probar que una migración fallida detiene el flujo sin restaurar automáticamente la base
  y perder escrituras válidas recientes;
- desplegar la versión estable sin perder los registros de publicación/incidente.

**Esperado**: la API puede volver a una versión operativa y los datos mantienen integridad.
La reversión de binarios por sí sola no se considera recuperación de cambios de esquema;
ante fallo de migración se prefiere una corrección hacia delante, y una restauración de
datos requiere decisión explícita y evaluación de pérdida de escrituras.
