# Research: Entornos y gestión de secretos

## Decisiones

### Reemplazo aprobado de Storage y ABAC de los tres entornos verificado (2026-11-04)

El usuario informo que elimino la cuenta anterior, sin datos que recuperar, y luego
solicito crear `cekideblobaccount` al comprobar que ARM aun mostraba la anterior.
Se mantuvo intacta la anterior; no se presupone que su eliminacion haya terminado.
El usuario eligio explicitamente Standard_LRS + Hot y autorizo creacion en CEKIDE,
Brazil South, asignaciones ABAC y consumo de pruebas sinteticas.

Se creo la nueva cuenta con contenedor privado `cekidecontainer`, HTTPS/TLS 1.2 y
shared key deshabilitada, reutilizando identidades y rol minimo. El what-if mostro
solo cuenta/servicio/contenedor nuevos y tres grants; rol existente sin cambios,
resto de recursos ignorados en modo incremental. `cekide-blob-replacement` termino
Succeeded y los ajustes/grants se releyeron por ARM.

Development paso 18 comprobaciones reales: CRUD/list propio, rechazo con 403
AuthorizationPermissionMismatch para los otros entornos y listados raiz/limite.
La limpieza nueva fue exitosa. El error 500 no se reprodujo, sin determinar su causa
original. LRS evita el costo de replica geografica pero no ofrece esa recuperacion;
Hot evita el minimo de retencion Cool para estas pruebas, sin implicar gratuidad.
Con autorizaciones posteriores separadas se verifico ejecucion .NET 10 y endpoint MI
en Kudu/SCM staging/production. Se subio unicamente el diagnostico a carpetas aleatorias
fuera de `site\wwwroot`, se ejecuto con `ManagedIdentityCredential(SystemAssigned)` y
se retiraron sus carpetas, comprobando ausencia. El probe compara principal/tenant del
token devuelto por la credencial contra IDs fijos antes de operar; rechaza ejecucion
local que intente seleccionar un App Service. Ningun token se imprime.
Cada identidad Azure paso 18 comprobaciones; development repitio otras 18 con el mismo
guard. Total vigente: 54 PASS, con limpieza correcta. No se modificaron User Secrets,
ajustes remotos de APIs ni se reiniciaron/desplegaron. Aun faltan configuracion del
destino en las APIs, rutinas y pruebas runtime SQL/vault. Ver [quickstart.md](quickstart.md).
Las decisiones e inventarios siguientes sobre la cuenta anterior son historicos.

### Blob ABAC anterior al reemplazo; validacion bloqueada (2026-11-04, historico)

Esta actualizacion reemplaza el estado anterior "ABAC no configurado".
El usuario eligio una aplicacion Entra dedicada para development con certificado local,
y autorizo identidad/certificado, what-if, tres asignaciones ABAC y consumo de pruebas
sinteticas en Cool RA-GRS. No autorizo cambiar claves compartidas, planes, datos de
negocio, desplegar APIs ni ejecutar pruebas suplantando identidades runtime.

La aplicacion `cekide-blob-development` se creo sin permisos administrativos, con
certificado RSA local (ACL restringida, fuera del repo, vence 2027-05-04) y solo la
clave publica en Entra. Los IDs no sensibles estan en [data-model.md](data-model.md).
Su rol condicionado no reutiliza el Owner de la sesion del operador.

`infra/blob-access.bicep` referencia recursos existentes e invoca
`infra/modules/environment-access.bicep`. Se uso una entrada acotada en vez del
`infra/main.bicep` aun no implementado, para no mezclar permisos Blob aprobados con
cambios de Web Apps, vaults o infraestructura pendientes. El what-if propuso solo
un rol y tres asignaciones; el deployment `cekide-blob-abac` termino Succeeded.
Una segunda revision/aplicacion solo modifico las tres condiciones para alinearlas
con la estructura de implicaciones del ejemplo Microsoft; no se ampliaron permisos.

El rol personalizado contiene exactamente read/write/delete/add Blob, sin gestion,
claves, delegacion, move ni comodines. Las condiciones separan lectura no-listado y
escritura/borrado por `@Resource path` de listado por `@Request prefix`, con
`StringStartsWith '<entorno>/'` sensible a mayusculas y scope del contenedor.
Listar raiz no debe funcionar; no es filtrado de resultados.
Se evito Storage Blob Data Contributor porque agrega gestion de contenedores y
delegation keys que runtime no necesita.

