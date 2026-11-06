# Implementation Plan: Entornos aislados de API

**Branch**: `001-environment-separation` | **Date**: 2026-11-03 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/001-environment-separation/spec.md`

## Summary

Definir y habilitar tres entornos con recursos, datos, configuración e identidades
separados: development local, staging en Azure y producción en Azure. Development
usará .NET User Secrets; las aplicaciones staging y producción leerán sus secretos
desde Azure Key Vault con identidad administrada. Los tres entornos compartirán una
cuenta de Blob Storage con prefijos separados y autorización restringida por entorno.
Cada actualización de `main` se despliega primero a staging; tras validaciones y
aprobación manual se promueve a producción el mismo artefacto. Las migraciones se ejecutan
automáticamente después del despliegue de la API y deben bloquear el flujo ante fallos sin
poner en riesgo datos reales.

La API ya está implementada en .NET 10 y cuenta con integración para Azure Key Vault
en staging/producción. El trabajo futuro debe completar aislamiento de recursos,
permisos, configuración y flujo de publicación, además de consolidar los workflows
de despliegue existentes. Según el usuario, ya existen las bases Azure `cekide-db-staging`
y `cekide-db`, y las Web Apps `cekide-api` y `cekide-api-staging`. Una consulta ARM de
solo lectura confirma staging `Running` en `CEKIDE`, West US 3, con el plan compartido F1
`ASP-AuthCraft-bc2b` en `AuthCraft`; esta dependencia externa no resuelve el hosting
independiente previsto y requiere decisión en T006. Con aprobacion del usuario se creo
la cuenta Blob compartida `cekideblobaccount`, Standard_LRS + Hot en Brazil South, y su
contenedor privado `cekidecontainer`. Reemplaza el destino anterior sin modificarlo:
ABAC aplicado y probado desde las tres identidades reales; falta configurar el destino
y verificar el flujo de rutinas en las APIs Azure.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`).

**Primary Dependencies**: ASP.NET Core Web API, Azure.Extensions.AspNetCore.Configuration.Secrets,
Azure.Identity, EF Core SQL Server, Azure Blob Storage; GitHub Actions (obligatorio) para CI/CD.

**Storage**: SQL Server (LocalDB en development; una base Azure SQL Database distinta para
staging y otra para producción, sin compartir base ni cadena de conexión); Azure Blob
Storage en una sola cuenta compartida con los prefijos `development/`, `staging/` y
`production/`, y permisos que impidan el acceso cruzado. Azurite permanece disponible
para pruebas automatizadas.

**Testing**: `dotnet test cekide-api.slnx`; integration tests usan WebApplicationFactory y
SQLite en memoria; validaciones smoke/health en staging antes de promover.

**Target Platform**: Azure App Service Web Apps independientes para staging y producción;
la aplicación de producción reportada como existente se llama `cekide-api` y la solicitada
para staging es `cekide-api-staging`; ambas deben verificarse antes de operar. Ejecución
local para development, autenticada con identidad Entra limitada al prefijo `development/`
de Blob Storage.

**Project Type**: Web service API.

**Performance Goals**: No se especifica un objetivo de carga. Elegir SKUs tras medir la
carga esperada y revisar el presupuesto antes de provisionar.

**Constraints**:
- Cada entorno mantiene recursos, configuración, datos y credenciales aislados.
- Se priorizan recursos gratuitos elegibles. Verificar precio, elegibilidad, cuotas,
  excedentes y cargos indirectos antes de crear/modificar recursos o habilitar consumo.
  Informar al usuario si hay costo posible y esperar aprobación explícita antes de incurrir
  en él; no afirmar que una oferta Free garantiza factura de $0.
- Development usa User Secrets locales; staging y producción usan Key Vault Secrets.
- El acceso de runtime a cada vault se concede a la identidad administrada exclusiva de
  su aplicación; el acceso de publicación se concede separadamente y con privilegio mínimo.
- La configuración no secreta, como el nombre del vault, se inyecta por entorno y no se
  duplica como secreto.
