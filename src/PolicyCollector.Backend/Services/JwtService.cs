using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using PolicyCollector.Backend.Config;

namespace PolicyCollector.Backend.Services;

public sealed class JwtService
{
    private readonly SymmetricSecurityKey _key;
    private readonly int _expiryMinutes;

    public JwtService(IOptions<BackendOptions> options)
    {
        _key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(options.Value.JwtSecret));
        _expiryMinutes = options.Value.JwtExpiryMinutes;
    }

    public string Generate(int userId, string username, string email, string role)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.Email, email),
            new Claim(ClaimTypes.Role, role)
        };

        var token = new JwtSecurityToken(
            issuer: "PolicyCollector",
            audience: "PolicyCollector.Dashboard",
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_expiryMinutes),
            signingCredentials: new SigningCredentials(_key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public ClaimsPrincipal? Validate(string token)
    {
        try
        {
            return new JwtSecurityTokenHandler().ValidateToken(token,
                new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = "PolicyCollector",
                    ValidateAudience = true,
                    ValidAudience = "PolicyCollector.Dashboard",
                    ValidateLifetime = true,
                    IssuerSigningKey = _key,
                    ValidateIssuerSigningKey = true,
                    ClockSkew = TimeSpan.FromSeconds(30)
                }, out _);
        }
        catch { return null; }
    }
}