Fuentes verificadas:
- [Ejemplo read/list por ruta](https://learn.microsoft.com/en-us/azure/storage/blobs/storage-auth-abac-examples#example-read-or-list-blobs-in-named-containers-with-a-path).
- [Acciones, suboperaciones y atributos admitidos](https://learn.microsoft.com/en-us/azure/storage/blobs/storage-auth-abac-attributes).

Build Bicep, validacion estructural del JSON compilado y build del probe .NET pasaron.
Azure confirmo las tres condiciones persistidas. La prueba real development fallo
con HTTP 500 OperationTimedOut en upload/listado y limpieza; REST independiente con
API 2023-11-03 reprodujo el error. No hay evidencia para atribuirlo a ABAC, SDK o red.
Se conserva el bloqueo de T020/T022/T026/T028 y no se afirman denegaciones efectivas.
Ver [quickstart.md](quickstart.md) para la limpieza sintetica aun no confirmada.
El resto del feature y los ensayos runtime Azure siguen pendientes; no hubo restart,
despliegue, migracion SQL ni publicacion GitHub en esta continuacion.

### Decision vigente y alcance autorizado (2026-11-04)

El usuario autorizo preparacion local, mantiene produccion bloqueada y no autorizo mover ni
cambiar el plan compartido `ASP-AuthCraft-bc2b`. El usuario administrara Azure; las
escrituras remotas requieren aprobacion expresa. Se aprobaron y crearon el contenedor
privado `cekidecontainer` y los vaults separados `cekide-key-vault-staging` y
`cekide-key-vault-prod`. El usuario confirmo que los tres entornos usaran ese unico contenedor compartido,
por lo que la autorizacion debera restringirse con ABAC por prefijo y validarse antes de
habilitar los entornos. El usuario acepto posibles cargos de Blob Storage para la cuenta y
contenedor existentes, y posibles cargos de operaciones de Key Vault; esto no incluye
cambios RBAC/ABAC, costos de hosting u otros recursos, reconfiguracion de la cuenta ni
despliegues. No se iniciaran pruebas reales hasta validar ABAC.
Para T006, el usuario indicó que staging ya está creado y rechazó planes de hosting con
costo adicional; aceptó el plan F1 compartido actual como excepción temporal, sin crear ni
modificar planes. La Web App sigue en `AuthCraft`, fuera de `CEKIDE`, y comparte compute
con otras apps. El usuario eligió los backups/PITR SQL existentes; cualquier ensayo de
restauración requiere aprobación separada y debe restaurar a una base nueva. Aceptó
detener staging durante la migración y que solicitudes de prueba en curso se interrumpan.
Solicitó vincular `cekide-api-staging` con `cekide-db-staging` mediante identidad
administrada. La Web App ya tenía identidades system-assigned y user-assigned; se usó la
system-assigned existente. Tras aprobación explícita de posibles cargos, la base pausada se
reanudó y se creó el principal SQL `cekide-api-staging` con `db_datareader` y `db_datawriter`,
sin DDL. La conexión usa `Authentication=Active Directory Managed Identity`. Se guardó la
cadena no secreta como `CekideDatabaseConnectionString` en el Key Vault de staging. Para
la operación se permitió temporalmente una única IP en el firewall y se eliminó esa regla
al finalizar. El estado SQL quedó `Online`, con auto-pausa de 60 minutos. T006 sigue abierta
por la ruta de red desde GitHub Actions.
El plan `ASP-AuthCraft-bc2b` no debe moverse: ARM confirma que es
F1/Free, Windows, West US 3 y tiene dos sitios asociados, por lo que un cambio de plan
puede afectar la aplicacion AuthCraft ajena al alcance.

### Ruta inicial de GitHub Actions a Azure SQL (2026-11-04)

Esta seleccion inicial fue reemplazada tras el diagnostico real: ver
"Diagnostico real de OIDC y SQL desde Actions" mas abajo.

El usuario eligio reutilizar la regla SQL existente `AllowAllWindowsAzureIps` para los
runners hospedados de GitHub Actions y acepto costos bajos de hasta USD 10. No se creara
un runner dedicado ni se modificara el firewall para el runner. Una consulta ARM/CLI inicial de solo lectura
confirmo que el servidor `cekide` tiene `publicNetworkAccess=Enabled` y que esa es la unica
regla de firewall reportada. La regla `0.0.0.0` no restringe el origen a GitHub ni a esta
suscripcion: la documentacion de Microsoft advierte que permite llegar al servidor a
cualquier recurso Azure, incluso en otras suscripciones. Esta excepcion amplifica la
superficie de red; la autorizacion efectiva debe seguir dependiendo de identidades Entra
separadas y permisos SQL minimos. No se usara autenticacion SQL con contrasenas en Actions.

Diseño aprobado para preparar el pipeline, sin habilitarlo:

1. Los runners hospedados de GitHub autentican a Azure mediante OIDC y credenciales
   federadas separadas por environment (`staging` y `production`), sin publish profiles
   ni secretos Azure de larga duracion.
2. Reutilizar, por decision posterior expresa, cada identidad de despliegue tambien para
   migraciones de su entorno, sin crear otras identidades. Conserva control limitado a
   su Web App y recibe DDL solo en su base. La identidad runtime de cada API sigue
   separada, con solo lectura/escritura de datos y sin DDL. Production mantiene
   aprobacion manual y sigue bloqueada hasta completar sus gates.
3. La secuencia de staging sera serial: detener `cekide-api-staging`, publicar el paquete
   con reinicio automatico desactivado, ejecutar el migration bundle desde el runner con
   la identidad DDL de staging, y arrancar la Web App solo si la migracion termina bien.
   Ante cualquier fallo, detener etapas posteriores y mantener la app detenida para evitar
   jobs o trafico contra un esquema incompatible. El usuario acepta la interrupcion de
   staging durante la ventana.
4. Antes de usar este mecanismo, comprobar en staging que el despliegue conserva el estado
   detenido, que el runner puede autenticar contra SQL y que el migration bundle termina.
   No se realizo aun esa prueba real. La CLI local confirma que `az webapp deploy` soporta
   `--restart false`; eso no sustituye la verificacion end-to-end.

La ruta de red queda seleccionada y los permisos SQL se aplicaron posteriormente, pero
T006 no se cierra hasta verificar acceso desde un runner y probar el ciclo stop/deploy/migrate/start
en staging. El consentimiento de costos de hasta USD 10 no autoriza crear infraestructura
ni exceder ese limite.

### Protecciones GitHub y OIDC aplicadas (2026-11-04)

Tras completar el login, se verifico acceso ADMIN al repositorio PUBLIC. Con autorizacion
expresa se desactivaron los tres workflows de despliegue (IDs `405933430`, `407146077` y
`405940689`), incluido `main_cekide-api-staging.yml`, presente en GitHub pero no en el
checkout local. No se cancelaron ejecuciones ni se desplego ninguna aplicacion.

Se crearon/configuraron los environments `staging` y `production`, ambos limitados a la
rama `main` (no tags). Production requiere aprobacion de `leoperetti`, permite
autoaprobacion por decision expresa del usuario y no permite bypass administrativo.
La rama `main` exige PR, check `test` de GitHub Actions (app ID `15368`) actualizado contra
la base y conversaciones resueltas; no exige aprobacion independiente, segun el flujo
individual autorizado. Se bloquearon force-push y borrado, y las reglas aplican a admins.

Se reutilizaron las identidades de despliegue existentes, distintas del runtime:

| Environment | Identidad de despliegue | Permiso existente |
|---|---|---|
| staging | `cekide-api-stagi-id-a925` | Website Contributor solo en `cekide-api-staging` |
| production | `cekide-api-id-8230` | Website Contributor solo en `cekide-api` |

Cada identidad conserva una unica credencial federada `3xt3lfnwtus3m`, actualizada desde
`ref:refs/heads/main` a su environment. Se consulto la configuracion OIDC del repositorio:
`use_default=true`, `use_immutable_subject=true`. Los subjects son
`repo:leoperetti@12513421/cekide-api@1444519630:environment:staging` y
`repo:leoperetti@12513421/cekide-api@1444519630:environment:production`.
Issuer `https://token.actions.githubusercontent.com`, audience
`api://AzureADTokenExchange`. No se agregaron permisos SQL, roles nuevos ni secretos.
Se configuraron cinco variables no sensibles por environment: `AZURE_CLIENT_ID`,
`AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `AZURE_RESOURCE_GROUP` y `AZURE_WEBAPP_NAME`.
Todas estas configuraciones se releyeron por API/CLI tras aplicarlas.

El [PR #1](https://github.com/leoperetti/cekide-api/pull/1) publica solo un workflow CI
independiente y la eliminacion autorizada de una referencia gitlink invalida `cekide-api`
sin `.gitmodules`, que impedia completar `actions/checkout`. No elimina carpetas locales.
No incluye los cambios locales de la feature ni autoriza despliegue. El usuario fusiono
el PR el 2026-11-04 a las 19:18:55 UTC (commit
`df015d8aa8cb69e4d4f9845c371622f9879efa20`). CI de `main` paso en `41895748105`.
No se actualizo el checkout local ni se tocaron los cambios sin confirmar del usuario.
La ejecucion CI `41895547091` completo el check `test` correctamente en 44 segundos; la
primera ejecucion `41895356084` habia fallado en checkout por el gitlink sin configurar.
Los workflows antiguos no deben reactivarse: aun carecen del circuito completo de
staging/migracion/validacion/promocion. T037 permanece abierta hasta completar sus
prerrequisitos y probar OIDC; configurar protecciones no demuestra un release real.

### Permisos SQL de migracion aplicados (2026-11-04)

El usuario autorizo reutilizar las dos identidades de despliegue para migraciones, sin
crear identidades adicionales. Se crearon usuarios externos contenidos en su propia base:

| Base | Usuario de despliegue/migracion | Client ID (SID SQL) |
|---|---|---|
| `cekide-db-staging` | `cekide-api-stagi-id-a925` | `592310f3-be13-498a-bf41-eda2d8762698` |
| `cekide-db` | `cekide-api-id-8230` | `acd40a92-c0c7-4bd7-bca0-60ae8b31bb1b` |

Cada usuario pertenece al rol personalizado `cekide_schema_migrator`, con `CREATE TABLE`
en su base y `ALTER`, `REFERENCES`, `SELECT`, `INSERT`, `UPDATE`, `DELETE` solo sobre
`SCHEMA::dbo`. Se revisaron las cuatro migraciones actuales: crean tablas, indices y
claves foraneas en ese esquema. No se asignaron `db_owner`, `db_ddladmin`, gestion de
usuarios ni permisos en la base ajena. Una futura migracion fuera de este alcance exige
revision de permisos, no ampliacion automatica.

La creacion uso `WITH SID = ..., TYPE = E` con el **client ID**, no el object/principal ID,
como corresponde a service principals/identidades administradas; asi no depende de que
SQL consulte Microsoft Graph. Referencia:
[CREATE USER, ejemplo J](https://learn.microsoft.com/en-us/sql/t-sql/statements/create-user-transact-sql?view=azuresqldb-current#j-create-a-microsoft-entra-user-without-a-microsoft-entra-login-using-the-sid-syntax).

Se verificaron permisos efectivos mediante `EXECUTE AS USER`: DDL/DML de migracion
habilitado, `CONTROL` de base y `ALTER ANY USER` ausentes. Los usuarios runtime
`cekide-api-staging` y `cekide-api` conservan `db_datareader`/`db_datawriter`, sin
`CREATE TABLE` ni `ALTER` sobre dbo. Nuevas conexiones administrativas confirmaron la
persistencia de usuarios, SID, roles y grants. Se comprobo que no existe mapeo del
migrador contrario ni grupos externos en cada base; no se probo aun login OIDC real ni
denegacion cruzada autenticando como esas identidades desde Actions.

Antes de otorgar DDL, se detecto que la identidad user-assigned de despliegue staging
tambien estaba adjunta a su Web App. Con aprobacion expresa se retiro solo esa asignacion;
se conservaron la identidad system-assigned, su principal, Key Vault y el usuario SQL
runtime. La Web App quedo `Running`, con `keyVaultReferenceIdentity=SystemAssigned`.
No se elimino la identidad user-assigned ni su federacion OIDC.

A pedido expreso del usuario, se dejo la regla SQL permanente `CekideOperatorPublicIp`
limitada a la unica IP publica indicada, sin publicar la IP en el repositorio. Si cambia
la IP, hay que actualizar la regla. No reemplaza autenticacion Entra ni permite rangos
adicionales; `AllowAllWindowsAzureIps` sigue sin cambios para el runner. Ambas bases
estaban pausadas; el primer intento devolvio 40613 y el reintento tras reanudacion funciono.
No se ejecutaron migraciones, consultas de datos de negocio, despliegues ni reinicios
explicitos. Los workflows de despliegue siguen deshabilitados y no se cambio GitHub.
Se verificaron los subjects OIDC y los `AZURE_CLIENT_ID` existentes por environment;
no hace falta una variable de migracion adicional al reutilizar la identidad.
La herramienta SQL temporal emitio avisos NuGet NU1902/NU1903 para su dependencia
transitiva `System.Security.Cryptography.Pkcs` 9.0.11; no se cambiaron paquetes del repo
y la herramienta temporal se retiro al terminar.

### Diagnostico real de OIDC y SQL desde Actions (2026-11-04)

El usuario autorizo publicar PR aislado, fusionar tras CI y ejecutar diagnostico, sin
incluir los cambios locales de la feature ni saltar la aprobacion productiva. PR #2
agrego el workflow manual `sql-connectivity.yml` y una herramienta .NET standalone.
Su build Release paso sin advertencias. El workflow solo corre en `main`, usa environments
y OIDC existentes, valida identidad/tenant esperados y consulta unicamente metadata SQL.
No ejecuta migraciones ni lee/modifica datos de negocio.

En `41899312406`, staging paso OIDC, acceso propio y denegacion SQL 18456 a prod.
Production fue aprobada por el usuario y paso OIDC, pero fallo SQL 40615: el runner
asignado no estaba cubierto por `AllowAllWindowsAzureIps`. La regla no garantiza la ruta
de todos los runners GitHub; reintentar buscando otra ubicacion no es una solucion fiable.

El usuario autorizo permitir temporalmente la IP exacta de cada runner y conceder los
permisos minimos de firewall. PR #3 agrego creacion de regla IPv4 unica por
run/attempt/environment, eliminacion `always()` y reintentos limitados de errores 40613
(reanudacion) y 40615 (propagacion). Estos errores nunca cuentan como denegacion cruzada.
Se creo `Cekide SQL Runner Firewall Operator`, asignado a las dos identidades de despliegue
solo en el servidor `cekide`: acciones `Microsoft.Sql/servers/firewallRules/read`,
`write`, `delete`; sin DataActions ni permisos de administracion de bases/roles.
El rol permite gestionar todas las reglas del servidor; la limitacion al prefijo `gha-`
la aplica el workflow, no Azure RBAC. No se asigno a identidades runtime.

Los PR #2/#3 se fusionaron solo tras pasar CI (`41899131611` y `41900019497`).
La ejecucion [41900175393](https://github.com/leoperetti/cekide-api/actions/runs/41900175393)
termino `success` para staging y production. El usuario aprobo production manualmente.
Ambas identidades conectaron a su base como el usuario esperado, con permisos
CREATE TABLE/ALTER/REFERENCES/DML en dbo y sin CONTROL/ALTER ANY USER/db_owner.
Ambas recibieron SQL 18456 al intentar conectar a la base contraria con el mismo token.
Las dos limpiezas pasaron; ARM confirmo solo las reglas permanentes
`AllowAllWindowsAzureIps` y `CekideOperatorPublicIp`. Ante perdida del runner o cancelacion
forzada, revisar y retirar manualmente la regla de esa ejecucion si quedara huerfana.
No se modificaron las reglas permanentes ni se activaron workflows de despliegue.
El diagnostico no prueba aun migracion real, stop/deploy/start, salud API o Blob ABAC;
T006 y los restantes gates siguen abiertos.

### Validacion temprana implementada localmente (T019/T023)

Se agregaron pruebas de arranque y se verifico la precedencia del proveedor Key Vault real
frente a JSON, variables de entorno y argumentos usando HTTP sintetico. Development
registra User Secrets e ignora Key Vault, y Testing sigue usando sustitutos sin Azure.
El codigo rechaza vaults del otro entorno antes de solicitar tokens, bases/servidores SQL
ajenos, credenciales SQL distintas de Managed Identity system-assigned, TLS inseguro y
destinos Blob distintos de la cuenta compartida. Los errores SQL de parsing se traducen
a errores de clave sin conservar mensajes internos que puedan revelar valores.
La suite Release paso con 233 pruebas unitarias y 202 de integracion.
No se aplicaron cambios Azure, no se leyeron valores de secretos remotos ni se ejercitaron
permisos reales. T029 sigue pendiente de rotacion/recarga y validaciones remotas previas.

La [documentacion de App Service](https://learn.microsoft.com/en-us/azure/app-service/overview-hosting-plans)
describe Free/Shared como computo compartido, con cuotas de CPU por aplicacion y sin
escalado horizontal. La [documentacion de cambio de plan](https://learn.microsoft.com/en-us/azure/app-service/app-service-plan-manage)
limita el cambio de una aplicacion a un plan compatible por grupo de recursos, region,
SO y webspace. La [guia de movimientos ARM](https://learn.microsoft.com/en-us/azure/azure-resource-manager/management/move-resource-group-and-subscription)
advierte que el movimiento cambia el resource ID, bloquea temporalmente los grupos y
puede requerir volver a crear asignaciones RBAC o reparar dependencias. Por eso no se
movera el plan compartido; cualquier migracion de la app Cekide se disena aparte y queda
sujeta a compatibilidad, costo, corte y aprobacion.

La consulta ARM GET inicial del 2026-11-04 encontro `cekide-api` y `cekidecontainer` en
`CEKIDE`, no encontro entonces `cekide-api-staging` y `az keyvault list` no devolvio vaults.
Despues, el usuario informo que creo staging; una consulta ARM GET posterior confirmo que
`cekide-api-staging` esta `Running` en `CEKIDE`, West US 3, y referencia el plan compartido
`ASP-AuthCraft-bc2b` en `AuthCraft` (F1/Free). No se movio ni modifico el plan. No se
consultaron secretos ni datos SQL, y las consultas no cambiaron recursos. La cuenta Storage
usa `Standard_RAGRS`, tier `Cool`; el usuario acepto los posibles costos de Blob en la
cuenta/contenedor existentes. El usuario también aceptó posibles costos de operaciones de
Key Vault; esto no confirma la factura real ni autoriza costos de hosting u otros recursos.

Con aprobación expresa del usuario, se crearon `cekide-key-vault-staging` y
`cekide-key-vault-prod` en `CEKIDE`, West US 3, Standard, RBAC habilitado, soft delete de
90 días y purge protection. El acceso público está habilitado. No se cargaron secretos,
no se asignaron roles ni se modificaron las Web Apps al momento de crearlos. No se
configuró diagnóstico/Log Analytics.

Después, con autorización explícita, se asignó `Key Vault Secrets User` a la identidad
system-assigned existente de `cekide-api-staging`, con scope limitado a su vault, y se
configuró `KeyVault__Name=cekide-key-vault-staging`. Posteriormente se guardó la conexión
de staging en ese vault. El inventario de nombres (sin leer valores) aún no contiene
`Jwt--Issuer` ni `Jwt--Audience`. Un GET a una ruta protegida respondió HTTP 401 sin token;
no se validó una consulta SQL ejecutada desde la API.

Después, con autorización expresa para cargos e interrupción en producción, se habilitó
la identidad system-assigned de `cekide-api`, se le asignó `Key Vault Secrets User` en
`cekide-key-vault-prod` y se creó el principal SQL `cekide-api` con `db_datareader` y
`db_datawriter`, sin DDL, en `cekide-db`. La base pausada se reanudó y quedó `Online`, con
auto-pausa de 60 minutos. La cadena Managed Identity se guardó como
`CekideDatabaseConnectionString` en el vault. El vault estaba vacío antes de guardar esta
clave. Se retiraron el rol temporal `Key Vault Secrets Officer` del operador y la regla
temporal de firewall de una sola IP. No se configuró `KeyVault__Name` ni se reinició la API:
faltan claves de configuración requeridas y el proveedor Key Vault no debe habilitarse
hasta cargarlas. No se validó una operación SQL desde la API productiva.

La implementacion de codigo local puede avanzar con pruebas, pero no se considera un
despliegue de staging ni de produccion. La Web App existente no resuelve el hosting
independiente: comparte el plan F1 externo a `CEKIDE`. Las plantillas, scripts y workflows
no deben aplicarse ni activarse desde este entorno; el modo de ejecucion autorizado es
preparacion para que el usuario administre Azure.

### Restriccion vigente y Storage verificado (2026-11-03)

El usuario rechazo las alternativas pagas siguientes: utilizar exclusivamente recursos
gratuitos. Autorizo verificar ofertas en modo lectura y detenerse si no se puede confirmar
su aplicacion. No hay autorizacion de cargos, excedentes ni cambios de SKU.

- La URL aportada identifica una cuenta Storage, no un contenedor:
  `cekidecontainer` en `CEKIDE`, Brazil South, `StorageV2`, SKU `Standard_RAGRS`,
  estado `Succeeded`. La consulta ARM inicial de contenedores devolvio una lista vacia;
  despues, a solicitud del usuario, se creo el contenedor privado homonimo.
- HTTPS obligatorio, TLS minimo `TLS1_2`, acceso publico Blob deshabilitado y claves
  compartidas permitidas. No se listaron claves, blobs ni valores secretos.
- Las politicas de suscripcion consultadas indican `PayAsYouGo_2014-09-01` y
  `spendingLimit=Off`. Esto no descarta beneficios gratuitos, pero tampoco acredita
  elegibilidad, vigencia o saldo de una cuota gratuita para Storage o Key Vault.
- No se confirmo una oferta gratuita aplicable a esa cuenta RA-GRS ni a Key Vault.
  La existencia de la cuenta no significa gratuidad. No aplicar a esta cuenta la
  estimacion historica Hot LRS / West US 3: tiene otra region y redundancia.
- La [guia oficial de uso gratuito](https://learn.microsoft.com/en-us/azure/cost-management-billing/manage/check-free-service-usage)
  indica verificar la tabla de servicios gratuitos en la vista de la suscripcion;
  sus datos pueden demorarse uno o dos dias. La documentacion publica no acredita
  el beneficio individual. Quedan pendientes medidores cubiertos, vigencia y cuotas.
- [Blob](https://azure.microsoft.com/en-us/pricing/details/storage/blobs/) depende
  de volumen, operaciones, transferencia y redundancia; las operaciones de secretos
  de [Key Vault](https://azure.microsoft.com/en-us/pricing/details/key-vault/) tienen
  tarifa. No se garantiza factura cero para ninguno con la evidencia disponible.

### Costos públicos del Blob existente (2026-11-04)

ARM confirma `Standard_RAGRS`, tier `Cool`, Brazil South, acceso público deshabilitado y
`allowSharedKeyAccess=true`; la aplicación no usa la clave compartida. La [Azure Retail
Prices API](https://prices.azure.com/api/retail/prices), consultada para `General Block
Blob v2`, `Cool RA-GRS`, `armRegionName=brazilsouth`, devuelve estos precios públicos de
lista en USD:

| Medidor | Precio |
|---|---:|
| Datos almacenados Cool RA-GRS | 0.0443 USD/GB-mes |
| Escrituras Cool GRS | 0.20 USD/10 000 operaciones |
| Lecturas Cool | 0.01 USD/10 000 operaciones |
| Recuperación de datos Cool | 0.01 USD/GB |
| Eliminación anticipada Cool | 0.052 USD/GB |

Ejemplo orientativo: 1 GB almacenado todo el mes, 10 000 escrituras, 100 000 lecturas y
1 GB recuperado suman 0.3543 USD en esos medidores. No incluye transferencia de salida,
operaciones adicionales, impuestos ni descuentos/créditos de la suscripción. Cool puede
cobrar recuperación y eliminación anticipada; el uso frecuente de PDFs debe medirse antes
de cambiar el tier. El usuario aceptó los posibles costos de Blob para la cuenta y el
contenedor existentes, pero no se iniciaron pruebas porque falta validar ABAC por prefijo
y evitar acceso cruzado desde la identidad de prueba.

Key Vault Standard figura públicamente a 0.03 USD/10 000 operaciones; auditoría/retención
puede sumar costos y no se confirmó elegibilidad gratuita. App Service F1 muestra precio
público de compute cero donde esté disponible, pero eso no confirma disponibilidad,
gratuidad de dependencias ni aptitud productiva. Los precios de lista no confirman la
oferta individual de la suscripción.

**Resultado inicial (antes de las actualizaciones del 2026-11-04)**: detener operaciones
remotas de implementacion por la instruccion del usuario. No crear vaults, no usar el
storage desde la API ni cambiar su redundancia. La creacion posterior del contenedor
privado homonimo fue autorizada explicitamente por el usuario. T004 se completo con el
inventario inicial de solo lectura; T005/T006 siguen abiertas. La Web App staging fue
creada despues por el usuario y verificada por ARM; ver la decision vigente al inicio de
este documento. Las propuestas B1 quedan rechazadas; no sustituir Key Vault o el
aislamiento por otra solucion sin una nueva decision.

### Evidencia de implementacion y bloqueo (2026-11-03)

- Consultas de solo lectura confirman las dos bases y `cekide-api` en `CEKIDE`.
  Ver [inventario](data-model.md) y [resultado inicial](quickstart.md).
- El plan de la Web App es `F1` / Free, Windows, pertenece a `AuthCraft` y aloja dos sitios.
  No cambiar su SKU ni moverlo: podria afectar otra aplicacion. No asumir que una nueva
  Web App en ese plan resuelve la aptitud de produccion o el requisito `CEKIDE`.
- Las dos bases tienen oferta gratuita activada y `AutoPause` al agotar el limite.
  No se verificaron factura, cuotas restantes ni restaurabilidad.
- Las paginas oficiales de Key Vault y Blob Storage se consultaron de nuevo: secretos
  se facturan por operaciones; Blob depende de volumen, transacciones, transferencia
  y redundancia. No se verifico una oferta gratuita aplicable a esta suscripcion y
  no hay una cotizacion aprobada. No crear esos recursos ni prometer factura cero.
- T005/T006 quedan bloqueadas para decisiones operativas y de costo. Primero resolver
  con el usuario si mantener solo preparacion local o evaluar una alternativa productiva
  con costo. Cualquier aprobacion debe delimitar recursos, SKU y gasto; no es implicita
  por invocar `/speckit-implement`.

### Alternativas de costo solicitadas (2026-11-03; sin aprobacion de compra)

El usuario eligio evaluar una alternativa apta para produccion y presentar costos antes
de aprobar. Se consulto la [Azure Retail Prices API](https://prices.azure.com/api/retail/prices)
para `armRegionName=westus3`, `type=Consumption`, moneda USD. Precios publicos de lista,
no una cotizacion de la suscripcion. Estimacion de compute con 730 horas/mes y una instancia:

| Componente | SKU / medidor consultado | Precio de lista USD |
|---|---|---|
| Produccion Windows, alternativa conservadora | Azure App Service Basic Plan, B1 App | 0.075/h; 54.75/mes a 730 h |
| Staging Windows, dedicado básico | Azure App Service Basic Plan, B1 App | 0.075/h; 54.75/mes a 730 h |
| Staging Windows, slots de despliegue | Azure App Service Standard Plan, S1 App | 0.10/h; 73.00/mes a 730 h |
| Produccion Linux, alternativa economica | Azure App Service Basic Plan - Linux, B1 | 0.017/h; 12.41/mes a 730 h |
| Staging, sujeto a cuota y disponibilidad | F1 / Free en un plan propio dentro de CEKIDE | Sin tarifa de compute del nivel gratuito; compute compartido, no dedicado; no incluye dependencias |
| Dos vaults de secretos | Key Vault Standard, Operations | 0.03 por 10 000 operaciones, segun consumo agregado |
| Blob compartido, Hot LRS | General Block Blob v2, Hot LRS Data Stored, primer tramo | 0.0184 por GB/mes |
| Escrituras Blob | Hot LRS Write Operations | 0.05 por 10 000 operaciones |
| Lecturas Blob | Hot Read Operations | 0.004 por 10 000 operaciones |
| Listado y creacion de contenedor | LRS List and Create Container Operations | 0.05 por 10 000 operaciones |

Ejemplo orientativo, no limite de gasto: 1 GB Blob, 10 000 escrituras,
100 000 lecturas, 10 000 listados y 10 000 operaciones de secretos entre ambos vaults
suponen 0.1884 USD/mes de esos medidores, ademas del compute. Subtotales:
54.9384 USD Windows o 12.5984 USD Linux. No incluyen impuestos, cambio de moneda,
trafico saliente/interregional, auditoria, retencion/recuperacion adicional, versiones de
blobs, consumo SQL no cubierto, GitHub Actions/artefactos fuera de cuota ni otros cargos.
Los meses con mas/menos horas cambian el compute. El consumo real y la elegibilidad no
estan verificados; estos subtotales no son una factura final ni un tope automatico.

**Opcion Windows B1**: crear un plan separado en `CEKIDE`, conservando el sistema
operativo Windows y sin escalar/mover el plan compartido de AuthCraft. B1 provee compute
dedicado a nivel de plan, pero no soporta deployment slots; un plan separado evita que
otras apps compartan sus instancias. Requiere acordar una ventana de mantenimiento y
tratamiento de solicitudes en curso para desplegar y migrar con tráfico bloqueado.
Antes de ejecutar, validar si la Web App existente puede cambiar a ese plan segun
webspace, region y grupo de origen; no prometer un traslado directo ni recrearla.

**Opcion Windows S1**: plan separado en `CEKIDE`, con slots de despliegue para preparar
y calentar la version antes del cambio de tráfico. Microsoft indica que Standard admite
slots y que el swap redirige el tráfico sin descartar solicitudes; las operaciones largas
pueden terminar al reciclar instancias. El slot no debe ejecutar jobs ni usar la identidad
de runtime antes de la migración. Usar migraciones expand/contract compatibles y una
identidad de migración separada. El costo de lista estimado para un plan S1 y 730 horas es
73.00 USD/mes; no incluye SQL, red, transferencia ni otros cargos. La configuración de
GitHub Actions/red de despliegue sigue por verificar.

La documentación oficial indica que Free/Shared ejecuta apps en compute compartido y no
permite scale-out; crear un plan F1 separado no satisface el requisito de compute dedicado.
Los costos B1/S1 son precios públicos de lista para West US 3, no una cotización de la
suscripción. El usuario rechazó los planes de hosting con costo adicional; no se aprobó
ninguno de estos costos ni la creación/modificación del plan.

**Opcion Linux**: menor tarifa base, pero la Web App actual es Windows. No es un cambio
de SKU equivalente ni una migracion in-place garantizada: requiere validar .NET 10,
compatibilidad, nuevo destino, nombre/DNS, publicacion, bundle y corte/rollback de trafico.
No eliminar `cekide-api` para liberar su nombre. Esa decision de migracion no esta aprobada.

Estas alternativas fueron investigadas pero no están aprobadas; no crear recursos ni
cambiar planes. B1 es compute dedicado básico, no alta disponibilidad; Basic no incluye
deployment slots. Standard incluye slots pero incrementa el costo.

Las bases existentes mantienen `useFreeLimit=true` y `AutoPause`; no se cambio esa
configuracion. La pausa por cuota limita disponibilidad; no equivale a garantia de
operacion continua ni costo cero de todas las dependencias.

**Estado historico, superado por la restriccion de gratuidad**: alternativas rechazadas.
T005 sigue abierta
por oferta real, consumo y aprobacion; T006 sigue abierta por migracion y mantenimiento.
No crear recursos ni cambiar planes hasta una decision explicita que delimite la opcion,
los cargos variables aceptados y el corte operativo.

### Seleccion de recuperación y decisión de no añadir hosting de pago (2026-11-04)

El usuario indicó que staging ya está creado, rechazó planes de hosting con costo
adicional y aceptó el plan F1 compartido actual como excepción temporal; no se creará ni
modificará ningún plan. La tarifa pública consultada para
West US 3 fue 0.075 USD/h para Windows B1 y 0.10 USD/h para Windows S1 (54.75 y 73.00 USD
por 730 h, respectivamente), únicamente como comparación. B1 ofrece compute dedicado pero
no slots; S1 admite slots. Un plan F1 propio seguiría usando compute compartido. El sitio
actual usa el plan F1 compartido `ASP-AuthCraft-bc2b` de `AuthCraft`, fuera de `CEKIDE`.
El usuario aceptó esa configuración como excepción temporal solo para staging; producción
sigue bloqueada.

El usuario eligió usar los backups automáticos/PITR existentes para recuperación. El
inventario SQL registra retención local de corto plazo de 7 días; no se ha ensayado una
restauración ni autorizado una. Azure SQL restaura a una base nueva y cobra la base
restaurada al completar la restauración. Cualquier ensayo o corte a una base restaurada
requiere aprobación separada; no crear backups ni bases adicionales.

Fuentes adicionales:

- [Planes App Service y facturacion](https://learn.microsoft.com/en-us/azure/app-service/overview-hosting-plans).
- [Restricciones para cambiar de plan](https://learn.microsoft.com/en-us/azure/app-service/app-service-plan-manage).
- [Niveles con deployment slots](https://learn.microsoft.com/en-us/azure/app-service/deploy-staging-slots).
- Medidores de la Retail Prices API: B1 Windows `ba302f7a-078b-4141-a636-a76315ba44ce`;
  B1 Linux `f8431db9-1242-59b0-816d-f3566b5b0823`;
  Key Vault Operations `0f824807-2376-435c-95c9-f992b67a07b0`;
  Hot LRS Data Stored `346659ec-c0fc-5215-bff0-df5cc2c59a51`.

### Recursos Azure existentes reportados

- **Decision**: El usuario reporta existentes la base staging `cekide-db-staging`, la
  base production `cekide-db` y la API/App Service production `cekide-api`. No tratar esos
  nombres como verificados: confirmar existencia, tipo, Resource Group `CEKIDE`, región,
  configuración y costo mediante lectura antes de usarlos.
- **Rationale**: Reutilizar recursos existentes puede evitar creación innecesaria, pero
  los nombres no confirman su identidad, ubicación o costo. La App Service `cekide-api-staging`
  fue creada posteriormente por el usuario y verificada por ARM; usa el plan compartido
  F1 en `AuthCraft`, lo que mantiene abierto el diseño de hosting de T006. La cuenta Blob
  compartida fue creada posteriormente por el usuario como `cekidecontainer`; ver la
  verificacion mas reciente al inicio de este documento.
- **Alternatives considered**: Crear o modificar recursos sin comprobar inventario/precios,
  descartado porque podrían tener configuración distinta o generar cargos no aprobados.

### Plataforma CI/CD

- **Decision**: Usar GitHub Actions obligatoriamente para CI, despliegue a staging y
  promoción aprobada a producción.
- **Rationale**: Es una restricción expresa del usuario y permite unificar los workflows
  existentes bajo un único flujo que conserva y promueve el mismo artefacto.
- **Alternatives considered**: Otras plataformas CI/CD, descartadas porque GitHub Actions
  es un requisito obligatorio.

### Grupo de recursos Azure

- **Decision**: Crear todos los recursos Azure de esta API dentro del grupo existente
  `CEKIDE`; no crear grupos de recursos adicionales para separar staging y producción.
- **Rationale**: La Constitution exige mantener la API bajo ese grupo. La separación de
  entornos se implementa con recursos, identidades, RBAC, configuración, datos y etiquetas
  distintos en el grupo compartido, sin confundir el alcance del grupo con el límite de
  aislamiento.
- **Alternatives considered**: Un grupo de recursos por entorno, rechazado porque viola
  la regla del proyecto. Compartir los mismos recursos de runtime/datos entre ambientes
  tampoco es aceptable porque elimina el aislamiento requerido.

### Blob Storage compartido y autorización por entorno

- **Decision**: Usar una única cuenta Azure Blob Storage para los tres entornos, con
  el contenedor privado `cekidecontainer` y prefijos `development/`, `staging/` y
  `production/`. Por decision del usuario, usar una condicion Azure ABAC propia para cada
  identidad y prefijo, cubriendo lectura, escritura, listado y borrado. Usar identidades
  Entra distintas; no usar claves de cuenta ni SAS de cuenta en las aplicaciones.
  Development autentica con la identidad Entra del desarrollador, limitada a
  `development/`.
- **Rationale**: Un nombre de prefijo por sí solo no restringe acceso. Azure RBAC permite
  asignar roles de datos a nivel de cuenta o contenedor. Azure ABAC puede condicionar
  operaciones de Blob por ruta en un contenedor comun; su condicion debe probar cada
  operacion soportada y los intentos cruzados. La API valida rutas como defensa adicional,
  no como sustituto de la autorizacion Azure.
- **Alternatives considered**: Tres contenedores con RBAC separado, alternativa tecnicamente
  mas simple que el usuario no selecciono. Un contenedor unico sin condicion por prefijo
  se rechaza porque permitiria acceso cruzado.
- **Pendiente**: Implementar y validar las condiciones ABAC para todas las operaciones
  soportadas y sus denegaciones cruzadas; verificar el costo de almacenamiento, operaciones
  y cualquier requisito operativo antes de habilitar consumo desde las identidades reales.

### Instancias independientes y promoción del artefacto

- **Decision**: Desplegar la API como dos Azure App Service Web Apps distintas, una para
  staging y otra para producción, dentro de `CEKIDE`. Construir una sola vez; desplegar el
  artefacto inmutable en staging y, tras validar y aprobar, promover el mismo artefacto
  a la App Service de producción sin reconstrucción.
- **Rationale**: Aplicaciones independientes aíslan configuración, identidad, conexiones
  y disponibilidad por entorno. Reutilizar el artefacto comprobado evita diferencias
  entre lo validado y lo que se publica a usuarios reales; registrar su hash permite
  verificar la identidad del paquete.
- **Alternatives considered**: Una App Service compartida con configuración condicional,
  descartada porque no constituye instancias independientes. Reconstruir para producción,
  descartado porque el paquete podría diferir del probado en staging.

### Costos y aprobación de recursos Azure

- **Decision**: Priorizar ofertas gratuitas elegibles y verificar precio, región,
  suscripción, límites, vencimiento de cuotas y cargos indirectos antes de crear o
  modificar recursos. Si hay costo o posibilidad de cargos, informar al usuario y esperar
  aprobación explícita antes de incurrir en ellos. No afirmar que una oferta Free garantiza
  factura cero.
- **Rationale**: La página oficial de Azure Free Services aclara que las ofertas son
  gratuitas hasta cantidades definidas y algunas solo por 12 meses para clientes nuevos.
  La página de App Service describe Free/Shared como para pruebas y aprendizaje, sin SLA,
  y dice que producción no está soportada. Key Vault cobra por operaciones; Azure SQL,
  Storage y observabilidad también dependen de servicio, región, cuota y consumo.
- **Alternatives considered**: Tratar créditos, cuotas promocionales o alertas presupuestarias
  como garantía de costo cero, rechazado porque pueden expirar o no evitar excedentes.
  App Service F1 tampoco es una opción productiva soportada.
- **Resultado**: No se verificaron costos de la suscripción/región ni de los recursos
  existentes. La creación de la App Service `cekide-api-staging` y la cuenta Blob, y
  cualquier operación con costo potencial, quedan pendientes de aviso y aprobación del
  usuario. No usar App Service Free/Shared para producción.

### Secretos locales de development

- **Decision**: Usar .NET User Secrets solo con `Development`. No consultar Azure Key Vault
  al ejecutar localmente.
- **Rationale**: ASP.NET Core carga User Secrets en desarrollo cuando existe `UserSecretsId`,
  que el proyecto `CekideApi.csproj` ya define. User Secrets evita guardar los valores en
  el repositorio, pero no cifra el archivo local y no es un almacén de producción.
- **Alternatives considered**: Variables de entorno locales o usar un vault remoto también
  en development. Se prefieren User Secrets porque el repositorio ya los usa y los usuarios
  no necesitan credenciales de staging/production para desarrollar.

### Staging y production en Azure Key Vault

- **Decision**: Usar un Key Vault por ambiente de Azure, con los mismos nombres lógicos
  de configuración que development y valores independientes. Usar `--` para representar
  `:` en nombres jerárquicos, por ejemplo `Jwt--Key`.
- **Rationale**: Los vaults son límites de seguridad; separarlos reduce el radio de impacto.
  Los valores se inyectan como proveedor de configuración, sin escribirlos en appsettings
  ni en el repositorio.
- **Alternatives considered**: Compartir un vault o usar variables de entorno con valores
  secretos. Se descartan por el mayor impacto de permisos incorrectos y exposición accidental.

### Identidad y autorización

- **Decision**: Autenticar las aplicaciones Azure con Managed Identity y autorizar lectura
  de secretos mediante Azure RBAC con privilegio mínimo y scope del vault propio. La
  identidad y permisos de aprovisionamiento/CI deben ser distintos de la identidad runtime.
- **Rationale**: Managed Identity evita credenciales de aplicación que haya que guardar y
  rotar. La guía Azure recomienda RBAC y no el modelo legacy de Access Policies.
- **Alternatives considered**: Client secret o certificado de service principal; rechazados
  para runtime Azure porque requieren custodiar otra credencial. Para apps separadas, usar
  identidades distintas mantiene aislamiento.

### Migraciones automáticas y protección de datos

- **Decision**: Después de desplegar cada versión de API en un entorno Azure, ejecutar
  automáticamente un migration bundle EF Core como job único y serializado, dirigido a la
  base de ese entorno. Generar el bundle una sola vez desde el mismo commit que el artefacto
  de API. Usar identidad de despliegue separada con privilegios de esquema; la identidad
  runtime no debe poder alterar el esquema. Las migraciones deben revisarse en PR,
  probarse y usar cambios compatibles hacia atrás/expand-contract.
- **Rationale**: EF Core recomienda migration bundles para despliegues automatizados y
  prefiere un paso de migración separado para revisión, privilegios mínimos y coordinación.
  La migración durante startup puede requerir privilegios excesivos, no ofrece revisión
  antes de aplicar y puede provocar problemas en despliegues multi-instancia. Fallos
  detienen el workflow; no se ejecuta rollback destructivo automático en producción.
- **Alternatives considered**: Llamar `Database.Migrate()` al iniciar cada réplica,
  descartado para producción. Aplicar `Down` o restaurar automáticamente al fallar,
  descartado porque puede perder datos y Azure SQL no restaura sobre la base existente.
- **Recuperación**: Verificar backups automáticos/PITR y su retención/costo para cada base.
  PITR crea una base nueva (no sobrescribe la existente), puede tardar horas y requiere
  un plan probado de cambio de destino. Ante una migración fallida, inspeccionar su estado
  y preferir una corrección hacia delante; una restauración requiere decisión operativa
  explícita y considerar las escrituras posteriores al punto restaurado.

### Orden y recarga de configuración

- **Decision**: Mantener User Secrets como proveedor de desarrollo y agregar Key Vault solo
  en Staging/Production, siguiendo el patrón ya presente en `Program.cs`. Tratar
  `KeyVault:Name` como configuración no secreta del entorno. Definir explícitamente el
  mecanismo de actualización/reinicio para que una rotación llegue a la aplicación.
- **Rationale**: ASP.NET Core aplica precedencia por orden de proveedores; el proveedor
  añadido después de `CreateBuilder` puede prevalecer sobre appsettings y variables de
  entorno ya cargadas. No se debe asumir que una variable de entorno sobrescribirá un
  secreto de Key Vault sin configurar deliberadamente el orden.
- **Alternatives considered**: Copiar secretos a App Service settings o consultar Key Vault
  por cada uso. Se descartan por duplicar secretos o aumentar dependencias y llamadas
  innecesarias en tiempo de petición.

### Rotación, auditoría y protección de datos

- **Decision**: Definir responsable, vencimiento y procedimiento de rotación por secreto;
  preferir identidad administrada para servicios Azure y automatizar rotación cuando el
  servicio/credencial lo permita. Habilitar auditoría de acceso a los vaults y no registrar
  valores de secretos.
- **Rationale**: Microsoft recomienda rotar credenciales restantes, usar etiquetas para
  metadatos de rotación, monitorear acceso y proteger vaults contra eliminación accidental
  o maliciosa mediante soft delete/purge protection.
- **Alternatives considered**: Rotación manual sin calendario y guardar metadatos sensibles
  dentro del valor; no proporcionan trazabilidad ni controles confiables.

## Fuentes oficiales

- [Azure: Free services and eligibility](https://azure.microsoft.com/en-us/pricing/free-services/)
- [Azure App Service pricing and Free/Shared plan limitations](https://azure.microsoft.com/en-us/pricing/details/app-service/windows/)
- [Azure App Service plans and compute tiers](https://learn.microsoft.com/en-us/azure/app-service/overview-hosting-plans)
- [Azure App Service deployment slots](https://learn.microsoft.com/en-us/azure/app-service/deploy-staging-slots)
- [Azure Key Vault pricing](https://azure.microsoft.com/en-us/pricing/details/key-vault/)
- [Azure SQL Database pricing](https://azure.microsoft.com/en-us/pricing/details/azure-sql-database/single/)
- [Azure Blob Storage pricing](https://azure.microsoft.com/en-us/pricing/details/storage/blobs/)
- [Azure Blob Storage RBAC assignments](https://learn.microsoft.com/en-us/azure/storage/blobs/assign-azure-role-data-access)
- [Azure Blob Storage ABAC conditions](https://learn.microsoft.com/en-us/azure/storage/blobs/storage-auth-abac)
- [Azure Blob Storage ABAC examples](https://learn.microsoft.com/en-us/azure/storage/blobs/storage-auth-abac-examples)
- [Azure role assignment conditions overview](https://learn.microsoft.com/en-us/azure/role-based-access-control/conditions-overview)
- [ASP.NET Core: Safe storage of app secrets in development](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets)
- [ASP.NET Core: Configuration with Azure Key Vault](https://learn.microsoft.com/en-us/aspnet/core/security/key-vault-configuration)
- [ASP.NET Core: Configuration providers and precedence](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/)
- [ASP.NET Core: Environments](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/environments)
- [Azure Key Vault: Secure your vault](https://learn.microsoft.com/en-us/azure/key-vault/general/secure-key-vault)
- [Azure Key Vault: RBAC vs access policies](https://learn.microsoft.com/en-us/azure/key-vault/general/rbac-access-policy)
- [Azure Key Vault: Secure secrets](https://learn.microsoft.com/en-us/azure/key-vault/secrets/secure-secrets)
- [Azure Key Vault: Secret rotation tutorial](https://learn.microsoft.com/en-us/azure/key-vault/secrets/tutorial-rotation)
- [EF Core: Applying migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying)
- [Azure SQL Database: Automated backups](https://learn.microsoft.com/en-us/azure/azure-sql/database/automated-backups-overview)
- [Azure SQL Database: Recovery using backups](https://learn.microsoft.com/en-us/azure/azure-sql/database/recovery-using-backups)

## Validación pendiente antes de aprovisionar

- Verificar en la suscripción y región reales el precio, elegibilidad, cuotas,
  excedentes y cargos indirectos de cada recurso. Avisar al usuario y esperar aprobación
  antes de cualquier acción que pueda generar cargos.
- Confirmar que el grupo existente `CEKIDE` está disponible para los recursos requeridos y
  definir una convención de nombres/etiquetas por entorno sin crear nuevos grupos.
- Verificar que App Service y los recursos de producción reportados cumplen requisitos de
  soporte; App Service Free/Shared no es apto para production.
- Verificar los recursos reportados y la App Service `cekide-api-staging`; la cuenta Blob
  aún debe crearse tras revisión de precio y aprobación de cargos potenciales.
- Seleccionar región, SKUs, redundancia y retención solo después de superar la verificación
  de costo y comunicar posibles cargos al usuario.
- Aprobar política de recuperación de base de datos y frecuencia de restauración de prueba.
- Definir el procedimiento de despliegue/migración que impide que una versión incompatible
  atienda tráfico antes de completar la migración post-despliegue.
- Determinar si el entorno de producción requiere acceso privado a Key Vault y cuáles
  secretos pueden sustituirse por identidad administrada del servicio destino.
- Probar en la versión elegida del proveedor de configuración cómo se aplica la rotación
  (recarga en caliente o reinicio controlado) antes de definir el procedimiento operativo.
