using MapperForge;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

var users = new List<User>
{
    new(1, "Ada Lovelace", "ADA@EXAMPLE.COM"),
    new(2, "Grace Hopper", "GRACE@EXAMPLE.COM"),
};

app.MapGet("/users", () => users.MapToList<UserDto>())
    .WithName("ListUsers")
    .WithOpenApi();

app.MapGet("/users/{id:int}", (int id) =>
    users.FirstOrDefault(user => user.Id == id) is { } user
        ? Results.Ok(UserDto.From(user))
        : Results.NotFound())
    .WithName("GetUser")
    .WithOpenApi();

app.MapPost("/users", (CreateUserCommand command) =>
{
    var user = new User(users.Count + 1, command.Name, command.Email);
    users.Add(user);

    return Results.Created($"/users/{user.Id}", UserDto.From(user));
})
.WithName("CreateUser")
.WithOpenApi();

app.Run();

public sealed record User(int Id, string DisplayName, string Email);

public sealed record CreateUserCommand(string Name, string Email);

[MapFrom(typeof(User))]
public sealed partial class UserDto
{
    public int Id { get; init; }

    [MapProperty(nameof(User.DisplayName))]
    public string Name { get; init; } = "";

    [MapTransform(nameof(NormalizeEmail))]
    public string Email { get; init; } = "";

    private static string NormalizeEmail(string email)
    {
        return email.Trim().ToLowerInvariant();
    }
}