- Staging usa datos sintéticos o anonimizados; producción conserva los datos reales.
- Los cambios de esquema de base de datos se realizan mediante migraciones EF Core y
  requieren un plan compatible de despliegue y recuperación. En Azure, se ejecutan
  automáticamente después de desplegar la API de cada entorno; los fallos detienen el
  flujo, no promueven la versión y no causan restauración destructiva automática.
- Los tres entornos usan la cuenta `cekideblobaccount` y el contenedor Blob privado `cekidecontainer`,
  con prefijos fijos. Por decisión del usuario, cada identidad requiere una condición
  ABAC que limite lectura, escritura, listado y borrado a su prefijo; el filtrado de rutas
  de la API por sí solo no autoriza acceso. No usar claves de cuenta compartidas.
- Un push/merge a `main` publica primero a staging; production requiere aprobación manual
  protegida y recibe el mismo artefacto validado.
- No habilitar publicación automática directa a producción desde más de un workflow.

**Scale/Scope**: Tres entornos de una API existente; dos Web Apps alojadas en Azure.
Ambas Web Apps y las dos bases Azure existen; staging referencia un plan compartido
externo a `CEKIDE`, cuya resolución queda pendiente de T006. La cuenta y el contenedor Blob
ya existen y sus posibles costos fueron aceptados; ABAC fue aplicado y verificado desde
las tres identidades (Azure desde SCM). Falta configurar y probar el flujo de las APIs Azure.
El trabajo incluye inventario, configuración, secretos, permisos, CI/CD, migraciones,
monitoreo y recuperación. No cambia endpoints ni lógica de dominio.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Entornos aislados**: PASS CONDICIONAL. Development usa base local; staging y producción
  usan App Services, bases y vaults distintos dentro de `CEKIDE`. Blob Storage se comparte
  por decisión del usuario, con prefijos fijos y autorización que debe impedir acceso cruzado.
- **Secretos protegidos**: PASS. User Secrets solo en development y Key Vault Secrets
  en staging y producción, sin valores secretos en el repositorio ni en logs.
- **Blob compartido**: PASS CONDICIONAL. La cuenta es única, con contenedores y prefijos
  por entorno; Azure RBAC debe denegar acceso cruzado y las claves de cuenta no se usan
  desde las aplicaciones.
- **Arquitectura por capas y contratos co-localizados**: PASS. No se requieren cambios
  en capas de aplicación ni contratos de servicios/repositorios.
- **Migraciones y datos**: PASS CONDICIONAL. Las migraciones se ejecutan automáticamente
  después del despliegue; producción requiere cambios compatibles, respaldo verificable,
  bloqueo en fallo y ausencia de restauración destructiva automática.
- **Pruebas**: PASS. CI ejecuta pruebas existentes; staging agrega validación funcional
  antes de cualquier promoción a producción.
- **Simplicidad y alcance**: PASS. No se cambian endpoints ni se agregan capas de código
  innecesarias; la implementación se limita a configuración, infraestructura y CI/CD.

**Gate pre-investigación**: PASS CONDICIONAL. La excepción de Blob compartido está
permitida por la Constitution, siempre que los prefijos y las autorizaciones eviten acceso
cruzado. Los costos y recursos reportados requieren verificación antes de operar.

## Diseño técnico

### Verificación de costos y aprobación de aprovisionamiento

Preferir ofertas gratuitas que sean elegibles y aptas para el entorno previsto, sin
prometer costo cero: la elegibilidad y facturación dependen de la suscripción, la región
y el consumo. Antes de crear o modificar cualquier recurso, verificar precio y posibles
cargos:

- Azure App Service F1 se describe para pruebas, experimentación y aprendizaje; no tiene
  SLA y su uso para cargas de producción no está soportado.
- Azure Key Vault factura operaciones de secretos; el número de lecturas, escrituras,
  auditoría y configuración elegida puede generar cargos.
