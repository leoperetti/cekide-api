# Operación de asistencias, turnos y recuperaciones

Esta funcionalidad abarca `cekide-gym-web`, `cekide-api` y el servicio de
invitaciones de `authcraft-api`. La API es la fuente de verdad para la
identidad de los alumnos, las fechas de asistencia, los cupos, la ocupación
de turnos, la elegibilidad de recuperaciones y la autorización.

## Desarrollo local y orden de despliegue

Para el desarrollo local, configurá `CekideDatabaseConnectionString` para la
base LocalDB `cekide-db`. Después, aplicá las migraciones e iniciá la API:

```powershell
dotnet ef database update --project src\CekideApi.Infrastructure --startup-project src\CekideApi
dotnet run --project src\CekideApi
```

No uses estos comandos ni las pruebas contra una base de producción o una
base operativa compartida. Ejecutá las pruebas relacionales únicamente con
`CEKIDE_TEST_SQL_CONNECTION` configurada para una base exclusiva
`cekide_attendance_test_<suffix>` en `localhost` o SQL Server LocalDB. Los
fixtures de prueba administran y limpian sus propios datos aislados.

Antes de habilitar la interfaz del gimnasio, desplegá los cambios de base de
datos necesarios e iniciá `authcraft-api` y `cekide-api` con la misma
configuración de tenant/JWT. Para las invitaciones de alumnos, desplegá el
proyecto SQL de authcraft mediante el proceso existente antes de habilitar la
nueva interfaz. Guardá las credenciales de Brevo y de almacenamiento en
user-secrets o Key Vault; nunca en el código fuente ni en la configuración del
cliente.

## Puestos de check-in

El personal con rol owner o teacher crea una sesión de puesto desde
**Puestos de check-in**. Al configurar la tablet, ingresá el código de
activación; la API lo devuelve únicamente al crear la sesión. La tablet activa
el puesto y luego envía el DNI del alumno. Si un puesto se pierde o deja de
usarse, revocá su sesión desde la pantalla del personal. El token del puesto
es distinto del JWT de usuario o personal, queda limitado a la pestaña actual
del navegador y no debe registrarse en logs ni guardarse en una URL.

Se registra como máximo una asistencia por alumno y por fecha local del
gimnasio. El check-in no verifica la capacidad del turno, no exige una reserva
y no bloquea al alumno por exceder el cupo. Los avisos de exceso semanal o
mensual son informativos: no conviertas el agotamiento del cupo en una
denegación de acceso.

## Turnos y recuperaciones

El personal publica turnos de lunes a viernes de exactamente una hora y
puede registrar hasta doce ocupaciones habituales previstas. Las reservas
comparten esa capacidad prevista. Las llegadas reales se registran por
separado y pueden superar las doce personas; un turno completo o cancelado
no debe impedir un check-in ordinario válido.

Los créditos de recuperación se generan a partir de la fotografía de
elegibilidad semanal guardada cuando cierra la semana. Los días habilitados y
las excepciones que cruzan de mes se determinan para cada crédito; no amplíes
una excepción parcial a todo el mes siguiente. El alumno consulta y reserva
un turno habilitado desde **Mis recuperaciones**. Si al llegar hay una reserva
confirmada que coincide con el turno, se consume el crédito de su período de
origen. Si no hay una reserva, la asistencia es ordinaria y consume el cupo
del mes actual.

## Avisos de cupo y fallas de entrega

El registro en la bandeja de salida (outbox) se crea únicamente cuando una
asistencia ordinaria hace que el cupo mensual consumido pase de estar por
debajo del límite a alcanzarlo. Los días retenidos, las recuperaciones, un
cupo ya excedido, los check-ins duplicados o la ocupación de un turno no crean
un aviso por sí solos. El proceso en segundo plano se ejecuta cada minuto y
usa la configuración `Brevo:ApiKey`, `Brevo:FromEmail` y `Brevo:FromName`.

El personal revisa en **Notificaciones** los avisos pendientes, enviados,
fallidos o con estado `delivery_unknown`. Los fallos ciertos siguen el
intervalo de reintentos automáticos configurado; los envíos cuyo resultado es
incierto no se reenvían automáticamente. Antes de marcarlos como enviados o
no enviados, o de programar un reintento manual, contrastá el caso con la
evidencia del proveedor. Conservá esa evidencia y la identidad de quien
resuelve el caso en el historial de auditoría.

## Invitaciones y sincronización del perfil

`authcraft-api` administra las invitaciones de alumnos. `cekide-api` reenvía
las solicitudes autorizadas del personal y consulta el perfil aceptado con
autenticación. El signup precarga el nombre, el email, el teléfono y cualquier
DNI existente. Si la invitación incluye un DNI, el signup verifica que
coincida y el formulario lo mantiene de solo lectura; si no incluye uno, el
alumno debe ingresar un DNI válido. El perfil se sincroniza durante el primer
acceso autenticado a una función para alumnos, no mediante una escritura
anónima desde el signup. Si falla la consulta a authcraft, el error debe ser
visible y permitir reintentar; no debe crearse otra ficha de alumno.

Para consultar el contrato de la API y los detalles del despliegue del
esquema, revisá la [guía de invitaciones de alumnos de
authcraft-api](../../authcraft-api/docs/invitations-readme.md).
