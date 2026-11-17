using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;

namespace SistemaAgendamentos;

public sealed class TokenService
{
    public const string Issuer = "sistema-agendamentos";
    public const string Audience = "agenda-api";
    private const int ExpiresInSeconds = 3600;

    private readonly string _usuario;
    private readonly string _hash;
    private readonly PasswordHasher<string> _hasher = new();

    public SymmetricSecurityKey SigningKey { get; }

    public TokenService(IConfiguration config)
    {
        _usuario = config["Agenda:Usuario"]?.Trim() ?? "admin";
        var senha = config["Agenda:Senha"];
        if (_usuario.Length is 0 or > 120 || senha is null || senha.Length is < 12 or > 256)
            throw new InvalidOperationException(
                "Configure Agenda:Usuario e uma senha Agenda:Senha de 12 a 256 caracteres.");

        var encodedKey = config["Agenda:ChaveJwt"];
        var secret = new byte[32];
        if (string.IsNullOrWhiteSpace(encodedKey)
            || !Convert.TryFromBase64String(encodedKey, secret, out var count)
            || count != secret.Length)
            throw new InvalidOperationException(
                "Configure Agenda:ChaveJwt com 32 bytes aleatórios codificados em Base64.");

        SigningKey = new SymmetricSecurityKey(secret);
        _hash = _hasher.HashPassword(_usuario, senha);
    }

    public LoginResponse? Login(LoginRequest input, DateTimeOffset now)
    {
        if (input.Senha is null || input.Senha.Length is 0 or > 256)
            return null;

        var validPassword = _hasher.VerifyHashedPassword(_usuario, _hash, input.Senha)
            != PasswordVerificationResult.Failed;
        if (!validPassword || !string.Equals(input.Usuario, _usuario, StringComparison.Ordinal))
            return null;

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, _usuario),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
            ],
            notBefore: now.UtcDateTime,
            expires: now.UtcDateTime.AddSeconds(ExpiresInSeconds),
            signingCredentials: new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256));
        return new LoginResponse(new JwtSecurityTokenHandler().WriteToken(token), "Bearer", ExpiresInSeconds);
    }
}