- Azure SQL, Blob Storage, monitoreo, transferencia de datos, respaldos y otros recursos
  tienen precios/cuotas que dependen de la región, oferta y suscripción. Una cuota gratuita
  limitada o temporal no demuestra que la factura vaya a ser cero.

Si se detecta un costo o posibilidad de cargos, informar al usuario y no crear, modificar
ni habilitar consumo de pago hasta recibir aprobación explícita. Una oferta gratuita con
límite o caducidad no equivale a garantía de costo cero. Las alertas presupuestarias no
son límites duros de gasto y no sustituyen la comprobación.

App Service Free/Shared se describe para pruebas y aprendizaje, sin SLA ni soporte para
cargas productivas; no proponerlo para producción aunque sea gratuito. Key Vault, Azure
SQL, Blob Storage, transferencias, monitoreo y respaldos pueden cobrar según el plan y el
uso. También deben revisarse los costos asociados a recursos reportados como existentes
antes de desplegar o cambiar configuración.

El usuario reporta como existentes `cekide-db-staging`, `cekide-db`, `cekide-api` y
`cekide-api-staging`; ARM verificó las Web Apps en `CEKIDE` y staging `Running` en West US 3.
Staging referencia el plan compartido F1 `ASP-AuthCraft-bc2b` de `AuthCraft`; no mover ni
cambiar ese plan. El usuario aceptó los posibles costos de Blob Storage para la cuenta y
contenedor existentes, operaciones de Key Vault y la reanudación/uso de SQL para vincular
staging; esto no autoriza cambios de acceso/configuración de Blob, costos de hosting ni
despliegues. Se crearon los vaults separados con aprobación expresa. El vault de staging
está asignado a su identidad system-assigned mediante `Key Vault Secrets User` y
`KeyVault__Name`. Se creó el principal SQL `cekide-api-staging` con `db_datareader` y
`db_datawriter`, sin DDL; la cadena Managed Identity está en `CekideDatabaseConnectionString`.
La API respondió 401 en una ruta protegida sin token, pero no se validó una consulta SQL
desde la aplicación. Para producción, se habilitó la identidad system-assigned de `cekide-api`,
se asignó `Key Vault Secrets User` en el vault prod, se creó el principal SQL `cekide-api`
con `db_datareader`/`db_datawriter` (sin DDL) y se guardó `CekideDatabaseConnectionString`.
La base quedó `Online`, con auto-pausa de 60 minutos. No se configuró `KeyVault__Name` ni
se reinició la API: el vault estaba vacío antes de guardar la conexión y faltan las demás
claves requeridas; el vínculo de runtime aún no está activado.
Antes de operar, verificar la configuración restante y detenerse ante todo impacto no aprobado.
El deployment a un recurso existente no autoriza a crear dependencias adicionales.

Para T006, el usuario rechazó incurrir en costos de hosting adicionales y aceptó el plan
F1 compartido actual como excepción temporal solo para staging; no crear ni modificar
planes. La consulta pública para West US 3 estimó Windows B1 en 54.75 USD/mes a 730 horas
y Windows S1 en 73.00 USD/mes a 730 horas, solo como comparación. La Web App actual usa
F1 compartido en `AuthCraft`, fuera de `CEKIDE`. Para recuperación, eligió backups/PITR
existentes y restaurar únicamente a una base nueva, con autorización separada para ensayos.
El usuario ya aceptó detener staging e interrumpir solicitudes de prueba durante la
migración. El vínculo runtime con SQL quedó aplicado con autorización posterior expresa;
la ruta de red desde GitHub Actions y el proceso de migración/despliegue de staging siguen
pendientes en T006. Se utilizó la identidad system-assigned preexistente.

### Topología propuesta

