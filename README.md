# User Management API

API REST de gestión de usuarios hecha como proyecto final del curso de desarrollo backend con .NET de Coursera. Usa **Minimal APIs** de ASP.NET Core y guarda los datos en memoria, con validación de entrada, manejo global de errores y respuestas en formato *Problem Details* (RFC 7807).

## Funcionalidades

- CRUD completo de usuarios (`FirstName`, `LastName`, `Email`, `Department`).
- Almacenamiento en memoria con `ConcurrentDictionary`. Las escrituras usan un lock para que la comprobación de email único y el guardado ocurran juntos.
- Datos de ejemplo cargados al arrancar.
- Validación de entrada:
  - Nombre y apellido obligatorios, hasta 50 caracteres, solo letras (con tildes), espacios, apóstrofes, guiones y puntos.
  - Email obligatorio, con formato válido y único (no distingue mayúsculas).
  - Departamento obligatorio, hasta 50 caracteres.
  - Se recortan espacios y el email se pasa a minúsculas antes de validar.
- Manejo global de excepciones, incluido el JSON mal formado, con respuestas `ProblemDetails`.
- Códigos HTTP correctos: `400`, `404`, `409` (email duplicado) y `500`.

## Endpoints

Base: `/api/users`

| Método   | Ruta         | Descripción             |
| -------- | ------------ | ----------------------- |
| `GET`    | `/`          | Lista todos los usuarios |
| `GET`    | `/{id:int}`  | Obtiene un usuario       |
| `POST`   | `/`          | Crea un usuario          |
| `PUT`    | `/{id:int}`  | Actualiza un usuario     |
| `DELETE` | `/{id:int}`  | Elimina un usuario       |

El archivo `UserManagementAPI.http` trae peticiones de ejemplo para probar la API desde VS Code o Visual Studio.

## Tecnologías

- .NET 10 / ASP.NET Core Minimal APIs
- OpenAPI

## Cómo ejecutarlo

Requisito: .NET 10 SDK.

```bash
cd UserManagementAPI
dotnet run
```

La API queda en `http://localhost:5261`. En desarrollo, el documento OpenAPI está en `/openapi/v1.json`.
