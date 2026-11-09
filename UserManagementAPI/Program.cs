using System.Collections.Concurrent;
using Microsoft.AspNetCore.Diagnostics;
using UserManagementAPI.Models;
using UserManagementAPI.Validation;

var builder = WebApplication.CreateBuilder(args);


builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

var app = builder.Build();

// Global exception handler: catches anything not handled inside the endpoints,
// including errors thrown while binding the request (e.g. malformed JSON).
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("GlobalExceptionHandler");

        IResult result;
        if (exception is BadHttpRequestException badRequest)
        {
            logger.LogWarning(exception, "Bad request: {Message}", badRequest.Message);
            result = Results.Problem(
                title: "Invalid request",
                detail: "The request body is missing or is not valid JSON.",
                statusCode: badRequest.StatusCode);
        }
        else
        {
            logger.LogError(exception, "Unhandled exception");
            result = Results.Problem(
                title: "Internal server error",
                detail: "An unexpected error occurred. Please try again later.",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        await result.ExecuteAsync(context);
    });
});

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// In-memory storage. Reads are lock-free; writes take a lock so the
// "email must be unique" check and the write happen atomically.
var users = new ConcurrentDictionary<int, User>();
var writeLock = new Lock();
var nextId = 0;

// Seed data
foreach (var seed in new[]
{
    new User { FirstName = "Alice", LastName = "Smith", Email = "alice@techhive.com", Department = "IT" },
    new User { FirstName = "Bob", LastName = "Johnson", Email = "bob@techhive.com", Department = "HR" }
})
{
    seed.Id = Interlocked.Increment(ref nextId);
    users[seed.Id] = seed;
}

bool EmailInUse(string email, int? excludeId) =>
    users.Values.Any(u => u.Id != excludeId &&
                          string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase));

IResult NotFoundUser(int id) =>
    Results.Problem(title: "User not found", detail: $"User with id {id} was not found.",
                    statusCode: StatusCodes.Status404NotFound);

IResult InvalidId() =>
    Results.ValidationProblem(new Dictionary<string, string[]>
    {
        ["id"] = ["Id must be a positive integer."]
    });

IResult EmptyBody() =>
    Results.Problem(title: "Invalid request", detail: "Request body is required.",
                    statusCode: StatusCodes.Status400BadRequest);

IResult EmailConflict(string email) =>
    Results.Problem(title: "Email already in use", detail: $"A user with email '{email}' already exists.",
                    statusCode: StatusCodes.Status409Conflict);

IResult ServerError(ILogger logger, Exception ex, string operation)
{
    logger.LogError(ex, "Error while {Operation}", operation);
    return Results.Problem(title: "Internal server error",
                           detail: $"An unexpected error occurred while {operation}.",
                           statusCode: StatusCodes.Status500InternalServerError);
}

// Returns a copy so callers never share (and mutate) the stored instance.
static User Copy(User u) => new()
{
    Id = u.Id, FirstName = u.FirstName, LastName = u.LastName, Email = u.Email, Department = u.Department
};

var usersApi = app.MapGroup("/api/users");

// GET all users
usersApi.MapGet("/", (ILogger<Program> logger) =>
{
    try
    {
        return Results.Ok(users.Values.OrderBy(u => u.Id).Select(Copy).ToList());
    }
    catch (Exception ex)
    {
        return ServerError(logger, ex, "retrieving users");
    }
})
.WithName("GetUsers");

// GET user by id
usersApi.MapGet("/{id:int}", (int id, ILogger<Program> logger) =>
{
    if (id <= 0) return InvalidId();

    try
    {
        return users.TryGetValue(id, out var user) ? Results.Ok(Copy(user)) : NotFoundUser(id);
    }
    catch (Exception ex)
    {
        return ServerError(logger, ex, $"retrieving user {id}");
    }
})
.WithName("GetUserById");

// POST create user
usersApi.MapPost("/", (User? input, ILogger<Program> logger) =>
{
    if (input is null) return EmptyBody();

    UserValidator.Normalize(input);
    var errors = UserValidator.Validate(input);
    if (errors.Count > 0) return Results.ValidationProblem(errors);

    try
    {
        User created;
        lock (writeLock)
        {
            if (EmailInUse(input.Email, excludeId: null)) return EmailConflict(input.Email);

            created = new User
            {
                Id = Interlocked.Increment(ref nextId),
                FirstName = input.FirstName,
                LastName = input.LastName,
                Email = input.Email,
                Department = input.Department
            };
            users[created.Id] = created;
        }

        return Results.CreatedAtRoute("GetUserById", new { id = created.Id }, Copy(created));
    }
    catch (Exception ex)
    {
        return ServerError(logger, ex, "creating the user");
    }
})
.WithName("CreateUser");

// PUT update user
usersApi.MapPut("/{id:int}", (int id, User? input, ILogger<Program> logger) =>
{
    if (id <= 0) return InvalidId();
    if (input is null) return EmptyBody();

    if (input.Id != 0 && input.Id != id)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["id"] = [$"Id in body ({input.Id}) does not match id in route ({id})."]
        });
    }

    UserValidator.Normalize(input);
    var errors = UserValidator.Validate(input);
    if (errors.Count > 0) return Results.ValidationProblem(errors);

    try
    {
        User updated;
        lock (writeLock)
        {
            if (!users.ContainsKey(id)) return NotFoundUser(id);
            if (EmailInUse(input.Email, excludeId: id)) return EmailConflict(input.Email);

            // Replace with a new instance instead of mutating the stored one,
            // so concurrent readers never see a half-updated user.
            updated = new User
            {
                Id = id,
                FirstName = input.FirstName,
                LastName = input.LastName,
                Email = input.Email,
                Department = input.Department
            };
            users[id] = updated;
        }

        return Results.Ok(Copy(updated));
    }
    catch (Exception ex)
    {
        return ServerError(logger, ex, $"updating user {id}");
    }
})
.WithName("UpdateUser");

// DELETE user
usersApi.MapDelete("/{id:int}", (int id, ILogger<Program> logger) =>
{
    if (id <= 0) return InvalidId();

    try
    {
        lock (writeLock)
        {
            return users.TryRemove(id, out _) ? Results.NoContent() : NotFoundUser(id);
        }
    }
    catch (Exception ex)
    {
        return ServerError(logger, ex, $"deleting user {id}");
    }
})
.WithName("DeleteUser");

app.Run();