| Entorno | Instancia de API | Base de datos | Datos | Estado |
|---|---|---|---|---|
| development | API ejecutada localmente | LocalDB propia | Desarrollo local | Usa identidad Entra del desarrollador, limitada por ABAC al prefijo `development/` del contenedor compartido |
| staging | `cekide-api-staging` (`Running`, `CEKIDE`, West US 3; usa plan compartido F1 en `AuthCraft`) | `cekide-db-staging` (Brazil South; `Online`, auto-pausa 60 min) | Sintéticos/anonimizados | Identidad, permisos SQL de datos y cadena en Key Vault configurados; validación SQL desde API, ruta Actions y recuperación pendientes |
| producción | `cekide-api` (`Running`, `CEKIDE`, West US 3) | `cekide-db` (Brazil South; `Online`, auto-pausa 60 min) | Datos reales | Identidad, permisos SQL de datos y cadena en Key Vault preparados; falta completar secretos y `KeyVault:Name` antes de activar; no desplegar |

El inventario combina recursos reportados por el usuario y consultas ARM de solo lectura
del 2026-11-04. La Web App staging comparte un plan fuera de `CEKIDE`; no se ha verificado
su identidad, configuración .NET ni acceso a datos.

Todos los recursos Azure de esta API deben permanecer dentro del grupo de recursos
existente `CEKIDE`; no crear grupos de recursos adicionales. El aislamiento de staging y
producción se realiza con recursos de ejecución, bases de datos distintas y no compartidas,
vaults, identidades, permisos, configuración y datos distintos, identificados por entorno.
Cada entorno usa su propia cadena de conexión y credenciales; staging nunca apunta a la
base de producción. Development permanece en LocalDB y no comparte la base local con
Azure. Blob Storage es la única excepción de recurso compartido; usar los prefijos de
entorno y autorización restringida descritos abajo.
Aplicar nombres y etiquetas de entorno (`development`, `staging`, `production`) para que
inventario, control de acceso y costos puedan distinguirse dentro del grupo compartido.
No crear App Insights, Log Analytics, Private Endpoints, respaldos adicionales ni otros
componentes opcionales sin verificar su costo e informar al usuario antes de cualquier
cargo potencial.

### Blob Storage compartido

**Actualizacion aplicada 2026-11-04**: con aprobacion expresa se implemento la entrada
acotada `infra/blob-access.bicep` y los modulos `infra/modules/storage.bicep` y
`infra/modules/environment-access.bicep`, sin tocar APIs/vaults/SQL. El deployment
incremental `cekide-blob-replacement` creo `cekideblobaccount`, Standard_LRS + Hot,
contenedor privado `cekidecontainer`, sin shared key, y tres asignaciones condicionadas por
prefijo. Development usa una aplicacion Entra dedicada con certificado local, no la
sesion Owner; el launcher `scripts/environments/Start-Development.ps1` selecciona
esa credencial sin modificar el login administrativo. Los grants se verificaron por ARM.
Los tests locales de estructura/build pasaron y cada entorno paso 18 comprobaciones reales
de CRUD/list propio y denegacion ajena/raiz, con limpieza exitosa. Los probes Azure se
ejecutaron con aprobacion separada en carpetas SCM fuera de la API, usando identidad
system-assigned y verificando principal/tenant del token; carpetas retiradas. No se
desplegaron/reiniciaron APIs ni cambiaron sus ajustes. El 500 de la cuenta
anterior no se reprodujo; su causa y limpieza previa no se verificaron. No se modifico
esa cuenta. Falta configurar el nuevo destino de las APIs, verificar rutinas y probar
SQL/Key Vault desde identidades runtime. No completar T022/T026 ni habilitar releases:
falta ademas componer `infra/main.bicep` con el resto de modulos.

Usar una sola cuenta y el contenedor privado compartido `cekidecontainer`, con los prefijos
fijos `development/`, `staging/` y `production/`. Un prefijo solo es un nombre de objeto;
no es una frontera de autorización en un contenedor Blob convencional. Por decisión del
usuario, cada identidad debe recibir una condición Azure ABAC limitada a su prefijo, que
cubra todas las operaciones requeridas (lectura, escritura, listado y borrado). Mantener
la validación del prefijo en la API como defensa adicional, no como sustituto de ABAC.

