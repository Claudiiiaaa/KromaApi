namespace KromaApi.Features.Auth;

public record RegisterRequest(string Email, string Password, string DisplayName);

public record LoginRequest(string Email, string Password);

/// <summary>Datos públicos de una cuenta. Nunca incluye el hash.</summary>
public record UserDto(Guid Id, string Email, string DisplayName);

public record AuthResponse(string Token, DateTimeOffset ExpiresAt, UserDto User);
