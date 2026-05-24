using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using MapperForge;

BenchmarkRunner.Run<UserMappingBenchmarks>();

[MemoryDiagnoser]
public class UserMappingBenchmarks
{
    private readonly User _user = new(42, "Ada Lovelace", "ADA@EXAMPLE.COM");

    [Benchmark(Baseline = true)]
    public UserDto Manual()
    {
        return new UserDto
        {
            Id = _user.Id,
            Name = _user.DisplayName,
            Email = _user.Email.Trim().ToLowerInvariant(),
        };
    }

    [Benchmark]
    public UserDto MapperForge()
    {
        return UserDto.From(_user);
    }
}

public sealed record User(int Id, string DisplayName, string Email);

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