En staging y production usar la identidad administrada exclusiva de cada App Service. En
development usar una identidad Entra del desarrollador limitada al prefijo
`development/`; no guardar una clave de cuenta en User Secrets. No usar claves compartidas
ni SAS de cuenta en las aplicaciones. La condición ABAC y su denegación efectiva de acceso
cruzado deben validarse antes de habilitar los entornos; RBAC sin condición ni el filtrado
de rutas en la API impiden por sí solos el acceso cruzado.

Cambiar la configuración de storage de `ConnectionString` a autenticación Entra
(`DefaultAzureCredential`) y una ruta/prefijo configurado por ambiente. Probar desde cada
entorno la lectura, escritura y eliminación dentro de su ubicación, y confirmar que las
operaciones equivalentes hacia los otros dos entornos son denegadas. Azurite permanece
para pruebas automatizadas, no como sustituto del Blob compartido en development.

### Configuración y secretos

- En development, ASP.NET Core carga User Secrets al ejecutar con `Development` gracias
  al `UserSecretsId` ya definido en `CekideApi.csproj`. La configuración local no debe
  tener credenciales Azure compartidas ni apuntar al vault de Azure. Se retiro
  `KeyVault:Name` de `appsettings.Development.json`; el proveedor no se agrega en
  Development ni Testing.
- En staging y producción, `KeyVault:Name` se configura como ajuste no secreto de la
  aplicación. `Program.cs` ya añade Azure Key Vault únicamente para esos dos entornos y
  usa `ManagedIdentityCredential` system-assigned de esa aplicación; Development
  conserva `DefaultAzureCredential`. El selector rechaza un vault distinto del asignado
  al entorno antes de solicitar tokens.
- Crear un vault distinto para staging y producción, con RBAC habilitado. Conceder a la
  identidad administrada de runtime solo `Key Vault Secrets User` sobre su vault; no
  asignar permisos de escritura al proceso de runtime. Aprovisionar/rotar secretos con
  una identidad administrativa independiente.
- Esta arquitectura de vaults separados es un objetivo de seguridad. El usuario aceptó
  posibles cargos por operaciones de Key Vault y se crearon ambos vaults; esta aceptación
  no cubre hosting ni otros recursos. No sustituir Key Vault silenciosamente por otro servicio.
- Usar federación OIDC de GitHub Actions para el workflow de despliegue, con permisos
  separados y mínimos por entorno; no usar publish profiles ni secretos de Azure de
  larga duración en el pipeline.
- Mapear las claves jerárquicas de configuración a nombres Key Vault con `--` como
  separador, por ejemplo `Jwt--Key`. Guardar solo valores sensibles como secretos;
  mantener URL, nombres de recurso y otros ajustes no sensibles en configuración
  específica del entorno.
- El proveedor Key Vault actualmente se agrega después de `WebApplication.CreateBuilder`
  y por precedencia puede sobrescribir appsettings y variables de entorno ya cargadas.
  No depender de una variable de entorno o argumento para sobrescribir secretos de Key
  Vault; cualquier excepción operativa debe diseñarse y probarse deliberadamente.
- User Secrets no cifra los valores y es solo para desarrollo. Key Vault debe tener
  auditoría de acceso, soft delete y purge protection; evaluar restricciones de red
  adicionales para producción. Definir rotación y la forma de recargar secretos en la
  aplicación (refresh explícito o reinicio controlado) antes de operar producción.
- Configuración sensible existente que requiere asignación por entorno: `Jwt:Key`,
  `Jwt:Issuer`, `Jwt:Audience`, `CekideDatabaseConnectionString`,
  `Brevo:ApiKey` y cualquier otra credencial de integración. `Storage:ConnectionString`
  debe retirarse al migrar a identidad Entra; no compartir claves de cuenta entre entornos.
  `Brevo:FromEmail`, `Authcraft:BaseUrl`, `KeyVault:Name`, CORS y nombres de contenedor
  son configuración y deben revisarse/clasificarse según sensibilidad, sin almacenarlos
  automáticamente como secretos.
- En el aprovisionamiento inicial, comprobar explícitamente que ambos vaults contienen
  todas las claves requeridas antes de iniciar la aplicación. La ausencia de una clave
  requerida debe impedir la publicación o provocar un fallo de inicio visible.
- La validacion temprana implementada en T023 exige LocalDB en Development, el servidor
  y base Azure SQL asignados a cada entorno con Managed Identity/TLS verificado, y el
  endpoint HTTPS estandar de la cuenta Blob compartida. Rechaza claves compartidas,
  Azurite fuera de Testing y destinos ajenos sin mostrar valores de configuracion.
  T019 cubre arranque, proveedores y precedencia con fixtures sinteticos; no acredita
  la autorizacion real entre identidades Azure.

### Flujo de CI/CD

1. GitHub Actions, en cada pull request: restaurar, compilar y ejecutar
   `dotnet test cekide-api.slnx`; no publicar.
2. Push/merge a la rama protegida `main`: construir una sola vez un artefacto
   inmutable, conservarlo con su hash y desplegarlo en la App Service Web App independiente
   `cekide-api-staging`; verificar existencia y aprobar previamente cualquier cargo.
3. Inmediatamente después de desplegar la API en staging, ejecutar una sola vez la
   migración EF Core construida para el mismo commit/versión, apuntando únicamente a
   `cekide-db-staging`. Ejecutarla como paso separado y serializado, no desde cada réplica
   al iniciar. Después correr health/smoke tests y verificar base, secretos y Blob Storage.
4. Si el despliegue, migración o validaciones fallan, detener el workflow y bloquear
   producción; no reintentar sin inspeccionar el estado de la migración.
5. Si staging está validado, solicitar aprobación manual del environment protegido
   `production` en GitHub Actions.
6. Tras la aprobación, desplegar exactamente el artefacto inmutable validado en staging
   en la App Service existente `cekide-api` (nombre reportado por el usuario; confirmar
   antes de operar). No ejecutar `build` ni `publish` de nuevo entre validación y
   producción; comprobar que el hash del artefacto coincide.
7. Después del despliegue productivo, ejecutar automáticamente la migración serializada
   de ese mismo commit contra `cekide-db`; tras éxito, correr validaciones de salud y
   registrar versión, hash, commit, aprobador y resultado de migración.
8. Si falla cualquier etapa productiva, detener pasos posteriores y no borrar ni
   sobrescribir datos reales. Conservar evidencia, determinar si la migración se aplicó
   parcial o completamente y preferir una corrección hacia delante. Restaurar una base
   solo mediante decisión explícita, backup verificado y evaluación de las escrituras que
   se perderían.

Antes de automatizar el despliegue se deben consolidar `.github/workflows/deploy-cekide-api.yml`
y `.github/workflows/main_cekide-api.yml`: ambos actualmente se disparan sobre `main` y
contienen caminos de publicación de producción distintos. Uno publica directamente a
producción; el estado final debe tener un solo flujo: PR con pruebas sin despliegue, push a
`main` a staging, luego job `production` dependiente de staging con approval protection y
el mismo artefacto sin recompilar. GitHub Actions es obligatorio.

Generar un artefacto de migración inmutable junto con el paquete de API desde el mismo
commit, preferiblemente una migration bundle para la ejecución automatizada. Revisar y
probar las migraciones en PR, usar estrategia expand/contract y serializar su ejecución
mediante un único job. El job de migración usa una identidad de despliegue separada con
permisos de cambio de esquema; la identidad runtime de la API mantiene permisos mínimos.
El despliegue debe impedir que una versión que requiere el nuevo esquema atienda tráfico
antes de que la migración termine correctamente; el mecanismo concreto (slot, compuerta de
tráfico o mantenimiento) debe verificarse para la App Service y su costo antes de elegirlo.

**Decision T006 para staging**: usar temporalmente la Web App existente y aceptar su
interrupcion durante el corte. GitHub-hosted runner con OIDC separado detiene la Web App,
despliega con reinicio automatico deshabilitado (`az webapp deploy --restart false`),
ejecuta el migration bundle con una identidad Entra DDL exclusiva de la base staging y
solo inicia la Web App tras exito. Un fallo la deja detenida. No se crea un runner.
La decision inicial de usar solo `AllowAllWindowsAzureIps` fue reemplazada con
autorizacion expresa por reglas temporales limitadas a la IP exacta de cada runner,
con eliminacion `always()`: un runner de production quedo fuera de esa regla Azure.
Microsoft advierte que esa regla permite acceso de red desde cualquier recurso Azure,
incluidas otras suscripciones; se acepta temporalmente para esta ruta y se conserva la
defensa de identidad/permisos SQL minimos. Validar conectividad y estado stop/deploy/start
en staging antes de activar el flujo. No aplicar el mismo procedimiento a produccion hasta
completar sus secretos, recuperacion ensayada y aprobacion manual.
La CLI local admite `--restart false`, pero la ejecucion real y la configuracion GitHub
estaban pendientes al seleccionar el mecanismo. Posteriormente se configuraron ambos
environments, aprobacion manual productiva, proteccion de `main`, variables por destino
y confianza OIDC de las identidades existentes limitada a su environment. Los tres
workflows de despliegue antiguos estan desactivados; el PR #1 agrega CI independiente.
Por decision expresa posterior se reutilizan las identidades de despliegue tambien para
migrar, una por entorno, manteniendo separadas las identidades runtime. Se crearon usuarios
SQL externos contenidos con el client ID de cada identidad y el rol `cekide_schema_migrator`:
`CREATE TABLE` en su base y `ALTER`, `REFERENCES`, `SELECT`, `INSERT`, `UPDATE`, `DELETE`
solo en `dbo`, sin `db_owner` ni gestion de usuarios. Se verificaron permisos efectivos
e independencia del runtime y se leyeron los grants desde conexiones nuevas.
La identidad user-assigned de despliegue staging se retiro de la Web App con aprobacion,
conservando la identidad system-assigned y su acceso SQL/Key Vault.
Se agrego por pedido expreso una regla SQL permanente `CekideOperatorPublicIp` limitada
a la IP del operador; es independiente de las reglas temporales del runner.
El diagnostico manual `sql-connectivity.yml`, agregado en PR #2 y corregido en PR #3,
paso OIDC, acceso propio, permisos efectivos y denegacion SQL 18456 a la base contraria
para ambas identidades en `41900175393`, con aprobacion manual del usuario en production.
Ambas reglas temporales se eliminaron y se confirmo su ausencia en ARM.
Se asigno un rol personalizado con solo `Microsoft.Sql/servers/firewallRules/read`,
`write`, `delete` en el servidor `cekide`; no concede administracion de base ni datos.
Ese scope permite gestionar cualquier regla del servidor: el workflow restringe sus
operaciones a nombres `gha-<run>-<attempt>-<environment>`. Una perdida del runner puede
requerir limpieza manual de esa regla especifica.
No se ejecutaron migraciones ni despliegues; el flujo de promocion y el control real de
trafico/jobs siguen pendientes. El diagnostico no completa T006.
La preparacion local siguiente extrae el manejo de reglas a
`scripts/deployment/Set-RunnerSqlFirewall.ps1`, consumido por el diagnostico local y
destinado tambien al futuro paso SQL del pipeline. Exige identidad de despliegue,
tenant/subscription, IPv4 exacta y nombre por run/attempt/environment; verifica creacion
y eliminacion, evita sobrescribir otra IP y permite limpieza repetida sin consultar IP.
Se validaron 16 casos offline, incluido fallo sintetico de migracion con limpieza.
Con autorizacion expresa, PR #4 publico solo scripts y wiring CI/diagnostico, y paso CI
con los 16 casos en PowerShell 7 antes de fusionarse. `41902573356` verifico la extraccion
en ambos entornos, con aprobacion manual del usuario para production: creacion/ausencia
de regla comprobadas y acceso propio/denegacion ajena SQL correctos.
No activa T038 ni sustituye el orden stop/deploy/migrate/start o las verificaciones
pendientes de US1/US2. Las reglas permanentes no cambiaron.
Ver [research.md](research.md).

Antes de migraciones productivas, verificar que Azure SQL tiene protección de datos
habilitada y que el procedimiento de restauración fue probado. PITR restaura a una base
nueva y no sobrescribe la existente; el corte al destino restaurado y la evaluación de
escrituras posteriores requieren decisión operativa. La restauración puede tardar horas y
depende del nivel de servicio. No ejecutar automáticamente `Down` o restaurar la base
productiva ante cualquier fallo; preferir una corrección hacia delante y preservar
escrituras válidas.

## Project Structure

### Documentation (this feature)

```text
specs/001-environment-separation/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── checklists/
│   └── requirements.md
└── tasks.md              # Fase posterior (/speckit-tasks)
```

### Source Code (repository root)

```text
.github/
└── workflows/            # Consolidar CI y promoción staging -> producción
infra/                    # Infraestructura declarativa por entorno (definir en implementación)
src/
├── CekideApi/            # Configuración de entorno y arranque de Key Vault
├── CekideApi.Application/
├── CekideApi.Domain/
└── CekideApi.Infrastructure/
tests/
├── CekideApi.UnitTests/
└── CekideApi.IntegrationTests/
```

**Structure Decision**: Mantener la solución .NET y sus proyectos actuales. La
implementación tendrá una definición de infraestructura versionada bajo `infra/` y un
workflow único bajo `.github/workflows/`; los artefactos de diseño de esta fase permanecen
en el directorio de la feature. La definición de infraestructura incluirá dos App Service
Web Apps y sus configuraciones aisladas, ambas en `CEKIDE`. Ningún endpoint público
requiere un contrato nuevo.

## Constitution Check (post-design)

- **Entornos aislados**: PASS CONDICIONAL. La topología separa aplicaciones App Service,
  bases, vaults e identidades entre staging y producción dentro de `CEKIDE`; Blob Storage
  se comparte bajo la excepción constitucional con contenedores/prefijos y permisos aislados.
  Development ejecuta la API y su base local, con acceso solo a su contenedor Blob.
- **Secretos**: PASS. La elección del usuario está aplicada literalmente y los permisos
  separan lectura de runtime de administración.
- **Pruebas y recuperación**: PASS CONDICIONAL. El flujo bloquea la promoción ante fallos
  de migración/validación y requiere migraciones compatibles, recuperación verificada y
  prohíbe rollback destructivo automático en producción.
- **Arquitectura y contratos**: PASS. No se alteran capas ni contratos de repositorio/API.
- **Cambios mínimos**: PASS. Se propone consolidar workflows duplicados y no rehacer la API.

**Gate post-diseño**: PASS CONDICIONAL para ubicación de recursos: las Web Apps de esta
API están en el grupo existente `CEKIDE`, pero staging usa el plan compartido
`AuthCraft/ASP-AuthCraft-bc2b` fuera del grupo; resolver esta dependencia antes de afirmar
aislamiento completo. No se crean grupos nuevos. PASS CONDICIONAL para
Blob Storage compartido: cada identidad debe tener acceso limitado a su contenedor y
prefijo; las pruebas deben demostrar denegación cruzada. PASS para despliegue: staging y
producción son Web Apps independientes; `main` despliega a staging, la aprobación manual
habilita producción y se promueve el mismo artefacto sin reconstruirlo. PASS CONDICIONAL
para migraciones automáticas: se ejecutan como un job único posterior a cada despliegue;
cualquier fallo bloquea etapas posteriores y no dispara restauración destructiva
automática. NO-GO para crear/modificar cualquier recurso hasta verificar costos; informar
al usuario y esperar aprobación explícita si existe costo posible. El usuario aceptó los
posibles costos de Blob Storage para la cuenta/contenedor actuales y operaciones de Key
Vault; los costos y la aptitud de hosting, SQL, respaldo y monitoreo siguen pendientes de
validar en la suscripción y región reales.

## Complexity Tracking

No hay violaciones de la Constitution que requieran justificación.
